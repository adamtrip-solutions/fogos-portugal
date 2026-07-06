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
        services.AddSingleton<ClientIpResolver>();
        services.AddSingleton<RefreshTokenStore>();
        services.AddSingleton<IFogosCallerAccessor, FogosCallerAccessor>();

        // Clerk identity: a second Bearer issuer (session-JWT validator) plus lazy local provisioning.
        // Both are inert until Clerk:Authority is configured.
        services.AddSingleton<ClerkTokenValidator>();
        services.AddSingleton<UserProvisioningService>();

        // JWKS fetches run while the validator holds its refresh lock, so a hanging response must
        // never wedge Clerk validation — bound the client tightly (2s per attempt, two retries,
        // 5s total) instead of the 100s unnamed-client default.
        services.AddHttpClient(ClerkTokenValidator.HttpClientName)
            .AddStandardResilienceHandler(o =>
            {
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                o.Retry.MaxRetryAttempts = 2;
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
            });

        services.AddAuthorization(options =>
        {
            foreach (var scope in ApiScopes.All)
                options.AddPolicy(scope, policy => policy.RequireClaim("scope", scope));
        });

        return services;
    }
}
