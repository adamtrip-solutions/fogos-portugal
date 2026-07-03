using Fogos.Infrastructure.DependencyInjection;
using Fogos.Worker.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFogosInfrastructure(builder.Configuration);

// Quartz scheduler with no jobs yet — the ingestion trigger table lands in Phase 3.
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

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
