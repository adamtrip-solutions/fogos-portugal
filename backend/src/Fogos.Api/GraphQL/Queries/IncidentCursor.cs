using System.Text;
using Fogos.Domain.Incidents;

namespace Fogos.Api.GraphQL.Queries;

/// <summary>
/// Opaque cursor for the incidents connection, encoding the sort key (occurredAt, id).
/// Format before base64: <c>{occurredAtTicksUtc}:{id}</c>.
/// </summary>
public static class IncidentCursor
{
    public static string Encode(Incident incident) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{incident.OccurredAt.UtcTicks}:{incident.Id}"));

    public static bool TryDecode(string cursor, out DateTimeOffset occurredAt, out string id)
    {
        occurredAt = default;
        id = "";
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var sep = raw.IndexOf(':');
            if (sep <= 0)
                return false;
            if (!long.TryParse(raw.AsSpan(0, sep), out var ticks))
                return false;
            occurredAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            id = raw[(sep + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
