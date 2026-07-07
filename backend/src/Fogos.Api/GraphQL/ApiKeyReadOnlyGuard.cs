using Fogos.Api.Auth;
using Fogos.Domain.Auth;
using HotChocolate.Execution;
using HotChocolate.Language;

namespace Fogos.Api.GraphQL;

/// <summary>
/// Central write-guard for issued API keys: a machine credential (<see cref="FogosCaller.ClientId"/> set)
/// that is neither first-party nor scope-bearing is read-only, so any GraphQL <b>mutation</b> operation it
/// sends is rejected with <c>API_KEY_READ_ONLY</c> before execution. Everyone else proceeds: anonymous
/// callers (public site flows), signed-in users, the first-party web key (which performs alert/device
/// mutations on behalf of visitors), and scoped operator keys (whose sensitive mutations remain gated
/// per-field by <c>[Authorize]</c> scope policies).
/// Enforced once, in the HotChocolate request pipeline right after the operation is resolved, so the
/// decision is based on the compiled operation (multi-operation documents, <c>operationName</c> selection
/// and shorthand queries are all handled by HotChocolate itself) and covers every transport — HTTP POST
/// and graphql-ws alike — unlike an ASP.NET middleware on the <c>/graphql</c> endpoint.
/// </summary>
internal static class ApiKeyReadOnlyGuard
{
    /// <summary>Pipeline key of the guard middleware (inserted after the operation resolver).</summary>
    public const string MiddlewareKey = "FogosApiKeyReadOnlyGuard";

    public const string ErrorCode = "API_KEY_READ_ONLY";

    public static ValueTask InvokeAsync(RequestContext context, HotChocolate.Execution.RequestDelegate next)
    {
        if (context.TryGetOperation(out var operation)
            && operation.Definition.Operation == OperationType.Mutation
            && IsReadOnlyMachineCaller(ResolveCaller(context)))
        {
            context.Result = OperationResult.FromError(
                ErrorBuilder.New()
                    .SetMessage("As chaves de API são apenas de leitura.")
                    .SetCode(ErrorCode)
                    .Build());
            return ValueTask.CompletedTask;
        }

        return next(context);
    }

    private static FogosCaller ResolveCaller(RequestContext context)
    {
        // graphql-ws operations carry the connect-payload identity, stamped into the request's global
        // state by SubscriptionSessionInterceptor.OnRequestAsync (the socket upgrade request itself
        // rarely carries credential headers, so the HTTP accessor would see it as anonymous).
        if (context.ContextData.TryGetValue(FogosCallerAccessor.ItemKey, out var stashed)
            && stashed is FogosCaller wsCaller)
        {
            return wsCaller;
        }

        // HTTP operations resolve through the accessor (HttpContext.Items, set by AuthenticationMiddleware).
        return context.RequestServices.GetRequiredService<IFogosCallerAccessor>().Caller;
    }

    private static bool IsReadOnlyMachineCaller(FogosCaller caller) =>
        caller.ClientId is not null
        && caller.Tier != ApiTier.FirstParty
        && caller.Scopes.Count == 0;
}
