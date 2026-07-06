using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;

namespace Fogos.Infrastructure.Ingest;

/// <summary>Outcome of mapping a <see cref="RawIncident"/>: the canonical incident, or a reason it was rejected.</summary>
public sealed record MapResult(Incident? Incident, string? Rejection)
{
    public bool Ok => Incident is not null;
    public static MapResult Reject(string reason) => new(null, reason);
    public static MapResult Success(Incident incident) => new(incident, null);
}

/// <summary>
/// Pure, Mongo-free mapping from a <see cref="RawIncident"/> + resolved <see cref="LocationInfo"/> to the
/// canonical <see cref="Incident"/>. Status is normalized through <see cref="IncidentStatusCatalog"/>
/// (including the ArcGIS 'Despacho de 1º Alerta' / 'Em Conclusão' aliases already in the catalog); kind
/// via <see cref="NaturezaCatalog"/>; coordinates via <see cref="GeoPoint.FromLatLng"/> behind a validity
/// guard (null when out of range or the (0,0) placeholder). Active iff the status code is in ActiveCodes.
/// </summary>
public static class IncidentMapper
{
    public static MapResult Map(RawIncident raw, LocationInfo location)
    {
        if (!IncidentStatusCatalog.TryNormalize(raw.StatusLabel, out var status))
            return MapResult.Reject($"unknown status '{raw.StatusLabel}'");

        var kind = NaturezaCatalog.Classify(raw.NaturezaCode);
        var coordinates = ResolveCoordinates(raw.Lat, raw.Lng);

        // Legacy `location` line: title-cased district, RAW concelho, title-cased freguesia.
        var locationLine = $"{location.District}, {raw.Concelho}, {location.Freguesia ?? ""}";

        var incident = new Incident
        {
            Id = raw.Id,
            OccurredAt = raw.OccurredAt,
            Location = locationLine,
            DetailLocation = raw.Localidade,
            District = location.District,
            Concelho = location.Concelho,
            Freguesia = location.Freguesia,
            Dico = location.Dico,
            LocationInferred = location.Inferred,
            Region = raw.Region,
            SubRegion = raw.SubRegion,
            Coordinates = coordinates,
            Status = status,
            Kind = kind,
            NaturezaCode = raw.NaturezaCode,
            Natureza = raw.Natureza,
            Resources = raw.Resources,
            Active = IncidentStatusCatalog.IsActive(status.Code),
            ArcGis = BuildArcGisDetails(raw),
        };

        return MapResult.Success(incident);
    }

    private static GeoPoint? ResolveCoordinates(double? lat, double? lng)
    {
        if (lat is not { } la || lng is not { } lo)
            return null;
        if (la is < -90 or > 90 || lo is < -180 or > 180)
            return null;
        if (la == 0 && lo == 0)
            return null; // the feed's "no fix" placeholder — never store it (it breaks $near).
        return GeoPoint.FromLatLng(la, lo);
    }

    private static ArcGisDetails? BuildArcGisDetails(RawIncident raw)
    {
        if (raw.EstadoAgrupado is null && raw.FaseIncendio is null && raw.Rasi is null
            && raw.DuracaoMinutos is null && raw.DataDosDados is null)
            return null;

        return new ArcGisDetails
        {
            EstadoAgrupado = raw.EstadoAgrupado,
            FaseIncendio = raw.FaseIncendio,
            Rasi = raw.Rasi,
            DuracaoMinutos = raw.DuracaoMinutos,
            DataDosDados = raw.DataDosDados,
        };
    }
}
