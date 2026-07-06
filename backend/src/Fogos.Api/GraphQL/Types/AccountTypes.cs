namespace Fogos.Api.GraphQL.Types;

/// <summary>
/// The signed-in user's own identity — the minimal <c>me</c> payload (PR2 extends this with keys,
/// webhooks, and subscriptions). Null for machine callers and anonymous requests.
/// </summary>
public sealed record Me(string Id, string? Email, string? Name, string Role);
