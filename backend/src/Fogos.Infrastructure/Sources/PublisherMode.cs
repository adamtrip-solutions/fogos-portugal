namespace Fogos.Infrastructure.Sources;

/// <summary>
/// Tri-state spend/side-effect gate. The only remaining consumer is the FR24 poller
/// (<c>Sources:Fr24:Mode</c>): <see cref="Off"/> disables it, <see cref="DryRun"/> runs the poll
/// gates but makes no paid API call, <see cref="On"/> is live. The default is <see cref="DryRun"/>
/// so a configured key never spends by accident.
/// </summary>
public enum PublisherMode
{
    Off,
    DryRun,
    On,
}
