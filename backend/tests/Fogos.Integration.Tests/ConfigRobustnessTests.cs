using System.Net;

namespace Fogos.Integration.Tests;

/// <summary>
/// Malformed operator-supplied config must never take the API down: a bad Sentry DSN or an
/// unparseable Auth RSA key should degrade gracefully (Sentry off / ephemeral key) and still boot.
/// </summary>
[Collection("fogos")]
public sealed class ConfigRobustnessTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Boots_and_serves_healthz_when_Sentry_DSN_is_not_a_valid_uri()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        // A non-URI DSN previously crashed the host at boot (Sentry.Dsn.Parse threw
        // UriFormatException from the DI container). The guard must skip Sentry and boot.
        using var factory = fixture.CreateFactory(new()
        {
            ["Sentry:Dsn"] = "not-a-uri",
        });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz/live");

        response.EnsureSuccessStatusCode();
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [SkippableFact]
    public async Task Serves_requests_when_Auth_RSA_key_is_not_valid_pem()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        // A garbage PEM (or a container-nonexistent file path) previously threw from
        // JwtService.ImportFromPem on the first request — 500-ing every request via the auth
        // middleware. The service must fall back to an ephemeral key and keep serving.
        using var factory = fixture.CreateFactory(new()
        {
            ["Auth:RsaPrivateKeyPem"] = "garbage-not-a-pem",
        });

        var client = factory.CreateClient();

        // healthz bypasses auth; a GraphQL POST exercises the auth middleware end-to-end.
        var live = await client.GetAsync("/healthz/live");
        live.EnsureSuccessStatusCode();

        var graphql = await client.GetAsync("/auth/jwks");
        Assert.NotEqual(HttpStatusCode.InternalServerError, graphql.StatusCode);
    }
}
