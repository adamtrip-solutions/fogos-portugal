using Fogos.Domain.Alerts;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;

namespace Fogos.Integration.Tests.Notifications;

/// <summary>
/// Pure unit tests (no containers) for Web Push endpoint validation, the exact key-shape checks
/// (65-byte uncompressed P-256 point / 16-byte auth secret), and the title copy mapping.
/// </summary>
public sealed class WebPushRegistrationTests
{
    private static readonly string[] Allowed = new WebPushOptions().AllowedEndpointHosts;

    // Spec-shaped keys: p256dh = 0x04 ‖ 64 bytes (uncompressed P-256 point), auth = 16 bytes.
    private static readonly string P256 = B64Url([0x04, .. Enumerable.Range(1, 64).Select(i => (byte)i)]);
    private static readonly string Auth = B64Url([.. Enumerable.Range(1, 16).Select(i => (byte)i)]);

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc123")]
    [InlineData("https://android.googleapis.com.fcm.googleapis.com/x")] // subdomain of an allowed host
    [InlineData("https://web.push.apple.com/QA")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/xyz")]
    [InlineData("https://sub.region.notify.windows.com/w")]
    public void Accepts_https_endpoints_on_allowlisted_hosts(string endpoint)
    {
        var result = WebPushRegistration.Validate(new WebPushSubscriptionInput(endpoint, P256, Auth), Allowed);
        Assert.Equal(WebPushValidationError.None, result);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/x")]              // not https
    [InlineData("ftp://fcm.googleapis.com/x")]               // not https
    [InlineData("not-a-url")]                                 // not absolute
    [InlineData("")]                                          // empty
    public void Rejects_non_https_or_malformed_endpoints(string endpoint)
    {
        var result = WebPushRegistration.Validate(new WebPushSubscriptionInput(endpoint, P256, Auth), Allowed);
        Assert.Equal(WebPushValidationError.EndpointInvalid, result);
    }

    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data")] // link-local private host (SSRF)
    [InlineData("https://localhost/x")]                       // loopback
    [InlineData("https://evil.fcm.googleapis.com.attacker.net/x")] // suffix-lookalike, not a real subdomain
    [InlineData("https://push.example.com/x")]                // arbitrary host
    public void Rejects_endpoints_off_the_allowlist(string endpoint)
    {
        var result = WebPushRegistration.Validate(new WebPushSubscriptionInput(endpoint, P256, Auth), Allowed);
        Assert.Equal(WebPushValidationError.EndpointHostNotAllowed, result);
    }

    [Fact]
    public void Rejects_keys_with_the_wrong_shape()
    {
        const string endpoint = "https://fcm.googleapis.com/fcm/send/abc";

        WebPushValidationError Check(string p256, string auth) =>
            WebPushRegistration.Validate(new WebPushSubscriptionInput(endpoint, p256, auth), Allowed);

        // The exact spec shapes pass.
        Assert.Equal(WebPushValidationError.None, Check(P256, Auth));

        // Empty / undecodable.
        Assert.Equal(WebPushValidationError.KeyInvalid, Check("", Auth));
        Assert.Equal(WebPushValidationError.KeyInvalid, Check(P256, ""));
        Assert.Equal(WebPushValidationError.KeyInvalid, Check("!!!not base64!!!", Auth));
        Assert.Equal(WebPushValidationError.KeyInvalid, Check(P256, "@@@"));

        // p256dh must be exactly 65 bytes and start with 0x04 (uncompressed point).
        Assert.Equal(WebPushValidationError.KeyInvalid,
            Check(B64Url([0x04, .. Enumerable.Range(1, 31).Select(i => (byte)i)]), Auth)); // 32 bytes
        Assert.Equal(WebPushValidationError.KeyInvalid,
            Check(B64Url([0x04, .. Enumerable.Range(1, 65).Select(i => (byte)i)]), Auth)); // 66 bytes
        Assert.Equal(WebPushValidationError.KeyInvalid,
            Check(B64Url([0x05, .. Enumerable.Range(1, 64).Select(i => (byte)i)]), Auth)); // wrong prefix

        // auth must be exactly 16 bytes.
        Assert.Equal(WebPushValidationError.KeyInvalid,
            Check(P256, B64Url([.. Enumerable.Range(1, 12).Select(i => (byte)i)]))); // 12 bytes
        Assert.Equal(WebPushValidationError.KeyInvalid,
            Check(P256, B64Url([.. Enumerable.Range(1, 32).Select(i => (byte)i)]))); // 32 bytes
    }

    [Fact]
    public void Base64url_decode_accepts_url_alphabet_and_missing_padding()
    {
        Assert.True(WebPushRegistration.TryDecodeBase64Url("BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkTrsT5Y6Yg", out var bytes));
        Assert.NotEmpty(bytes);
        Assert.False(WebPushRegistration.TryDecodeBase64Url("a", out _)); // impossible base64 length
    }

    [Theory]
    [InlineData(AlertEventKind.NewIncident, "Novo incêndio")]
    [InlineData(AlertEventKind.Escalation, "Incêndio em agravamento")]
    [InlineData(AlertEventKind.Rekindle, "Possível reacendimento")]
    [InlineData(AlertEventKind.Risk, "Risco de incêndio elevado")]
    public void Title_maps_each_kind_to_its_pt_copy(string kind, string expected) =>
        Assert.Equal(expected, WebPushCopy.Title(kind));

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
