using System.Net.Http.Json;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Posts to the Discord *posts* webhook (the public posts channel — separate from the ops/dry-run
/// webhooks owned by <see cref="IOpsNotifier"/>). Sends a plain <c>content</c> payload.
/// </summary>
public sealed class DiscordPostPublisher(
    IHttpClientFactory httpFactory,
    IOptions<PublishingOptions> publishing,
    IOptions<DiscordPostOptions> discord,
    IOpsNotifier ops,
    ILogger<DiscordPostPublisher> logger) : SocialPublisherBase(publishing, ops, logger), IDiscordPostPublisher
{
    public const string HttpClientName = "discord-posts";

    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "discordPosts", CancellationToken ct = default) =>
        PublishCoreAsync(channelKey, post, ct);

    protected override async Task<PublishResult> SendAsync(SocialPost post, string channelKey, CancellationToken ct)
    {
        var webhook = discord.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhook))
            return await FailAsync("Discord posts publish skipped: webhook not configured.", ct);

        var client = httpFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync(webhook, new { content = post.Text }, ct);
        if (!response.IsSuccessStatusCode)
            return await FailAsync($"Discord posts webhook returned {(int)response.StatusCode}", ct);

        return PublishResult.Ok(null);
    }
}
