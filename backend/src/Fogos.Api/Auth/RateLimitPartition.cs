using Fogos.Domain.Auth;

namespace Fogos.Api.Auth;

/// <summary>
/// Computes the limiter partition key for a caller (MIGRATION-PLAN §2b):
/// signed-in user → <c>uk:{userId}</c> (by account, across devices/IPs); anonymous → <c>ip:{ip}</c>;
/// public-context credential → <c>pk:{clientId}:{ip}</c> (each visitor gets its own budget);
/// any other credential → <c>ck:{clientId}</c> (server-held, by credential alone).
/// </summary>
public static class RateLimitPartition
{
    public static string For(FogosCaller caller)
    {
        if (caller.UserId is not null)
            return $"uk:{caller.UserId}";

        if (caller.Tier == ApiTier.Anonymous || caller.ClientId is null)
            return $"ip:{caller.RemoteIp}";

        return caller.PublicContext
            ? $"pk:{caller.ClientId}:{caller.RemoteIp}"
            : $"ck:{caller.ClientId}";
    }
}
