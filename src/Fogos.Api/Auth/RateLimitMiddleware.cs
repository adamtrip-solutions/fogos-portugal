using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;

namespace Fogos.Api.Auth;

/// <summary>
/// Per-caller request-rate limiter (fixed 60s Redis windows). Runs after the auth middleware so the
/// caller/tier/partition is known. Exempts health checks and the public JWKS endpoint. On 429 it sets
/// <c>Retry-After</c> (seconds to window end) and a small JSON body. Redis failures fail open upstream.
/// </summary>
public sealed class RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
{
    private readonly RateLimitOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, IFogosCallerAccessor callerAccessor, RequestRateLimiter limiter)
    {
        if (IsExempt(context.Request.Path))
        {
            await next(context);
            return;
        }

        var caller = callerAccessor.Caller;
        var limit = _options.For(caller.Tier).Requests;
        var partition = RateLimitPartition.For(caller);

        var decision = await limiter.AcquireAsync(partition, limit, _options.WindowSeconds);
        if (!decision.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                error = "rate_limited",
                message = "Request rate limit exceeded.",
                limit = decision.Limit,
                retryAfterSeconds = decision.RetryAfterSeconds,
            });
            return;
        }

        await next(context);
    }

    private static bool IsExempt(PathString path) =>
        path.StartsWithSegments("/healthz") ||
        path.StartsWithSegments("/auth/jwks");
}
