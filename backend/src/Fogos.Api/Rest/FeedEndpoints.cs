using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Fogos.Domain.Incidents;
using Fogos.Domain.Time;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Reads;

namespace Fogos.Api.Rest;

/// <summary>
/// RSS 2.0 / GeoRSS feeds for third-party aggregators. Active + recently concluded fires and the latest
/// broadcast warnings, in European Portuguese. Built with <see cref="System.Xml.Linq"/> (everything is
/// escaped by <see cref="XElement"/>); the georss namespace carries each fire's point. Cached 60 s.
/// </summary>
public static class FeedEndpoints
{
    private const string RssContentType = "application/rss+xml";
    private const string FeedCacheControl = "public, max-age=60";
    private const int MaxIncidents = 100;
    private const int MaxWarnings = 50;

    private static readonly XNamespace GeoRss = "http://www.georss.org/georss";

    public static void MapFeeds(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v3/feeds");
        group.MapGet("/incidents.rss", IncidentsRssAsync);
        group.MapGet("/warnings.rss", WarningsRssAsync);
    }

    // ── incidents.rss ───────────────────────────────────────────────────────────
    private static async Task<IResult> IncidentsRssAsync(HttpContext http, IncidentReads reads, IClock clock, CancellationToken ct)
    {
        var since = clock.UtcNow - TimeSpan.FromHours(24);
        var incidents = await reads.ActiveOrRecentlyConcludedFiresAsync(since, MaxIncidents, ct);

        var items = incidents.Select(i =>
        {
            var item = new XElement("item",
                new XElement("title", $"{i.Natureza} — {i.Concelho} ({i.Status.Label})"),
                new XElement("description", IncidentDescription(i, clock)),
                new XElement("guid", new XAttribute("isPermaLink", "false"), $"{i.Id}:{i.Status.Code}"),
                new XElement("pubDate", Rfc822(i.OccurredAt)));

            if (i.Coordinates is { } c)
                item.Add(new XElement(GeoRss + "point",
                    $"{c.Latitude.ToString(CultureInfo.InvariantCulture)} {c.Longitude.ToString(CultureInfo.InvariantCulture)}"));

            return item;
        });

        var xml = BuildRss(
            title: "FogosPortugal — Ocorrências ativas",
            description: "Ocorrências de incêndio ativas e concluídas nas últimas 24 horas.",
            items: items,
            withGeoRss: true);

        http.Response.Headers.CacheControl = FeedCacheControl;
        return Results.Content(xml, RssContentType, Encoding.UTF8);
    }

    // ── warnings.rss ────────────────────────────────────────────────────────────
    private static async Task<IResult> WarningsRssAsync(HttpContext http, WarningReads reads, CancellationToken ct)
    {
        var warnings = await reads.LatestAsync(null, MaxWarnings, ct);

        var items = warnings.Select(w => new XElement("item",
            new XElement("title", w.Message),
            new XElement("description", w.Message),
            new XElement("link", string.IsNullOrWhiteSpace(w.Url) ? "https://fogosportugal.pt" : w.Url),
            new XElement("guid", new XAttribute("isPermaLink", "false"), w.Id),
            new XElement("pubDate", Rfc822(w.CreatedAt))));

        var xml = BuildRss(
            title: "FogosPortugal — Avisos",
            description: "Últimos avisos emitidos.",
            items: items,
            withGeoRss: false);

        http.Response.Headers.CacheControl = FeedCacheControl;
        return Results.Content(xml, RssContentType, Encoding.UTF8);
    }

    private static string IncidentDescription(Incident i, IClock clock)
    {
        var start = clock.ToLisbon(i.OccurredAt).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
        return $"Operacionais: {i.Resources.Man} · Veículos: {i.Resources.Terrain} · Meios aéreos: {i.Resources.Aerial}. " +
               $"Início: {start}. Estado: {i.Status.Label}.";
    }

    private static string BuildRss(string title, string description, IEnumerable<XElement> items, bool withGeoRss)
    {
        var channel = new XElement("channel",
            new XElement("title", title),
            new XElement("link", "https://fogosportugal.pt"),
            new XElement("description", description),
            new XElement("language", "pt-PT"));
        channel.Add(items);

        var rss = new XElement("rss", new XAttribute("version", "2.0"));
        if (withGeoRss)
            rss.Add(new XAttribute(XNamespace.Xmlns + "georss", GeoRss.NamespaceName));
        rss.Add(channel);

        return new XDeclaration("1.0", "utf-8", null) + "\n" + rss;
    }

    /// <summary>RFC 822 date (RFC 1123 GMT) as RSS pubDate expects.</summary>
    private static string Rfc822(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
}
