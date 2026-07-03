namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// A channel-agnostic post request. Each publisher reads what it understands and ignores the rest.
/// Exactly one of <see cref="ImagePath"/> / <see cref="ImageBytes"/> is used when an image is present
/// (bytes win when both are set).
/// </summary>
public sealed record SocialPost
{
    public required string Text { get; init; }

    /// <summary>Local PNG path (e.g. a renderer capture written to disk).</summary>
    public string? ImagePath { get; init; }

    /// <summary>In-memory image bytes (renderer client returns these directly).</summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>External id to reply/thread onto (Twitter <c>in_reply_to_tweet_id</c>).</summary>
    public string? ReplyToId { get; init; }

    /// <summary>Telegram <c>message_thread_id</c> for forum-style channels (optional).</summary>
    public string? TelegramThreadId { get; init; }

    public bool HasImage => ImageBytes is { Length: > 0 } || !string.IsNullOrWhiteSpace(ImagePath);
}
