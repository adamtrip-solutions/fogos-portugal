namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Every outbound side-effect channel (Twitter, Telegram, Facebook, FCM, Discord posts,
/// FR24 spend) runs in one of these modes. The default everywhere is DryRun: the external
/// accounts are shared with the live platform until the switchover playbook flips each
/// channel to On.
/// </summary>
public enum PublisherMode
{
    Off,
    DryRun,
    On,
}
