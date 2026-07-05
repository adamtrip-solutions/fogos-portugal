namespace Fogos.Domain.Aircraft;

/// <summary>Firefighting fleet whitelist (legacy `tracked_aircraft`). `_id` = ICAO hex.</summary>
public sealed class TrackedAircraft
{
    public required string Icao { get; set; }
    public required string Registration { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }

    /// <summary>plane / helicopter.</summary>
    public string? Kind { get; set; }

    public string? Base { get; set; }
    public string? Operator { get; set; }

    /// <summary>Post first sighting of the day to social.</summary>
    public bool Notify { get; set; }

    public bool Active { get; set; } = true;
}
