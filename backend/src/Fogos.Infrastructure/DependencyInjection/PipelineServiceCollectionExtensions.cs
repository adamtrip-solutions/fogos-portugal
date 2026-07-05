using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Rendering;
using Fogos.Infrastructure.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the ingestion-pipeline services: the Redis Streams queue (dispatchers + idempotency),
/// the social publishers, FCM, the renderer client, and the external-source HTTP clients. The Api
/// only needs <see cref="ServiceCollectionExtensions.AddFogosInfrastructure"/>; the Worker adds this
/// on top.
/// </summary>
public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddFogosPipeline(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Options ──────────────────────────────────────────────────────────────────────────
        services.Configure<QueueOptions>(configuration.GetSection(QueueOptions.SectionName));
        services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.Configure<FacebookOptions>(configuration.GetSection(FacebookOptions.SectionName));
        services.Configure<DiscordPostOptions>(configuration.GetSection(DiscordPostOptions.SectionName));
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        services.Configure<RendererOptions>(configuration.GetSection(RendererOptions.SectionName));
        services.Configure<FogosSourcesOptions>(configuration.GetSection(FogosSourcesOptions.SectionName));

        // ── Queue ────────────────────────────────────────────────────────────────────────────
        services.AddSingleton<IEventDispatcher, RedisEventDispatcher>();
        services.AddSingleton<IDelayedDispatcher, RedisDelayedDispatcher>();
        services.AddSingleton<IProcessedMarker, RedisProcessedMarker>();

        // ── Social publishers (each has its own named HttpClient) ─────────────────────────────
        services.AddHttpClient(TwitterPublisher.HttpClientName);
        services.AddHttpClient(TelegramPublisher.HttpClientName);
        services.AddHttpClient(FacebookPublisher.HttpClientName);
        services.AddHttpClient(DiscordPostPublisher.HttpClientName);
        services.AddSingleton<ITwitterPublisher, TwitterPublisher>();
        services.AddSingleton<ITelegramPublisher, TelegramPublisher>();
        services.AddSingleton<IFacebookPublisher, FacebookPublisher>();
        services.AddSingleton<IDiscordPostPublisher, DiscordPostPublisher>();

        // ── Webhooks ───────────────────────────────────────────────────────────────────────────
        // Delivery client: a fixed per-request timeout, NO retry handler — transport failures raise the
        // endpoint's ConsecutiveFailures instead (the spec forbids in-request retries).
        services.AddHttpClient(Webhooks.WebhookSigner.HttpClientName, (sp, client) =>
        {
            var o = sp.GetRequiredService<IOptions<WebhookOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // ── FCM ──────────────────────────────────────────────────────────────────────────────
        services.AddSingleton<IFcmSender, FcmSender>();
        services.AddSingleton<FcmNotifier>();
        services.AddSingleton<NotificationScheduler>();

        // ── Renderer ─────────────────────────────────────────────────────────────────────────
        services.AddHttpClient(RendererClient.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<RendererOptions>>().Value;
            client.Timeout = opts.Timeout;
        });
        services.AddSingleton<RendererClient>();

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
