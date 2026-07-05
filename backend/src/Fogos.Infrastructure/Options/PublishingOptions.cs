using Fogos.Infrastructure.Publishing;

namespace Fogos.Infrastructure.Options;

/// <summary>
/// Per-channel publisher mode. Everything defaults to <see cref="PublisherMode.DryRun"/>: the
/// external accounts are shared with the live platform until the switchover playbook flips
/// each channel to <see cref="PublisherMode.On"/> one at a time. Only the push (FCM) channel
/// remains after social posting was removed; FR24 spend also reads its mode from here.
/// </summary>
public sealed class PublishingOptions
{
    public const string SectionName = "Publishing";

    public Dictionary<string, PublisherMode> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fcm"] = PublisherMode.DryRun,
    };

    /// <summary>Mode for a channel; unknown channels are treated as dry-run (never live by accident).</summary>
    public PublisherMode ModeFor(string channel) =>
        Channels.TryGetValue(channel, out var mode) ? mode : PublisherMode.DryRun;
}
