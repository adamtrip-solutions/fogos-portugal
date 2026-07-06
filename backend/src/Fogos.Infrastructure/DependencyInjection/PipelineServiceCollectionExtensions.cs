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
            .AddStandardResilienceHandler(o =>
            {
                // fogos.icnf.pt is a slow legacy ASP site (the occurrences table is ~4.7k rows of HTML and
                // grows through fire season; 10–30s responses under summer load are normal). The default 10s
                // attempt timeout abandons requests the server would have answered and immediately re-sends
                // the same work — tripling ICNF's load for zero answers. Longer attempts, fewer retries; the
                // circuit breaker still bounds a sustained outage (its sampling window must be ≥ 2× the
                // attempt timeout, hence 60s).
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                o.Retry.MaxRetryAttempts = 2;
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        services.AddSingleton<Fr24CreditMeter>();

        // ── Scheduling (single-flight lock jobs opt into) ─────────────────────────────────────
        services.AddSingleton<Scheduling.ISingleFlightLock, Scheduling.RedisSingleFlightLock>();

        return services;
    }
}
