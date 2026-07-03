using Fogos.Infrastructure.DependencyInjection;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Sentry only when a DSN is configured — dev runs without it.
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(o => o.Dsn = sentryDsn);
}

builder.Services.AddFogosInfrastructure(builder.Configuration);

var app = builder.Build();

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
