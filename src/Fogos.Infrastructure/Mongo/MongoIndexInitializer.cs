using Fogos.Domain.Aircraft;
using Fogos.Domain.Auth;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Risk;
using Fogos.Domain.Stats;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
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

        await context.ApiClients.Indexes.CreateManyAsync(
        [
            Unique(Builders<ApiClient>.IndexKeys.Ascending("keyHash"), "keyHash"),
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
