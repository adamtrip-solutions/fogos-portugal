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

    private static DateTimeOffset DayStart(DateOnly day, IClock clock) =>
        clock.FromLisbon(day.ToDateTime(TimeOnly.MinValue));
}
