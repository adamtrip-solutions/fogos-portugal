using Fogos.Domain.Time;
using Fogos.Infrastructure.Reads;
using HotChocolate;
using HotChocolate.Types;

namespace Fogos.Api.GraphQL.Types;

/// <summary>All <c>stats</c> fields. Windows are Lisbon calendar days via <see cref="IClock"/>.</summary>
[ExtendObjectType(typeof(Stats))]
public sealed class StatsExtensions
{
    public async Task<int> ActiveFires([Parent] Stats _, StatsReads reads, CancellationToken ct) =>
        await reads.ActiveCountAsync(fire: true, ct);

    public async Task<int> ActiveOther([Parent] Stats _, StatsReads reads, CancellationToken ct) =>
        await reads.ActiveCountAsync(fire: false, ct);

    public async Task<ResourceTotals?> Totals([Parent] Stats _, StatsReads reads, CancellationToken ct)
    {
        var t = await reads.LatestTotalsAsync(ct);
        return t is null ? null : new ResourceTotals(t.Man, t.Terrain, t.Aerial, t.Total, t.At);
    }

    public async Task<int> Today([Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct)
    {
        var start = DayStart(clock.LisbonToday, clock);
        var end = DayStart(clock.LisbonToday.AddDays(1), clock);
        return await reads.IgnitionCountAsync(start, end, ct);
    }

    public async Task<int> Yesterday([Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct)
    {
        var start = DayStart(clock.LisbonToday.AddDays(-1), clock);
        var end = DayStart(clock.LisbonToday, clock);
        return await reads.IgnitionCountAsync(start, end, ct);
    }

    /// <summary>Last 7 Lisbon calendar days, inclusive of today.</summary>
    public async Task<int> Week([Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct)
    {
        var start = DayStart(clock.LisbonToday.AddDays(-6), clock);
        var end = DayStart(clock.LisbonToday.AddDays(1), clock);
        return await reads.IgnitionCountAsync(start, end, ct);
    }

    /// <summary>Sum of ICNF burn area (ha) for fires ignited in the given year (default: current Lisbon year).</summary>
    public async Task<double?> BurnAreaTotalHa(
        [Parent] Stats _,
        StatsReads reads,
        IClock clock,
        CancellationToken ct,
        int? year = null)
    {
        var y = year ?? clock.LisbonToday.Year;
        var from = clock.FromLisbon(new DateTime(y, 1, 1, 0, 0, 0));
        var to = clock.FromLisbon(new DateTime(y + 1, 1, 1, 0, 0, 0));
        return await reads.BurnAreaTotalHaAsync(from, to, ct);
    }

    /// <summary>24 hourly ignition buckets for a Lisbon day (default: today).</summary>
    public async Task<IReadOnlyList<HourBucket>> IgnitionsHourly(
        [Parent] Stats _,
        StatsReads reads,
        IClock clock,
        CancellationToken ct,
        DateOnly? day = null)
    {
        var d = day ?? clock.LisbonToday;
        var from = DayStart(d, clock);
        var to = DayStart(d.AddDays(1), clock);
        var occurrences = await reads.IgnitionOccurredAtsAsync(from, to, ct);

        var counts = new int[24];
        foreach (var o in occurrences)
            counts[clock.ToLisbon(o).Hour]++;

        return Enumerable.Range(0, 24).Select(h => new HourBucket(h, counts[h])).ToList();
    }

    /// <summary>Fire ignitions per Lisbon day for a year, gaps filled with 0, up to today (past years: full year).</summary>
    public async Task<IReadOnlyList<DayCount>> IgnitionsByDay(
        [Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct, int year)
    {
        var rows = await reads.IgnitionsByDayAsync(YearFrom(year, clock), YearTo(year, clock), ct);
        var counts = rows.ToDictionary(r => r.Date, r => r.Count);
        var result = new List<DayCount>();
        foreach (var day in DaysOfYear(year, clock))
            result.Add(new DayCount(day, counts.GetValueOrDefault(day)));
        return result;
    }

    /// <summary>Cumulative accounted burn area (ha) by Lisbon day for a year, up to today.</summary>
    public async Task<IReadOnlyList<DayArea>> BurnAreaCumulative(
        [Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct, int year)
    {
        var rows = await reads.BurnAreaByDayAsync(YearFrom(year, clock), YearTo(year, clock), ct);
        var daily = rows.ToDictionary(r => r.Date, r => r.TotalHa);
        var result = new List<DayArea>();
        var cumulative = 0.0;
        foreach (var day in DaysOfYear(year, clock))
        {
            cumulative += daily.GetValueOrDefault(day);
            result.Add(new DayArea(day, cumulative));
        }
        return result;
    }

    /// <summary>Fire counts + accounted burn area grouped by ICNF cause family, count desc.</summary>
    public async Task<IReadOnlyList<CauseCount>> CauseBreakdown(
        [Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct, int year)
    {
        var rows = await reads.CauseBreakdownAsync(YearFrom(year, clock), YearTo(year, clock), ct);
        return rows.Select(r => new CauseCount(r.CauseFamily, r.Count, r.BurnAreaHa)).ToList();
    }

    /// <summary>Per-district false-alarm rate over all incident kinds (≥ 20 total to appear), rate desc.</summary>
    public async Task<IReadOnlyList<DistrictFalseAlarms>> FalseAlarmStats(
        [Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct, int year)
    {
        var rows = await reads.FalseAlarmStatsAsync(YearFrom(year, clock), YearTo(year, clock), minTotal: 20, ct);
        return rows
            .Select(r => new DistrictFalseAlarms(r.District, r.Total, r.FalseAlarms, r.Total == 0 ? 0 : (double)r.FalseAlarms / r.Total))
            .OrderByDescending(x => x.Rate)
            .ThenByDescending(x => x.Total)
            .ToList();
    }

    /// <summary>Median dispatch→arrival and arrival→control response times for a year, optionally per district.</summary>
    public async Task<ResponseTimeStats?> ResponseTimeStats(
        [Parent] Stats _, StatsReads reads, IClock clock, CancellationToken ct, int year, string? district = null)
    {
        var ids = await reads.FireIdsAsync(YearFrom(year, clock), YearTo(year, clock), district, ct);
        if (ids.Count == 0)
            return null;

        var pairs = await reads.ResponseTimePairsAsync(ids, ct);
        var dispatchToArrival = pairs.Where(p => p.DispatchToArrivalSeconds is not null).Select(p => p.DispatchToArrivalSeconds!.Value).ToList();
        var arrivalToControl = pairs.Where(p => p.ArrivalToControlSeconds is not null).Select(p => p.ArrivalToControlSeconds!.Value).ToList();
        return new ResponseTimeStats(pairs.Count, Median(dispatchToArrival), Median(arrivalToControl));
    }

    private static DateTimeOffset YearFrom(int year, IClock clock) => clock.FromLisbon(new DateTime(year, 1, 1, 0, 0, 0));

    private static DateTimeOffset YearTo(int year, IClock clock) => clock.FromLisbon(new DateTime(year + 1, 1, 1, 0, 0, 0));

    /// <summary>Jan 1 → min(Dec 31, today) of the year (past years: the whole year; current year: up to today).</summary>
    private static IEnumerable<DateOnly> DaysOfYear(int year, IClock clock)
    {
        var last = clock.LisbonToday < new DateOnly(year, 12, 31) ? clock.LisbonToday : new DateOnly(year, 12, 31);
        for (var day = new DateOnly(year, 1, 1); day <= last; day = day.AddDays(1))
            yield return day;
    }

    private static int? Median(List<int> values)
    {
        if (values.Count == 0)
            return null;
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (int)Math.Round((values[mid - 1] + values[mid]) / 2.0, MidpointRounding.AwayFromZero);
    }

    private static DateTimeOffset DayStart(DateOnly day, IClock clock) =>
        clock.FromLisbon(day.ToDateTime(TimeOnly.MinValue));
}
