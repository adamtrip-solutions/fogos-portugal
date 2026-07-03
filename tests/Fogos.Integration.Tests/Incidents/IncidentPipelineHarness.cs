using System.Collections.Concurrent;
using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Locations;
using Fogos.Domain.Time;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Ingest;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Rendering;
using Fogos.Infrastructure.Scheduling;
using Fogos.Infrastructure.Sources;
using Fogos.Worker.Handlers;
using Fogos.Worker.Jobs.Icnf;
using Fogos.Worker.Jobs.Incidents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// Wires the incident cluster against the shared Testcontainers Mongo/Redis with recording social/FCM
/// doubles and a fail-fast renderer stub. Provides ingest + handler routing so a fixture can flow through
/// the real pipeline (upsert → events on the stream → handler side effects) deterministically.
/// </summary>
internal sealed class IncidentPipelineHarness : IDisposable
{
    public MongoContext Mongo { get; }
    public IConnectionMultiplexer Redis { get; }
    public RecordingOps Ops { get; } = new();
    public TestClock Clock { get; } = new() { UtcNow = new DateTimeOffset(2026, 8, 1, 15, 0, 0, TimeSpan.Zero) };

    public RecordingTwitter Twitter { get; } = new();
    public RecordingTelegram Telegram { get; } = new();
    public RecordingFacebook Facebook { get; } = new();
    public RecordingDiscord Discord { get; } = new();
    public RecordingFcmSender Fcm { get; } = new();

    public RedisEventDispatcher Dispatcher { get; }
    public IcnfClientStub Icnf { get; } = new();

    private readonly MongoClient _client;
    private readonly Dictionary<Type, List<Func<IDomainEvent, CancellationToken, Task>>> _routes = new();

