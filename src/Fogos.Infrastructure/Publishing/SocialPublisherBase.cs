using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Shared Off/DryRun/On dispatch for single-shot publishers (Telegram/Facebook/Discord). Off is a
/// silent no-op, DryRun captures a summary and returns a fake id, On calls <see cref="SendAsync"/>
/// guarded so nothing throws.
/// </summary>
public abstract class SocialPublisherBase(
    IOptions<PublishingOptions> publishing,
    IOpsNotifier ops,
    ILogger logger)
{
    protected IOpsNotifier Ops => ops;

    /// <summary>The real provider call. Return <see cref="PublishResult.Fail"/> on non-success; may throw (caught here).</summary>
    protected abstract Task<PublishResult> SendAsync(SocialPost post, string channelKey, CancellationToken ct);

    /// <summary>Human-readable one-liner for the dry-run capture channel.</summary>
    protected virtual string Summarize(SocialPost post) =>
        $"{(post.HasImage ? "[img] " : "")}{post.Text}";

    protected async Task<PublishResult> PublishCoreAsync(string channelKey, SocialPost post, CancellationToken ct)
    {
        switch (publishing.Value.ModeFor(channelKey))
        {
            case PublisherMode.Off:
                return PublishResult.Skipped;

            case PublisherMode.DryRun:
                await ops.DryRunCaptureAsync(channelKey, Summarize(post), ct);
                return PublishResult.Ok(PublishResult.DryRunId());

            default:
                try
                {
                    return await SendAsync(post, channelKey, ct);
                }
                catch (Exception ex)
                {
                    return await FailAsync($"{channelKey} publish failed: {ex.Message}", ct);
                }
        }
    }

    protected async Task<PublishResult> FailAsync(string error, CancellationToken ct)
    {
        logger.LogWarning("{Error}", error);
        await ops.ErrorAsync(error, ct);
        return PublishResult.Fail(error);
    }
}
