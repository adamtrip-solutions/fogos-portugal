using Fogos.Domain.Incidents;

namespace Fogos.Domain.Tests;

public class SignalRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Two snapshots: a baseline aged <paramref name="baselineAgeMin"/> and a latest at now.</summary>
    private static List<(DateTimeOffset At, int Assets, int Aerial)> Series(
        int baselineAgeMin, int baselineAssets, int baselineAerial, int latestAssets, int latestAerial) =>
    [
        (Now.AddMinutes(-baselineAgeMin), baselineAssets, baselineAerial),
        (Now, latestAssets, latestAerial),
    ];

    // ── Escalation onset ─────────────────────────────────────────────────────────

    [Fact]
    public void Escalation_onset_when_assets_grow_by_50pct_and_10_absolute()
    {
        var series = Series(baselineAgeMin: 40, baselineAssets: 20, baselineAerial: 0, latestAssets: 30, latestAerial: 0);
        Assert.True(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void No_escalation_when_growth_meets_factor_but_not_absolute_threshold()
    {
        // 10 → 15 is 1.5x but only +5 absolute (< 10).
        var series = Series(40, 10, 0, 15, 0);
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void No_escalation_when_growth_meets_absolute_but_not_factor()
    {
        // 30 → 44 is +14 absolute but only 1.467x (< 1.5).
        var series = Series(40, 30, 0, 44, 0);
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void Escalation_onset_when_aerial_jumps_from_zero_to_two()
    {
        // Assets barely move, but aerial 0 → 2 escalates.
        var series = Series(40, 8, 0, 9, 2);
        Assert.True(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void No_escalation_when_aerial_jumps_to_one_only()
    {
        var series = Series(40, 8, 0, 9, 1);
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    // ── Window selection edges ───────────────────────────────────────────────────

    [Fact]
    public void No_baseline_in_window_means_not_escalating()
    {
        // Only a 15-min-old baseline (below the 25-min floor) → no valid window.
        var series = Series(15, 10, 0, 40, 0);
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void Baseline_older_than_max_is_ignored()
    {
        var series = Series(120, 10, 0, 40, 0);
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void Single_snapshot_is_not_escalating()
    {
        List<(DateTimeOffset, int, int)> series = [(Now, 100, 5)];
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    [Fact]
    public void Window_prefers_the_snapshot_closest_to_thirty_minutes()
    {
        // Baselines at 28 min (assets 30) and 80 min (assets 5); 28-min is closer to target.
        List<(DateTimeOffset At, int Assets, int Aerial)> series =
        [
            (Now.AddMinutes(-80), 5, 0),
            (Now.AddMinutes(-28), 30, 0),
            (Now, 33, 0),
        ];
        // Against the 28-min baseline (30 → 33): not escalating (only +3). If it wrongly picked the
        // 80-min baseline (5 → 33) it would escalate.
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: false));
    }

    // ── Hysteresis ───────────────────────────────────────────────────────────────

    [Fact]
    public void Stays_escalating_while_growth_at_least_10pct()
    {
        var series = Series(40, 100, 0, 111, 0); // 1.11x ≥ 1.10
        Assert.True(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: true));
    }

    [Fact]
    public void Declassifies_when_growth_below_10pct()
    {
        var series = Series(40, 100, 0, 109, 0); // 1.09x < 1.10
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: true));
    }

    [Fact]
    public void Flat_activity_declassifies_when_already_escalating()
    {
        var series = Series(40, 100, 0, 100, 0);
        Assert.False(SignalRules.EvaluateEscalation(series, Now, currentlyEscalating: true));
    }

    // ── Status regression ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(7, 5, true)]
    [InlineData(8, 5, true)]
    [InlineData(9, 5, true)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, false)]
    [InlineData(10, 5, false)]
    [InlineData(3, 5, false)]
    [InlineData(7, 6, false)]
    [InlineData(8, 7, false)]
    public void Status_regression_matrix(int from, int to, bool expected) =>
        Assert.Equal(expected, SignalRules.IsStatusRegression(from, to));

    // ── 30-30-30 critical conditions ─────────────────────────────────────────────

    [Fact]
    public void All_three_conditions_are_critical()
    {
        var r = SignalRules.EvaluateCriticalConditions(35, 20, 40, riskLevel: null, heatWaveOngoing: false);
        Assert.True(r.Critical);
        Assert.Equal(
            new[] { SignalRules.TempAbove30, SignalRules.HumidityBelow30, SignalRules.WindAbove30 },
            r.Reasons);
    }

    [Fact]
    public void Exactly_two_conditions_are_critical()
    {
        var r = SignalRules.EvaluateCriticalConditions(35, 20, 10, riskLevel: null, heatWaveOngoing: false);
        Assert.True(r.Critical);
        Assert.Equal(new[] { SignalRules.TempAbove30, SignalRules.HumidityBelow30 }, r.Reasons);
    }

    [Fact]
    public void One_condition_is_not_critical_but_key_is_recorded()
    {
        var r = SignalRules.EvaluateCriticalConditions(35, 50, 10, riskLevel: null, heatWaveOngoing: false);
        Assert.False(r.Critical);
        Assert.Equal(new[] { SignalRules.TempAbove30 }, r.Reasons);
    }

    [Fact]
    public void Boundary_values_do_not_count_thirty_is_not_above_thirty()
    {
        var r = SignalRules.EvaluateCriticalConditions(30, 30, 30, riskLevel: null, heatWaveOngoing: false);
        Assert.False(r.Critical);
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public void All_null_inputs_are_not_critical()
    {
        var r = SignalRules.EvaluateCriticalConditions(null, null, null, riskLevel: null, heatWaveOngoing: false);
        Assert.False(r.Critical);
        Assert.Empty(r.Reasons);
    }

    [Fact]
    public void Maximum_risk_is_context_only_and_does_not_set_the_flag()
    {
        var r = SignalRules.EvaluateCriticalConditions(35, 50, 10, riskLevel: 5, heatWaveOngoing: false);
        Assert.False(r.Critical); // only temp holds (1 of 3)
        Assert.Equal(new[] { SignalRules.TempAbove30, SignalRules.RiskMaximum }, r.Reasons);
    }

    [Fact]
    public void Heat_wave_is_context_only_and_does_not_set_the_flag()
    {
        var r = SignalRules.EvaluateCriticalConditions(null, null, null, riskLevel: null, heatWaveOngoing: true);
        Assert.False(r.Critical);
        Assert.Equal(new[] { SignalRules.HeatWave }, r.Reasons);
    }

    [Fact]
    public void Context_keys_accompany_a_critical_flag_when_conditions_also_hold()
    {
        var r = SignalRules.EvaluateCriticalConditions(35, 20, 40, riskLevel: 5, heatWaveOngoing: true);
        Assert.True(r.Critical);
        Assert.Equal(
            new[]
            {
                SignalRules.TempAbove30, SignalRules.HumidityBelow30, SignalRules.WindAbove30,
                SignalRules.RiskMaximum, SignalRules.HeatWave,
            },
            r.Reasons);
    }
}
