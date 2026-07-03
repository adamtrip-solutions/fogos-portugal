using System.Security.Claims;
using Fogos.Domain.Auth;

namespace Fogos.Api.Auth;

/// <summary>
/// Resolves the caller for every request in order: (1) <c>Authorization: Bearer</c> self-issued JWT,
/// (2) <c>X-API-Key</c>, (3) anonymous. Populates <see cref="HttpContext.Items"/> and a
/// <see cref="ClaimsPrincipal"/> (so ASP.NET/HotChocolate authorization policies work), then enforces
/// Origin pinning for public-context credentials. Invalid credentials short-circuit with 401/403.
/// </summary>
public sealed class AuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, JwtService jwt, ApiKeyResolver apiKeys)
    {
        var ip = ResolveIp(context);

        // (1) Bearer JWT — present-but-invalid is a hard 401 (never silently downgraded to anonymous).
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();
            if (!jwt.TryValidate(token, out var claims) || claims is null)
            {
                await WriteError(context, StatusCodes.Status401Unauthorized, "invalid_token", "The bearer token is invalid or expired.");
                return;
            }

            var caller = new FogosCaller
            {
                Tier = claims.Tier,
                ClientId = claims.ClientId,
                Name = claims.Name,
                Scopes = claims.Scopes,
                RemoteIp = ip,
            };
            Assign(context, caller);
            await next(context);
            return;
        }

        // (2) X-API-Key.
        var apiKey = context.Request.Headers["X-API-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var client = await apiKeys.ResolveAsync(apiKey, context.RequestAborted);
            if (client is null || client.IsRevoked)
            {
                await WriteError(context, StatusCodes.Status401Unauthorized, "invalid_api_key", "The API key is unknown or revoked.");
                return;
            }

            var caller = new FogosCaller
            {
                Tier = client.Tier,
                ClientId = client.Id,
                Name = client.Name,
                Scopes = client.Scopes,
                PublicContext = client.PublicContext,
                AllowedOrigins = client.AllowedOrigins,
                RemoteIp = ip,
            };

            if (!OriginAllowed(context, caller))
            {
                await WriteError(context, StatusCodes.Status403Forbidden, "origin_not_allowed", "This credential is not permitted for the request Origin.");
                return;
            }

            Assign(context, caller);
            await next(context);
            return;
        }

        // (3) Anonymous.
        Assign(context, FogosCaller.Anonymous(ip));
        await next(context);
    }

    /// <summary>
    /// Origin pinning: a public-context credential with configured Origins only serves browser
    /// requests (those carrying an <c>Origin</c>) whose Origin matches (scheme+host, wildcard
    /// subdomain supported). Non-browser requests (no Origin) are allowed — the key buys nothing
    /// beyond per-IP limits there anyway.
    /// </summary>
    private static bool OriginAllowed(HttpContext context, FogosCaller caller)
    {
        if (!caller.PublicContext || caller.AllowedOrigins.Count == 0)
            return true;

        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true; // non-browser request.

        return caller.AllowedOrigins.Any(pattern => OriginMatches(pattern, origin));
    }

    private static bool OriginMatches(string pattern, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return false;

        // Scheme-qualified pattern (e.g. "https://fogos.pt"): match scheme + host + port.
        if (Uri.TryCreate(pattern, UriKind.Absolute, out var patternUri) && !string.IsNullOrEmpty(patternUri.Host))
        {
            return string.Equals(originUri.Scheme, patternUri.Scheme, StringComparison.OrdinalIgnoreCase)
                && originUri.Port == patternUri.Port
                && HostMatches(patternUri.Host, originUri.Host);
        }

        // Host-only pattern (e.g. "*.fogos.pt" or "fogos.pt"): match the host regardless of scheme.
        return HostMatches(pattern, originUri.Host);
    }

    private static bool HostMatches(string patternHost, string originHost)
    {
        if (patternHost.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = patternHost[1..]; // ".fogos.pt" — matches any subdomain of it.
            return originHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(patternHost, originHost, StringComparison.OrdinalIgnoreCase);
    }

    private static void Assign(HttpContext context, FogosCaller caller)
    {
        context.Items[FogosCallerAccessor.ItemKey] = caller;
        context.User = ToPrincipal(caller);
    }

    private static ClaimsPrincipal ToPrincipal(FogosCaller caller)
    {
        if (caller.IsAnonymous)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.ClientId ?? ""),
            new("tier", caller.Tier.ToString()),
        };
        if (!string.IsNullOrEmpty(caller.Name))
            claims.Add(new Claim(ClaimTypes.Name, caller.Name));
        claims.AddRange(caller.Scopes.Select(s => new Claim("scope", s)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Fogos"));
    }

    private static string ResolveIp(HttpContext context)
    {
        // Cloudflare is the edge — trust the first hop of X-Forwarded-For, else the socket address.
        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(forwarded))
        {
            var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
                return first;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static Task WriteError(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = code, message });
    }
}
