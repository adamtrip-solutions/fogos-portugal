namespace Fogos.Infrastructure.Publishing;

/// <summary>Posts to X/Twitter with thread-splitting and reply chaining. Channel key defaults to "twitter".</summary>
public interface ITwitterPublisher
{
    Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "twitter", CancellationToken ct = default);
}

/// <summary>Posts to a Telegram chat/channel via the bot API.</summary>
public interface ITelegramPublisher
{
    Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "telegram", CancellationToken ct = default);
}

/// <summary>Posts to a Facebook page (feed/photo) and comments on an existing post.</summary>
public interface IFacebookPublisher
{
    Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "facebook", CancellationToken ct = default);

    /// <summary>Comment on a stored post id (used to document incident status transitions).</summary>
    Task<PublishResult> CommentOnPostAsync(string postId, string message, string channelKey = "facebook", CancellationToken ct = default);
}

/// <summary>Posts to the Discord *posts* webhook (public posts channel, distinct from ops alerting).</summary>
public interface IDiscordPostPublisher
{
    Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "discordPosts", CancellationToken ct = default);
}