    public IncidentPipelineHarness(ContainerFixture fixture)
    {
        FogosClassMaps.Register();
        var database = "fogos_inc_" + Guid.NewGuid().ToString("N")[..8];
        _client = new MongoClient(fixture.MongoConnectionString);
        Mongo = new MongoContext(_client, Options.Create(new MongoOptions
        {
            ConnectionString = fixture.MongoConnectionString,
            Database = database,
        }));
        Redis = ConnectionMultiplexer.Connect(fixture.RedisConnectionString);

        Dispatcher = new RedisEventDispatcher(Redis, Clock);

        var publishing = Options.Create(new PublishingOptions
        {
            Channels = new(StringComparer.OrdinalIgnoreCase) { ["fcm"] = PublisherMode.On },
        });
        var fcmOptions = Options.Create(new FcmOptions());
        FcmNotifier = new FcmNotifier(Fcm, publishing, fcmOptions, Ops,
            new FakeHostEnvironment("Development"), NullLogger<FcmNotifier>.Instance);
        Delayed = new RedisDelayedDispatcher(Redis, Clock);
        Scheduler = new NotificationScheduler(Delayed, fcmOptions);
        Locks = new RedisSingleFlightLock(Redis);
        Threads = new SocialThreadStore(Mongo, Clock);
        PipelineOptions = Options.Create(new IncidentPipelineOptions { SocialLinkDomain = "fogos.pt" });

        Renderer = new RendererClient(
            new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError))),
            Options.Create(new RendererOptions { Retries = 1, RetryBaseDelay = TimeSpan.Zero, MinBytes = 1 }),
            Ops, NullLogger<RendererClient>.Instance);

        Resolver = new LocationResolver(Mongo, Ops);
        Ingest = new IncidentIngestService(Mongo, Resolver, Dispatcher, Clock, Ops, NullLogger<IncidentIngestService>.Instance);
        Enrichment = new IcnfEnrichmentService(Icnf.Client(), Mongo, Dispatcher, Clock, NullLogger<IcnfEnrichmentService>.Instance);
        Important = new ImportantFireChecker(Mongo, Threads, Locks, Clock, Twitter, Telegram, Facebook, Scheduler, FcmNotifier, PipelineOptions, NullLogger<ImportantFireChecker>.Instance);

        BuildRoutes();
        EnsureIndexesAsync().GetAwaiter().GetResult();
    }

    public FcmNotifier FcmNotifier { get; }
    public RedisDelayedDispatcher Delayed { get; }
    public NotificationScheduler Scheduler { get; }
    public ISingleFlightLock Locks { get; }
    public SocialThreadStore Threads { get; }
    public RendererClient Renderer { get; }
    public LocationResolver Resolver { get; }
    public IncidentIngestService Ingest { get; }
    public IcnfEnrichmentService Enrichment { get; }
    public ImportantFireChecker Important { get; }
    public IOptions<IncidentPipelineOptions> PipelineOptions { get; }

    private async Task EnsureIndexesAsync()
    {
        await Mongo.Incidents.Indexes.CreateOneAsync(
            new CreateIndexModel<Domain.Incidents.Incident>(Builders<Domain.Incidents.Incident>.IndexKeys.Geo2DSphere("coordinates")));
        await Mongo.WeatherStations.Indexes.CreateOneAsync(
            new CreateIndexModel<WeatherStation>(Builders<WeatherStation>.IndexKeys.Geo2DSphere("coordinates")));
    }

    // ── Seeding ───────────────────────────────────────────────────────────────
    public Task SeedConcelhoAsync(string name, string code, string dico, string districtCode) =>
        Mongo.Locations.InsertOneAsync(new Location { Level = LocationLevel.Concelho, Name = name, Code = code, Dico = dico });

    public Task SeedDistrictAsync(string name, string code) =>
        Mongo.Locations.InsertOneAsync(new Location { Level = LocationLevel.Distrito, Name = name, Code = code });

    public Task SeedStationAsync(int id, double lat, double lng) =>
        Mongo.WeatherStations.InsertOneAsync(new WeatherStation { Id = id, Coordinates = GeoPoint.FromLatLng(lat, lng), Name = $"S{id}" });

    public IIncidentSource ArcGisSource(string featureServerJson)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(featureServerJson),
        });
        var client = new ArcGisClient(new HttpClient(handler), Options.Create(new FogosSourcesOptions()));
        return new ArcGisOcorrenciasSource(client);
    }

    // ── Event routing (stands in for the stream consumer, deterministically) ───
    public async Task<IReadOnlyList<IDomainEvent>> DrainAsync(string stream, CancellationToken ct = default)
    {
        var key = QueueKeys.Stream(stream);
        var entries = await Redis.GetDatabase().StreamRangeAsync(key);
        var events = new List<IDomainEvent>();
        foreach (var entry in entries)
        {
            var type = entry[RedisEventDispatcher.TypeField];
            var data = entry[RedisEventDispatcher.DataField];
            var clr = EventSerializer.Resolve(type!);
            if (clr is null)
                continue;
            var evt = EventSerializer.Deserialize(clr, data!);
            events.Add(evt);
            if (_routes.TryGetValue(clr, out var handlers))
                foreach (var h in handlers)
                    await h(evt, ct);
        }
        // Clear so a subsequent drain (e.g. icnf events raised during this drain) starts fresh.
        await Redis.GetDatabase().KeyDeleteAsync(key);
        return events;
    }

    private void BuildRoutes()
    {
        var nearest = new AssignNearestWeatherStationHandler(Mongo);
        var history = new IncidentHistoryHandler(Mongo, Clock, Threads, Twitter, Telegram, Facebook, FcmNotifier, PipelineOptions);
        var statusHistory = new IncidentStatusHistoryHandler(Mongo, Clock, Threads, Twitter, Telegram, Facebook, Discord, Renderer, Scheduler, FcmNotifier, PipelineOptions);
        var social = new NewIncidentSocialHandler(Mongo, Clock, Threads, Twitter, Telegram, Facebook, Renderer, Scheduler, FcmNotifier, PipelineOptions);
        var notify = new NewIncidentNotificationsHandler(Mongo, Clock, FcmNotifier, Scheduler, Ops);
        var kickoff = new IcnfKickoffHandler(Mongo, Clock, Dispatcher);
        var icnfProcess = new ProcessIcnfFireDataHandler(Enrichment);
        var icnfSocial = new IcnfSocialHandler(Mongo, Threads, Twitter, Telegram, Facebook, Renderer, Scheduler, FcmNotifier, PipelineOptions);

        Add<IncidentCreated>((e, ct) => nearest.HandleAsync(e, ct));
        Add<IncidentCreated>((e, ct) => history.HandleAsync(e, ct));
        Add<IncidentCreated>((e, ct) => social.HandleAsync(e, ct));
        Add<IncidentCreated>((e, ct) => notify.HandleAsync(e, ct));
        Add<IncidentCreated>((e, ct) => kickoff.HandleAsync(e, ct));
        Add<IncidentResourcesChanged>((e, ct) => history.HandleAsync(e, ct));
        Add<IncidentStatusChanged>((e, ct) => statusHistory.HandleAsync(e, ct));
        Add<ProcessIcnfFireData>((e, ct) => icnfProcess.HandleAsync(e, ct));
        Add<IcnfEnriched>((e, ct) => icnfSocial.HandleAsync(e, ct));
    }

    private void Add<T>(Func<T, CancellationToken, Task> handler) where T : IDomainEvent
    {
        if (!_routes.TryGetValue(typeof(T), out var list))
            _routes[typeof(T)] = list = [];
        list.Add((e, ct) => handler((T)e, ct));
    }

    public void Dispose()
    {
        _client.DropDatabase(Mongo.Database.DatabaseNamespace.DatabaseName);
        Redis.Dispose();
    }
}

