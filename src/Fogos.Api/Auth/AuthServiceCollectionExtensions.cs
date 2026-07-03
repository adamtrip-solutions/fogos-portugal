using Fogos.Domain.Auth;

namespace Fogos.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers the caller-identity services (JWT, API-key resolver, refresh store, accessor) and the
    /// scope authorization policies (<c>write:incidents</c>, <c>write:warnings</c>, <c>moderate:photos</c>),
    /// each satisfied by a matching <c>scope</c> claim. Rate-limiting services come from
    /// <c>AddFogosRateLimiting</c> in Infrastructure.
    /// </summary>
    public static IServiceCollection AddFogosAuth(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<JwtService>();
        services.AddSingleton<ApiKeyResolver>();
        services.AddSingleton<RefreshTokenStore>();
        services.AddSingleton<IFogosCallerAccessor, FogosCallerAccessor>();

        services.AddAuthorization(options =>
        {
            foreach (var scope in ApiScopes.All)
                options.AddPolicy(scope, policy => policy.RequireClaim("scope", scope));
        });

        return services;
    }
}
