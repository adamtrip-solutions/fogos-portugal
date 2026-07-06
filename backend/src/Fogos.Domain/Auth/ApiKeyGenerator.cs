using System.Security.Cryptography;
using System.Text;

namespace Fogos.Domain.Auth;

/// <summary>
/// Generates <c>fgs_live_</c> API keys and their SHA-256 hex hash. Lives in Domain (BCL-only) so every
/// consumer — the admin CLI, the API's <c>ApiKeyResolver</c>, and the self-service key mutation — shares
/// one canonical implementation.
/// </summary>
public static class ApiKeyGenerator
{
    public const string Prefix = "fgs_live_";
    private const string Base62 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int SecretLength = 40;

    /// <summary>The number of leading characters kept as a display-only key prefix.</summary>
    public const int PrefixLength = 12;

    public static string NewPlaintext()
    {
        var sb = new StringBuilder(Prefix, Prefix.Length + SecretLength);
        for (var i = 0; i < SecretLength; i++)
            sb.Append(Base62[RandomNumberGenerator.GetInt32(Base62.Length)]);
        return sb.ToString();
    }

    public static string Hash(string apiKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
