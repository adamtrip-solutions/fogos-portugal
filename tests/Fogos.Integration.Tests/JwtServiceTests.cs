using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fogos.Api.Auth;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>
/// Pure unit tests for <see cref="JwtService"/> lifetime validation — no containers. Mints tokens with
/// the same RSA key the service loads, so signatures pass and only the claim under test decides the outcome.
/// </summary>
public sealed class JwtServiceTests
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly JwtService _jwt;
    private readonly AuthOptions _options = new();

    public JwtServiceTests()
    {
        _options.RsaPrivateKeyPem = _rsa.ExportRSAPrivateKeyPem();
        _jwt = new JwtService(Options.Create(_options), NullLogger<JwtService>.Instance);
    }

    [Fact]
    public void Token_without_exp_is_rejected()
    {
        var token = Mint(new Dictionary<string, object>
        {
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["sub"] = "client-1",
            ["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            // no exp — such a token would never expire.
        });

        Assert.False(_jwt.TryValidate(token, out var claims));
        Assert.Null(claims);
    }

    [Fact]
    public void Token_with_non_numeric_exp_is_rejected()
    {
        var token = Mint(new Dictionary<string, object>
        {
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["sub"] = "client-1",
            ["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = "not-a-number",
        });

        Assert.False(_jwt.TryValidate(token, out _));
    }

    [Fact]
    public void Token_with_valid_exp_is_accepted()
    {
        var token = Mint(new Dictionary<string, object>
        {
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["sub"] = "client-1",
            ["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(),
        });

        Assert.True(_jwt.TryValidate(token, out var claims));
        Assert.Equal("client-1", claims!.ClientId);
    }

    private string Mint(Dictionary<string, object> payload)
    {
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = "test" };
        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signature = _rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static string Encode(Dictionary<string, object> map) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(map));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
