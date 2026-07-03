namespace Fogos.Infrastructure.Options;

/// <summary>StackExchange.Redis connection settings (queue, locks, subscription bridge, limiter).</summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "";
}
