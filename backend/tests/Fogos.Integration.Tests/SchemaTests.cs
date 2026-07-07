using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Fogos.Integration.Tests;

[Collection("fogos")]
public sealed class SchemaTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Schema_exposes_the_expected_query_and_subscription_surface()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);

        var provider = fixture.Factory.Services.GetRequiredService<IRequestExecutorProvider>();
        var executor = await provider.GetExecutorAsync();
        var sdl = executor.Schema.ToString();

        // Query surface.
        Assert.Contains("type Query", sdl);
        Assert.Contains("incident(id: ID!): Incident", sdl);
        Assert.Contains("incidents(filter: IncidentFilter", sdl);
        Assert.Contains("activeIncidents(kind: [IncidentKind!]): [Incident!]!", sdl);
        Assert.Contains("stats: Stats!", sdl);
        Assert.Contains("fireRisk(day: RiskDay!", sdl);
        Assert.Contains("aircraftTrack(icao: String!", sdl);

        // Incident resolver fields.
        Assert.Contains("statusChangedAt: DateTime", sdl);
        Assert.Contains("signals: IncidentSignals!", sdl);
        Assert.Contains("responseTimes: ResponseTimes", sdl);

        // Signals / response-time output types.
        Assert.Contains("type IncidentSignals", sdl);
        Assert.Contains("criticalReasons: [String!]!", sdl);
        Assert.Contains("rekindleOfId: ID", sdl);
        Assert.Contains("type ResponseTimes", sdl);
        Assert.Contains("dispatchToArrivalSeconds: Int", sdl);

        // WP2 — aircraft association + KML versioning.
        Assert.Contains("aircraft: [IncidentAircraft!]!", sdl);
        Assert.Contains("kmlHistory: [KmlVersionMeta!]!", sdl);
        Assert.Contains("type IncidentAircraft", sdl);
        Assert.Contains("icao: ID!", sdl);
        Assert.Contains("type KmlVersionMeta", sdl);
        Assert.Contains("currentIncidentId: ID", sdl);

        // WP3 — season analytics, concelho profile, ignition clustering.
        Assert.Contains("ignitionsByDay(year: Int!): [DayCount!]!", sdl);
        Assert.Contains("burnAreaCumulative(year: Int!): [DayArea!]!", sdl);
        Assert.Contains("causeBreakdown(year: Int!): [CauseCount!]!", sdl);
        Assert.Contains("falseAlarmStats(year: Int!): [DistrictFalseAlarms!]!", sdl);
        Assert.Contains("responseTimeStats(year: Int!, district: String): ResponseTimeStats", sdl);
        Assert.Contains("concelhoProfile(dico: String!): ConcelhoProfile", sdl);
        Assert.Contains("ignitionClusters(activeOnly: Boolean! = true): [IgnitionCluster!]!", sdl);
        Assert.Contains("clusterId: ID", sdl);
        Assert.Contains("type ConcelhoProfile", sdl);
        Assert.Contains("type ConcelhoRiskDay", sdl);
        Assert.Contains("type IgnitionCluster", sdl);
        Assert.Contains("type DayCount", sdl);
        Assert.Contains("type ResponseTimeStats", sdl);

        // WP4 — alert subscriptions, webhooks, situation reports.
        Assert.Contains("createAlertSubscription(input: CreateAlertSubscriptionInput!): AlertSubscription!", sdl);
        Assert.Contains("deleteAlertSubscription(id: ID!): Boolean!", sdl);
        Assert.Contains("registerWebhook(url: String!, events: [String!]!): Webhook!", sdl);
        Assert.Contains("deleteWebhook(id: ID!): Boolean!", sdl);
        Assert.Contains("situationReports(first: Int! = 7): [SituationReport!]!", sdl);
        Assert.Contains("webhooks: [Webhook!]!", sdl);
        Assert.Contains("type AlertSubscription", sdl);
        Assert.Contains("type Webhook", sdl);
        Assert.Contains("type SituationReport", sdl);

        // N1 — Web Push device registry + delivery surface.
        Assert.Contains("webPushPublicKey: String", sdl);
        Assert.Contains("deviceSubscriptions(deviceId: ID!): [AlertSubscription!]!", sdl);
        Assert.Contains("registerWebPushDevice(input: RegisterWebPushDeviceInput!): RegisteredDevice!", sdl);
        Assert.Contains("deleteWebPushDevice(endpoint: String!): Boolean!", sdl);
        Assert.Contains("input RegisterWebPushDeviceInput", sdl);
        Assert.Contains("type RegisteredDevice", sdl);
        Assert.Contains("deviceId: ID", sdl); // on AlertSubscription + CreateAlertSubscriptionInput
        // The Device entity itself is never exposed as an output type — only RegisteredDevice.id escapes.
        Assert.DoesNotContain("type Device ", sdl);
        Assert.DoesNotContain("type Device\n", sdl);
        Assert.DoesNotContain("pushEndpoint", sdl);
        Assert.DoesNotContain("pushAuth", sdl);

        // Accounts (Clerk) — the signed-in user's own identity; nullable for machine/anonymous callers.
        Assert.Contains("me: Me", sdl);
        Assert.Contains("type Me", sdl);
        Assert.Contains("input CreateAlertSubscriptionInput", sdl);
        Assert.Contains("enum AlertSubscriptionKind", sdl);
        // FCM was fully removed: no device-token field survives on the input or output type.
        Assert.DoesNotContain("fcmToken", sdl);

        // PR2 — self-service API keys + owned subscriptions + full `me`.
        Assert.Contains("role: UserRole!", sdl);
        Assert.Contains("enum UserRole", sdl);
        Assert.Contains("apiKeys: [ApiKeyInfo!]!", sdl);
        Assert.Contains("alertSubscriptions: [AlertSubscription!]!", sdl);
        Assert.Contains("createApiKey(name: String!): CreatedApiKey!", sdl);
        Assert.Contains("revokeApiKey(id: ID!): Boolean!", sdl);
        Assert.Contains("updateAlertSubscription(id: ID!, input: CreateAlertSubscriptionInput!): AlertSubscription!", sdl);
        Assert.Contains("type ApiKeyInfo", sdl);
        Assert.Contains("keyPrefix: String", sdl);
        Assert.Contains("type CreatedApiKey", sdl);
        Assert.Contains("plaintextKey: String!", sdl);
        // The owning user id is internal — it must never leak on the public AlertSubscription type.
        Assert.DoesNotContain("ownerUserId", sdl);

        // Input + key types.
        Assert.Contains("input IncidentFilter", sdl);
        Assert.Contains("type GeoPoint", sdl);
        Assert.Contains("type WeatherObservation", sdl);
        Assert.Contains("type AircraftPosition", sdl);

        // Subscription surface.
        Assert.Contains("type Subscription", sdl);
        Assert.Contains("incidentUpdated(id: ID): Incident!", sdl);
        Assert.Contains("activeIncidentsChanged: ActiveIncidentsDelta!", sdl);
        Assert.Contains("warningAdded: Warning!", sdl);
    }
}
