namespace Fogos.Domain.Weather;

public enum WaveType
{
    Heat,
    Cold,
}

/// <summary>
/// Detected temperature wave (WMO 6-day rule). Upsert key (StationId, Type, StartDate).
/// </summary>
public sealed class TemperatureWave
{
    public string Id { get; set; } = "";
    public required int StationId { get; set; }
    public required WaveType Type { get; set; }
    public required DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Window still touching today/yesterday at last detection run.</summary>
    public bool Ongoing { get; set; }

    public List<WaveDay> Days { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One qualifying day: observed extreme vs the month's normal.</summary>
public sealed record WaveDay(DateOnly Date, double Observed, double Normal, double Deviation);
