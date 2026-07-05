using Fogos.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Fogos.Api.Auth;

/// <summary>REST auth surface: API-key → JWT exchange, refresh rotation, and the public JWK set.</summary>
public static class AuthEndpoints
{
    public sealed record TokenRequest(string? ApiKey);
    public sealed record RefreshRequest(string? RefreshToken);
    public sealed record TokenResponse(string AccessToken, int ExpiresIn, string RefreshToken);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        // Exchange an API key for a short-lived JWT + refresh token. Only server-held tiers may exchange.
        app.MapPost("/auth/token", async (
            [FromBody] TokenRequest body,
            ApiKeyResolver apiKeys,
            JwtService jwt,
            RefreshTokenStore refresh,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.ApiKey))
                return Results.Json(new { error = "invalid_request", message = "apiKey is required." }, statusCode: 400);

            var client = await apiKeys.ResolveAsync(body.ApiKey, ct);
            if (client is null || client.IsRevoked)
                return Results.Json(new { error = "invalid_api_key", message = "The API key is unknown or revoked." }, statusCode: 401);

            if (client.Tier is not (ApiTier.FirstParty or ApiTier.Operator))
                return Results.Json(new { error = "tier_forbidden", message = "This tier cannot exchange keys for tokens." }, statusCode: 403);

            var response = await IssueAsync(jwt, refresh, client, RefreshTokenStore.NewFamily());
            return Results.Ok(response);
        });

        // Rotate a refresh token: new access + new refresh, old one consumed; replay revokes the family.
        app.MapPost("/auth/refresh", async (
            [FromBody] RefreshRequest body,
            ApiKeyResolver apiKeys,
            JwtService jwt,
            RefreshTokenStore refresh,
            Fogos.Infrastructure.Mongo.MongoContext mongo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.RefreshToken))
                return Results.Json(new { error = "invalid_request", message = "refreshToken is required." }, statusCode: 400);

            var consumed = await refresh.ConsumeAsync(body.RefreshToken);
            if (consumed.Outcome != RefreshOutcome.Valid || consumed.ClientId is null || consumed.Family is null)
                return Results.Json(new { error = "invalid_grant", message = "The refresh token is invalid, expired, or reused." }, statusCode: 401);

            var client = await mongo.ApiClients
                .Find(MongoDB.Driver.Builders<ApiClient>.Filter.Eq(x => x.Id, consumed.ClientId))
                .FirstOrDefaultAsync(ct);
            if (client is null || client.IsRevoked)
                return Results.Json(new { error = "invalid_grant", message = "The credential is no longer valid." }, statusCode: 401);

            var response = await IssueAsync(jwt, refresh, client, consumed.Family);
            return Results.Ok(response);
        });

        // Public JWK set for verifying self-issued tokens (exempt from rate limiting).
        app.MapGet("/auth/jwks", (JwtService jwt) => Results.Ok(jwt.Jwks()));

        // Scope-policy probe (used by tests + ops): 200 when the caller satisfies the policy, else 401/403.
        app.MapGet("/auth/check/{scope}", async (string scope, HttpContext ctx, IAuthorizationService authz) =>
        {
            if (!ApiScopes.All.Contains(scope))
                return Results.Json(new { error = "unknown_scope" }, statusCode: 400);

            var result = await authz.AuthorizeAsync(ctx.User, scope);
            return result.Succeeded
                ? Results.Ok(new { ok = true, scope })
                : Results.Json(new { ok = false, scope }, statusCode: 403);
        });
    }

    private static async Task<TokenResponse> IssueAsync(JwtService jwt, RefreshTokenStore refresh, ApiClient client, string family)
    {
        var access = jwt.Issue(client.Id, client.Name, client.Tier, client.Scopes);
        var refreshToken = await refresh.IssueAsync(client.Id, family);
        return new TokenResponse(access, jwt.AccessTokenSeconds, refreshToken.PlainText);
    }
}
