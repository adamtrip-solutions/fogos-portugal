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
}
