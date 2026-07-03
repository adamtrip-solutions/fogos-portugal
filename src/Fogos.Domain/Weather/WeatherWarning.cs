namespace Fogos.Domain.Weather;

/// <summary>IPMA awareness warning (scraped). Dedup by <see cref="Control"/> hash.</summary>
public sealed class WeatherWarning
{
    public string Id { get; set; } = "";

    /// <summary>IPMA area code (district-level idAreaAviso).</summary>
    public required string AreaCode { get; set; }

    /// <summary>Warning subject (e.g. "Agitação Marítima", "Tempo Quente").</summary>
    public required string AwarenessType { get; set; }

    /// <summary>yellow / orange / red as IPMA publishes them.</summary>
    public required string Level { get; set; }

    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    public string? Text { get; set; }

    /// <summary>Content hash used for idempotent ingest (legacy md5 `control`).</summary>
    public required string Control { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    private static readonly Dictionary<string, string> LevelsPt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = "Amarelo",
        ["orange"] = "Laranja",
        ["red"] = "Vermelho",
    };

    public string LevelPt => LevelsPt.GetValueOrDefault(Level, Level);
}
