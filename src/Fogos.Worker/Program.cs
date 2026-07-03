using Fogos.Infrastructure.DependencyInjection;
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
// [jobs:weather] registered in wave 2
// [jobs:risk] registered in wave 2
// [jobs:planes] registered in wave 2
// [jobs:incidents] registered in wave 3

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
host.Run();
