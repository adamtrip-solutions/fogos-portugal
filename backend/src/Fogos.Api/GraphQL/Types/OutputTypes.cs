using Fogos.Domain.Aircraft;
using Fogos.Domain.Incidents;
using Fogos.Domain.Risk;
using Fogos.Domain.Weather;
using HotChocolate;

namespace Fogos.Api.GraphQL.Types;

/// <summary>Incident weather: latest observation from the nearest station + station name + distance.</summary>
[GraphQLName("WeatherObservation")]
public sealed record IncidentWeather(
    int StationId,
    string StationName,
    double DistanceKm,
    DateTimeOffset At,
    double? Temperature,
    double? Humidity,
    double? WindSpeedKmh,
    string? WindDirection,
    double? PrecipitationMm,
    double? Pressure,
    double? Radiation);

/// <summary>Latest nationwide resource totals over active fires (from <c>history_totals</c>).</summary>
public sealed record ResourceTotals(int Man, int Terrain, int Aerial, int Total, DateTimeOffset At);

/// <summary>One hour of the ignition histogram.</summary>
public sealed record HourBucket(int Hour, int Count);

/// <summary>One calendar day and its fire-ignition count (season analytics; gaps filled with 0).</summary>
public sealed record DayCount(DateOnly Date, int Count);

/// <summary>One calendar day and the cumulative accounted burn area (ha) up to and including it.</summary>
public sealed record DayArea(DateOnly Date, double TotalHa);

/// <summary>Ignition count and accounted burn area for one ICNF cause family in a season.</summary>
public sealed record CauseCount(string CauseFamily, int Count, double BurnAreaHa);

/// <summary>Per-district false-alarm counters and rate for a season.</summary>
public sealed record DistrictFalseAlarms(string District, int Total, int FalseAlarms, double Rate);

/// <summary>Median first-transition response times for a season (nullable medians when no sample).</summary>
public sealed record ResponseTimeStats(int Count, int? MedianDispatchToArrivalSeconds, int? MedianArrivalToControlSeconds);

/// <summary>One risk horizon in a concelho profile (a forecast date + its 1–5 level and PT label).</summary>
public sealed record ConcelhoRiskDay(DateOnly Date, int Level, string Label);

/// <summary>
/// Everything the concelho page needs: identity, the risk strip, active incidents, in-force IPMA
/// warnings mapped to the district, and year-over-year ignition / burn-area counters.
/// </summary>
public sealed record ConcelhoProfile(
    string Dico,
    string Name,
    string District,
    IReadOnlyList<ConcelhoRiskDay> Risk,
    IReadOnlyList<Fogos.Domain.Incidents.Incident> ActiveIncidents,
    IReadOnlyList<Fogos.Domain.Weather.WeatherWarning> WeatherWarnings,
    int YearIgnitions,
    int PreviousYearIgnitions,
    double YearBurnAreaHa);

/// <summary>Marker for the <c>stats</c> field; all data is computed by <c>StatsExtensions</c>.</summary>
public sealed class Stats;

/// <summary>Ongoing temperature waves split by kind.</summary>
public sealed record TemperatureWaves(
    IReadOnlyList<TemperatureWave> Heat,
    IReadOnlyList<TemperatureWave> Cold);

/// <summary>
/// Fire risk for a horizon. With a concelho argument the <c>concelho</c> field is populated;
/// without one, the stored <c>geoJson</c> payload for that horizon is returned.
/// </summary>
[GraphQLName("FireRisk")]
public sealed record FireRiskResult(
    RiskDay Day,
    DateOnly? ForecastDate,
    string? GeoJson,
    ConcelhoRisk? Concelho);

/// <summary>A tracked aircraft joined with its latest flight position.</summary>
public sealed record Aircraft(
    TrackedAircraft Tracked,
    FlightPosition? Position,
    bool Active);

/// <summary>An aircraft associated with an incident (link joined to the tracked-fleet metadata).</summary>
public sealed record IncidentAircraft(
    string Icao,
    string? Registration,
    string? Name,
    string? Kind,
    bool Active,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int Samples);

