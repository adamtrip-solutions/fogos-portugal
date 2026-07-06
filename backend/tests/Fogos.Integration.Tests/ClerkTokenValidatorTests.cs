using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fogos.Api.Auth;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>
/// Pure unit tests for <see cref="ClerkTokenValidator"/> — no containers. A stubbed
/// <see cref="HttpMessageHandler"/> serves a JWKS derived from an in-test RSA key, so signatures pass
/// and only the claim (or key) under test decides the outcome.
/// </summary>
public sealed class ClerkTokenValidatorTests
{
    private const string Authority = "https://clerk.test.example";
    private const string Kid = "test-kid";

    private readonly RSA _rsa = RSA.Create(2048);

    [Fact]
    public async Task Valid_token_returns_claims()
    {
        var (validator, _) = Build();
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["email"] = "a@b.pt",
            ["name"] = "Ana",
            ["nbf"] = Now(),
            ["exp"] = Now(300),
        });

        var claims = await validator.ValidateAsync(token);

        Assert.NotNull(claims);
        Assert.Equal("user_abc", claims!.ClerkUserId);
        Assert.Equal("a@b.pt", claims.Email);
        Assert.Equal("Ana", claims.Name);
    }

    [Fact]
    public async Task Sparse_token_tolerates_missing_email_and_name()
    {
        var (validator, _) = Build();
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_sparse",
            ["exp"] = Now(300),
        });

        var claims = await validator.ValidateAsync(token);

        Assert.NotNull(claims);
        Assert.Equal("user_sparse", claims!.ClerkUserId);
        Assert.Null(claims.Email);
        Assert.Null(claims.Name);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (validator, _) = Build();
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["exp"] = Now(-10),
        });

        Assert.Null(await validator.ValidateAsync(token));
    }

    [Fact]
    public async Task Token_without_exp_is_rejected()
    {
        var (validator, _) = Build();
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            // no exp — such a token would never expire.
        });

        Assert.Null(await validator.ValidateAsync(token));
    }

    [Fact]
    public async Task Wrong_issuer_is_rejected()
    {
        var (validator, _) = Build();
        var token = Mint(new()
        {
            ["iss"] = "https://evil.example",
            ["sub"] = "user_abc",
            ["exp"] = Now(300),
        });

        Assert.Null(await validator.ValidateAsync(token));
    }

    [Fact]
    public async Task Wrong_azp_is_rejected_when_authorized_parties_configured()
    {
        var (validator, _) = Build(authorizedParties: ["https://app.fogos.pt"]);
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["azp"] = "https://evil.example",
            ["exp"] = Now(300),
        });

        Assert.Null(await validator.ValidateAsync(token));
    }

    [Fact]
    public async Task Matching_azp_is_accepted()
    {
        var (validator, _) = Build(authorizedParties: ["https://app.fogos.pt"]);
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["azp"] = "https://app.fogos.pt",
            ["exp"] = Now(300),
        });

        Assert.NotNull(await validator.ValidateAsync(token));
    }

    [Fact]
    public async Task Garbage_token_is_rejected()
    {
        var (validator, _) = Build();
        Assert.Null(await validator.ValidateAsync("not.a.jwt"));
        Assert.Null(await validator.ValidateAsync("garbage"));
    }

    [Fact]
    public async Task Signature_from_a_different_key_is_rejected()
    {
        var (validator, _) = Build();
        using var other = RSA.Create(2048);
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["exp"] = Now(300),
        }, signWith: other);

        Assert.Null(await validator.ValidateAsync(token));
    }

    [Fact]
    public async Task Disabled_when_authority_empty()
    {
        var (validator, handler) = Build(authority: "");
        var token = Mint(new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["exp"] = Now(300),
        });

        Assert.Null(await validator.ValidateAsync(token));
        Assert.Equal(0, handler.Calls); // never even fetched JWKS
    }

    [Fact]
    public async Task Unknown_kid_refetch_is_throttled()
    {
        var (validator, handler) = Build();

        // A token with an unknown kid. First call fetches JWKS once and fails to find the key.
        var token = MintWithKid("unknown-kid", new()
        {
            ["iss"] = Authority,
            ["sub"] = "user_abc",
            ["exp"] = Now(300),
        });

        Assert.Null(await validator.ValidateAsync(token));
        Assert.Null(await validator.ValidateAsync(token));
        Assert.Null(await validator.ValidateAsync(token));

        // Fetched at most once despite three unknown-kid tokens (throttled refetch).
        Assert.Equal(1, handler.Calls);
    }

    private (ClerkTokenValidator, CountingHandler) Build(
        string authority = Authority,
        List<string>? authorizedParties = null)
    {
        var options = new ClerkOptions
        {
            Authority = authority,
            JwksUrl = "https://clerk.test.example/.well-known/jwks.json",
            AuthorizedParties = authorizedParties ?? [],
            JwksCacheMinutes = 60,
        };
        var handler = new CountingHandler(JwksJson());
        var factory = new StubHttpClientFactory(handler);
        var validator = new ClerkTokenValidator(
            Options.Create(options), factory, NullLogger<ClerkTokenValidator>.Instance);
        return (validator, handler);
    }

    private string JwksJson()
    {
        var p = _rsa.ExportParameters(false);
        var jwks = new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid = Kid,
                    n = Base64Url(p.Modulus!),
                    e = Base64Url(p.Exponent!),
                },
            },
        };
        return JsonSerializer.Serialize(jwks);
    }

    private string Mint(Dictionary<string, object> payload, RSA? signWith = null) =>
        MintWithKid(Kid, payload, signWith);

    private string MintWithKid(string kid, Dictionary<string, object> payload, RSA? signWith = null)
    {
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = kid };
        var signingInput = $"{Encode(header)}.{Encode(payload)}";
        var signer = signWith ?? _rsa;
        var signature = signer.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static long Now(int deltaSeconds = 0) => DateTimeOffset.UtcNow.AddSeconds(deltaSeconds).ToUnixTimeSeconds();

    private static string Encode(Dictionary<string, object> map) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(map));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CountingHandler(string json) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
