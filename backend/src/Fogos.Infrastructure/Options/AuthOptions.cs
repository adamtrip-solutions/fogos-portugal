namespace Fogos.Infrastructure.Options;

/// <summary>
/// Self-issued JWT settings. When <see cref="RsaPrivateKeyPem"/> is unset an ephemeral RSA
/// key is generated at startup (dev only — a loud warning is logged and tokens die on restart).
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; set; } = "fogos.pt";
    public string Audience { get; set; } = "fogos-api";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>An RSA private key: either a PEM string, or a filesystem path to a PEM file.</summary>
    public string? RsaPrivateKeyPem { get; set; }
}
