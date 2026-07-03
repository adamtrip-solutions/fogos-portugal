using Fogos.Domain.Geo;

namespace Fogos.Domain.Photos;

public enum ModerationStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>Citizen-submitted incident photo. The binary lives in object storage under <see cref="StorageKey"/>.</summary>
public sealed class IncidentPhoto
{
    public string Id { get; set; } = "";
    public required string IncidentId { get; set; }

    public ModerationStatus Status { get; set; } = ModerationStatus.Pending;

    /// <summary>Approved photos may still be held back from public listings.</summary>
    public bool Public { get; set; }

    /// <summary>Content signature (dedup).</summary>
    public string? Signature { get; set; }

    /// <summary>Object-storage key — never a full URL; the public base is configuration.</summary>
    public required string StorageKey { get; set; }

    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Mime { get; set; } = "image/jpeg";

    /// <summary>EXIF GPS — required at upload (422 without it).</summary>
    public GeoPoint? Gps { get; set; }
    public double? Altitude { get; set; }
    public double? Heading { get; set; }

    public DateTimeOffset? TakenAt { get; set; }

    /// <summary>Uploading client identifier (app/web).</summary>
    public string? Client { get; set; }

    public PhotoModeration? Moderation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record PhotoModeration(DateTimeOffset At, string Decision, string? Reason);
