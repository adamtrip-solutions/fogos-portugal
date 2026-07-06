using Fogos.Api.GraphQL.DataLoaders;
using Fogos.Api.GraphQL.Queries;
using Fogos.Api.GraphQL.RateLimiting;
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
        services.AddDataLoader<IncidentAircraftDataLoader>();
        services.AddDataLoader<IncidentKmlHistoryDataLoader>();
        services.AddDataLoader<AircraftCurrentIncidentDataLoader>();
        services.AddDataLoader<IncidentClusterDataLoader>();

        services
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddMutationType<Mutations.Mutation>()
            .AddSubscriptionType<Subscription>()
            .AddTypeExtension<IncidentExtensions>()
            .AddTypeExtension<IncidentPhotoExtensions>()
            .AddTypeExtension<AircraftExtensions>()
            .AddTypeExtension<StatsExtensions>()
            .AddTypeExtension<IgnitionClusterExtensions>()
            .AddTypeExtension<MeExtensions>()
            .AddType<Filters.IncidentFilterType>()
            // Concelho profile: DICO is an ID.
            .AddType(new ObjectType<ConcelhoProfile>(d =>
            {
                d.Name("ConcelhoProfile");
                d.Field(x => x.Dico).ID();
            }))
            // Aircraft associated with an incident: ICAO is an ID.
            .AddType(new ObjectType<IncidentAircraft>(d =>
            {
                d.Name("IncidentAircraft");
                d.Field(x => x.Icao).ID();
            }))
            // KML version metadata: surrogate id is an ID.
            .AddType(new ObjectType<KmlVersionMeta>(d =>
            {
                d.Name("KmlVersionMeta");
                d.Field(x => x.Id).ID();
            }))
            // Incident signals: expose the domain value object directly; the prior-incident link is an ID.
            .AddType(new ObjectType<Fogos.Domain.Incidents.IncidentSignals>(d =>
            {
                d.Name("IncidentSignals");
                d.Field(x => x.RekindleOfId).ID();
                d.Ignore(x => x.RekindleKinds); // internal per-kind claim bookkeeping
            }))
            // Alert subscription: id is an ID; the owning user id is internal and never exposed publicly.
            .AddType(new ObjectType<Fogos.Domain.Alerts.AlertSubscription>(d =>
            {
                d.Name("AlertSubscription");
                d.Field(x => x.Id).ID();
                d.Ignore(x => x.OwnerUserId);
            }))
            // Situation report: surrogate id is an ID.
            .AddType(new ObjectType<Fogos.Domain.Reports.SituationReport>(d =>
            {
                d.Name("SituationReport");
                d.Field(x => x.Id).ID();
            }))
            // The signed-in user's own identity: id is an ID.
            .AddType(new ObjectType<Types.Me>(d =>
            {
                d.Name("Me");
                d.Field(x => x.Id).ID();
            }))
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
            // Scope policies (write:incidents / write:warnings / moderate:photos) usable on mutations,
            // which land in a later phase; the @authorize directive resolves against the caller principal
            // via the ASP.NET authorization bridge (DefaultAuthorizationHandler).
            .AddAuthorization()
            .AddCostAnalyzer()
            .ModifyCostOptions(o =>
            {
                o.MaxFieldCost = 1_000_000;
                o.MaxTypeCost = 1_000_000;
                o.EnforceCostLimits = true;
            })
            // Resolve caller identity from the websocket connect payload and enforce subscription caps.
            .AddSocketSessionInterceptor(sp => new SubscriptionSessionInterceptor(
                sp.GetRequiredService<Fogos.Api.Auth.JwtService>(),
                sp.GetRequiredService<Fogos.Api.Auth.ApiKeyResolver>(),
                sp.GetRequiredService<Fogos.Infrastructure.RateLimiting.SubscriptionLimiter>(),
                sp.GetRequiredService<Fogos.Api.Auth.ClientIpResolver>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Fogos.Infrastructure.Options.RateLimitOptions>>()))
            .AddRedisSubscriptions(sp => sp.GetRequiredService<IConnectionMultiplexer>());

        return services;
    }
}
