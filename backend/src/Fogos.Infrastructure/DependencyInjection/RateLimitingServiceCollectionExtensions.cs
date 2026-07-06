using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fogos.Infrastructure.DependencyInjection;

public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Binds the auth/limit options and registers the Redis-backed counters and the request-rate,
    /// GraphQL-cost, subscription, and photo-gate limiters. Depends on the Redis multiplexer and
    /// Mongo context wired by <see cref="ServiceCollectionExtensions.AddFogosInfrastructure"/>.
    /// </summary>
    public static IServiceCollection AddFogosRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<ClerkOptions>(configuration.GetSection(ClerkOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.Configure<PhotoGateOptions>(configuration.GetSection(PhotoGateOptions.SectionName));

        services.AddSingleton<RedisCounters>();
        services.AddSingleton<RequestRateLimiter>();
        services.AddSingleton<GraphQLCostBudget>();
        services.AddSingleton<SubscriptionLimiter>();
        services.AddSingleton<PhotoUploadGates>();
        services.AddSingleton<AlertSubscriptionGate>();

        return services;
    }
}
