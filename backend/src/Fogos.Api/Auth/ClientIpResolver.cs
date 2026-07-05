using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Api.Auth;

/// <summary>
/// Single source of truth for the caller's IP, used by every rate-limit / abuse gate. Cloudflare is
/// the only trusted edge, so:
/// <list type="number">
///   <item>prefer the configured edge header (<c>CF-Connecting-IP</c>) — Cloudflare overwrites it, a
///   client cannot forge it past the edge;</item>
///   <item>else the <b>last</b> hop of <c>X-Forwarded-For</c> (the nearest trusted proxy appended it;
///   the first hop is fully attacker-controlled, which is why the legacy code was spoofable);</item>
///   <item>else the socket <see cref="Microsoft.AspNetCore.Http.ConnectionInfo.RemoteIpAddress"/>.</item>
/// </list>
/// The preferred header is configurable via <c>RateLimit:ClientIpHeader</c>.
/// </summary>
public sealed class ClientIpResolver(IOptions<RateLimitOptions> options)
{
    private readonly string _header = options.Value.ClientIpHeader ?? "";

    public string Resolve(HttpContext context)
    {
        // (1) Trusted edge header (Cloudflare) — not forgeable past the edge.
        if (!string.IsNullOrWhiteSpace(_header))
        {
            var edge = context.Request.Headers[_header].ToString();
            if (!string.IsNullOrWhiteSpace(edge))
                return edge.Trim();
        }

        // (2) Last hop of X-Forwarded-For — the nearest trusted proxy; the first hop is spoofable.
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(forwarded))
        {
            var last = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (!string.IsNullOrEmpty(last))
                return last;
        }

        // (3) Socket address.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
