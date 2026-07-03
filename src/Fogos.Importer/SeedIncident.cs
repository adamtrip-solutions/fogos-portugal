using Fogos.Domain.Geo;
using Fogos.Domain.Incidents;

namespace Fogos.Importer;

/// <summary>
/// Seed-fixture shape for an incident: a plain <c>statusCode</c> int (the seeder builds the
/// status object) and <c>[lng, lat]</c> coordinates. Kind is derived from the natureza code.
/// </summary>
public sealed class SeedIncident
{
    public required string Id { get; set; }
    public required DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public required string Location { get; set; }
    public string? DetailLocation { get; set; }
    public string? District { get; set; }
    public string? Concelho { get; set; }
    public string? Freguesia { get; set; }
    public string? Dico { get; set; }
    public string? Region { get; set; }
    public string? SubRegion { get; set; }

    /// <summary>GeoJSON <c>[lng, lat]</c>.</summary>
    public double[]? Coordinates { get; set; }

    public required int StatusCode { get; set; }
    public required string NaturezaCode { get; set; }
    public string? Natureza { get; set; }

    public Resources? Resources { get; set; }

    public bool? Active { get; set; }
    public bool? Important { get; set; }
    public string? Extra { get; set; }

    public Incident ToIncident() => new()
    {
        Id = Id,
        OccurredAt = OccurredAt,
        CreatedAt = CreatedAt ?? OccurredAt,
        UpdatedAt = UpdatedAt ?? OccurredAt,
        Location = Location,
        DetailLocation = DetailLocation,
        District = District ?? "",
        Concelho = Concelho ?? "",
        Freguesia = Freguesia,
        Dico = Dico ?? "",
        Region = Region,
        SubRegion = SubRegion,
        Coordinates = Coordinates is { } c ? GeoPoint.FromGeoJson(c) : null,
        Status = IncidentStatusCatalog.FromCode(StatusCode),
        Kind = NaturezaCatalog.Classify(NaturezaCode),
        NaturezaCode = NaturezaCode,
        Natureza = Natureza ?? "",
        Resources = Resources ?? Fogos.Domain.Incidents.Resources.Zero,
        Active = Active ?? IncidentStatusCatalog.IsActive(StatusCode),
        Important = Important ?? false,
        Extra = Extra,
    };
}
