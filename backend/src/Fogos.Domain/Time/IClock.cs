namespace Fogos.Domain.Time;

/// <summary>
/// All time in fogos flows through this. Storage is UTC; every human-facing or
/// stats-window computation is Lisbon-local. Never use DateTime.Now / UtcNow directly.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current wall-clock time in Europe/Lisbon.</summary>
    DateTimeOffset LisbonNow { get; }

    /// <summary>Today's date in Europe/Lisbon (stats windows, "yesterday", cron semantics).</summary>
    DateOnly LisbonToday { get; }

    /// <summary>Interprets a naive local timestamp (e.g. parsed from an IPMA/ANEPC feed) as Lisbon time → UTC.</summary>
    DateTimeOffset FromLisbon(DateTime naiveLocal);

    /// <summary>Converts a UTC instant to Lisbon wall-clock time.</summary>
    DateTimeOffset ToLisbon(DateTimeOffset utc);
}

public sealed class FogosClock : IClock
{
    public static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LisbonNow => TimeZoneInfo.ConvertTime(UtcNow, Lisbon);

    public DateOnly LisbonToday => DateOnly.FromDateTime(LisbonNow.Date);

    public DateTimeOffset FromLisbon(DateTime naiveLocal)
    {
        var unspecified = DateTime.SpecifyKind(naiveLocal, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Lisbon.GetUtcOffset(unspecified));
    }

    public DateTimeOffset ToLisbon(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Lisbon);
}
