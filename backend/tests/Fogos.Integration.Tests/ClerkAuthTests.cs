using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fogos.Domain.Auth;
using Fogos.Domain.Users;
using Fogos.Infrastructure.Mongo;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests;

/// <summary>
/// End-to-end Clerk Bearer path without a real Clerk instance: <see cref="FakeClerk"/> is a loopback
/// JWKS server signing tokens with an in-test RSA key, wired in through <c>fixture.CreateFactory</c>
/// config overrides (Clerk:Authority + Clerk:JwksUrl). Proves the token → validate → provision → me
/// flow, the hard-401 contract, and that the machine-JWT path is untouched when Clerk is enabled.
/// </summary>
[Collection("fogos")]
public sealed class ClerkAuthTests(ContainerFixture fixture)
{
    private const string Azp = "https://app.fogos.pt";

    [SkippableFact]
    public async Task Valid_clerk_token_authenticates_and_provisions_a_single_user()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);
        var mongo = factory.Services.GetRequiredService<MongoContext>();

        var token = clerk.Mint(sub: "user_alice", email: "alice@fogos.pt", name: "Alice", azp: Azp);

        // First call — provisions the user.
        var first = await MeAsync(factory, token);
        Assert.Equal(HttpStatusCode.OK, first.status);
        var me = first.doc!.RootElement.GetProperty("data").GetProperty("me");
        Assert.Equal("alice@fogos.pt", me.GetProperty("email").GetString());
        Assert.Equal("Alice", me.GetProperty("name").GetString());
        Assert.Equal("User", me.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(me.GetProperty("id").GetString()));

        // Second call — same identity, no duplicate document.
        var second = await MeAsync(factory, token);
        Assert.Equal(HttpStatusCode.OK, second.status);

        var count = await mongo.Users.CountDocumentsAsync(
            Builders<User>.Filter.Eq(u => u.ClerkUserId, "user_alice"));
        Assert.Equal(1, count);
    }

    [SkippableFact]
    public async Task Expired_wrong_azp_unknown_iss_and_garbage_are_401()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);

        var expired = clerk.Mint(sub: "user_a", azp: Azp, expiresInSeconds: -30);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(factory, expired)).status);

        var wrongAzp = clerk.Mint(sub: "user_a", azp: "https://evil.example");
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(factory, wrongAzp)).status);

        var unknownIss = clerk.Mint(sub: "user_a", azp: Azp, issuer: "https://evil.example");
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(factory, unknownIss)).status);

        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(factory, "not.a.jwt")).status);
    }

    [SkippableFact]
    public async Task Machine_jwt_still_works_when_clerk_is_enabled()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var firstPartyKey = "fgs_live_firstparty_clerk_0009";
        await SeedData.InsertApiKeyAsync(fixture, firstPartyKey, ApiTier.FirstParty, name: "web");

        await using var clerk = await FakeClerk.StartAsync();
        var factory = ClerkFactory(clerk);
        var client = factory.CreateClient();

        // Self-issued machine token, minted through the real /auth/token exchange.
        var tokenResponse = await client.PostAsJsonAsync("/auth/token", new { apiKey = firstPartyKey });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var machineToken = (await tokenResponse.Content.ReadFromJsonAsync<TokenBody>())!.AccessToken;

        // The self-issued Bearer still routes to JwtService and works; `me` is null for a machine caller.
        var (status, doc) = await MeAsync(factory, machineToken);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, doc!.RootElement.GetProperty("data").GetProperty("me").ValueKind);
    }

    private WebApplicationFactory<Program> ClerkFactory(FakeClerk clerk) =>
        fixture.CreateFactory(new()
        {
            ["Clerk:Authority"] = clerk.Authority,
            ["Clerk:JwksUrl"] = clerk.JwksUrl,
            ["Clerk:AuthorizedParties:0"] = Azp,
            ["Clerk:JwksCacheMinutes"] = "60",
        });

    private static async Task<(HttpStatusCode status, JsonDocument? doc)> MeAsync(
        WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ me { id email name role } }" });
        if (response.StatusCode != HttpStatusCode.OK)
            return (response.StatusCode, null);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return (response.StatusCode, doc);
    }

    private sealed record TokenBody(string AccessToken, int ExpiresIn, string RefreshToken);

    /// <summary>
    /// A loopback HTTP server exposing <c>/.well-known/jwks.json</c> for a single in-test RSA key,
    /// plus a token mint helper. Reachable by the API's outbound <see cref="IHttpClientFactory"/> over
    /// real sockets (the WebApplicationFactory TestServer only intercepts inbound requests).
    /// </summary>
    private sealed class FakeClerk : IAsyncDisposable
    {
        private const string Kid = "clerk-test-kid";

        private readonly RSA _rsa = RSA.Create(2048);
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public string Authority { get; }

        public string JwksUrl => $"{Authority}/.well-known/jwks.json";

        private FakeClerk(int port)
        {
            Authority = $"http://localhost:{port}";
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _loop = Task.Run(AcceptLoopAsync);
        }

        public static Task<FakeClerk> StartAsync() => Task.FromResult(new FakeClerk(FreePort()));

        public string Mint(
            string sub,
            string? email = null,
            string? name = null,
            string? azp = null,
            string? issuer = null,
            int expiresInSeconds = 300)
        {
            var now = DateTimeOffset.UtcNow;
            var payload = new Dictionary<string, object>
            {
                ["iss"] = issuer ?? Authority,
                ["sub"] = sub,
                ["nbf"] = now.ToUnixTimeSeconds(),
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddSeconds(expiresInSeconds).ToUnixTimeSeconds(),
            };
            if (email is not null) payload["email"] = email;
            if (name is not null) payload["name"] = name;
            if (azp is not null) payload["azp"] = azp;

            var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = Kid };
            var signingInput = $"{Encode(header)}.{Encode(payload)}";
            var signature = _rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{signingInput}.{Base64Url(signature)}";
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return; // listener stopped
                }

                var body = Encoding.UTF8.GetBytes(JwksJson());
                ctx.Response.ContentType = "application/json";
                ctx.Response.StatusCode = 200;
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            }
        }

        private string JwksJson()
        {
            var p = _rsa.ExportParameters(false);
            return JsonSerializer.Serialize(new
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
            });
        }

        private static int FreePort()
        {
            var l = new TcpListener(System.Net.IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private static string Encode(Dictionary<string, object> map) => Base64Url(JsonSerializer.SerializeToUtf8Bytes(map));

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Close();
            try { await _loop; } catch { /* ignore */ }
            _rsa.Dispose();
            _cts.Dispose();
        }
    }
}
