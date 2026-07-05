using Fogos.Api.Auth;
using Fogos.Api.GraphQL.Filters;
using Fogos.Api.GraphQL.Types;
using Fogos.Domain.Aircraft;
using Fogos.Domain.Alerts;
using Fogos.Domain.Auth;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Reports;
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

    /// <summary>The concelho page payload: identity, risk strip, active incidents, warnings, YoY counters. Null when the DICO is unknown.</summary>
    public async Task<ConcelhoProfile?> ConcelhoProfile(
        string dico,
        LocationReads locations,
        RiskReads risk,
        IncidentReads incidents,
        WeatherReads weather,
        StatsReads stats,
        IClock clock,
        CancellationToken ct)
    {
        var location = await locations.ByDicoAsync(dico, ct);
        if (location is null)
            return null;

        var riskDoc = await risk.LatestByDicoAsync(dico, ct);
        var riskDays = BuildRiskDays(riskDoc);

        var activeIncidents = await incidents.ActiveByDicoAsync(dico, ct);

        var areaCodes = IpmaAreaCatalog.AreaCodesForDistrict(location.District);
        var warnings = await weather.WarningsByAreasEndingAfterAsync(areaCodes, clock.UtcNow, ct);

        var today = clock.LisbonToday;
        var thisStart = clock.FromLisbon(new DateTime(today.Year, 1, 1, 0, 0, 0));
        var thisEnd = clock.FromLisbon(today.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var lastYearDay = today.AddYears(-1);
        var prevStart = clock.FromLisbon(new DateTime(lastYearDay.Year, 1, 1, 0, 0, 0));
        var prevEnd = clock.FromLisbon(lastYearDay.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var yearIgnitions = await stats.ConcelhoIgnitionCountAsync(dico, thisStart, thisEnd, ct);
        var previousYearIgnitions = await stats.ConcelhoIgnitionCountAsync(dico, prevStart, prevEnd, ct);
        var yearBurnAreaHa = await stats.ConcelhoBurnAreaHaAsync(dico, thisStart, thisEnd, ct);

        return new ConcelhoProfile(
            location.Dico, location.Name, location.District,
            riskDays, activeIncidents, warnings,
            yearIgnitions, previousYearIgnitions, yearBurnAreaHa);
    }

    /// <summary>Recent situation reports, newest first.</summary>
    public async Task<IReadOnlyList<SituationReport>> SituationReports(
        SituationReportReads reads,
        CancellationToken ct,
        int first = 7) =>
        await reads.LatestAsync(Math.Clamp(first, 1, 30), ct);

    /// <summary>The authenticated client's own webhooks (secret never exposed here).</summary>
    public async Task<IReadOnlyList<Webhook>> Webhooks(
        WebhookReads reads,
        IFogosCallerAccessor callerAccessor,
        CancellationToken ct)
    {
        var caller = callerAccessor.Caller;
        if (caller.IsAnonymous || string.IsNullOrEmpty(caller.ClientId))
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage("É necessária autenticação de cliente.").SetCode("UNAUTHENTICATED").Build());

        var endpoints = await reads.ByClientAsync(caller.ClientId, ct);
        return endpoints.Select(Webhook.WithoutSecret).ToList();
    }

    /// <summary>Ignition clusters (single-linkage groupings of recent fires); active-only by default.</summary>
    public async Task<IReadOnlyList<IgnitionCluster>> IgnitionClusters(
        IgnitionClusterReads reads,
        CancellationToken ct,
        bool activeOnly = true) =>
        await reads.ListAsync(activeOnly, ct);

    /// <summary>Risk strip for a concelho: today + up to four horizons from the latest RCM run (null levels dropped).</summary>
    private static IReadOnlyList<ConcelhoRiskDay> BuildRiskDays(ConcelhoRisk? risk)
    {
        if (risk is null)
            return [];
        var levels = new[] { risk.Today, risk.Tomorrow, risk.After, risk.After2, risk.After3 };
        var days = new List<ConcelhoRiskDay>(levels.Length);
        for (var i = 0; i < levels.Length; i++)
            if (levels[i] is int level)
                days.Add(new ConcelhoRiskDay(risk.Date.AddDays(i), level, RiskLevels.Label(level)));
        return days;
    }

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
