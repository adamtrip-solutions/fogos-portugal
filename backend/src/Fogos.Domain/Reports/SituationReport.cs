namespace Fogos.Domain.Reports;

/// <summary>
/// A twice-daily nationwide situation report composed from live data (active fires, mobilized means,
/// escalating count, recent warnings, ICNF burn area). Persisted, then announced via
/// <c>SituationReportCreated</c> for social fan-out and webhook delivery.
/// </summary>
public sealed class SituationReport
{
    /// <summary>Surrogate ObjectId (string).</summary>
    public string Id { get; set; } = "";

    public DateTimeOffset At { get; set; }

    /// <summary><c>morning</c> (09:00 Lisbon) or <c>evening</c> (20:00 Lisbon).</summary>
    public required string Slot { get; set; }

    /// <summary>The rendered report body (European Portuguese, markdown-ish plain text).</summary>
    public required string Body { get; set; }

    public int ActiveFires { get; set; }
    public int TotalMan { get; set; }
    public int TotalTerrain { get; set; }
    public int TotalAerial { get; set; }

    /// <summary>Top active fires by mobilized assets (ids), for linking from the report.</summary>
    public List<string> TopIncidentIds { get; set; } = [];
}
