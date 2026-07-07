using Fogos.Domain.Alerts;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;

namespace Fogos.Integration.Tests.Notifications;

/// <summary>
/// Pure unit tests (no containers) for Web Push endpoint validation, base64url key handling, and the
/// title copy mapping.
/// </summary>
public sealed class WebPushRegistrationTests
{
    private static readonly string[] Allowed = new WebPushOptions().AllowedEndpointHosts;

    // A valid base64url P-256 point / auth secret (shape only — decodability is what's checked).
    private const string P256 = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkTrsT5Y6Yg";
    private const string Auth = "k8JV6sjdbhAi";

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

    [Theory]
    [InlineData("", Auth)]                          // empty p256dh
    [InlineData(P256, "")]                          // empty auth
    [InlineData("!!!not base64!!!", Auth)]          // undecodable
    [InlineData(P256, "@@@")]                        // undecodable
    public void Rejects_non_base64url_keys(string p256, string auth)
    {
        var endpoint = "https://fcm.googleapis.com/fcm/send/abc";
        var result = WebPushRegistration.Validate(new WebPushSubscriptionInput(endpoint, p256, auth), Allowed);
        Assert.Equal(WebPushValidationError.KeyInvalid, result);
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
}
