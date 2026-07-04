using System.Globalization;
using Fogos.Domain.Incidents;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Social/push copy for an approved citizen photo, ported from
/// <c>PhotoModerationController::buildPublicationText</c>: a base line plus optional "taken at" time and
/// distance-from-incident extras. Kept alongside <see cref="IncidentCopy"/> so the exact strings are
/// auditable against the live platform.
/// </summary>
public static class PhotoCopy
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-PT");

    public static string NewPhoto(Incident incident, IncidentPhoto photo, string domain, IClock clock)
    {
        var baseLine = $"Foi publicada uma nova foto no incêndio em {incident.Location}. https://{domain}/fogo/{incident.Id}/detalhe";

        var extras = new List<string>();

        if (photo.TakenAt is { } takenAt)
            extras.Add($"Tirada às {clock.ToLisbon(takenAt):HH\\:mm}");

        if (photo.Gps is { } gps && incident.Coordinates is { } incidentPoint)
        {
            var km = gps.DistanceKm(incidentPoint).ToString("0.0", Pt);
            if (extras.Count == 0)
                extras.Add($"A {km} km do local do incêndio");
            else
                extras[^1] += $" a {km} km do local do incêndio";
        }

        return extras.Count > 0
            ? baseLine + "\n" + string.Join(' ', extras) + "."
            : baseLine;
    }

    /// <summary>NotificationTool::sendNewPhotoNotification push body.</summary>
    public const string PushBody = "Foi publicada uma nova foto deste incêndio.";
}
