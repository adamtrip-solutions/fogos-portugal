namespace Fogos.Infrastructure.Options;

/// <summary>
/// Tunables for the incident-signals pipeline (escalation, rekindle, critical conditions). Defaults
/// match the WP1 spec; ops can override any of them under the <c>Signals</c> config section.
/// </summary>
public sealed class SignalsOptions
{
    public const string SectionName = "Signals";

    // ── Escalation window ───────────────────────────────────────────────────────
    /// <summary>Preferred age of the baseline snapshot (minutes before now).</summary>
    public int WindowTargetMinutes { get; set; } = 30;

    /// <summary>Minimum accepted baseline age.</summary>
    public int WindowMinMinutes { get; set; } = 25;

    /// <summary>Maximum accepted baseline age.</summary>
    public int WindowMaxMinutes { get; set; } = 90;

    /// <summary>Onset: assets must have grown by at least this factor (1.5 = +50%).</summary>
    public double EscalationGrowthFactor { get; set; } = 1.5;

    /// <summary>Onset: assets must also have grown by at least this absolute amount.</summary>
    public int EscalationAbsoluteGrowth { get; set; } = 10;

    /// <summary>Onset: aerial means jumping from 0 to at least this many also escalates.</summary>
    public int EscalationAerialThreshold { get; set; } = 2;

    /// <summary>Hysteresis: stays escalating while growth over the window is at least this factor (1.10 = +10%).</summary>
    public double HysteresisGrowthFactor { get; set; } = 1.10;

    /// <summary>Hysteresis: only de-escalate once this long has passed since detection.</summary>
    public int HysteresisMinMinutes { get; set; } = 30;

    // ── Rekindle (proximity) ────────────────────────────────────────────────────
    /// <summary>Proximity rekindle: a recently closed fire within this radius (km).</summary>
    public double ProximityKm { get; set; } = 5;

    /// <summary>Proximity rekindle: the prior fire's last status change must be within this many hours.</summary>
    public int ProximityWindowHours { get; set; } = 48;

    // ── Critical conditions (30-30-30) ──────────────────────────────────────────
    public double CriticalTempAbove { get; set; } = 30;
    public double CriticalHumidityBelow { get; set; } = 30;
    public double CriticalWindAbove { get; set; } = 30;

    /// <summary>Re-evaluate critical conditions only when the last evaluation is older than this.</summary>
    public int ConditionsEvalMinMinutes { get; set; } = 55;

    // ── Push ────────────────────────────────────────────────────────────────────
    /// <summary>Only send the escalation push when total assets reach this many.</summary>
    public int EscalationPushMinAssets { get; set; } = 50;

    // ── Ignition clustering ─────────────────────────────────────────────────────
    /// <summary>Single-linkage distance (km) that links two ignitions into the same cluster.</summary>
    public double ClusterLinkKm { get; set; } = 10;

    /// <summary>Minimum member count for a linked group to become (or stay) a cluster.</summary>
    public int ClusterMinSize { get; set; } = 3;

    /// <summary>
    /// Rolling window (hours): only fires ignited within this window are clustered, and a cluster whose
    /// latest member ignition is older than this deactivates.
    /// </summary>
    public int ClusterWindowHours { get; set; } = 6;
}
