namespace Fogos.Domain.Incidents;

/// <summary>
/// An immutable snapshot of an incident's KML perimeter, appended whenever the inline
/// <c>Incident.Kml</c> / <c>Incident.KmlVost</c> changes (deduplicated by <see cref="Sha256"/> per
/// incident+slot). The inline latest-wins fields stay authoritative; these rows are the history.
/// </summary>
public sealed class IncidentKmlVersion
{
    /// <summary>Surrogate ObjectId (string).</summary>
    public string Id { get; set; } = "";

    public required string IncidentId { get; set; }

    /// <summary>True for the VOST-curated slot, false for the ANEPC/ICNF slot.</summary>
    public bool Vost { get; set; }

    /// <summary>The raw KML document (never exposed via GraphQL — reached only through REST by id).</summary>
    public string Kml { get; set; } = "";

    /// <summary>Lower-case hex SHA-256 of the KML, used to dedup identical perimeters.</summary>
    public required string Sha256 { get; set; }

    public int SizeBytes { get; set; }

    public DateTimeOffset CapturedAt { get; set; }
}
