using System.Security.Cryptography;
using System.Text;

namespace Fogos.Infrastructure.Webhooks;

/// <summary>
/// Webhook signing + secret minting. Deliveries carry <see cref="SignatureHeader"/> whose value is
/// <c>sha256=&lt;lower-hex HMAC-SHA256(secret, body)&gt;</c> so receivers can verify authenticity.
/// </summary>
public static class WebhookSigner
{
    /// <summary>Signature header name on every delivery.</summary>
    public const string SignatureHeader = "X-Fogos-Signature";

    /// <summary>Named HttpClient used for webhook delivery (registered in the Worker pipeline).</summary>
    public const string HttpClientName = "webhook-delivery";

    /// <summary>Computes the <c>sha256=…</c> signature for <paramref name="body"/> under <paramref name="secret"/>.</summary>
    public static string Sign(string secret, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    /// <summary>A fresh 256-bit signing secret (lower-hex).</summary>
    public static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
