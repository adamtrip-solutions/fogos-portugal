using System.Security.Claims;
using System.Text.Json;
using Fogos.Domain.Auth;
using Fogos.Domain.Users;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Api.Auth;

/// <summary>
/// Resolves the caller for every request in order: (1) <c>Authorization: Bearer</c> JWT — a self-issued
/// machine token or a Clerk session token, routed by an unverified <c>iss</c> peek (trust still comes
/// only from the routed validator); (2) <c>X-API-Key</c>; (3) anonymous. Populates
/// <see cref="HttpContext.Items"/> and a <see cref="ClaimsPrincipal"/> (so ASP.NET/HotChocolate
/// authorization policies work), then enforces Origin pinning for public-context credentials. A present
/// Bearer that fails validation is always a hard 401 — it is never silently downgraded to anonymous.
/// </summary>
public sealed class AuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        JwtService jwt,
        ApiKeyResolver apiKeys,
        ClientIpResolver ipResolver,
        ClerkTokenValidator clerk,
        UserProvisioningService provisioning,
        IOptions<ClerkOptions> clerkOptions)
    {
        var ip = ipResolver.Resolve(context);

        // (1) Bearer JWT — present-but-invalid is a hard 401 (never silently downgraded to anonymous).
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader["Bearer ".Length..].Trim();

            // Route by an unverified iss peek. Clerk authority (when enabled) → Clerk validator +
            // provisioning; everything else → the self-issued machine validator, which itself rejects
            // any non-self issuer / unparseable token → 401. Either way, present-but-invalid = 401.
            var clerkCfg = clerkOptions.Value;
            if (clerkCfg.Enabled && PeekIssuer(token) == clerkCfg.Authority)
            {
                var clerkClaims = await clerk.ValidateAsync(token, context.RequestAborted);
                if (clerkClaims is null)
                {
                    await WriteError(context, StatusCodes.Status401Unauthorized, "invalid_token", "The bearer token is invalid or expired.");
                    return;
                }

                var user = await provisioning.GetOrProvisionAsync(clerkClaims, context.RequestAborted);
                var userCaller = new FogosCaller
                {
                    Tier = ApiTier.Registered,
                    UserId = user.Id,
                    ClerkUserId = user.ClerkUserId,
                    Name = user.DisplayName ?? clerkClaims.Name,
                    IsAdmin = user.Role == UserRole.Admin,
                    RemoteIp = ip,
                };
                Assign(context, userCaller);
                await next(context);
                return;
            }

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

        // Users carry NameIdentifier=UserId and a role claim, but deliberately NO scope claims — every
        // existing scope policy (write:*, moderate:*) therefore correctly denies a signed-in human.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, caller.UserId ?? caller.ClientId ?? ""),
            new("tier", caller.Tier.ToString()),
        };
        if (!string.IsNullOrEmpty(caller.Name))
            claims.Add(new Claim(ClaimTypes.Name, caller.Name));
        if (caller.IsUser)
            claims.Add(new Claim("role", caller.IsAdmin ? "Admin" : "User"));
        claims.AddRange(caller.Scopes.Select(s => new Claim("scope", s)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Fogos"));
    }

    /// <summary>
    /// Reads the (unverified) <c>iss</c> from a JWT's payload for routing only — one base64url decode,
    /// no signature check. Trust always comes from the routed validator. Null on any malformed token.
    /// </summary>
    private static string? PeekIssuer(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = (payload.Length % 4) switch { 2 => payload + "==", 3 => payload + "=", _ => payload };
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("iss", out var iss) && iss.ValueKind == JsonValueKind.String
                ? iss.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static Task WriteError(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error = code, message });
    }
}
