namespace Fogos.Infrastructure.Queue;

/// <summary>Central Redis key layout for the queue so producer and consumer never drift.</summary>
public static class QueueKeys
{
    /// <summary>The stream a logical queue name maps to: <c>fogos:stream:{stream}</c>.</summary>
    public static string Stream(string stream) => $"fogos:stream:{stream}";

    /// <summary>Sorted set of delayed events, scored by due-time (unix ms): <c>fogos:delayed</c>.</summary>
    public const string DelayedSet = "fogos:delayed";

    /// <summary>Idempotency marker for a processed event: <c>fogos:processed:{key}</c>.</summary>
    public static string Processed(string key) => $"fogos:processed:{key}";
}
