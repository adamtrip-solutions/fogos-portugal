using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Api.Auth;

/// <summary>Claims lifted from a validated Clerk session token. Clerk tokens are sparse — tolerate nulls.</summary>
public sealed record ClerkClaims(string ClerkUserId, string? Email, string? Name);

/// <summary>
/// Validates Clerk session JWTs directly on <see cref="RSA"/> — same house style as
/// <see cref="JwtService"/>, no Microsoft.IdentityModel dependency. Fetches Clerk's JWKS via
/// <see cref="IHttpClientFactory"/>, caches <c>kid → RSA</c> for <see cref="ClerkOptions.JwksCacheMinutes"/>,
/// and refetches on an unknown <c>kid</c> (throttled, so a burst of garbage-kid tokens can't hammer Clerk).
/// Enforces RS256, <c>iss == Authority</c>, a mandatory future <c>exp</c>, <c>nbf</c> with 60s leeway, and
/// <c>azp ∈ AuthorizedParties</c> when both are present.
/// </summary>
public sealed class ClerkTokenValidator
{
    public const string HttpClientName = "clerk-jwks";

    private static readonly TimeSpan UnknownKidThrottle = TimeSpan.FromMinutes(5);

    private readonly ClerkOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ClerkTokenValidator> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Dictionary<string, RSA> _keys = new();
    private DateTimeOffset _keysFetchedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRefetch = DateTimeOffset.MinValue;

    public ClerkTokenValidator(
        IOptions<ClerkOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<ClerkTokenValidator> logger)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Validates signature, issuer, lifetime, and authorized party; returns the claims or null.</summary>
    public async Task<ClerkClaims?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return null;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        try
        {
            using var headerDoc = JsonDocument.Parse(FromBase64Url(parts[0]));
            var header = headerDoc.RootElement;
            if (GetString(header, "alg") != "RS256")
                return null;
            var kid = GetString(header, "kid");
            if (string.IsNullOrEmpty(kid))
                return null;

            var rsa = await GetKeyAsync(kid, ct);
            if (rsa is null)
                return null;

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = FromBase64Url(parts[2]);
            if (!rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return null;

            using var doc = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = doc.RootElement;

            if (GetString(root, "iss") != _options.Authority)
                return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // exp is mandatory: a token without a numeric expiry would never expire — reject it.
            if (!root.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number || exp.GetInt64() < now)
                return null;
            if (root.TryGetProperty("nbf", out var nbf) && nbf.ValueKind == JsonValueKind.Number && nbf.GetInt64() > now + 60)
                return null;

            // azp allow-list is enforced only when both the config and the token carry it (empty
            // entries — e.g. an unset compose env var — are ignored so they never lock everyone out).
            var parties = _options.AuthorizedParties.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (parties.Count > 0 && root.TryGetProperty("azp", out var azp) && azp.ValueKind == JsonValueKind.String)
            {
                if (!parties.Contains(azp.GetString()!))
                    return null;
            }

            var sub = GetString(root, "sub");
            if (string.IsNullOrEmpty(sub))
                return null;

            return new ClerkClaims(sub, NullIfEmpty(GetString(root, "email")), NullIfEmpty(GetString(root, "name")));
        }
        catch
        {
            return null;
        }
    }

    private async Task<RSA?> GetKeyAsync(string kid, CancellationToken ct)
    {
        var cacheDuration = TimeSpan.FromMinutes(_options.JwksCacheMinutes);
        var now = DateTimeOffset.UtcNow;

        // Fast path: a known key inside the cache window.
        var snapshot = _keys;
        if (now - _keysFetchedAt < cacheDuration && snapshot.TryGetValue(kid, out var cached))
            return cached;

        await _refreshLock.WaitAsync(ct);
        try
        {
            now = DateTimeOffset.UtcNow;
            // Another request may have refreshed while we waited on the lock.
            if (now - _keysFetchedAt < cacheDuration && _keys.TryGetValue(kid, out var afterWait))
                return afterWait;

            var expired = now - _keysFetchedAt >= cacheDuration;
            // Unknown kid but the cache is still fresh: refetch at most once per throttle window so a
            // stream of forged-kid tokens can't turn into a stream of JWKS fetches.
            if (!expired && now - _lastRefetch < UnknownKidThrottle)
                return _keys.TryGetValue(kid, out var throttled) ? throttled : null;

            var fetched = await FetchJwksAsync(ct);
            _lastRefetch = now;
            if (fetched is not null)
            {
                _keys = fetched;
                _keysFetchedAt = now;
            }

            return _keys.TryGetValue(kid, out var result) ? result : null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<Dictionary<string, RSA>?> FetchJwksAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            var json = await client.GetStringAsync(_options.ResolvedJwksUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var keys = new Dictionary<string, RSA>();
            if (doc.RootElement.TryGetProperty("keys", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in arr.EnumerateArray())
                {
                    if (GetString(key, "kty") != "RSA")
                        continue;
                    var kid = GetString(key, "kid");
                    var n = GetString(key, "n");
                    var e = GetString(key, "e");
                    if (string.IsNullOrEmpty(kid) || string.IsNullOrEmpty(n) || string.IsNullOrEmpty(e))
                        continue;

                    var rsa = RSA.Create();
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = FromBase64Url(n),
                        Exponent = FromBase64Url(e),
                    });
                    keys[kid] = rsa;
                }
            }

            return keys;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CLERK: JWKS fetch failed from {Url}", _options.ResolvedJwksUrl);
            return null;
        }
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static byte[] FromBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }
}
