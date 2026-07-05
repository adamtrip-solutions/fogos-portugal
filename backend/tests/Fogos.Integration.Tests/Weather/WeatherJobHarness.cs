using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Jobs.Weather;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Weather;

/// <summary>
/// Wires up the weather jobs against the shared Testcontainers Mongo/Redis, each test getting an
/// isolated database. IPMA HTTP is served from <see cref="WeatherFixtures"/> via a URL-routing stub;
/// Telegram is a recording double so STB dispatch can be asserted without a real bot.
/// </summary>
internal sealed class WeatherJobHarness : IDisposable
{
    public MongoContext Mongo { get; }
    public IConnectionMultiplexer Redis { get; }
    public RecordingOps Ops { get; } = new();
    public RecordingTelegram Telegram { get; } = new();
    public IpmaClient Ipma { get; }
    public WeatherFreshnessTracker Freshness { get; }
    public ISingleFlightLock Locks { get; }
    public FakeClock Clock { get; } = new();

    private readonly MongoClient _client;

    public WeatherJobHarness(ContainerFixture fixture)
    {
        FogosClassMaps.Register();

        var database = "fogos_weather_" + Guid.NewGuid().ToString("N")[..8];
        _client = new MongoClient(fixture.MongoConnectionString);
        Mongo = new MongoContext(_client, Options.Create(new MongoOptions
        {
            ConnectionString = fixture.MongoConnectionString,
            Database = database,
        }));

        Redis = ConnectionMultiplexer.Connect(fixture.RedisConnectionString);
        Freshness = new WeatherFreshnessTracker(Redis, Ops);
        Locks = new RedisSingleFlightLock(Redis);

        var handler = new IpmaRoutingHandler();
        Ipma = new IpmaClient(new HttpClient(handler), Options.Create(new FogosSourcesOptions()));
    }

    public IHttpClientFactory NormalsHttpFactory() => new StubHttpClientFactory(new IpmaRoutingHandler());

    public void Dispose()
    {
        _client.DropDatabase(Mongo.Database.DatabaseNamespace.DatabaseName);
        Redis.Dispose();
    }
}

/// <summary>Recording Telegram publisher — captures posts (STB warning dispatch) without a bot.</summary>
internal sealed class RecordingTelegram : ITelegramPublisher
{
    public readonly List<(SocialPost Post, string Channel)> Posts = [];

    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "telegram", CancellationToken ct = default)
    {
        Posts.Add((post, channelKey));
        return Task.FromResult(PublishResult.Ok("recorded"));
    }
}

/// <summary>Deterministic clock so wave-detection date maths are stable across the test run.</summary>
internal sealed class FakeClock : Fogos.Domain.Time.IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LisbonNow => TimeZoneInfo.ConvertTime(UtcNow, Fogos.Domain.Time.FogosClock.Lisbon);
    public DateOnly LisbonToday => DateOnly.FromDateTime(LisbonNow.Date);
    public DateTimeOffset FromLisbon(DateTime naiveLocal)
    {
        var unspecified = DateTime.SpecifyKind(naiveLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Fogos.Domain.Time.FogosClock.Lisbon.GetUtcOffset(unspecified));
    }
    public DateTimeOffset ToLisbon(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Fogos.Domain.Time.FogosClock.Lisbon);
}

/// <summary>Routes IPMA requests to the right fixture payload by URL, mirroring the real endpoints.</summary>
internal sealed class IpmaRoutingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = url switch
        {
            _ when url.Contains("observations.json") => WeatherFixtures.Observations,
            _ when url.Contains("obs-daily.json") => WeatherFixtures.DailyObservations,
            _ when url.Contains("stations.json") => WeatherFixtures.Stations,
            _ when url.Contains("index.html") => WeatherFixtures.Homepage,
            _ when url.Contains("normais.clima") => WeatherFixtures.Normals,
            _ => throw new InvalidOperationException($"Unexpected IPMA URL in test: {url}"),
        };
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        });
    }
}
