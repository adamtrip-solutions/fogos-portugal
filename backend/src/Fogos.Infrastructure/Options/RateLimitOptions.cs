using Fogos.Domain.Auth;

namespace Fogos.Infrastructure.Options;

/// <summary>Per-tier limits for the request-rate window, the GraphQL cost budget, and subscriptions.</summary>
public sealed class TierLimits
{
    /// <summary>Requests per window.</summary>
    public int Requests { get; set; }

    /// <summary>GraphQL operation-cost budget per window.</summary>
    public double CostBudget { get; set; }

    /// <summary>Maximum concurrent subscriptions.</summary>
    public int Subscriptions { get; set; }

    public TierLimits() { }

    public TierLimits(int requests, double costBudget, int subscriptions)
    {
        Requests = requests;
        CostBudget = costBudget;
        Subscriptions = subscriptions;
    }
}

/// <summary>
/// All rate-limiting knobs (fully tunable via config). Windows are fixed 60s by default.
/// Defaults follow MIGRATION-PLAN §2b.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>
    /// Master switch for all general rate limiting (request rate, GraphQL cost budget, subscription
    /// caps). Meant for local development only — the photo-upload abuse gates are product rules and
    /// are NOT covered by this flag.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Request header carrying the trusted client IP set by the edge (Cloudflare). Preferred over
    /// X-Forwarded-For, whose first hop is attacker-controlled. Set empty to rely on XFF's last hop.
    /// </summary>
    public string ClientIpHeader { get; set; } = "CF-Connecting-IP";

    public TierLimits Anonymous { get; set; } = new(30, 500, 0);
    public TierLimits Registered { get; set; } = new(300, 5000, 2);
    public TierLimits FirstParty { get; set; } = new(1200, 50000, 10);
    public TierLimits Operator { get; set; } = new(600, 20000, 4);

    /// <summary>
    /// Mobile app devices (App tier, partition <c>dk:{deviceId}</c>). Deliberately ~8× real app usage so a
    /// genuine device NEVER feels a limit (owner's ship condition): 240 requests/min and a GraphQL cost
    /// budget of 20000 = 4× the Registered tier's 5000. Fully tunable via <c>RateLimit:App:*</c>.
    /// </summary>
    public TierLimits App { get; set; } = new(240, 20_000, 2);

    public TierLimits For(ApiTier tier) => tier switch
    {
        ApiTier.Registered => Registered,
        ApiTier.FirstParty => FirstParty,
        ApiTier.Operator => Operator,
        ApiTier.App => App,
        _ => Anonymous,
    };
}
