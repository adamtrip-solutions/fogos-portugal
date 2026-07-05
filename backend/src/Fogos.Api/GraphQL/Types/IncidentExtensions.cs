using Fogos.Api.GraphQL.DataLoaders;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Risk;
using Fogos.Infrastructure.Storage;
using HotChocolate;
using HotChocolate.Types;

namespace Fogos.Api.GraphQL.Types;

/// <summary>
/// Resolver fields on <see cref="Incident"/>. Raw KML strings and the internal
/// nearest-station id are hidden; content is reached through REST / dedicated fields.
/// Every list/lookup field goes through a DataLoader — no N+1.
/// </summary>
[ExtendObjectType(typeof(Incident), IgnoreProperties =
[
    nameof(Incident.Id),
    nameof(Incident.Kml),
    nameof(Incident.KmlVost),
    nameof(Incident.NearestWeatherStationId),
    nameof(Incident.Signals),
])]
public sealed class IncidentExtensions
{
    [ID]
    public string Id([Parent] Incident incident) => incident.Id;

    /// <summary>Derived escalation / rekindle / critical-conditions signals; never null (absent → defaults).</summary>
    public IncidentSignals Signals([Parent] Incident incident) => incident.Signals ?? new IncidentSignals();

    /// <summary>Operational response durations derived from the status log; null when the log is empty.</summary>
    public async Task<ResponseTimes?> ResponseTimes(
        [Parent] Incident incident,
        IncidentStatusHistoryDataLoader loader,
        CancellationToken ct)
    {
        var history = await loader.LoadAsync(incident.Id, ct) ?? [];
        return Fogos.Domain.Incidents.ResponseTimes.From(history);
    }

    public bool HasKml([Parent] Incident incident) => !string.IsNullOrEmpty(incident.Kml);

    public bool HasKmlVost([Parent] Incident incident) => !string.IsNullOrEmpty(incident.KmlVost);

    public async Task<IEnumerable<IncidentHistorySnapshot>> History(
        [Parent] Incident incident,
        IncidentHistoryDataLoader loader,
        CancellationToken ct,
        int first = 50)
    {
        var all = await loader.LoadAsync(incident.Id, ct);
        return (all ?? []).Take(first);
    }

    public async Task<IReadOnlyList<IncidentStatusChange>> StatusHistory(
        [Parent] Incident incident,
        IncidentStatusHistoryDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(incident.Id, ct) ?? [];

    /// <summary>
    /// When the status last changed (latest entry in the status log); null when
    /// no transition was ever recorded. Unlike <c>updatedAt</c>, enrichment
    /// (ICNF, weather, resources) never bumps this.
    /// </summary>
    public async Task<DateTimeOffset?> StatusChangedAt(
        [Parent] Incident incident,
        IncidentStatusHistoryDataLoader loader,
        CancellationToken ct)
    {
        var history = await loader.LoadAsync(incident.Id, ct);
        // StatusHistoryByIncidentsAsync sorts descending by time — first is latest.
        return history is { Length: > 0 } ? history[0].At : null;
    }

    public async Task<IReadOnlyList<IncidentPhoto>> Photos(
        [Parent] Incident incident,
        IncidentPhotosDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(incident.Id, ct) ?? [];

    public async Task<IncidentWeather?> Weather(
        [Parent] Incident incident,
        LatestWeatherByStationDataLoader observations,
        WeatherStationByIdDataLoader stations,
        CancellationToken ct)
    {
        if (incident.NearestWeatherStationId is not int stationId)
            return null;

        var obs = await observations.LoadAsync(stationId, ct);
        if (obs is null)
            return null;

        var station = await stations.LoadAsync(stationId, ct);
        var name = station?.Name ?? "";
        var distanceKm = station is not null && incident.Coordinates is { } coords
            ? coords.DistanceKm(station.Coordinates)
            : 0;

        return new IncidentWeather(
            stationId, name, distanceKm, obs.At,
            obs.Temperature, obs.Humidity, obs.WindSpeedKmh, obs.WindDirection,
            obs.PrecipitationMm, obs.Pressure, obs.Radiation);
    }

    public async Task<Hotspots?> Hotspots(
        [Parent] Incident incident,
        IncidentHotspotsDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(incident.Id, ct);

    [GraphQLName("fireRisk")]
    public async Task<ConcelhoRisk?> FireRisk(
        [Parent] Incident incident,
        IncidentFireRiskDataLoader loader,
        CancellationToken ct) =>
        string.IsNullOrEmpty(incident.Dico) ? null : await loader.LoadAsync(incident.Dico, ct);

    /// <summary>Aircraft associated with this fire (active + historical), joined to the tracked fleet.</summary>
    public async Task<IReadOnlyList<IncidentAircraft>> Aircraft(
        [Parent] Incident incident,
        IncidentAircraftDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(incident.Id, ct) ?? [];

    /// <summary>KML perimeter version history (metadata only — the raw KML is fetched via REST).</summary>
    public async Task<IReadOnlyList<KmlVersionMeta>> KmlHistory(
        [Parent] Incident incident,
        IncidentKmlHistoryDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(incident.Id, ct) ?? [];

    /// <summary>Id of the active ignition cluster this fire belongs to, if any.</summary>
    [ID]
    [GraphQLName("clusterId")]
    public async Task<string?> ClusterId(
        [Parent] Incident incident,
        IncidentClusterDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(incident.Id, ct);
}

/// <summary>
/// Resolver fields on an ignition cluster: the member incidents (via the incident-by-id DataLoader) and
/// their count. The raw incident-id list is hidden — clients reach the incidents through the resolver.
/// </summary>
[ExtendObjectType(typeof(Fogos.Domain.Incidents.IgnitionCluster), IgnoreProperties =
[
    nameof(Fogos.Domain.Incidents.IgnitionCluster.Id),
    nameof(Fogos.Domain.Incidents.IgnitionCluster.IncidentIds),
    nameof(Fogos.Domain.Incidents.IgnitionCluster.UpdatedAt),
])]
public sealed class IgnitionClusterExtensions
{
    [ID]
    public string Id([Parent] Fogos.Domain.Incidents.IgnitionCluster cluster) => cluster.Id;

    public int Count([Parent] Fogos.Domain.Incidents.IgnitionCluster cluster) => cluster.IncidentIds.Count;

    public async Task<IReadOnlyList<Incident>> Incidents(
        [Parent] Fogos.Domain.Incidents.IgnitionCluster cluster,
        IncidentByIdDataLoader loader,
        CancellationToken ct)
    {
        var loaded = await Task.WhenAll(cluster.IncidentIds.Select(id => loader.LoadAsync(id, ct)));
        return loaded.Where(i => i is not null).Select(i => i!).ToList();
    }
}

/// <summary>Extra resolver fields on the aircraft type (the active incident it is currently attached to).</summary>
[ExtendObjectType(typeof(Aircraft))]
public sealed class AircraftExtensions
{
    [ID]
    [GraphQLName("currentIncidentId")]
    public async Task<string?> CurrentIncidentId(
        [Parent] Aircraft aircraft,
        AircraftCurrentIncidentDataLoader loader,
        CancellationToken ct) =>
        await loader.LoadAsync(aircraft.Tracked.Icao, ct);
}

/// <summary>Photo view: exposes a public URL, never the raw storage key.</summary>
[ExtendObjectType(typeof(IncidentPhoto), IgnoreProperties = [nameof(IncidentPhoto.StorageKey)])]
public sealed class IncidentPhotoExtensions
{
    public string PublicUrl([Parent] IncidentPhoto photo, IObjectStorage storage) =>
        storage.PublicUrl(photo.StorageKey);
}
