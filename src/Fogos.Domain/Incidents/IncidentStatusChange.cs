namespace Fogos.Domain.Incidents;

/// <summary>Status transition log per incident (legacy `statusHistory` collection).</summary>
public sealed class IncidentStatusChange
{
    public string Id { get; set; } = "";
    public required string IncidentId { get; set; }
    public required DateTimeOffset At { get; set; }
    public required int Code { get; set; }
    public required string Label { get; set; }
}
