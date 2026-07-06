using Fogos.Domain.Auth;
using Fogos.Domain.Users;
using HotChocolate;

namespace Fogos.Api.GraphQL.Types;

/// <summary>
/// The signed-in user's own identity — the <c>me</c> payload. Null for machine callers and anonymous
/// requests. The caller's API keys, webhooks, and alert subscriptions are resolved by
/// <see cref="MeExtensions"/> (which re-derives the identity from the caller accessor, never trusting
/// <see cref="Id"/>).
/// </summary>
public sealed record Me(string Id, string? Email, string? Name, UserRole Role);

/// <summary>
/// A self-service API key as seen by its owner. The plaintext is never stored, so it is absent here;
/// only the display-only <see cref="KeyPrefix"/> lets the owner tell keys apart. Revoked keys are
/// included in the listing (<see cref="RevokedAt"/> drives the UI badge).
/// </summary>
public sealed record ApiKeyInfo(
    [property: ID] string Id,
    string Name,
    string? KeyPrefix,
    ApiTier Tier,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt)
{
    public static ApiKeyInfo From(ApiClient c) =>
        new(c.Id, c.Name, c.KeyPrefix, c.Tier, c.CreatedAt, c.RevokedAt);
}

/// <summary>
/// The one-time response of <c>createApiKey</c>: the freshly-minted key's metadata plus the plaintext,
/// shown exactly once — it is not recoverable afterwards.
/// </summary>
public sealed record CreatedApiKey(ApiKeyInfo ApiKey, string PlaintextKey);
