using Fogos.Infrastructure.DependencyInjection;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFogosInfrastructure(builder.Configuration);

// Quartz scheduler with no jobs yet — the ingestion trigger table lands in Phase 3.
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var host = builder.Build();
host.Run();
