using Fogos.Api.GraphQL.Filters;
using Fogos.Api.GraphQL.Types;
using Fogos.Domain.Aircraft;
using Fogos.Domain.Auth;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Risk;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Domain.Weather;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Storage;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Types;
using MongoDB.Driver;

namespace Fogos.Api.GraphQL.Queries;

/// <summary>The read schema root.</summary>
public sealed class Query
{
    public async Task<Incident?> Incident([ID] string id, IncidentReads reads, CancellationToken ct) =>
        await reads.GetByIdAsync(id, ct);

    public async Task<IncidentConnection> Incidents(
        IncidentReads reads,
        IClock clock,
        CancellationToken ct,
        IncidentFilter? filter = null,
        string? after = null,
        int first = 25)
    {
        first = Math.Clamp(first, 1, 100);
        var filterDef = IncidentFilterMapper.Build(filter, clock);
        var unpagedFilter = filterDef;

        if (after is not null && IncidentCursor.TryDecode(after, out var occ, out var id))
        {
            var fb = Builders<Incident>.Filter;
            var afterPredicate = fb.Or(
                fb.Lt(x => x.OccurredAt, occ),
                fb.And(fb.Eq(x => x.OccurredAt, occ), fb.Lt(x => x.Id, id)));
            filterDef = Builders<Incident>.Filter.And(filterDef, afterPredicate);
        }

        var rows = await reads.FindAsync(filterDef, first + 1, ct);
        var hasNext = rows.Count > first;
        var page = rows.Take(first).ToList();
        var edges = page.Select(i => new IncidentEdge(IncidentCursor.Encode(i), i)).ToList();

        return new IncidentConnection
        {
            Nodes = page,
            Edges = edges,
            UnpagedFilter = unpagedFilter,
            PageInfo = new IncidentPageInfo
            {
                HasNextPage = hasNext,
                HasPreviousPage = after is not null,
                StartCursor = edges.Count > 0 ? edges[0].Cursor : null,
                EndCursor = edges.Count > 0 ? edges[^1].Cursor : null,
            },
        };
    }

    /// <summary>Active incidents; defaults to fires only when <paramref name="kind"/> is omitted (legacy default).</summary>
    public async Task<IReadOnlyList<Incident>> ActiveIncidents(
        IncidentReads reads,
        CancellationToken ct,
        IReadOnlyList<IncidentKind>? kind = null) =>
        await reads.ActiveAsync(kind is { Count: > 0 } ? kind : [IncidentKind.Fire], ct);

    public Stats Stats() => new();

    public async Task<IReadOnlyList<WeatherStation>> WeatherStations(
        WeatherReads reads,
        CancellationToken ct,
        string? place = null) =>
        await reads.StationsAsync(place, ct);

    public async Task<IReadOnlyList<DailyWeather>> DailyWeather(DateOnly date, WeatherReads reads, CancellationToken ct) =>
        await reads.DailyAsync(date, ct);

    public async Task<TemperatureWaves> TemperatureWaves(
        WeatherReads reads,
        CancellationToken ct,
        bool ongoingOnly = true)
    {
        var waves = await reads.WavesAsync(ongoingOnly, ct);
        return new TemperatureWaves(
            waves.Where(w => w.Type == WaveType.Heat).ToList(),
            waves.Where(w => w.Type == WaveType.Cold).ToList());
    }

    public async Task<FireRiskResult> FireRisk(
        RiskDay day,
        RiskReads reads,
        IClock clock,
        CancellationToken ct,
        string? concelho = null)
    {
        if (!string.IsNullOrWhiteSpace(concelho))
        {
            var risk = await reads.ConcelhoAsync(concelho, clock.LisbonToday, ct);
            return new FireRiskResult(day, risk?.Date, null, risk);
        }

        var geo = await reads.GeoJsonAsync(day, ct);
        return new FireRiskResult(day, geo?.ForecastDate, geo?.GeoJson, null);
    }

    public async Task<IReadOnlyList<Warning>> Warnings(
        WarningReads reads,
        CancellationToken ct,
        WarningKind? kind = null) =>
        await reads.LatestAsync(kind, 100, ct);

    public async Task<IReadOnlyList<Aircraft>> Aircraft(
        AircraftReads reads,
        IClock clock,
        CancellationToken ct,
        bool activeOnly = false)
    {
        var tracked = await reads.TrackedAsync(ct);
        var icaos = tracked.Select(t => t.Icao).ToList();
        var latest = await reads.LatestPositionsByIcaosAsync(icaos, ct);
        var now = clock.UtcNow;

        var aircraft = tracked.Select(t =>
        {
            latest.TryGetValue(t.Icao, out var pos);
            var active = pos is not null && now - pos.SampledAt <= TimeSpan.FromMinutes(10);
            return new Aircraft(t, pos, active);
        });

        if (activeOnly)
            aircraft = aircraft.Where(a => a.Active);

        return aircraft.ToList();
    }

    [GraphQLName("aircraftTrack")]
    public async Task<IReadOnlyList<FlightPosition>> AircraftTrack(
        string icao,
        AircraftReads reads,
        CancellationToken ct,
        int limit = 20) =>
        await reads.TrackAsync(icao, Math.Clamp(limit, 1, 100), ct);

    /// <summary>
    /// Moderation queue: pending photos oldest-first, each with a 15-minute presigned GET URL
    /// (pending photos are not public — the CDN base never serves them). Requires <c>moderate:photos</c>.
    /// </summary>
    [Authorize(Policy = ApiScopes.ModeratePhotos)]
    public async Task<IReadOnlyList<PendingPhoto>> PendingPhotos(
        MongoContext mongo,
        IObjectStorage storage,
        CancellationToken ct,
        int first = 50)
    {
        first = Math.Clamp(first, 1, 200);

        var pending = await mongo.IncidentPhotos
            .Find(Builders<IncidentPhoto>.Filter.Eq(x => x.Status, ModerationStatus.Pending))
            .Sort(Builders<IncidentPhoto>.Sort.Ascending(x => x.CreatedAt))
            .Limit(first)
            .ToListAsync(ct);

        var result = new List<PendingPhoto>(pending.Count);
        foreach (var p in pending)
        {
            var url = await storage.PresignGetAsync(p.StorageKey, TimeSpan.FromMinutes(15), ct);
            result.Add(new PendingPhoto(p.Id, p.IncidentId, p.Width, p.Height, p.TakenAt, p.Gps, p.CreatedAt, url));
        }

        return result;
    }
}
