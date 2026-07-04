using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Writes;

/// <summary>
/// <c>addPosit</c> operator mutation: applies the reported means, dispatches <see cref="IncidentResourcesChanged"/>
/// (which the worker turns into an incident_history snapshot), enforces the <c>write:incidents</c> scope,
/// and 404s on an unknown incident.
/// </summary>
[Collection("fogos")]
public sealed class AddPositTests(ContainerFixture fixture)
{
    private const string OperatorKey = "fgs_live_operator_incidents_posit";

    [SkippableFact]
    public async Task Operator_posit_updates_resources_appends_history_and_raises_event()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("POS1"));
        await SeedData.InsertApiKeyAsync(fixture, OperatorKey, ApiTier.Operator,
            name: "posit operator", scopes: [ApiScopes.WriteIncidents]);

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$input:PositInput!){ addPosit(incidentId:$id, input:$input){ id resources { man terrain aerial heliFight } } }",
            new { id = "POS1", input = new { man = 42, terrain = 12, aerial = 5, heliFight = 2, notes = "Frente estabilizada", cos = "Ana Silva" } });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var res = doc.RootElement.GetProperty("data").GetProperty("addPosit").GetProperty("resources");
        Assert.Equal(42, res.GetProperty("man").GetInt32());
        Assert.Equal(12, res.GetProperty("terrain").GetInt32());
        Assert.Equal(5, res.GetProperty("aerial").GetInt32());
        Assert.Equal(2, res.GetProperty("heliFight").GetInt32());

        // Persisted resources + folded narrative (legacy stored the POSIT text in `extra`).
        var stored = await ctx.Incidents.Find(Builders<Incident>.Filter.Eq(x => x.Id, "POS1")).SingleAsync();
        Assert.Equal(42, stored.Resources.Man);
        Assert.Equal(2, stored.Resources.HeliFight);
        Assert.Contains("Frente estabilizada", stored.Extra);
        Assert.Contains("COS: Ana Silva", stored.Extra);

        // IncidentResourcesChanged landed on the hot stream.
        var evt = await ReadResourcesChangedAsync();
        Assert.NotNull(evt);
        Assert.Equal("POS1", evt!.IncidentId);
        Assert.Equal(42, evt.Current.Man);
        Assert.Equal(10, evt.Previous.Man); // seeded value

        // The worker history handler turns the event into an incident_history snapshot.
        var handler = BuildHistoryHandler();
        await handler.HandleAsync(evt, CancellationToken.None);

        var snapshot = await ctx.IncidentHistory
            .Find(Builders<IncidentHistorySnapshot>.Filter.Eq(x => x.IncidentId, "POS1")).SingleAsync();
        Assert.Equal(42, snapshot.Man);
        Assert.Equal(12, snapshot.Terrain);
        Assert.Equal(5, snapshot.Aerial);
    }

    [SkippableFact]
    public async Task Registered_tier_caller_is_denied()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("POS2"));
        var registeredKey = "fgs_live_registered_noposit";
        await SeedData.InsertApiKeyAsync(fixture, registeredKey, ApiTier.Registered);

        var doc = await fixture.GraphQLAsync(registeredKey,
            "mutation($id:ID!,$input:PositInput!){ addPosit(incidentId:$id, input:$input){ id } }",
            new { id = "POS2", input = new { man = 1 } });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
    }

    [SkippableFact]
    public async Task Unknown_incident_is_a_not_found_error()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await SeedData.InsertApiKeyAsync(fixture, OperatorKey, ApiTier.Operator,
            name: "posit operator", scopes: [ApiScopes.WriteIncidents]);

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!,$input:PositInput!){ addPosit(incidentId:$id, input:$input){ id } }",
            new { id = "NOPE", input = new { man = 1 } });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Contains("INCIDENT_NOT_FOUND", doc.RootElement.GetProperty("errors").ToString());
    }

    private async Task<IncidentResourcesChanged?> ReadResourcesChangedAsync()
    {
        await using var mux = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
        var entries = await mux.GetDatabase().StreamRangeAsync(QueueKeys.Stream("default"));
        foreach (var entry in entries)
        {
            var type = entry[RedisEventDispatcher.TypeField];
            if (type != nameof(IncidentResourcesChanged))
                continue;
            var clr = EventSerializer.Resolve(type!)!;
            return (IncidentResourcesChanged)EventSerializer.Deserialize(clr, entry[RedisEventDispatcher.DataField]!);
        }
        return null;
    }

    private IncidentHistoryHandler BuildHistoryHandler()
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var clock = services.GetRequiredService<IClock>();
        var threads = new SocialThreadStore(mongo, clock);

        var ops = new RecordingOps();
        var publishing = Options.Create(new PublishingOptions()); // DryRun defaults
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage()));
        var twitter = new TwitterPublisher(factory, publishing, Options.Create(new TwitterOptions()), ops, NullLogger<TwitterPublisher>.Instance);
        var telegram = new TelegramPublisher(factory, publishing, Options.Create(new TelegramOptions()), ops, NullLogger<TelegramPublisher>.Instance);
        var facebook = new FacebookPublisher(factory, publishing, Options.Create(new FacebookOptions()), ops, NullLogger<FacebookPublisher>.Instance);
        var fcm = new FcmNotifier(new RecordingFcmSender(), publishing, Options.Create(new FcmOptions()), ops,
            new FakeHostEnvironment("Production"), NullLogger<FcmNotifier>.Instance);

        return new IncidentHistoryHandler(mongo, clock, threads, twitter, telegram, facebook, fcm,
            Options.Create(new IncidentPipelineOptions()));
    }
}