/// <summary>Settable clock for deterministic age/year maths.</summary>
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LisbonNow => TimeZoneInfo.ConvertTime(UtcNow, FogosClock.Lisbon);
    public DateOnly LisbonToday => DateOnly.FromDateTime(LisbonNow.Date);
    public DateTimeOffset FromLisbon(DateTime naiveLocal)
    {
        var unspecified = DateTime.SpecifyKind(naiveLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, FogosClock.Lisbon.GetUtcOffset(unspecified));
    }
    public DateTimeOffset ToLisbon(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, FogosClock.Lisbon);
}

internal sealed class RecordingTwitter : ITwitterPublisher
{
    public readonly ConcurrentQueue<SocialPost> Posts = new();
    private int _n;
    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "twitter", CancellationToken ct = default)
    {
        Posts.Enqueue(post);
        return Task.FromResult(PublishResult.Ok($"tw-{Interlocked.Increment(ref _n)}"));
    }
}

internal sealed class RecordingTelegram : ITelegramPublisher
{
    public readonly ConcurrentQueue<SocialPost> Posts = new();
    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "telegram", CancellationToken ct = default)
    {
        Posts.Enqueue(post);
        return Task.FromResult(PublishResult.Ok("tg"));
    }
}

internal sealed class RecordingFacebook : IFacebookPublisher
{
    public readonly ConcurrentQueue<SocialPost> Posts = new();
    public readonly ConcurrentQueue<(string PostId, string Message)> Comments = new();
    private int _n;
    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "facebook", CancellationToken ct = default)
    {
        Posts.Enqueue(post);
        return Task.FromResult(PublishResult.Ok($"fb-{Interlocked.Increment(ref _n)}"));
    }
    public Task<PublishResult> CommentOnPostAsync(string postId, string message, string channelKey = "facebook", CancellationToken ct = default)
    {
        Comments.Enqueue((postId, message));
        return Task.FromResult(PublishResult.Ok("cmt"));
    }
}

internal sealed class RecordingDiscord : IDiscordPostPublisher
{
    public readonly ConcurrentQueue<SocialPost> Posts = new();
    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "discordPosts", CancellationToken ct = default)
    {
        Posts.Enqueue(post);
        return Task.FromResult(PublishResult.Ok("dc"));
    }
}
