using System.Text.Json;
using System.Text.Json.Serialization;
using Fogos.Domain.Events;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// Serializes domain events to/from the stream payload with an explicit type discriminator.
/// The discriminator keeps the Domain layer free of any serialization attributes while still
/// letting the consumer recover the concrete type to resolve <c>IEventHandler&lt;TEvent&gt;</c>.
/// </summary>
public static class EventSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    // The one place a new event type must be registered. Discriminator == the record's simple name.
    private static readonly IReadOnlyDictionary<string, Type> ByDiscriminator = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        [nameof(IncidentCreated)] = typeof(IncidentCreated),
        [nameof(IncidentResourcesChanged)] = typeof(IncidentResourcesChanged),
        [nameof(IncidentStatusChanged)] = typeof(IncidentStatusChanged),
        [nameof(IcnfEnriched)] = typeof(IcnfEnriched),
        [nameof(ProcessIcnfFireData)] = typeof(ProcessIcnfFireData),
        [nameof(KmlAttached)] = typeof(KmlAttached),
        [nameof(PhotoSubmitted)] = typeof(PhotoSubmitted),
        [nameof(PhotoApproved)] = typeof(PhotoApproved),
        [nameof(IncidentEscalating)] = typeof(IncidentEscalating),
        [nameof(RekindleDetected)] = typeof(RekindleDetected),
        [nameof(ClusterDetected)] = typeof(ClusterDetected),
        [nameof(RcmProcessed)] = typeof(RcmProcessed),
        [nameof(SituationReportCreated)] = typeof(SituationReportCreated),
    };

    /// <summary>Discriminator string for an event instance (its runtime type's simple name).</summary>
    public static string Discriminator(IDomainEvent evt) => evt.GetType().Name;

    /// <summary>Serializes the event body (concrete type) to JSON.</summary>
    public static string Serialize(IDomainEvent evt) => JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions);

    /// <summary>
    /// Resolves the concrete type for a discriminator, or null when unknown (a message written by a
    /// newer producer we don't understand — the consumer dead-letters it rather than crashing).
    /// </summary>
    public static Type? Resolve(string discriminator) =>
        ByDiscriminator.TryGetValue(discriminator, out var type) ? type : null;

    /// <summary>Deserializes a payload for a known concrete type.</summary>
    public static IDomainEvent Deserialize(Type type, string json) =>
        (IDomainEvent)(JsonSerializer.Deserialize(json, type, JsonOptions)
            ?? throw new InvalidOperationException($"Event payload for {type.Name} deserialized to null."));
}
