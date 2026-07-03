using Fogos.Api.GraphQL.DataLoaders;
using Fogos.Api.GraphQL.Queries;
using Fogos.Api.GraphQL.Subscriptions;
using Fogos.Api.GraphQL.Types;
using Fogos.Domain.Aircraft;
using Fogos.Domain.Geo;
using HotChocolate.Types;
using StackExchange.Redis;

namespace Fogos.Api.GraphQL;

/// <summary>
/// Wires the HotChocolate read schema: query/subscription roots, resolver extensions,
/// DataLoaders, the Date scalar, guard rails, and the Redis subscription provider.
/// Registered only when Redis is configured (see Program.cs).
/// </summary>
public static class GraphQLServiceCollectionExtensions
{
    public static IServiceCollection AddFogosGraphQL(this IServiceCollection services)
    {
        // DataLoaders (GreenDonut DI: request-scoped, batch-scheduled).
        services.AddDataLoader<IncidentByIdDataLoader>();
        services.AddDataLoader<IncidentHistoryDataLoader>();
        services.AddDataLoader<IncidentStatusHistoryDataLoader>();
        services.AddDataLoader<IncidentPhotosDataLoader>();
        services.AddDataLoader<IncidentHotspotsDataLoader>();
        services.AddDataLoader<LatestWeatherByStationDataLoader>();
        services.AddDataLoader<WeatherStationByIdDataLoader>();
        services.AddDataLoader<IncidentFireRiskDataLoader>();

        services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddSubscriptionType<Subscription>()
            .AddTypeExtension<IncidentExtensions>()
            .AddTypeExtension<IncidentPhotoExtensions>()
            .AddTypeExtension<StatsExtensions>()
            .AddType<Filters.IncidentFilterType>()
            // FlightPosition is surfaced as AircraftPosition.
            .AddType(new ObjectType<FlightPosition>(d => d.Name("AircraftPosition")))
            // GeoPoint is a value object: expose only its coordinates, not the haversine helper.
            .AddType(new ObjectType<GeoPoint>(d =>
            {
                d.Name("GeoPoint");
                d.BindFieldsExplicitly();
                d.Field(p => p.Latitude);
                d.Field(p => p.Longitude);
            }))
            .BindRuntimeType<DateOnly, DateType>()
            .AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)
            .AddRedisSubscriptions(sp => sp.GetRequiredService<IConnectionMultiplexer>());

        return services;
    }
}
