namespace Fogos.Infrastructure.Options;

/// <summary>One X/Twitter user-context credential set (OAuth 1.0a). All empty by default.</summary>
public sealed class TwitterAccount
{
    public string ConsumerKey { get; set; } = "";
    public string ConsumerSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string AccessTokenSecret { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConsumerKey) &&
        !string.IsNullOrWhiteSpace(ConsumerSecret) &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(AccessTokenSecret);
}

/// <summary>
/// X/Twitter options. The top-level fields are the main <c>fogospt</c> account; <see cref="Accounts"/>
/// is the hook for future account variants keyed by channel key (e.g. a VOST/Emergencias account).
/// </summary>
public sealed class TwitterOptions
{
    public const string SectionName = "Twitter";

    public string ConsumerKey { get; set; } = "";
    public string ConsumerSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string AccessTokenSecret { get; set; } = "";

    /// <summary>Additional accounts by channel key. The main channel ("twitter") uses the fields above.</summary>
    public Dictionary<string, TwitterAccount> Accounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Credential for a channel key; falls back to the main account.</summary>
    public TwitterAccount AccountFor(string channelKey)
    {
        if (!string.Equals(channelKey, "twitter", StringComparison.OrdinalIgnoreCase) &&
            Accounts.TryGetValue(channelKey, out var variant))
            return variant;

        return new TwitterAccount
        {
            ConsumerKey = ConsumerKey,
            ConsumerSecret = ConsumerSecret,
            AccessToken = AccessToken,
            AccessTokenSecret = AccessTokenSecret,
        };
    }
}

/// <summary>Telegram bot options. Empty by default.</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = "";

    /// <summary>Target chat/channel; the legacy main channel is <c>@fogospt</c>.</summary>
    public string ChatId { get; set; } = "@fogospt";
}

/// <summary>Facebook Graph API page options. Empty by default.</summary>
public sealed class FacebookOptions
{
    public const string SectionName = "Facebook";

    public string PageId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>Page access token (legacy <c>FACEBOOK_ACCESS_CODE</c>).</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>Graph API version segment (legacy used none; kept configurable, default empty = no version).</summary>
    public string ApiVersion { get; set; } = "";
}

/// <summary>Discord *posts* webhook (the public-facing posts channel, distinct from ops).</summary>
public sealed class DiscordPostOptions
{
    public const string SectionName = "DiscordPosts";

    public string WebhookUrl { get; set; } = "";
}
