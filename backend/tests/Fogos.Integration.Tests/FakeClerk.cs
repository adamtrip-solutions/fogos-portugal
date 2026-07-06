using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fogos.Integration.Tests;

/// <summary>
/// A loopback HTTP server exposing <c>/.well-known/jwks.json</c> for a single in-test RSA key, plus a token
/// mint helper. Reachable by the API's outbound <see cref="IHttpClientFactory"/> over real sockets (the
/// WebApplicationFactory TestServer only intercepts inbound requests). Shared by the Clerk auth and account
/// self-service integration tests — no real Clerk instance required.
/// </summary>
internal sealed class FakeClerk : IAsyncDisposable
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
        var l = new TcpListener(IPAddress.Loopback, 0);
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
