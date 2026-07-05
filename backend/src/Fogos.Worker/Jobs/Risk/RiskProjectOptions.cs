namespace Fogos.Worker.Jobs.Risk;

/// <summary>
/// Config for the daily PS-project risk push (legacy <c>SendRiskPSProject</c>): which concelho's risk
/// to report and which Telegram channel/thread to post it to. Empty <see cref="Dico"/> disables the job
/// (mirrors the legacy env-driven wiring, which had no built-in default).
/// </summary>
public sealed class RiskProjectOptions
{
    public const string SectionName = "RiskProject";

    /// <summary>Concelho DICO whose "today" risk is reported (legacy <c>PS_PROJECT_TELEGRAM_CHANNEL_1_DICOS</c>).</summary>
    public string Dico { get; set; } = "";

    /// <summary>Publisher channel key — its own mode entry, defaulting to dry-run like every channel.</summary>
    public string TelegramChannelKey { get; set; } = "telegramProject";

    /// <summary>Optional Telegram <c>message_thread_id</c> for the project channel.</summary>
    public string? TelegramThreadId { get; set; }
}
