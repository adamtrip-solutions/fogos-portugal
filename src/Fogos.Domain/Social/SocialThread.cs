namespace Fogos.Domain.Social;

/// <summary>
/// Per-incident social publishing state, moved off the incident document
/// (legacy lastTweetId / facebookPostId / sentCheckImportant / notifyBig fields on `data`).
/// `_id` = incident id.
/// </summary>
public sealed class SocialThread
{
    public required string IncidentId { get; set; }

    /// <summary>Last tweet in the incident's thread — replies chain onto this.</summary>
    public string? LastTweetId { get; set; }

    /// <summary>Facebook post id — status transitions are appended as comments.</summary>
    public string? FacebookPostId { get; set; }

    /// <summary>The "important incident" fan-out already fired for this incident.</summary>
    public bool SentImportantPost { get; set; }

    /// <summary>The "big incident" (man ≥ 100) fan-out already fired.</summary>
    public bool SentBigIncidentPost { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
