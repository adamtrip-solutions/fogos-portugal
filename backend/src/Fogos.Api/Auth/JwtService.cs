using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fogos.Domain.Auth;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Api.Auth;

/// <summary>Claims lifted from a validated access token.</summary>
public sealed record JwtClaims(string ClientId, string? Name, ApiTier Tier, IReadOnlyList<string> Scopes);

/// <summary>
/// Self-issued RS256 access tokens — signing, validation, and JWKS export — implemented directly
/// on <see cref="RSA"/> so no JWT library is needed. When no private key is configured an ephemeral
/// key is generated (dev only) and a loud warning is logged; those tokens die on restart.
/// </summary>
public sealed class JwtService
{
    private readonly AuthOptions _options;
    private readonly RSA _rsa;
    private readonly string _kid;

    public JwtService(IOptions<AuthOptions> options, ILogger<JwtService> logger)
    {
        _options = options.Value;
        _rsa = LoadOrGenerate(_options.RsaPrivateKeyPem, logger);
        _kid = ComputeKid(_rsa);
    }

    public int AccessTokenSeconds => _options.AccessTokenMinutes * 60;

    /// <summary>Issues an access token for a resolved credential.</summary>
    public string Issue(string clientId, string? name, ApiTier tier, IReadOnlyList<string> scopes)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = _kid,
        };
        var payload = new Dictionary<string, object>
        {
            ["sub"] = clientId,
            ["tier"] = tier.ToString(),
            ["scope"] = string.Join(' ', scopes),
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(_options.AccessTokenMinutes).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (!string.IsNullOrEmpty(name))
            payload["name"] = name;

        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signature = _rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    /// <summary>Validates signature, issuer, audience, and lifetime; returns the claims or null.</summary>
    public bool TryValidate(string token, out JwtClaims? claims)
    {
        claims = null;
        var parts = token.Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var signature = FromBase64Url(parts[2]);
            if (!_rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return false;

            using var doc = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = doc.RootElement;

            if (GetString(root, "iss") != _options.Issuer || GetString(root, "aud") != _options.Audience)
                return false;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // exp is mandatory: a token without a (numeric) expiry would never expire — reject it.
            if (!root.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number || exp.GetInt64() < now)
                return false;
            if (root.TryGetProperty("nbf", out var nbf) && nbf.GetInt64() > now + 60)
                return false;

            var sub = GetString(root, "sub");
            if (string.IsNullOrEmpty(sub))
                return false;

            var tier = Enum.TryParse<ApiTier>(GetString(root, "tier"), out var t) ? t : ApiTier.Anonymous;
            var scope = GetString(root, "scope");
            var scopes = string.IsNullOrWhiteSpace(scope)
                ? []
                : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            claims = new JwtClaims(sub, GetString(root, "name"), tier, scopes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The public JWK set for <c>GET /auth/jwks</c>.</summary>
    public object Jwks()
    {
        var p = _rsa.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = _kid,
                    n = Base64Url(p.Modulus!),
                    e = Base64Url(p.Exponent!),
                },
            },
        };
    }

    private static RSA LoadOrGenerate(string? pemOrPath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(pemOrPath))
        {
            logger.LogWarning(
                "AUTH: no RSA private key configured (Auth:RsaPrivateKeyPem). Generated an EPHEMERAL key — " +
                "tokens will not survive a restart and instances will not share keys. Configure a key for production.");
            return GenerateEphemeral();
        }

        // A configured-but-unparseable key (a host path that does not exist inside the container, or
        // PEM with mangled newlines) previously threw from ImportFromPem — crashing every request via
        // the auth middleware. Availability over strictness: fall back to an ephemeral key on any failure.
        try
        {
            var rsa = RSA.Create();
            var pem = File.Exists(pemOrPath) ? File.ReadAllText(pemOrPath) : pemOrPath;
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "AUTH: RSA key is configured but could not be parsed (not valid PEM and not an existing file " +
                "path inside the container) — falling back to an EPHEMERAL key; issued tokens will not survive " +
                "restarts and instances will not share keys. Configure a valid key for production.");
            return GenerateEphemeral();
        }
    }

    private static RSA GenerateEphemeral()
    {
        var rsa = RSA.Create();
        rsa.KeySize = 2048;
        return rsa;
    }

    private static string ComputeKid(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        var material = Encoding.ASCII.GetBytes(Base64Url(p.Modulus!) + "." + Base64Url(p.Exponent!));
        return Base64Url(SHA256.HashData(material))[..16];
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static string Encode(Dictionary<string, object> map) =>
        Base64Url(JsonSerializer.SerializeToUtf8Bytes(map));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }
}
