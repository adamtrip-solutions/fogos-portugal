using Fogos.Domain.Geo;

namespace Fogos.Domain.Incidents;

/// <summary>
/// The central entity. `_id` is the ANEPC business id (numero_sado) — a string,
/// no ObjectId duality. Social threading state lives in <c>social_threads</c>, not here.
/// </summary>
public sealed class Incident
{
    /// <summary>ANEPC business id (numero_sado / Numero).</summary>
    public required string Id { get; set; }

    /// <summary>When the occurrence started (legacy dateTime), UTC.</summary>
    public required DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // ── Where ────────────────────────────────────────────────────────────────
    /// <summary>Human-readable place line as published by ANEPC.</summary>
    public required string Location { get; set; }

    /// <summary>Extra location detail (legacy detailLocation / endereco).</summary>
    public string? DetailLocation { get; set; }

    public string District { get; set; } = "";
    public string Concelho { get; set; } = "";
    public string? Freguesia { get; set; }

    /// <summary>Distrito+concelho code, zero-padded to 4 chars ("00" = Spain).</summary>
    public string Dico { get; set; } = "";

    public string? Region { get; set; }
    public string? SubRegion { get; set; }

    public GeoPoint? Coordinates { get; set; }

    // ── What ────────────────────────────────────────────────────────────────
    public required IncidentStatus Status { get; set; }
    public required IncidentKind Kind { get; set; }

    /// <summary>ANEPC natureza code (string; classification lives in NaturezaCatalog).</summary>
    public required string NaturezaCode { get; set; }

    /// <summary>Natureza display name as published.</summary>
    public string Natureza { get; set; } = "";

    public Resources Resources { get; set; } = Resources.Zero;

    public bool Active { get; set; }

    /// <summary>Flagged by the important-incident check (assets > 15, age > 3h, status 1–6).</summary>
    public bool Important { get; set; }

    /// <summary>Free-text extra info from the source feed.</summary>
    public string? Extra { get; set; }

    // ── Enrichment ──────────────────────────────────────────────────────────
    public IcnfData? Icnf { get; set; }

    /// <summary>ANEPC-provided KML perimeter (attached via operator mutation or ICNF download).</summary>
    public string? Kml { get; set; }

    /// <summary>VOST-curated KML variant.</summary>
    public string? KmlVost { get; set; }

    public int? NearestWeatherStationId { get; set; }

    /// <summary>Extra attributes only the ArcGIS source provides.</summary>
    public ArcGisDetails? ArcGis { get; set; }
}

/// <summary>ArcGIS OcorrenciasSite extra attributes, kept verbatim for the detail views.</summary>
public sealed record ArcGisDetails
{
    public string? EstadoAgrupado { get; init; }
    public string? FaseIncendio { get; init; }
    public bool? Rasi { get; init; }
    public int? DuracaoMinutos { get; init; }
    public DateTimeOffset? DataDosDados { get; init; }
}
