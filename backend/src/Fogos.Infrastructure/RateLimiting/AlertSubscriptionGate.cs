using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.RateLimiting;

/// <summary>
/// Per-IP abuse gate for anonymous alert-subscription creation — the same posture as the photo-upload
/// gates: fixed Redis windows (per-minute and per-day), failing open when Redis is unreachable.
/// </summary>
public sealed class AlertSubscriptionGate(RedisCounters counters, IOptions<AlertOptions> options)
{
    private readonly AlertOptions _o = options.Value;

    /// <summary>True when the IP is under both windows (or Redis is down → allow).</summary>
    public async Task<bool> TryAcquireAsync(string ip, CancellationToken ct = default)
    {
        var perMinute = await counters.HitAsync($"rl:alertsub:min:{ip}", 1, 60);
        if (perMinute is { } m && m.Total > _o.CreatePerIpPerMinute)
            return false;

        var perDay = await counters.HitAsync($"rl:alertsub:day:{ip}", 1, 86_400);
        if (perDay is { } d && d.Total > _o.CreatePerIpPerDay)
            return false;

        return true;
    }
}
