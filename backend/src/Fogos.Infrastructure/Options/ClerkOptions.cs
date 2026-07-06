namespace Fogos.Infrastructure.Options;

/// <summary>
/// Clerk identity-provider settings. Clerk validates human sign-in and issues short-lived session
/// JWTs that the API accepts as a second Bearer issuer. When <see cref="Authority"/> is empty the
/// whole Clerk path is disabled and behaviour is unchanged (machine JWTs and API keys only).
/// </summary>
public sealed class ClerkOptions
{
    public const string SectionName = "Clerk";

    /// <summary>The Clerk instance issuer URL (matches the token <c>iss</c>). Empty = Clerk disabled.</summary>
    public string Authority { get; set; } = "";

    /// <summary>JWKS endpoint; defaults to <c>{Authority}/.well-known/jwks.json</c> when unset.</summary>
    public string? JwksUrl { get; set; }

    /// <summary>Allowed <c>azp</c> (authorized-party) values; empty = <c>azp</c> not enforced.</summary>
    public List<string> AuthorizedParties { get; set; } = [];

    /// <summary>How long a fetched JWK set is trusted before a scheduled refetch.</summary>
    public int JwksCacheMinutes { get; set; } = 60;

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority);

    /// <summary>The effective JWKS URL — the configured override, or derived from the authority.</summary>
    public string ResolvedJwksUrl => string.IsNullOrWhiteSpace(JwksUrl)
        ? $"{Authority.TrimEnd('/')}/.well-known/jwks.json"
        : JwksUrl;
}
