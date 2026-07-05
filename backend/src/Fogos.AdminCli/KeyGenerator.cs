using System.Security.Cryptography;
using System.Text;

namespace Fogos.AdminCli;

/// <summary>Generates <c>fgs_live_</c> API keys and their SHA-256 hex hash (matching the API resolver).</summary>
public static class KeyGenerator
{
    private const string Prefix = "fgs_live_";
    private const string Base62 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const int SecretLength = 40;

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
