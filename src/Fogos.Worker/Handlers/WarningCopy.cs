using Fogos.Domain.Warnings;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Broadcast-warning copy, ported from <c>WarningsController</c>. MANUAL warnings are prefixed with the
/// "ALERTA:" banner (legacy <c>"ALERTA: \r\n" . $status</c>); AGIF warnings post the message verbatim
/// (legacy passed <c>$status</c> straight through). An optional link is appended to either shape.
/// (Facebook's legacy <c>%0A</c> variant is unnecessary here — the Facebook publisher URL-encodes the text
/// itself, so the same newline-bearing string is used for every channel.)
/// </summary>
public static class WarningCopy
{
    public static string For(WarningKind kind, string message, string? url)
    {
        var body = kind == WarningKind.Manual ? $"ALERTA: \r\n{message}" : message;
        return string.IsNullOrWhiteSpace(url) ? body : $"{body} {url}";
    }
}
