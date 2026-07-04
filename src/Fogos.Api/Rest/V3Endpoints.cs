using System.Globalization;
using System.Text;
using System.Xml;
using CsvHelper;
using CsvHelper.Configuration;
using Fogos.Domain.Geo;
using Fogos.Domain.Hotspots;
using Fogos.Domain.Incidents;
using Fogos.Infrastructure.Reads;

namespace Fogos.Api.Rest;

/// <summary>
/// REST v3 format outputs (KML / GeoJSON / CSV) for Google Earth &amp; GIS consumers.
/// Extension-based content negotiation, anonymous for now. Replaces legacy v2 KML/CSV/GeoJSON.
/// </summary>
public static class V3Endpoints
{
    private const string ActiveCacheControl = "public, max-age=15, s-maxage=30, stale-while-revalidate=30";
    private const string KmlContentType = "application/vnd.google-earth.kml+xml";

    private static readonly IReadOnlyList<IncidentKind> ActiveKinds = [IncidentKind.Fire];

    public static void MapV3(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v3");

        group.MapGet("/incidents/active.geojson", ActiveGeoJsonAsync);
        group.MapGet("/incidents/active.csv", ActiveCsvAsync);
        group.MapGet("/incidents/active.kml", ActiveKmlAsync);
        group.MapGet("/incidents/{id}/kml", (string id, IncidentReads reads, CancellationToken ct) => StoredKmlAsync(id, reads, ct, vost: false));
        group.MapGet("/incidents/{id}/kml-vost", (string id, IncidentReads reads, CancellationToken ct) => StoredKmlAsync(id, reads, ct, vost: true));
        group.MapGet("/incidents/{id}/kml-firms", FirmsKmlAsync);
    }

    // ── active.geojson ─────────────────────────────────────────────────────────
    private static async Task<IResult> ActiveGeoJsonAsync(HttpContext http, IncidentReads reads, CancellationToken ct)
    {
        var incidents = await reads.ActiveAsync(ActiveKinds, ct);
        var features = incidents
            .Where(i => i.Coordinates is not null)
            .Select(i =>
            {
                var c = i.Coordinates!.Value;
                return new
                {
                    type = "Feature",
                    geometry = new { type = "Point", coordinates = new[] { c.Longitude, c.Latitude } },
                    properties = new
                    {
                        id = i.Id,
                        status = i.Status.Label,
                        kind = i.Kind.ToString(),
                        man = i.Resources.Man,
                        terrain = i.Resources.Terrain,
                        aerial = i.Resources.Aerial,
                        location = i.Location,
                        concelho = i.Concelho,
                        district = i.District,
                        occurredAt = i.OccurredAt.ToString("o", CultureInfo.InvariantCulture),
                    },
                };
            });

        http.Response.Headers.CacheControl = ActiveCacheControl;
        return Results.Json(new { type = "FeatureCollection", features }, contentType: "application/geo+json");
    }

