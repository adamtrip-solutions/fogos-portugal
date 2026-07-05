namespace Fogos.Domain.Incidents;

/// <summary>
/// Pure, side-effect-free domain rules for incident signals: escalation detection (with hysteresis),
/// status-regression rekindle, and the 30-30-30 critical-conditions rule. No I/O, no clock — every
/// input is passed in so the logic is fully unit-testable. The signals pipeline (Worker) owns the
/// stored state and the time gate around hysteresis; this type owns the maths.
/// </summary>
public static class SignalRules
{
    // ── Critical-conditions machine keys ───────────────────────────────────────
    public const string TempAbove30 = "TEMP_ABOVE_30";
    public const string HumidityBelow30 = "HUMIDITY_BELOW_30";
    public const string WindAbove30 = "WIND_ABOVE_30";
    public const string RiskMaximum = "RISK_MAXIMUM";
    public const string HeatWave = "HEAT_WAVE";

    /// <summary>RCM fire-risk level that raises the <see cref="RiskMaximum"/> context key.</summary>
    public const int MaximumRiskLevel = 5;

    /// <summary>
    /// Status codes a fire must be leaving to count as a status-regression rekindle. Includes the
    /// feed-drop terminal (13): a fire closed out for going quiet that reappears in the feed as "Em Curso"
    /// is a genuine revival and must register as a rekindle-by-status.
    /// </summary>
    private static readonly IReadOnlySet<int> RegressionFromCodes =
        new HashSet<int>
        {
            IncidentStatusCatalog.EmResolucao,
            IncidentStatusCatalog.Conclusao,
            IncidentStatusCatalog.Vigilancia,
            IncidentStatusCatalog.EncerradaSemAtualizacao,
        };

    /// <summary>Tunable escalation thresholds (defaults per spec; the job binds these from options).</summary>
    public sealed record EscalationThresholds(
        int WindowTargetMinutes = 30,
        int WindowMinMinutes = 25,
        int WindowMaxMinutes = 90,
        double GrowthFactor = 1.5,
        int AbsoluteGrowth = 10,
        int AerialThreshold = 2,
        double HysteresisGrowthFactor = 1.10)
    {
        public static readonly EscalationThresholds Default = new();
    }

    /// <summary>Tunable 30-30-30 thresholds (defaults per spec).</summary>
    public sealed record CriticalThresholds(
        double TempAbove = 30,
        double HumidityBelow = 30,
        double WindAbove = 30)
    {
        public static readonly CriticalThresholds Default = new();
    }

    /// <summary>The two snapshots the escalation comparison runs over.</summary>
    public readonly record struct EscalationWindow(int BaselineAssets, int BaselineAerial, int LatestAssets, int LatestAerial);

    /// <summary>Result of the 30-30-30 evaluation.</summary>
    public readonly record struct CriticalConditionsResult(bool Critical, IReadOnlyList<string> Reasons);

    /// <summary>
    /// Selects the escalation comparison window: the newest snapshot against the snapshot whose age is
    /// closest to the target (30 min), accepting only baselines aged in [min, max] (25–90 min). Returns
    /// null when there is no snapshot in range (or fewer than two snapshots).
    /// </summary>
    public static EscalationWindow? SelectWindow(
        IReadOnlyList<(DateTimeOffset At, int Assets, int Aerial)> history,
        DateTimeOffset now,
        EscalationThresholds? thresholds = null)
    {
        var t = thresholds ?? EscalationThresholds.Default;
        if (history.Count < 2)
            return null;

        var latestIndex = 0;
        for (var i = 1; i < history.Count; i++)
            if (history[i].At > history[latestIndex].At)
                latestIndex = i;
        var latest = history[latestIndex];

        var target = TimeSpan.FromMinutes(t.WindowTargetMinutes);
        var min = TimeSpan.FromMinutes(t.WindowMinMinutes);
        var max = TimeSpan.FromMinutes(t.WindowMaxMinutes);

        (DateTimeOffset At, int Assets, int Aerial)? baseline = null;
        var bestDelta = TimeSpan.MaxValue;
        for (var i = 0; i < history.Count; i++)
        {
            if (i == latestIndex)
                continue;
            var s = history[i];
            var age = now - s.At;
            if (age < min || age > max)
                continue;
            var delta = (age - target).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                baseline = s;
            }
        }

        return baseline is { } b
            ? new EscalationWindow(b.Assets, b.Aerial, latest.Assets, latest.Aerial)
            : null;
    }

    /// <summary>
    /// Whether the incident is escalating. Onset (from not-escalating): assets grew by ≥50% AND by ≥10
    /// absolute over the window, OR aerial went from 0 to ≥2. While already escalating, stays true until
    /// growth over the same window falls below 10% (the additional "30 min since detected" gate is
    /// applied by the caller, which holds the stored <c>EscalationDetectedAt</c>).
    /// </summary>
    public static bool EvaluateEscalation(
        IReadOnlyList<(DateTimeOffset At, int Assets, int Aerial)> history,
        DateTimeOffset now,
        bool currentlyEscalating,
        EscalationThresholds? thresholds = null)
    {
        var t = thresholds ?? EscalationThresholds.Default;
        if (SelectWindow(history, now, t) is not { } w)
            return false;

        if (currentlyEscalating)
            return w.LatestAssets >= w.BaselineAssets * t.HysteresisGrowthFactor && w.LatestAssets > w.BaselineAssets;

        var assetsGrew = w.LatestAssets >= w.BaselineAssets * t.GrowthFactor
                         && w.LatestAssets - w.BaselineAssets >= t.AbsoluteGrowth;
        var aerialJump = w.BaselineAerial == 0 && w.LatestAerial >= t.AerialThreshold;
        return assetsGrew || aerialJump;
    }

    /// <summary>A fire dropping back to "Em Curso" from a winding-down state (7/8/9 → 5) is a rekindle.</summary>
    public static bool IsStatusRegression(int from, int to) =>
        to == IncidentStatusCatalog.EmCurso && RegressionFromCodes.Contains(from);

    /// <summary>
    /// The 30-30-30 rule. Each of temp&gt;30, humidity&lt;30, wind&gt;30 that holds adds its key;
    /// the flag is set when ≥2 of the three hold. <see cref="RiskMaximum"/> (risk level 5) and
    /// <see cref="HeatWave"/> are context keys that never set the flag on their own. All-null inputs
    /// (and no context) → false with no reasons.
    /// </summary>
    public static CriticalConditionsResult EvaluateCriticalConditions(
        double? temperature,
        double? humidity,
        double? windKmh,
        int? riskLevel,
        bool heatWaveOngoing,
        CriticalThresholds? thresholds = null)
    {
        var t = thresholds ?? CriticalThresholds.Default;
        var reasons = new List<string>(5);
        var held = 0;

        if (temperature is { } temp && temp > t.TempAbove)
        {
            reasons.Add(TempAbove30);
            held++;
        }
        if (humidity is { } hum && hum < t.HumidityBelow)
        {
            reasons.Add(HumidityBelow30);
            held++;
        }
        if (windKmh is { } wind && wind > t.WindAbove)
        {
            reasons.Add(WindAbove30);
            held++;
        }
        if (riskLevel is MaximumRiskLevel)
            reasons.Add(RiskMaximum);
        if (heatWaveOngoing)
            reasons.Add(HeatWave);

        return new CriticalConditionsResult(held >= 2, reasons);
    }
}
