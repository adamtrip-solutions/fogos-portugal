using Fogos.Domain.Events;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Fans a newly-issued <see cref="WarningCreated"/> out to the main channels (Twitter / Telegram /
/// Facebook / Discord posts), dry-run by default. Ports <c>WarningsController::add</c> / <c>::addAgif</c>
/// — MANUAL prefixes the "ALERTA:" banner, AGIF posts the message verbatim (see <see cref="WarningCopy"/>).
/// Re-fetches the warning first. NOTE: Bluesky is deleted (v5); the legacy per-kind push notifications
/// (<c>sendWarningNotification</c> / <c>sendAllNotification</c>) are not part of this social fan-out.
/// warningAdded subscriptions are delivered by the Mongo change stream, not here (exactly-once).
/// </summary>
public sealed class WarningSocialHandler(
    WarningReads warnings,
    ITwitterPublisher twitter,
    ITelegramPublisher telegram,
    IFacebookPublisher facebook,
    IDiscordPostPublisher discord) : IEventHandler<WarningCreated>
{
    public async Task HandleAsync(WarningCreated evt, CancellationToken ct)
    {
        var warning = await warnings.GetByIdAsync(evt.WarningId, ct);
        if (warning is null)
            return;

        var post = new SocialPost { Text = WarningCopy.For(warning.Kind, warning.Message, warning.Url) };

        await twitter.PublishAsync(post, ct: ct);
        await telegram.PublishAsync(post, ct: ct);
        await facebook.PublishAsync(post, ct: ct);
        await discord.PublishAsync(post, ct: ct);
    }
}
