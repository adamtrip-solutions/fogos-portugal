using System.Security.Cryptography;
using System.Text;
using Fogos.Infrastructure.Options;

namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// OAuth 1.0a user-context request signing (HMAC-SHA1), ported directly so we depend on no Twitter
/// client library. Produces the <c>Authorization: OAuth …</c> header for a request.
/// </summary>
/// <remarks>
/// For JSON-body requests (X API v2 <c>POST /2/tweets</c>) pass only the URL query params in
/// <c>extraParams</c> — the JSON body is not part of the signature base. For form-encoded requests
/// (v1.1 <c>media/upload</c>) pass the form fields so they are signed.
/// </remarks>
public static class OAuth1Signer
{
    public static string BuildAuthorizationHeader(
        string method,
        string url,
        IEnumerable<KeyValuePair<string, string>> extraParams,
        TwitterAccount account,
        string? nonce = null,
        long? timestamp = null)
    {
        nonce ??= Guid.NewGuid().ToString("N");
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();

        var oauthParams = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = account.ConsumerKey,
            ["oauth_nonce"] = nonce,
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = ts,
            ["oauth_token"] = account.AccessToken,
            ["oauth_version"] = "1.0",
        };

        // Signature base: all oauth_* params + any request params, percent-encoded, sorted, joined.
        var allParams = new List<KeyValuePair<string, string>>();
        foreach (var kv in oauthParams)
            allParams.Add(new(Encode(kv.Key), Encode(kv.Value)));
        foreach (var kv in extraParams)
            allParams.Add(new(Encode(kv.Key), Encode(kv.Value)));

        allParams.Sort((a, b) =>
        {
            var byKey = string.CompareOrdinal(a.Key, b.Key);
            return byKey != 0 ? byKey : string.CompareOrdinal(a.Value, b.Value);
        });

        var paramString = string.Join("&", allParams.Select(p => $"{p.Key}={p.Value}"));
        var baseUrl = url.Split('?')[0];
        var signatureBase = $"{method.ToUpperInvariant()}&{Encode(baseUrl)}&{Encode(paramString)}";

        var signingKey = $"{Encode(account.ConsumerSecret)}&{Encode(account.AccessTokenSecret)}";
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(signatureBase)));

        // The Authorization header carries only the oauth_* params (plus the signature), quoted.
        var headerParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in oauthParams)
            headerParams[kv.Key] = kv.Value;
        headerParams["oauth_signature"] = signature;

        var header = string.Join(", ", headerParams.Select(p => $"{Encode(p.Key)}=\"{Encode(p.Value)}\""));
        return "OAuth " + header;
    }

    /// <summary>RFC 3986 percent-encoding (unreserved set A-Za-z0-9-._~ kept literal).</summary>
    public static string Encode(string value)
    {
        var sb = new StringBuilder(value.Length * 2);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' or '_' or '~')
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
