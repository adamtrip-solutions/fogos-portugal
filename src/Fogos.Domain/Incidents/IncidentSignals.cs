namespace Fogos.Domain.Incidents;

/// <summary>
/// Derived escalation / rekindle / critical-conditions signals for an incident. Embedded on
/// <see cref="Incident"/> as <c>Signals</c> (nullable — an absent value is treated as all-false
/// defaults on the read side). Updated in place with targeted <c>$set</c>s by the signals pipeline;
/// the document is never rewritten wholesale.
/// </summary>
public sealed class IncidentSignals
{
    /// <summary>Means are growing rapidly over the last ~30 min window (with hysteresis).</summary>
    public bool Escalating { get; set; }

    /// <summary>When the current escalation was first detected (drives hysteresis).</summary>
    public DateTimeOffset? EscalationDetectedAt { get; set; }

    /// <summary>Running max of <see cref="Resources.TotalAssets"/> ever seen for this incident.</summary>
    public int? PeakAssets { get; set; }

    /// <summary>Flagged as a rekindle (status regression or proximity to a recently closed fire).</summary>
    public bool Rekindle { get; set; }

    /// <summary>Prior incident id this rekindle references (proximity-based only; null for status regression).</summary>
    public string? RekindleOfId { get; set; }

    /// <summary>
    /// Which rekindle triggers have already fired for this incident (<c>STATUS_REGRESSION</c> /
    /// <c>PROXIMITY</c>). Claimed per-kind so each trigger dispatches at most once while the other kind
    /// can still fire independently. Internal — never exposed on the GraphQL surface.
    /// </summary>
    public List<string> RekindleKinds { get; set; } = [];

    public DateTimeOffset? RekindleDetectedAt { get; set; }

    /// <summary>The 30-30-30 rule holds (≥2 of temp&gt;30 / humidity&lt;30 / wind&gt;30).</summary>
    public bool CriticalConditions { get; set; }

    /// <summary>Machine keys of the conditions present (see <see cref="SignalRules"/>).</summary>
    public List<string> CriticalReasons { get; set; } = [];

    public DateTimeOffset? ConditionsEvaluatedAt { get; set; }
}
