using System.Text;
using System.Text.Json;
using Fogos.Api.Auth;
using Fogos.Domain.Auth;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;

namespace Fogos.Api.GraphQL.RateLimiting;

/// <summary>
/// The second GraphQL guard rail: a per-caller, per-window operation-cost budget. Sits in front of the
/// <c>/graphql</c> POST handler, computes the deterministic cost of the operation, debits it in Redis,
/// and — when the budget is exceeded — returns a GraphQL error with code <c>RATE_LIMITED</c> (HTTP 200,
/// GraphQL convention) <i>without</i> executing. Anything it cannot read (websocket upgrades, GETs,
/// non-JSON) is passed straight through; subscriptions are governed by the socket interceptor instead.
/// </summary>
public sealed class GraphQLCostMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options)
{
    private readonly RateLimitOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, IFogosCallerAccessor callerAccessor, GraphQLCostBudget budget)
    {
        if (!ShouldInspect(context))
        {
            await next(context);
            return;
        }

        var (query, operationName) = await ReadOperationAsync(context);
        if (query is null)
        {
            await next(context);
            return;
        }

        var cost = GraphQLCostCalculator.Compute(query, operationName);
        var caller = callerAccessor.Caller;
        var tierBudget = _options.For(caller.Tier).CostBudget;
        var partition = RateLimitPartition.For(caller);

        var decision = await budget.DebitAsync(partition, cost, tierBudget, _options.WindowSeconds);
        if (!decision.Allowed)
        {
            await WriteRateLimited(context, decision);
            return;
        }

        await next(context);
    }

    private static bool ShouldInspect(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.StartsWithSegments("/graphql") &&
        (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);

    private static async Task<(string? Query, string? OperationName)> ReadOperationAsync(HttpContext context)
    {
        try
        {
            // Buffer the body into a fresh, rewound MemoryStream and swap it in, so the GraphQL handler
            // reads a clean stream from position 0 (avoids EnableBuffering/PipeReader read-position pitfalls).
            var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
            buffer.Position = 0;
            context.Request.Body = buffer;

            var raw = Encoding.UTF8.GetString(buffer.ToArray());
            buffer.Position = 0;

            if (string.IsNullOrWhiteSpace(raw))
                return (null, null);

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null); // batched operations — let HotChocolate handle; base cost not applied.

            var query = doc.RootElement.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
                ? q.GetString()
                : null;
            var opName = doc.RootElement.TryGetProperty("operationName", out var o) && o.ValueKind == JsonValueKind.String
                ? o.GetString()
                : null;
            return (query, opName);
        }
        catch
        {
            return (null, null); // fail open on any read/parse issue.
        }
    }

    private static Task WriteRateLimited(HttpContext context, CostDecision decision)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/graphql-response+json; charset=utf-8";
        context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString();

        var payload = new
        {
            errors = new[]
            {
                new
                {
                    message = "GraphQL operation cost budget exceeded.",
                    extensions = new
                    {
                        code = "RATE_LIMITED",
                        cost = decision.Cost,
                        budget = decision.Budget,
                        retryAfterSeconds = decision.RetryAfterSeconds,
                    },
                },
            },
        };
        return context.Response.WriteAsJsonAsync(payload);
    }
}
