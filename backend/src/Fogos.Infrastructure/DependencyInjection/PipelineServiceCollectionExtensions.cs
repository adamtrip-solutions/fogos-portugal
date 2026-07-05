using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the ingestion-pipeline services: the Redis Streams queue (dispatchers + idempotency)
/// and the external-source HTTP clients. The Api only needs
/// <see cref="ServiceCollectionExtensions.AddFogosInfrastructure"/>; the Worker adds this on top.
/// </summary>
public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddFogosPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Options ──────────────────────────────────────────────────────────────────────────
        services.Configure<QueueOptions>(configuration.GetSection(QueueOptions.SectionName));
        services.Configure<FogosSourcesOptions>(configuration.GetSection(FogosSourcesOptions.SectionName));

        // ── Queue ────────────────────────────────────────────────────────────────────────────
        services.AddSingleton<IEventDispatcher, RedisEventDispatcher>();
        services.AddSingleton<IProcessedMarker, RedisProcessedMarker>();

        // ── Webhooks ───────────────────────────────────────────────────────────────────────────
        // Delivery client: a fixed per-request timeout, NO retry handler — transport failures raise the
        // endpoint's ConsecutiveFailures instead (the spec forbids in-request retries).
        services.AddHttpClient(Webhooks.WebhookSigner.HttpClientName, (sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<WebhookOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // ── External sources (typed clients, standard resilience: 3 retries + backoff) ─────────
        services.AddHttpClient<ArcGisClient>().AddStandardResilienceHandler();
        services.AddHttpClient<IpmaClient>().AddStandardResilienceHandler();
        services.AddHttpClient<FirmsClient>().AddStandardResilienceHandler();
        services.AddHttpClient<Fr24Client>().AddStandardResilienceHandler();
        services.AddHttpClient<AdsbFiClient>().AddStandardResilienceHandler();
        services.AddHttpClient<AirplanesLiveClient>().AddStandardResilienceHandler();
        services.AddHttpClient<GitHubClient>().AddStandardResilienceHandler();

        // ICNF gets a dedicated handler with optional TLS relaxation — scoped to these hosts only.
        services.AddHttpClient<IcnfClient>()
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var icnf = sp.GetRequiredService<IOptions<FogosSourcesOptions>>().Value.Icnf;
                var handler = new HttpClientHandler();
                if (icnf.AllowInsecureTls)
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            })
            .AddStandardResilienceHandler();

        services.AddSingleton<Fr24CreditMeter>();

        // ── Scheduling (single-flight lock jobs opt into) ─────────────────────────────────────
        services.AddSingleton<Scheduling.ISingleFlightLock, Scheduling.RedisSingleFlightLock>();

        return services;
    }
}
