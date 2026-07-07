using Fogos.Domain.Aircraft;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Devices;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Reports;
using Fogos.Domain.Risk;
using Fogos.Domain.Stats;
using Fogos.Domain.Users;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Mongo;

/// <summary>Idempotent index creation. <c>CreateMany</c> is safe to re-run: existing indexes are left as-is.</summary>
public sealed class MongoIndexInitializer(MongoContext context, IOptions<MongoOptions> options)
{
    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        await context.Incidents.Indexes.CreateManyAsync(
        [
            Model(Builders<Incident>.IndexKeys.Geo2DSphere("coordinates"), "coordinates_2dsphere"),
            Model(Builders<Incident>.IndexKeys.Ascending("active").Ascending("kind"), "active_kind"),
            Model(Builders<Incident>.IndexKeys.Descending("occurredAt"), "occurredAt_desc"),
            Model(Builders<Incident>.IndexKeys.Ascending("dico"), "dico"),
        ], ct);

        await context.IncidentHistory.Indexes.CreateManyAsync(
        [
            Model(Builders<IncidentHistorySnapshot>.IndexKeys.Ascending("incidentId").Descending("at"), "incidentId_at"),
        ], ct);

        await context.IncidentStatusHistory.Indexes.CreateManyAsync(
        [
            Model(Builders<IncidentStatusChange>.IndexKeys.Ascending("incidentId").Descending("at"), "incidentId_at"),
        ], ct);

        await context.IncidentPhotos.Indexes.CreateManyAsync(
        [
            Model(Builders<IncidentPhoto>.IndexKeys.Ascending("incidentId").Ascending("status"), "incidentId_status"),
            Model(Builders<IncidentPhoto>.IndexKeys.Ascending("signature"), "signature"),
        ], ct);

        await context.WeatherStations.Indexes.CreateManyAsync(
        [
            Model(Builders<WeatherStation>.IndexKeys.Geo2DSphere("coordinates"), "coordinates_2dsphere"),
        ], ct);

        await context.WeatherHourly.Indexes.CreateManyAsync(
        [
            Unique(Builders<WeatherObservation>.IndexKeys.Ascending("stationId").Ascending("at"), "stationId_at"),
        ], ct);

        await context.WeatherDaily.Indexes.CreateManyAsync(
        [
            Unique(Builders<DailyWeather>.IndexKeys.Ascending("stationId").Ascending("date"), "stationId_date"),
        ], ct);

        await context.WeatherNormals.Indexes.CreateManyAsync(
        [
            Unique(Builders<WeatherNormal>.IndexKeys.Ascending("stationId").Ascending("period"), "stationId_period"),
        ], ct);

        await context.TemperatureWaves.Indexes.CreateManyAsync(
        [
            Unique(Builders<TemperatureWave>.IndexKeys.Ascending("stationId").Ascending("type").Ascending("startDate"), "stationId_type_startDate"),
        ], ct);

        await context.WeatherWarnings.Indexes.CreateManyAsync(
        [
            Unique(Builders<WeatherWarning>.IndexKeys.Ascending("control"), "control"),
        ], ct);

        await context.RcmDaily.Indexes.CreateManyAsync(
        [
            Unique(Builders<ConcelhoRisk>.IndexKeys.Ascending("dico").Ascending("date"), "dico_date"),
        ], ct);

        await context.RcmGeoJson.Indexes.CreateManyAsync(
        [
            Unique(Builders<RiskGeoJson>.IndexKeys.Ascending("when").Ascending("forecastDate"), "when_forecastDate"),
        ], ct);

        await context.Warnings.Indexes.CreateManyAsync(
        [
            Model(Builders<Warning>.IndexKeys.Descending("createdAt"), "createdAt_desc"),
        ], ct);

        var ttl = TimeSpan.FromDays(options.Value.FlightPositionTtlDays);
        await context.FlightPositions.Indexes.CreateManyAsync(
        [
            Model(Builders<FlightPosition>.IndexKeys.Ascending("icao").Descending("sampledAt"), "icao_sampledAt"),
            new CreateIndexModel<FlightPosition>(
                Builders<FlightPosition>.IndexKeys.Ascending("sampledAt"),
                new CreateIndexOptions { Name = "sampledAt_ttl", ExpireAfter = ttl }),
        ], ct);

