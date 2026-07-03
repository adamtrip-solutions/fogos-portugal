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

    public int WindowSeconds { get; set; } = 60;

    public TierLimits Anonymous { get; set; } = new(30, 500, 0);
    public TierLimits Registered { get; set; } = new(300, 5000, 2);
    public TierLimits FirstParty { get; set; } = new(1200, 50000, 10);
    public TierLimits Operator { get; set; } = new(600, 20000, 4);

    public TierLimits For(ApiTier tier) => tier switch
    {
        ApiTier.Registered => Registered,
        ApiTier.FirstParty => FirstParty,
        ApiTier.Operator => Operator,
        _ => Anonymous,
    };
}
