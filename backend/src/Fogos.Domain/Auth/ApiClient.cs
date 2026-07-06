namespace Fogos.Domain.Auth;

public enum ApiTier
{
    Anonymous,
    Registered,
    FirstParty,
    Operator,
}

/// <summary>Scope names carried by JWTs and operator keys.</summary>
public static class ApiScopes
{
    public const string WriteIncidents = "write:incidents";
    public const string WriteWarnings = "write:warnings";
    public const string ModeratePhotos = "moderate:photos";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        WriteIncidents, WriteWarnings, ModeratePhotos,
    };
}

/// <summary>
/// Issued API credential. Only the SHA-256 hash of the key is stored; the plaintext
/// (`fgs_live_…`) is shown once at issue time by the admin CLI.
/// </summary>
public sealed class ApiClient
{
    public string Id { get; set; } = "";
    public required string Name { get; set; }
    public required string KeyHash { get; set; }
    public required ApiTier Tier { get; set; }
    public List<string> Scopes { get; set; } = [];

    /// <summary>
    /// Public-context credentials (the web frontend's site key) pin allowed Origins and
    /// rate-partition by (credential, IP) instead of credential alone.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>Marks credentials whose limiter partitions per caller IP.</summary>
    public bool PublicContext { get; set; }

    /// <summary>
    /// The local <c>users</c> id that self-issued this key (self-service portal). Null for keys minted by
    /// the admin CLI. Non-unique-indexed for the owner's key listing and per-user cap.
    /// </summary>
    public string? OwnerUserId { get; set; }

    /// <summary>The first characters of the plaintext key (display-only), so the owner can tell keys apart.</summary>
    public string? KeyPrefix { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsRevoked => RevokedAt is not null;
}
