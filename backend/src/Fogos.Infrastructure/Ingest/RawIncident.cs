using Fogos.Domain.Incidents;

namespace Fogos.Infrastructure.Ingest;

/// <summary>
/// A source-agnostic, normalized attribute bag for one occurrence. Every <see cref="IIncidentSource"/>
/// (ArcGIS primary, ANEPC fallback, ICNF new-fire) produces this shape; <see cref="IncidentIngestService"/>
/// resolves location, maps to the canonical <see cref="Incident"/>, and diffs it against the stored doc.
/// Casing/dico/status normalization happens downstream so the sources stay thin adapters.
/// </summary>
public sealed record RawIncident
{
    /// <summary>Business id (ANEPC numero_sado / ICNF ncco).</summary>
    public required string Id { get; init; }

    /// <summary>Occurrence start instant (UTC), already parsed from the source's epoch/string form.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Overrides the initial status-history stamp written on insert (defaults to ingestion time when null).
    /// Set by the ICNF new-fire job for fires that arrive already extinguished, so the timeline — and the
    /// map-safety window keyed off <c>statusChangedAt</c> — reflects the real extinction time, not "now".
    /// </summary>
    public DateTimeOffset? ObservedStatusAt { get; init; }

    /// <summary>Raw ANEPC natureza code as a string (feed sends an int; we never do arithmetic on it).</summary>
    public string NaturezaCode { get; init; } = "";

    /// <summary>Natureza display name (the part after " - " for ArcGIS; abreviatura_natureza for ANEPC).</summary>
    public string Natureza { get; init; } = "";

    /// <summary>Inbound status label (normalized to a canonical code by <see cref="IncidentStatusCatalog"/>).</summary>
    public string StatusLabel { get; init; } = "";

    // ── Where (raw, resolved by LocationResolver) ─────────────────────────────
    /// <summary>Raw concelho name as published (looked up against the <c>locations</c> table verbatim).</summary>
    public string Concelho { get; init; } = "";

    public string? Freguesia { get; init; }

    /// <summary>Local + address line (legacy localidade).</summary>
    public string? Localidade { get; init; }

    /// <summary>ANEPC Spain special case: outra_localizacao == "Espanha" → DICO "00".</summary>
    public bool SpainOverride { get; init; }

    /// <summary>ICNF supplies district + DICO directly (INE); skip the concelho→level-2→level-1 lookup.</summary>
    public string? PreResolvedDistrict { get; init; }

    /// <summary>Canonical 4-char DICO (ICNF INE normalized upstream) used directly when <see cref="PreResolvedDistrict"/> is set.</summary>
    public string? PreResolvedDico { get; init; }

    public double? Lat { get; init; }
    public double? Lng { get; init; }

    public Resources Resources { get; init; } = Resources.Zero;

    public string? Region { get; init; }
    public string? SubRegion { get; init; }

    // ── ArcGIS-only extras ────────────────────────────────────────────────────
    public string? EstadoAgrupado { get; init; }
    public string? FaseIncendio { get; init; }
    public bool? Rasi { get; init; }
    public int? DuracaoMinutos { get; init; }
    public string? Endereco { get; init; }
    public DateTimeOffset? DataDosDados { get; init; }
}
