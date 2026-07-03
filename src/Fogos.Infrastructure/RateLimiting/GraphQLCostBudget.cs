namespace Fogos.Infrastructure.RateLimiting;

/// <summary>Decision for a GraphQL operation against its per-window cost budget.</summary>
public readonly record struct CostDecision(bool Allowed, double Cost, double Budget, int RetryAfterSeconds)
{
    public static CostDecision FailOpen(double cost, double budget) => new(true, cost, budget, 0);
}

/// <summary>
/// Second GraphQL layer: debits an operation's computed cost against a per-caller, per-window
/// budget in Redis. Over budget → the caller is told to back off (the middleware turns this into
/// a <c>RATE_LIMITED</c> GraphQL error without executing). Fail-open when Redis is down.
/// </summary>
public sealed class GraphQLCostBudget(RedisCounters counters)
{
    public async Task<CostDecision> DebitAsync(string partitionKey, double cost, double budget, int windowSeconds)
    {
        var hit = await counters.HitAsync($"rl:cost:{partitionKey}", cost, windowSeconds);
        if (hit is null)
            return CostDecision.FailOpen(cost, budget); // Redis down → allow.

        return hit.Value.Total > budget
            ? new CostDecision(false, cost, budget, hit.Value.RetryAfterSeconds)
            : new CostDecision(true, cost, budget, hit.Value.RetryAfterSeconds);
    }
}
