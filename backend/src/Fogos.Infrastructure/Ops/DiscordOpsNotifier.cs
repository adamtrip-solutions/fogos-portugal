using System.Net.Http.Json;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Ops;

/// <summary>
/// Posts operational messages to Discord webhooks. No-ops silently when a webhook is not
/// configured (errors fall back from the errors webhook to the general one). Never throws.
/// </summary>
public sealed class DiscordOpsNotifier(
    IHttpClientFactory httpFactory,
    IOptions<OpsOptions> options,
    ILogger<DiscordOpsNotifier> logger) : IOpsNotifier
{
    public const string HttpClientName = "discord-ops";
    private const int MaxContentLength = 1900;

    private OpsOptions Options => options.Value;

    public Task InfoAsync(string message, CancellationToken ct = default) =>
        PostAsync(Options.DiscordGeneralWebhook, message, ct);

    public Task ErrorAsync(string message, CancellationToken ct = default) =>
        PostAsync(Options.DiscordErrorsWebhook ?? Options.DiscordGeneralWebhook, message, ct);

    public Task DryRunCaptureAsync(string channel, string payload, CancellationToken ct = default) =>
        PostAsync(Options.DiscordDryRunWebhook, $"[{channel}] {payload}", ct);

    private async Task PostAsync(string? webhook, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webhook))
            return;

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(webhook, new { content = Truncate(content) }, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Discord ops webhook returned {StatusCode}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to post to Discord ops webhook");
        }
    }

    private static string Truncate(string content) =>
        content.Length <= MaxContentLength ? content : content[..MaxContentLength];
}
