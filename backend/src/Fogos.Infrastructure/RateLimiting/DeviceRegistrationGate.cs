using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.RateLimiting;

/// <summary>
/// Per-IP abuse gate for anonymous Web Push device registration — the same posture as
/// <see cref="AlertSubscriptionGate"/>: fixed Redis windows (per-minute and per-day), failing open when
/// Redis is unreachable.
/// </summary>
public sealed class DeviceRegistrationGate(RedisCounters counters, IOptions<WebPushOptions> options)
{
    private readonly WebPushOptions _o = options.Value;

    /// <summary>True when the IP is under both windows (or Redis is down → allow).</summary>
    public async Task<bool> TryAcquireAsync(string ip, CancellationToken ct = default)
    {
        var perMinute = await counters.HitAsync($"rl:device:min:{ip}", 1, 60);
        if (perMinute is { } m && m.Total > _o.RegisterPerIpPerMinute)
            return false;

        var perDay = await counters.HitAsync($"rl:device:day:{ip}", 1, 86_400);
        if (perDay is { } d && d.Total > _o.RegisterPerIpPerDay)
            return false;

        return true;
    }
}
