using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Domain.Auth;

namespace Fogos.Integration.Tests;

[Collection("fogos")]
public sealed class AuthTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task ApiKey_authenticates_and_revoked_key_is_401()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var goodKey = "fgs_live_registered_good_0001";
        await SeedData.InsertApiKeyAsync(fixture, goodKey, ApiTier.Registered);

        var revokedKey = "fgs_live_registered_revoked_002";
        await SeedData.InsertApiKeyAsync(fixture, revokedKey, ApiTier.Registered, revoked: true);

        var client = fixture.Factory.CreateClient();

        var good = new HttpRequestMessage(HttpMethod.Get, "/v3/incidents/active.geojson");
        good.Headers.Add("X-API-Key", goodKey);
        var goodResponse = await client.SendAsync(good);
        Assert.Equal(HttpStatusCode.OK, goodResponse.StatusCode);

        var revoked = new HttpRequestMessage(HttpMethod.Get, "/v3/incidents/active.geojson");
        revoked.Headers.Add("X-API-Key", revokedKey);
        var revokedResponse = await client.SendAsync(revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    [SkippableFact]
    public async Task PublicContext_key_pins_origin()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var key = "fgs_live_public_site_key_00003";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.Registered,
            publicContext: true, allowedOrigins: ["https://fogos.pt", "*.fogos.pt"]);

        var client = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, await OriginStatus(client, key, "https://evil.example"));
        Assert.Equal(HttpStatusCode.OK, await OriginStatus(client, key, "https://fogos.pt"));
        Assert.Equal(HttpStatusCode.OK, await OriginStatus(client, key, "https://app.fogos.pt")); // wildcard subdomain
    }

    [SkippableFact]
    public async Task Token_endpoint_gates_by_tier_and_issues_working_jwt()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var registeredKey = "fgs_live_registered_notoken_04";
        await SeedData.InsertApiKeyAsync(fixture, registeredKey, ApiTier.Registered);

        var firstPartyKey = "fgs_live_firstparty_token_0005";
        await SeedData.InsertApiKeyAsync(fixture, firstPartyKey, ApiTier.FirstParty, name: "web");

        var client = fixture.Factory.CreateClient();

        // Registered tier may not exchange keys for tokens.
        var registeredResponse = await client.PostAsJsonAsync("/auth/token", new { apiKey = registeredKey });
        Assert.Equal(HttpStatusCode.Forbidden, registeredResponse.StatusCode);

        // First-party gets tokens.
        var tokenResponse = await client.PostAsJsonAsync("/auth/token", new { apiKey = firstPartyKey });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenBody>();
        Assert.False(string.IsNullOrEmpty(token!.AccessToken));
        Assert.False(string.IsNullOrEmpty(token.RefreshToken));
        Assert.True(token.ExpiresIn > 0);

        // The JWT works as a Bearer on GraphQL.
        var authed = fixture.Factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var graph = await authed.PostAsJsonAsync("/graphql", new { query = "{ __typename }" });
        Assert.Equal(HttpStatusCode.OK, graph.StatusCode);
        using var graphDoc = JsonDocument.Parse(await graph.Content.ReadAsStringAsync());
        Assert.Equal("Query", graphDoc.RootElement.GetProperty("data").GetProperty("__typename").GetString());

        // Garbage JWT → 401.
        var garbage = fixture.Factory.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");
        var garbageResponse = await garbage.GetAsync("/v3/incidents/active.geojson");
        Assert.Equal(HttpStatusCode.Unauthorized, garbageResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Refresh_rotation_is_single_use_and_reuse_revokes_family()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var key = "fgs_live_firstparty_refresh_06";
        await SeedData.InsertApiKeyAsync(fixture, key, ApiTier.FirstParty);
        var client = fixture.Factory.CreateClient();

        var first = (await (await client.PostAsJsonAsync("/auth/token", new { apiKey = key }))
            .Content.ReadFromJsonAsync<TokenBody>())!;

        // Rotate R1 → R2.
        var rotateResponse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotateResponse.StatusCode);
        var second = (await rotateResponse.Content.ReadFromJsonAsync<TokenBody>())!;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        // Reusing the consumed R1 → 401, and it revokes the whole family.
        var reuse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // The newer R2 is now dead too.
        var afterRevoke = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [SkippableFact]
    public async Task Scope_policy_passes_for_operator_and_fails_for_registered()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);

        var operatorKey = "fgs_live_operator_moderate_07";
        await SeedData.InsertApiKeyAsync(fixture, operatorKey, ApiTier.Operator, scopes: [ApiScopes.ModeratePhotos]);

        var registeredKey = "fgs_live_registered_noscope_08";
        await SeedData.InsertApiKeyAsync(fixture, registeredKey, ApiTier.Registered);

        var client = fixture.Factory.CreateClient();

        var operatorReq = new HttpRequestMessage(HttpMethod.Get, "/auth/check/moderate:photos");
        operatorReq.Headers.Add("X-API-Key", operatorKey);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(operatorReq)).StatusCode);

        var registeredReq = new HttpRequestMessage(HttpMethod.Get, "/auth/check/moderate:photos");
        registeredReq.Headers.Add("X-API-Key", registeredKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(registeredReq)).StatusCode);
    }

    private static async Task<HttpStatusCode> OriginStatus(HttpClient client, string apiKey, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v3/incidents/active.geojson");
        request.Headers.Add("X-API-Key", apiKey);
        request.Headers.Add("Origin", origin);
        return (await client.SendAsync(request)).StatusCode;
    }

    private sealed record TokenBody(string AccessToken, int ExpiresIn, string RefreshToken);
}
