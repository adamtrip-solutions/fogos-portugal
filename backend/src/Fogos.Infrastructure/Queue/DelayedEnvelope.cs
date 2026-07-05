using System.Text.Json;

namespace Fogos.Infrastructure.Queue;

/// <summary>
/// The unit stored in the <c>fogos:delayed</c> sorted set: a serialized event plus the stream it
/// should land on once due. Shared by the dispatcher (writer) and the Worker's pump (reader) so
/// the wire shape can never drift between them.
/// </summary>
public sealed record DelayedEnvelope(string Stream, string Type, string Data, string EventId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static DelayedEnvelope FromJson(string json) =>
        JsonSerializer.Deserialize<DelayedEnvelope>(json, JsonOptions)
        ?? throw new InvalidOperationException("Delayed envelope deserialized to null.");
}