        await context.IncidentAircraft.Indexes.CreateManyAsync(
        [
            Unique(Builders<IncidentAircraftLink>.IndexKeys.Ascending("incidentId").Ascending("icao"), "incidentId_icao"),
            Model(Builders<IncidentAircraftLink>.IndexKeys.Ascending("icao").Ascending("active"), "icao_active"),
        ], ct);

        await context.IncidentKmlVersions.Indexes.CreateManyAsync(
        [
            Model(Builders<IncidentKmlVersion>.IndexKeys.Ascending("incidentId").Descending("capturedAt"), "incidentId_capturedAt"),
        ], ct);

        await context.IgnitionClusters.Indexes.CreateManyAsync(
        [
            Model(Builders<IgnitionCluster>.IndexKeys.Ascending("active").Descending("lastAt"), "active_lastAt"),
            Model(Builders<IgnitionCluster>.IndexKeys.Ascending("incidentIds"), "incidentIds"),
        ], ct);

        await context.ApiClients.Indexes.CreateManyAsync(
        [
            Unique(Builders<ApiClient>.IndexKeys.Ascending("keyHash"), "keyHash"),
            Model(Builders<ApiClient>.IndexKeys.Ascending("ownerUserId"), "ownerUserId"),
        ], ct);

        await context.Users.Indexes.CreateManyAsync(
        [
            Unique(Builders<User>.IndexKeys.Ascending("clerkUserId"), "clerkUserId"),
        ], ct);

        // ── Alerts / Webhooks / Reports (WP4) ────────────────────────────────────
        await context.AlertSubscriptions.Indexes.CreateManyAsync(
        [
            Model(Builders<AlertSubscription>.IndexKeys.Ascending("kind").Ascending("dico"), "kind_dico"),
            Model(Builders<AlertSubscription>.IndexKeys.Ascending("createdAt"), "createdAt"),
            Model(Builders<AlertSubscription>.IndexKeys.Ascending("ownerUserId"), "ownerUserId"),
            Model(Builders<AlertSubscription>.IndexKeys.Ascending("deviceId"), "deviceId"),
        ], ct);

        await context.Devices.Indexes.CreateManyAsync(
        [
            Unique(Builders<Device>.IndexKeys.Ascending("pushEndpoint"), "pushEndpoint"),
            Model(Builders<Device>.IndexKeys.Ascending("ownerUserId"), "ownerUserId"),
            Model(Builders<Device>.IndexKeys.Ascending("disabled").Ascending("lastSeenAt"), "disabled_lastSeenAt"),
        ], ct);

        await context.AlertEvents.Indexes.CreateManyAsync(
        [
            Unique(Builders<AlertEvent>.IndexKeys.Ascending("subscriptionId").Ascending("dedupeKey"), "subscriptionId_dedupeKey"),
            Model(Builders<AlertEvent>.IndexKeys.Ascending("subscriptionId").Descending("createdAt"), "subscriptionId_createdAt"),
            new CreateIndexModel<AlertEvent>(
                Builders<AlertEvent>.IndexKeys.Ascending("createdAt"),
                new CreateIndexOptions { Name = "createdAt_ttl", ExpireAfter = TimeSpan.FromDays(7) }),
        ], ct);

        await context.WebhookEndpoints.Indexes.CreateManyAsync(
        [
            Model(Builders<WebhookEndpoint>.IndexKeys.Ascending("clientId").Ascending("active"), "clientId_active"),
            Model(Builders<WebhookEndpoint>.IndexKeys.Ascending("events").Ascending("active"), "events_active"),
        ], ct);

        await context.SituationReports.Indexes.CreateManyAsync(
        [
            Model(Builders<SituationReport>.IndexKeys.Descending("at"), "at_desc"),
        ], ct);

        await context.HistoryTotals.Indexes.CreateManyAsync(
        [
            Model(Builders<HistoryTotal>.IndexKeys.Descending("at"), "at_desc"),
        ], ct);
    }

    private static CreateIndexModel<T> Model<T>(IndexKeysDefinition<T> keys, string name) =>
        new(keys, new CreateIndexOptions { Name = name });

    private static CreateIndexModel<T> Unique<T>(IndexKeysDefinition<T> keys, string name) =>
        new(keys, new CreateIndexOptions { Name = name, Unique = true });
}
