using Fogos.Api.Auth;
using Fogos.Api.GraphQL;
using Fogos.Api.GraphQL.RateLimiting;
using Fogos.Api.Rest;
using Fogos.Infrastructure.DependencyInjection;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Sentry only when a DSN is configured — dev runs without it. A malformed DSN must never
// kill the process: Sentry.Dsn.Parse throws UriFormatException from the DI container, so we
// validate the URI up front and boot without Sentry when it is invalid.
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    if (Uri.TryCreate(sentryDsn, UriKind.Absolute, out _))
    {
        builder.WebHost.UseSentry(o => o.Dsn = sentryDsn);
    }
    else
    {
        Console.Error.WriteLine(
            $"Sentry DSN is set but not a valid URI — Sentry disabled: '{sentryDsn}'");
    }
}

builder.Services.AddFogosInfrastructure(builder.Configuration);

// Verify-and-heal the static `locations` reference table at startup (see SeedGuard: prod once ran for days
// with an empty table and silently skipped every incident). Idempotent; safe to run in both API and Worker.
builder.Services.AddHostedService<Fogos.Infrastructure.Seeding.SeedGuard>();

builder.Services.AddFogosAuth();
builder.Services.AddFogosRateLimiting(builder.Configuration);

// GraphQL (+ Redis subscriptions) only when Redis is configured. A plain `dotnet build`
// and `--info`-style runs must not require a running Redis; integration tests supply it.
var redisConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Redis:ConnectionString"]);
if (redisConfigured)
{
    builder.Services.AddFogosGraphQL();
}

var app = builder.Build();

// Resolve caller identity (JWT → API key → anonymous) and pin Origins for every request.
app.UseMiddleware<AuthenticationMiddleware>();

// Request-rate + GraphQL cost budget (both Redis-backed, fail-open) run only where Redis exists.
if (redisConfigured)
{
    app.UseMiddleware<RateLimitMiddleware>();
    app.UseMiddleware<GraphQLCostMiddleware>();
}

// REST auth surface (token exchange, refresh rotation, JWKS, scope probe).
app.MapAuth();

// REST v3 format outputs (only need Mongo) are always available.
app.MapV3();

// RSS / GeoRSS feeds (incidents + warnings).
app.MapFeeds();

// Situation reports (latest, JSON).
app.MapReports();

// Citizen photo submission + public listing (anonymous upload behind the abuse gates).
app.MapPhotos();

if (redisConfigured)
{
    app.UseWebSockets();
    app.MapGraphQL();
}

app.MapGet("/healthz/live", () => Results.Text("ok"));

app.MapGet("/healthz/ready", async (MongoContext mongo, IConnectionMultiplexer redis, CancellationToken ct) =>
{
    var mongoOk = false;
    var redisOk = false;

    try
    {
        await mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
        mongoOk = true;
    }
    catch
    {
        // reported below
    }

    try
    {
        await redis.GetDatabase().PingAsync();
        redisOk = true;
    }
    catch
    {
        // reported below
    }

    var ready = mongoOk && redisOk;
    return Results.Json(
        new { status = ready ? "ready" : "unhealthy", mongo = mongoOk, redis = redisOk },
        statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

// Build indexes at startup: background and non-fatal — a slow/absent Mongo must not block boot.
_ = Task.Run(async () =>
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var initializer = scope.ServiceProvider.GetRequiredService<MongoIndexInitializer>();
    var ops = scope.ServiceProvider.GetRequiredService<IOpsNotifier>();
    try
    {
        await initializer.EnsureIndexesAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Mongo index initialization failed");
        await ops.ErrorAsync($"Mongo index initialization failed: {ex.Message}");
    }
});

app.Run();

/// <summary>Exposed so the integration tests can boot the API through <c>WebApplicationFactory</c>.</summary>
public partial class Program;
