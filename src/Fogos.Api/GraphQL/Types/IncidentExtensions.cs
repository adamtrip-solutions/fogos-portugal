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
])]
public sealed class IncidentExtensions
{
    [ID]
    public string Id([Parent] Incident incident) => incident.Id;

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
}

/// <summary>Photo view: exposes a public URL, never the raw storage key.</summary>
[ExtendObjectType(typeof(IncidentPhoto), IgnoreProperties = [nameof(IncidentPhoto.StorageKey)])]
public sealed class IncidentPhotoExtensions
{
    public string PublicUrl([Parent] IncidentPhoto photo, IObjectStorage storage) =>
        storage.PublicUrl(photo.StorageKey);
}
