namespace Fogos.Domain.Incidents;

/// <summary>Resource time series per incident (legacy `history` collection).</summary>
public sealed class IncidentHistorySnapshot
{
    public string Id { get; set; } = "";
    public required string IncidentId { get; set; }
    public required DateTimeOffset At { get; set; }
    public int Man { get; set; }
    public int Terrain { get; set; }
    public int Aerial { get; set; }
    public string? Location { get; set; }
}
