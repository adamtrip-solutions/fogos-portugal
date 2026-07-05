using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Publishing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// Delivers push notifications via FCM topic conditions. Honours the "fcm" publisher mode
/// (Off / DryRun / On), prepends the title prefix, applies the environment topic prefix, and splits
/// large topic sets across multiple ≤5-topic conditions. Never throws.
/// </summary>
public sealed class FcmNotifier(
    IFcmSender sender,
    IOptions<PublishingOptions> publishing,
    IOptions<FcmOptions> fcmOptions,
    IOpsNotifier ops,
    IHostEnvironment environment,
    ILogger<FcmNotifier> logger)
{
    public const string ChannelKey = "fcm";

    /// <summary>Empty in production, <c>dev-</c> elsewhere (mirrors the legacy topic prefix).</summary>
    public string Prefix => environment.IsProduction() ? "" : "dev-";

    /// <summary>Topic helpers bound to the current prefix and legacy-topics setting.</summary>
    public FcmTopics Topics => new(Prefix, fcmOptions.Value.LegacyTopicsEnabled);

    /// <summary>Send a notification to the union of <paramref name="topics"/>, batched into ≤5-topic conditions.</summary>
    public async Task<bool> SendNotificationAsync(
        string title,
        string body,
        IReadOnlyList<string> topics,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        if (topics.Count == 0)
            return true;

        var mode = publishing.Value.ModeFor(ChannelKey);
        if (mode == PublisherMode.Off)
            return true;

        var fullTitle = fcmOptions.Value.TitlePrefix + title;
        var conditions = FcmTopics.ChunkConditions(topics);

        if (mode == PublisherMode.DryRun)
        {
            foreach (var condition in conditions)
                await ops.DryRunCaptureAsync(ChannelKey, $"[{fullTitle}] {body} :: {condition}", ct);
            return true;
        }

        foreach (var condition in conditions)
        {
            try
            {
                await sender.SendAsync(new FcmSend(condition, Topic: null, fullTitle, body, data, DataOnly: false), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM send failed for condition {Condition}", condition);
                await ops.ErrorAsync($"FCM send failed: {ex.Message}", ct);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Send a notification directly to one device token (alert-subscription delivery). Honours the
    /// publisher mode and never throws — a bad/expired token is logged and escalated, not surfaced.
    /// </summary>
    public async Task<bool> SendToTokenAsync(
        string token,
        string title,
        string body,
        IReadOnlyDictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return true;

        var mode = publishing.Value.ModeFor(ChannelKey);
        if (mode == PublisherMode.Off)
            return true;

        var fullTitle = fcmOptions.Value.TitlePrefix + title;

        if (mode == PublisherMode.DryRun)
        {
            await ops.DryRunCaptureAsync(ChannelKey, $"[{fullTitle}] {body} :: token={token}", ct);
            return true;
        }

        try
        {
            await sender.SendAsync(new FcmSend(Condition: null, Topic: null, fullTitle, body, data, DataOnly: false, Token: token), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FCM token send failed");
            await ops.ErrorAsync($"FCM token send failed: {ex.Message}", ct);
            return false;
        }
    }

    /// <summary>Send a data-only message to a single topic (the legacy "nearby" proximity path).</summary>
    public async Task<bool> SendDataOnlyAsync(
        string topic,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct = default)
    {
        var mode = publishing.Value.ModeFor(ChannelKey);
        if (mode == PublisherMode.Off)
            return true;

        if (mode == PublisherMode.DryRun)
        {
            await ops.DryRunCaptureAsync(ChannelKey, $"[data-only] topic={topic} data={string.Join(",", data.Select(kv => $"{kv.Key}={kv.Value}"))}", ct);
            return true;
        }

        try
        {
            await sender.SendAsync(new FcmSend(Condition: null, topic, Title: "", Body: "", data, DataOnly: true), ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FCM data-only send failed for topic {Topic}", topic);
            await ops.ErrorAsync($"FCM data-only send failed: {ex.Message}", ct);
            return false;
        }
    }
}
