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
    [InlineData(13, 5, true)] // feed-drop close-out that revives as Em Curso is a rekindle
    [InlineData(5, 5, false)]
    [InlineData(6, 5, false)]
    [InlineData(10, 5, false)]
    [InlineData(3, 5, false)]
    [InlineData(13, 6, false)] // reviving to a non-EmCurso code is not a status regression
    [InlineData(7, 6, false)]
    [InlineData(8, 7, false)]
    public void Status_regression_matrix(int from, int to, bool expected) =>
        Assert.Equal(expected, SignalRules.IsStatusRegression(from, to));

    // ── Demobilization (zero-personnel streak) ───────────────────────────────────

    [Fact]
    public void Demobilized_is_null_when_currently_manned()
    {
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-3), 20),
            (Now.AddHours(-1), 8),
        ];
        Assert.Null(SignalRules.DemobilizedSince(history, currentMan: 8, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_is_null_when_man_is_unknown_minus_one()
    {
        // Missing data (the -1 sentinel) must never read as demobilized, even with prior zero snapshots.
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-3), 20),
            (Now.AddHours(-2), 0),
        ];
        Assert.Null(SignalRules.DemobilizedSince(history, currentMan: -1, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_is_transition_time_when_man_drops_to_zero()
    {
        // Manned until 2h ago, then zero: the streak starts at the first zero snapshot.
        var transition = Now.AddHours(-2);
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-4), 15),
            (Now.AddHours(-3), 6),
            (transition, 0),
            (Now.AddHours(-1), 0),
        ];
        Assert.Equal(transition, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_transition_dates_from_unknown_to_zero()
    {
        // A jump from unknown (-1) straight to 0 is a transition; the streak starts at the first zero.
        var transition = Now.AddHours(-2);
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-4), -1),
            (transition, 0),
        ];
        Assert.Equal(transition, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_falls_back_to_first_record_when_zero_from_the_start()
    {
        var first = Now.AddHours(-5);
        List<(DateTimeOffset At, int Man)> history =
        [
            (first, 0),
            (Now.AddHours(-2), 0),
        ];
        Assert.Equal(first, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_single_zero_record_dates_from_that_record()
    {
        var only = Now.AddHours(-6);
        List<(DateTimeOffset At, int Man)> history = [(only, 0)];
        Assert.Equal(only, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_falls_back_when_history_is_empty()
    {
        // Current man is 0 but no history to date it → the conservative fallback (e.g. updatedAt).
        List<(DateTimeOffset At, int Man)> history = [];
        Assert.Equal(Now, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_falls_back_when_newest_snapshot_is_non_zero()
    {
        // Resources say 0 now, but the latest snapshot still shows crews: the drop post-dates the series,
        // so we cannot date it from history and use the conservative fallback.
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-3), 12),
            (Now.AddHours(-1), 4),
        ];
        Assert.Equal(Now, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_resets_the_streak_after_re_manning()
    {
        // Zero early, re-manned, then zero again: the streak dates from the SECOND (current) drop.
        var secondDrop = Now.AddHours(-1);
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-6), 0),   // earlier zero streak
            (Now.AddHours(-4), 18),  // re-manned — resets the streak
            (Now.AddHours(-2), 9),
            (secondDrop, 0),         // current streak starts here
        ];
        Assert.Equal(secondDrop, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

    [Fact]
    public void Demobilized_tolerates_unsorted_history()
    {
        var transition = Now.AddHours(-2);
        List<(DateTimeOffset At, int Man)> history =
        [
            (Now.AddHours(-1), 0),
            (Now.AddHours(-4), 15),
            (transition, 0),
        ];
        Assert.Equal(transition, SignalRules.DemobilizedSince(history, currentMan: 0, fallbackAt: Now));
    }

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
