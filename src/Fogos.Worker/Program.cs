using Fogos.Infrastructure.DependencyInjection;
using Fogos.Worker.Jobs.Planes;
using Fogos.Worker.Jobs.Weather;
using Fogos.Worker.Queue;
using Fogos.Worker.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFogosInfrastructure(builder.Configuration);

// Pipeline infrastructure: Redis Streams dispatchers, social publishers, FCM, renderer, sources.
builder.Services.AddFogosPipeline(builder.Configuration);

// Redis Streams consumers (default + icnf) + the delayed-dispatch pump (FCM 3-min delay mechanism).
builder.Services.AddQueueWorkers(builder.Configuration);

// Event handlers discovered by DI scanning of the Worker assembly (multiple handlers per event ok).
builder.Services.AddEventHandlers();

// Quartz scheduler. Jobs opt into single-flight uniqueness by extending UniqueJob; triggers use the
// Lisbon-timezone JobScheduleBuilder helpers. The wave registrations land on the marker lines below.
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// Ingestion jobs are registered by the next waves — each agent edits only its own marker line:
builder.Services.AddWeatherJobs(); // [jobs:weather] registered in wave 2
Fogos.Worker.Jobs.Risk.RiskFirmsJobRegistration.AddRiskAndFirmsJobs(builder.Services, builder.Configuration); // [jobs:risk]
builder.Services.AddPlaneJobs(); // [jobs:planes] registered in wave 2
Fogos.Worker.Jobs.Incidents.IncidentJobsRegistration.AddIncidentJobs(builder.Services, builder.Configuration); // [jobs:incidents]
Fogos.Worker.Jobs.Photos.PhotoJobsRegistration.AddPhotoJobs(builder.Services); // [jobs:photos] Phase 4

// Change-stream → subscriptions bridge (publisher side of the SAME Redis provider the Api
// subscribes to). Only when Redis is configured; a plain run without Redis stays a no-op.
var redisConfigured = !string.IsNullOrWhiteSpace(builder.Configuration["Redis:ConnectionString"]);
if (redisConfigured)
{
    builder.Services
        .AddGraphQL()
        .AddRedisSubscriptions(sp => sp.GetRequiredService<IConnectionMultiplexer>());
    builder.Services.AddHostedService<ChangeStreamBridge>();
}

var host = builder.Build();

// Ensure indexes from the Worker too — jobs ($near station assignment, TTL, uniques) must not
// depend on the Api having booted first against this database. Background and non-fatal.
_ = Task.Run(async () =>
{
    using var scope = host.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await scope.ServiceProvider
            .GetRequiredService<Fogos.Infrastructure.Mongo.MongoIndexInitializer>()
            .EnsureIndexesAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Mongo index initialization failed");
        await scope.ServiceProvider.GetRequiredService<Fogos.Infrastructure.Ops.IOpsNotifier>()
            .ErrorAsync($"Worker index initialization failed: {ex.Message}");
    }
});

host.Run();
