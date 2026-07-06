using Fogos.Domain.Auth;

namespace Fogos.Api.Auth;

/// <summary>
/// The resolved identity of the current request: anonymous, an API-key client, or a JWT bearer.
/// Stored on <see cref="HttpContext.Items"/> and exposed through <see cref="IFogosCallerAccessor"/>.
/// </summary>
public sealed class FogosCaller
{
    public required ApiTier Tier { get; init; }
    public string? ClientId { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>The local <c>users</c> id when the caller is a signed-in human (Clerk Bearer); null otherwise.</summary>
    public string? UserId { get; init; }

    /// <summary>Clerk's stable subject for a signed-in human; null for machine callers.</summary>
    public string? ClerkUserId { get; init; }

    /// <summary>Whether the signed-in user's local role is Admin. Always false for machine callers.</summary>
    public bool IsAdmin { get; init; }

    /// <summary>True when the caller is a signed-in human (as opposed to anonymous or a machine credential).</summary>
    public bool IsUser => UserId is not null;

    /// <summary>Public-context credentials (the web site key) pin Origins and partition by (credential, IP).</summary>
    public bool PublicContext { get; init; }

    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    public required string RemoteIp { get; init; }

    public bool IsAnonymous => Tier == ApiTier.Anonymous;

    public bool HasScope(string scope) => Scopes.Contains(scope);

    public static FogosCaller Anonymous(string remoteIp) => new()
    {
        Tier = ApiTier.Anonymous,
        RemoteIp = remoteIp,
    };
}

/// <summary>DI accessor for the current request's <see cref="FogosCaller"/>.</summary>
public interface IFogosCallerAccessor
{
    /// <summary>The resolved caller, or anonymous when the auth middleware has not (yet) run.</summary>
    FogosCaller Caller { get; }
}

public sealed class FogosCallerAccessor(IHttpContextAccessor http) : IFogosCallerAccessor
{
    public const string ItemKey = "FogosCaller";

    public FogosCaller Caller
    {
        get
        {
            var ctx = http.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(ItemKey, out var value) && value is FogosCaller caller)
                return caller;
            var ip = ctx?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return FogosCaller.Anonymous(ip);
        }
    }
}