/// <summary>
/// A registered webhook as seen by its owner. <see cref="Secret"/> is populated only on the creation
/// response (<c>registerWebhook</c>); the <c>webhooks</c> query always returns it null.
/// </summary>
public sealed record Webhook(
    [property: ID] string Id,
    string Url,
    IReadOnlyList<string> Events,
    bool Active,
    int ConsecutiveFailures,
    DateTimeOffset CreatedAt,
    string? Secret)
{
    public static Webhook WithSecret(Fogos.Domain.Webhooks.WebhookEndpoint e) =>
        new(e.Id, e.Url, e.Events, e.Active, e.ConsecutiveFailures, e.CreatedAt, e.Secret);

    public static Webhook WithoutSecret(Fogos.Domain.Webhooks.WebhookEndpoint e) =>
        new(e.Id, e.Url, e.Events, e.Active, e.ConsecutiveFailures, e.CreatedAt, null);
}

/// <summary>
/// The result of registering a Web Push device: just its capability id (a random GUID the browser
/// persists and later presents to bind subscriptions / list them). The full device is never exposed.
/// </summary>
public sealed record RegisteredDevice([property: ID] string Id);

/// <summary>
/// The credential minted by <c>registerAppDevice</c>: the device id plus its secret, shown exactly ONCE.
/// The server stores only the SHA-256 hash of the secret (never the plaintext). The app persists both in
/// secure storage and thereafter authenticates with <c>X-Device-Key: fdv1.{deviceId}.{deviceSecret}</c>.
/// </summary>
public sealed record AppDeviceCredential([property: ID] string DeviceId, string DeviceSecret);

/// <summary>Metadata of a stored KML perimeter version (the raw KML is reached only via REST by id).</summary>
public sealed record KmlVersionMeta(
    string Id,
    bool Vost,
    DateTimeOffset CapturedAt,
    int SizeBytes);

/// <summary>
/// A photo awaiting moderation, with a short-lived presigned GET URL (pending photos are not public,
/// so the CDN base cannot serve them). Only reachable through the scope-gated <c>pendingPhotos</c> query.
/// </summary>
public sealed record PendingPhoto(
    [property: ID] string Id,
    [property: ID] string IncidentId,
    int Width,
    int Height,
    DateTimeOffset? TakenAt,
    Fogos.Domain.Geo.GeoPoint? Gps,
    DateTimeOffset CreatedAt,
    string PresignedUrl);

/// <summary>Delta emitted when the active-incident set changes.</summary>
public sealed record ActiveIncidentsDelta(
    DateTimeOffset At,
    IReadOnlyList<Incident> Added,
    IReadOnlyList<Incident> Updated,
    [property: ID] IReadOnlyList<string> Removed);

// ── Cursor connection for incidents ──────────────────────────────────────────

public sealed record IncidentEdge(string Cursor, Incident Node);

public sealed class IncidentPageInfo
{
    public required bool HasNextPage { get; init; }
    public required bool HasPreviousPage { get; init; }
    public string? StartCursor { get; init; }
    public string? EndCursor { get; init; }
}

public sealed class IncidentConnection
{
    public required IReadOnlyList<IncidentEdge> Edges { get; init; }
    public required IReadOnlyList<Incident> Nodes { get; init; }
    public required IncidentPageInfo PageInfo { get; init; }

    /// <summary>The filter WITHOUT the after-cursor predicate — totals cover the whole result set.</summary>
    [GraphQLIgnore]
    public required MongoDB.Driver.FilterDefinition<Incident> UnpagedFilter { get; init; }

    /// <summary>
    /// Total matches for the filter across all pages. Resolved lazily — queries that don't select
    /// it never pay the count.
    /// </summary>
    public async Task<int> TotalCount(Fogos.Infrastructure.Reads.IncidentReads reads, CancellationToken ct) =>
        (int)Math.Min(int.MaxValue, await reads.CountAsync(UnpagedFilter, ct));
}
