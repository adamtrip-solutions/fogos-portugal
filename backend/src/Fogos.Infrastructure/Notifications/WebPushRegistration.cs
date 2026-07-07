namespace Fogos.Infrastructure.Notifications;

/// <summary>The validated shape of a Web Push subscription submitted for device registration.</summary>
public sealed record WebPushSubscriptionInput(string Endpoint, string P256dh, string Auth);

/// <summary>Why a submitted Web Push subscription was rejected (maps to a GraphQL error code).</summary>
public enum WebPushValidationError
{
    None,
    EndpointInvalid,
    EndpointHostNotAllowed,
    KeyInvalid,
}

/// <summary>
/// Pure validation of a Web Push subscription before it is persisted as a device. The endpoint is what the
/// worker later POSTs to, so it is the SSRF surface: it must be an absolute <c>https</c> URL whose host
/// suffix-matches the allow-list (defeating private-host / http / arbitrary-host endpoints). The keys must
/// decode (base64url) to their exact spec'd shapes: p256dh an uncompressed P-256 point (65 bytes, leading
/// <c>0x04</c>), auth a 16-byte secret — anything else is garbage no browser produces.
/// </summary>
public static class WebPushRegistration
{
    private const int MaxEndpointLength = 2048;

    /// <summary>Decode-attempt cap; the largest legitimate key (p256dh) is ~88 base64url chars.</summary>
    private const int MaxKeyLength = 256;

    private const int P256PointLength = 65; // 0x04 ‖ X(32) ‖ Y(32)
    private const byte UncompressedPointPrefix = 0x04;
    private const int AuthSecretLength = 16;

    public static WebPushValidationError Validate(WebPushSubscriptionInput input, IReadOnlyList<string> allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(input.Endpoint) || input.Endpoint.Length > MaxEndpointLength
            || !Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            return WebPushValidationError.EndpointInvalid;

        if (!HostIsAllowed(uri.Host, allowedHosts))
            return WebPushValidationError.EndpointHostNotAllowed;

        if (!TryDecodeKey(input.P256dh, out var point)
            || point.Length != P256PointLength || point[0] != UncompressedPointPrefix)
            return WebPushValidationError.KeyInvalid;

        if (!TryDecodeKey(input.Auth, out var auth) || auth.Length != AuthSecretLength)
            return WebPushValidationError.KeyInvalid;

        return WebPushValidationError.None;
    }

    /// <summary>
    /// Suffix match: the endpoint host must equal an allowed host or be a subdomain of one (so regional
    /// prefixes like <c>fcm.googleapis.com</c> siblings and <c>*.push.services.mozilla.com</c> are covered),
    /// never a mere string suffix (<c>evilfcm.googleapis.com.attacker.net</c> must not pass).
    /// </summary>
    private static bool HostIsAllowed(string host, IReadOnlyList<string> allowedHosts)
    {
        foreach (var allowed in allowedHosts)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;
            if (host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryDecodeKey(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxKeyLength)
            return false;
        return TryDecodeBase64Url(value, out bytes);
    }

    /// <summary>Decodes a base64url string (no padding), tolerating standard base64 too.</summary>
    public static bool TryDecodeBase64Url(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value))
            return false;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2: normalized += "=="; break;
            case 3: normalized += "="; break;
            case 1: return false; // never a valid base64 length
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