    // ── active.csv ─────────────────────────────────────────────────────────────
    private static async Task<IResult> ActiveCsvAsync(HttpContext http, IncidentReads reads, CancellationToken ct)
    {
        var incidents = await reads.ActiveAsync(ActiveKinds, ct);

        using var buffer = new MemoryStream();
        // UTF-8 with BOM.
        await using (var writer = new StreamWriter(buffer, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        await using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        {
            foreach (var h in new[] { "id", "occurredAt", "district", "concelho", "freguesia", "location", "lat", "lng", "status", "kind", "man", "terrain", "aerial" })
                csv.WriteField(h);
            await csv.NextRecordAsync();

            foreach (var i in incidents)
            {
                csv.WriteField(i.Id);
                csv.WriteField(i.OccurredAt.ToString("o", CultureInfo.InvariantCulture));
                csv.WriteField(i.District);
                csv.WriteField(i.Concelho);
                csv.WriteField(i.Freguesia ?? "");
                csv.WriteField(i.Location);
                csv.WriteField(i.Coordinates?.Latitude.ToString(CultureInfo.InvariantCulture) ?? "");
                csv.WriteField(i.Coordinates?.Longitude.ToString(CultureInfo.InvariantCulture) ?? "");
                csv.WriteField(i.Status.Label);
                csv.WriteField(i.Kind.ToString());
                csv.WriteField(i.Resources.Man);
                csv.WriteField(i.Resources.Terrain);
                csv.WriteField(i.Resources.Aerial);
                await csv.NextRecordAsync();
            }
        }

        http.Response.Headers.CacheControl = ActiveCacheControl;
        return Results.File(buffer.ToArray(), "text/csv; charset=utf-8");
    }

    // ── active.kml ─────────────────────────────────────────────────────────────
    private static async Task<IResult> ActiveKmlAsync(HttpContext http, IncidentReads reads, CancellationToken ct)
    {
        var incidents = (await reads.ActiveAsync(ActiveKinds, ct))
            .Where(i => i.Coordinates is not null)
            .ToList();

        var kml = BuildString(w =>
        {
            w.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            w.WriteStartElement("Document");

            foreach (var code in incidents.Select(i => i.Status.Code).Distinct())
            {
                w.WriteStartElement("Style");
                w.WriteAttributeString("id", $"s{code}");
                w.WriteStartElement("IconStyle");
                w.WriteElementString("color", KmlColor(IncidentStatusCatalog.ColorFor(code)));
                w.WriteEndElement();
                w.WriteStartElement("LineStyle");
                w.WriteElementString("color", KmlColor(IncidentStatusCatalog.ColorFor(code)));
                w.WriteEndElement();
                w.WriteEndElement();
            }

            foreach (var i in incidents)
            {
                var c = i.Coordinates!.Value;
                w.WriteStartElement("Placemark");
                w.WriteElementString("name", i.Location);
                w.WriteElementString("description", $"{i.Natureza} — {i.Status.Label}");
                w.WriteElementString("styleUrl", $"#s{i.Status.Code}");
                w.WriteStartElement("Point");
                w.WriteElementString("coordinates", Coord(c));
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement(); // Document
            w.WriteEndElement(); // kml
        });

        http.Response.Headers.CacheControl = ActiveCacheControl;
        return Results.Content(kml, KmlContentType, Encoding.UTF8);
    }

    // ── {id}/kml and {id}/kml-vost ─────────────────────────────────────────────
    private static async Task<IResult> StoredKmlAsync(string id, IncidentReads reads, CancellationToken ct, bool vost)
    {
        var incident = await reads.GetByIdAsync(id, ct);
        var content = vost ? incident?.KmlVost : incident?.Kml;
        if (string.IsNullOrEmpty(content))
            return Results.NotFound();

        var bytes = Encoding.UTF8.GetBytes(content);
        return Results.File(bytes, KmlContentType, fileDownloadName: $"{id}{(vost ? "-vost" : "")}.kml");
    }

    // ── {id}/kml-firms (generated AOI + hotspots) ──────────────────────────────
    private static async Task<IResult> FirmsKmlAsync(string id, IncidentReads reads, CancellationToken ct)
    {
        var incident = await reads.GetByIdAsync(id, ct);
        if (incident?.Coordinates is not { } incidentPoint)
            return Results.NotFound();

        var hotspotsMap = await reads.HotspotsByIdsAsync([id], ct);
        hotspotsMap.TryGetValue(id, out var hotspots);

        var viirs = hotspots?.Viirs ?? [];
        var modis = hotspots?.Modis ?? [];

        var points = new List<GeoPoint> { incidentPoint };
        points.AddRange(viirs.Select(h => h.Position));
        points.AddRange(modis.Select(h => h.Position));

        var hull = ConvexHull.Compute(points);
        if (hull.Count < 3)
            return Results.NotFound();

        var kml = BuildString(w =>
        {
            w.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
            w.WriteStartElement("Document");

            // AOI polygon: red outline, semi-transparent red fill (AABBGGRR).
            WriteStyle(w, "aoi", lineColor: "ff0000ff", polyColor: "330000ff");
            WriteIconStyle(w, "viirs", "ff00ffff", "0.8");
            WriteIconStyle(w, "modis", "ff0080ff", "0.8");
            WriteIconStyle(w, "incident", "ff0000ff", "1.2");

            // AOI polygon (closed ring).
            w.WriteStartElement("Placemark");
            w.WriteElementString("name", $"AOI - {incident.Id}");
            w.WriteElementString("description", "Área de Interesse gerada a partir de deteções NASA FIRMS (VIIRS/MODIS)");
            w.WriteElementString("styleUrl", "#aoi");
            w.WriteStartElement("Polygon");
            w.WriteElementString("altitudeMode", "clampToGround");
            w.WriteStartElement("outerBoundaryIs");
            w.WriteStartElement("LinearRing");
            var ring = new StringBuilder();
            foreach (var v in hull)
                ring.Append(Coord(v)).Append('\n');
            ring.Append(Coord(hull[0])).Append('\n'); // close the ring
            w.WriteElementString("coordinates", ring.ToString());
            w.WriteEndElement(); // LinearRing
            w.WriteEndElement(); // outerBoundaryIs
            w.WriteEndElement(); // Polygon
            w.WriteEndElement(); // Placemark

            // Incident point.
            w.WriteStartElement("Placemark");
            w.WriteElementString("name", $"Incêndio {incident.Id}");
            w.WriteElementString("description", $"{incident.Location} — {incident.Natureza}");
            w.WriteElementString("styleUrl", "#incident");
            w.WriteStartElement("Point");
            w.WriteElementString("coordinates", Coord(incidentPoint));
            w.WriteEndElement();
            w.WriteEndElement();

            WriteHotspots(w, viirs, "VIIRS", "#viirs");
            WriteHotspots(w, modis, "MODIS", "#modis");

            w.WriteEndElement(); // Document
            w.WriteEndElement(); // kml
        });

        var bytes = Encoding.UTF8.GetBytes(kml);
        return Results.File(bytes, KmlContentType, fileDownloadName: $"{id}-firms.kml");
    }

    private static void WriteHotspots(XmlWriter w, IReadOnlyList<HotspotSample> samples, string source, string styleUrl)
    {
        for (var i = 0; i < samples.Count; i++)
        {
            var h = samples[i];
            w.WriteStartElement("Placemark");
            w.WriteElementString("name", $"{source} #{i}");
            w.WriteElementString("description",
                $"Satélite: {source}\nBrilho: {h.Brightness?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}\n" +
                $"Confiança: {h.Confidence ?? "n/a"}\nData/Hora (UTC): {h.AcquiredAt?.ToString("o", CultureInfo.InvariantCulture) ?? ""}");
            w.WriteElementString("styleUrl", styleUrl);
            w.WriteStartElement("Point");
            w.WriteElementString("coordinates", Coord(h.Position));
            w.WriteEndElement();
            w.WriteEndElement();
        }
    }

    private static void WriteStyle(XmlWriter w, string id, string lineColor, string polyColor)
    {
        w.WriteStartElement("Style");
        w.WriteAttributeString("id", id);
        w.WriteStartElement("LineStyle");
        w.WriteElementString("color", lineColor);
        w.WriteElementString("width", "2");
        w.WriteEndElement();
        w.WriteStartElement("PolyStyle");
        w.WriteElementString("color", polyColor);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteIconStyle(XmlWriter w, string id, string color, string scale)
    {
        w.WriteStartElement("Style");
        w.WriteAttributeString("id", id);
        w.WriteStartElement("IconStyle");
        w.WriteElementString("color", color);
        w.WriteElementString("scale", scale);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static string BuildString(Action<XmlWriter> write)
    {
        // XmlWriter over a StringBuilder always writes UTF-16 (the in-memory string encoding), so its
        // own declaration would read encoding="utf-16" even though we serve the bytes as UTF-8. Suppress
        // the writer's declaration and prepend an explicit UTF-8 one that matches the response charset.
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
        {
            write(w);
            w.Flush();
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + sb;
    }

    private static string Coord(GeoPoint p) =>
        $"{p.Longitude.ToString(CultureInfo.InvariantCulture)},{p.Latitude.ToString(CultureInfo.InvariantCulture)},0";

    /// <summary>RGB "RRGGBB" → KML "AABBGGRR" with full alpha.</summary>
    private static string KmlColor(string rgb)
    {
        rgb = rgb.TrimStart('#');
        if (rgb.Length != 6)
            return "ffffffff";
        var rr = rgb.Substring(0, 2);
        var gg = rgb.Substring(2, 2);
        var bb = rgb.Substring(4, 2);
        return $"ff{bb}{gg}{rr}".ToLowerInvariant();
    }
}
