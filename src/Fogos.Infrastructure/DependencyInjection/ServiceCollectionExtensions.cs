using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds all options, registers the class maps, and wires the Mongo/Redis/storage/ops
    /// singletons plus the clock and index initializer.
    /// </summary>
    public static IServiceCollection AddFogosInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Class maps must be registered before any driver serialization happens.
        FogosClassMaps.Register();

        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<ObjectStorageOptions>(configuration.GetSection(ObjectStorageOptions.SectionName));
        services.Configure<OpsOptions>(configuration.GetSection(OpsOptions.SectionName));
        services.Configure<PublishingOptions>(configuration.GetSection(PublishingOptions.SectionName));

        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });
        services.AddSingleton<MongoContext>();
        services.AddSingleton<MongoIndexInitializer>();

        // Lazy, resilient: created on first resolve and never throws the host down when Redis is unreachable.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = ConfigurationOptions.Parse(options.ConnectionString);
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
        });

        services.AddHttpClient();
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddSingleton<IOpsNotifier, DiscordOpsNotifier>();
        services.AddSingleton<IClock, FogosClock>();
        services.AddSingleton<Images.ImageProcessor>();

        // Event dispatcher (Redis Streams). The Worker's AddFogosPipeline registers the same impl; the Api
        // needs it here for the photo-upload and moderation flows (PhotoSubmitted / PhotoApproved).
        services.AddSingleton<Queue.IEventDispatcher, Queue.RedisEventDispatcher>();

        // Read-side query layer (thin, driver-direct) used by GraphQL resolvers/DataLoaders and REST v3.
        services.AddSingleton<Reads.IncidentReads>();
        services.AddSingleton<Reads.WeatherReads>();
        services.AddSingleton<Reads.RiskReads>();
        services.AddSingleton<Reads.WarningReads>();
        services.AddSingleton<Reads.AircraftReads>();
        services.AddSingleton<Reads.StatsReads>();

        return services;
    }
}
