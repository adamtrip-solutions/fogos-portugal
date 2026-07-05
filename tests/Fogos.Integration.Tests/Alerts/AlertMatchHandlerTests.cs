using Fogos.Domain.Alerts;
using Fogos.Domain.Events;
using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Alerts;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Reads;
using Fogos.Integration.Tests.Incidents;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Alerts;

/// <summary>
/// AlertMatchHandler matches incident events to concelho (by DICO) and point (by distance) subscriptions,
/// records one deduped alert event each, and pushes to token-bearing subscriptions exactly once.
/// </summary>
[Collection("fogos")]
public sealed class AlertMatchHandlerTests(ContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task Concelho_and_point_subscriptions_match_and_dedupe_across_redelivery()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        var incident = SeedData.Incident("ALRT1", coordinates: GeoPoint.FromLatLng(38.72, -9.14));
        incident.Dico = "1106";
        incident.Concelho = "Lisboa";
        incident.Freguesia = "Santa Maria Maior";
        await ctx.Incidents.InsertOneAsync(incident);

        // Concelho subscription (matches by DICO) with a push token.
        var conc = new AlertSubscription { Kind = AlertSubscriptionKind.Concelho, Dico = "1106", FcmToken = "tok-conc", CreatedAt = Now };
        await ctx.AlertSubscriptions.InsertOneAsync(conc);
        // Point subscription inside the radius.
        var near = new AlertSubscription { Kind = AlertSubscriptionKind.Point, Point = GeoPoint.FromLatLng(38.73, -9.14), RadiusKm = 5, CreatedAt = Now };
        await ctx.AlertSubscriptions.InsertOneAsync(near);
        // Point subscription far away — must not match.
        var far = new AlertSubscription { Kind = AlertSubscriptionKind.Point, Point = GeoPoint.FromLatLng(41.15, -8.61), RadiusKm = 5, CreatedAt = Now };
        await ctx.AlertSubscriptions.InsertOneAsync(far);

        var (handler, fcm) = BuildHandler();
        await handler.HandleAsync(new IncidentCreated("ALRT1"), CancellationToken.None);
        await handler.HandleAsync(new IncidentCreated("ALRT1"), CancellationToken.None); // redelivery — no dupes

        var events = await ctx.AlertEvents.Find(FilterDefinition<AlertEvent>.Empty).ToListAsync();
        Assert.Equal(2, events.Count); // conc + near, not far
        Assert.All(events, e => Assert.Equal(AlertEventKind.NewIncident, e.Kind));
        Assert.All(events, e => Assert.Equal("inc:ALRT1", e.DedupeKey));
        Assert.Contains(events, e => e.SubscriptionId == conc.Id);
        Assert.Contains(events, e => e.SubscriptionId == near.Id);
        Assert.DoesNotContain(events, e => e.SubscriptionId == far.Id);
        Assert.Contains(events, e => e.Message == "Novo incêndio em Santa Maria Maior — Incêndio.");

        // Token push fired once for the concelho subscription (near/far have no token).
        var pushes = fcm.Sends.Where(s => s.Token == "tok-conc").ToList();
        Assert.Single(pushes);
    }

    [SkippableFact]
    public async Task Escalation_event_records_escalation_alert()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await ResetAsync();
        var ctx = SeedData.Context(fixture);

        var incident = SeedData.Incident("ALRT2");
        incident.Dico = "1106";
        incident.Concelho = "Lisboa";
        await ctx.Incidents.InsertOneAsync(incident);
        await ctx.AlertSubscriptions.InsertOneAsync(new AlertSubscription { Kind = AlertSubscriptionKind.Concelho, Dico = "1106", CreatedAt = Now });

        var (handler, _) = BuildHandler();
        await handler.HandleAsync(new IncidentEscalating("ALRT2", 60, 20), CancellationToken.None);

        var evt = Assert.Single(await ctx.AlertEvents.Find(FilterDefinition<AlertEvent>.Empty).ToListAsync());
        Assert.Equal(AlertEventKind.Escalation, evt.Kind);
        Assert.Equal("esc:ALRT2", evt.DedupeKey);
        Assert.Equal("A ocorrência em Lisboa está em escalada: 60 operacionais no terreno.", evt.Message);
    }

    private async Task ResetAsync()
    {
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.DeleteManyAsync(FilterDefinition<Incident>.Empty);
        await ctx.AlertSubscriptions.DeleteManyAsync(FilterDefinition<AlertSubscription>.Empty);
        await ctx.AlertEvents.DeleteManyAsync(FilterDefinition<AlertEvent>.Empty);
        // Ensure the dedupe unique index exists (same name as the app init, so this is idempotent).
        await ctx.AlertEvents.Indexes.CreateOneAsync(new CreateIndexModel<AlertEvent>(
            Builders<AlertEvent>.IndexKeys.Ascending("subscriptionId").Ascending("dedupeKey"),
            new CreateIndexOptions { Unique = true, Name = "subscriptionId_dedupeKey" }));
    }

    private (AlertMatchHandler Handler, RecordingFcmSender Fcm) BuildHandler()
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var clock = new TestClock { UtcNow = Now };
        var fcmSender = new RecordingFcmSender();
        var publishing = Options.Create(new PublishingOptions
        {
            Channels = new(StringComparer.OrdinalIgnoreCase) { ["fcm"] = PublisherMode.On },
        });
        var fcm = new FcmNotifier(fcmSender, publishing, Options.Create(new FcmOptions()),
            new RecordingOps(), new FakeHostEnvironment("Development"), NullLogger<FcmNotifier>.Instance);

        var handler = new AlertMatchHandler(mongo, new AlertReads(mongo), new AlertEventStore(mongo, clock), fcm);
        return (handler, fcmSender);
    }
}
