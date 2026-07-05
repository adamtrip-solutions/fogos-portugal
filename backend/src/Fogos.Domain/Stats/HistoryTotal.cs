namespace Fogos.Domain.Stats;

/// <summary>
/// Rolling nationwide resource totals over active fires (legacy `historyTotal`,
/// appended every 2 minutes only when the numbers changed).
/// </summary>
public sealed class HistoryTotal
{
    public string Id { get; set; } = "";
    public required DateTimeOffset At { get; set; }
    public int Man { get; set; }
    public int Terrain { get; set; }
    public int Aerial { get; set; }
    public int Total { get; set; }
}
