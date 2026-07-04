using System.Globalization;
using Fogos.Api.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Images;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.RateLimiting;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Fogos.Api.Rest;

/// <summary>
/// Citizen photo submission + public listing (REST v3). Upload is the deliberate anonymous-write
/// exception, fenced by the abuse gates; it re-encodes to a metadata-stripped JPEG, requires GPS, and
/// dedups by content signature. Listing exposes only approved + public photos. Ports
/// <c>IncidentPhotoController</c>.
/// </summary>
public static class PhotoEndpoints
{
    /// <summary>Hard cap on the raw upload (15 MB).</summary>
    public const long MaxUploadBytes = 15L * 1024 * 1024;

    private const string ListCacheControl = "public, s-maxage=300, max-age=120, stale-while-revalidate=300";

    public static void MapPhotos(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v3/incidents/{id}/photos");
        group.MapPost("", UploadAsync).DisableAntiforgery();
        group.MapGet("", ListAsync);
    }

    // ── POST /v3/incidents/{id}/photos ─────────────────────────────────────────
    private static async Task<IResult> UploadAsync(
        HttpContext http,
        string id,
        IncidentReads incidents,
        PhotoUploadGates gates,
        ImageProcessor processor,
        IObjectStorage storage,
        MongoContext mongo,
        IEventDispatcher dispatcher,
        IClock clock,
        IFogosCallerAccessor callerAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var incident = await incidents.GetByIdAsync(id, ct);
        if (incident is null)
            return Results.NotFound(new { success = false, error = "incident_not_found" });

        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { success = false, error = "missing_photo" });

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files["photo"] ?? form.Files["file"];
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { success = false, error = "missing_photo" });

        if (file.Length > MaxUploadBytes)
            return Results.Json(new { success = false, error = "too_large" }, statusCode: StatusCodes.Status413PayloadTooLarge);

        // Abuse gates (per-IP/min, per-incident/IP/hour, per-incident global/hour, pending cap).
        var ip = callerAccessor.Caller.RemoteIp;
        var gate = await gates.CheckAsync(id, ip, ct);
        if (!gate.Passed)
        {
            if (gate.RetryAfterSeconds > 0)
                http.Response.Headers.RetryAfter = gate.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Results.Json(
                new { success = false, error = "rate_limited", gate = gate.Gate.ToString(), retryAfter = gate.RetryAfterSeconds },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        ProcessedPhoto processed;
        try
        {
            await using var stream = file.OpenReadStream();
            processed = await processor.ProcessAsync(stream, ct);
        }
        catch (MissingGpsException)
        {
            return Results.Json(new { success = false, error = "missing_gps_exif" }, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (UnsupportedImageFormatException)
        {
            return Results.Json(new { success = false, error = "unsupported_format" }, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }
        catch (UndecodableImageException)
        {
            return Results.BadRequest(new { success = false, error = "invalid_image" });
        }

        // Dedup: same content signature already submitted for this incident.
        var duplicate = await mongo.IncidentPhotos
            .Find(Builders<IncidentPhoto>.Filter.And(
                Builders<IncidentPhoto>.Filter.Eq(x => x.IncidentId, id),
                Builders<IncidentPhoto>.Filter.Eq(x => x.Signature, processed.Signature)))
            .AnyAsync(ct);
        if (duplicate)
            return Results.Json(new { success = false, error = "duplicate" }, statusCode: StatusCodes.Status409Conflict);

        var storageKey = $"incidents/{id}/{Guid.NewGuid():N}.jpg";
        try
        {
            using var jpeg = new MemoryStream(processed.JpegBytes, writable: false);
            await storage.PutAsync(storageKey, jpeg, "image/jpeg", ct);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("PhotoUpload").LogError(ex, "photo storage failed for incident {IncidentId}", id);
            return Results.Json(new { success = false, error = "storage_failed" }, statusCode: StatusCodes.Status500InternalServerError);
        }

        var now = clock.UtcNow;
        var photo = new IncidentPhoto
        {
            IncidentId = id,
            Status = ModerationStatus.Pending,
            Public = false,
            Signature = processed.Signature,
            StorageKey = storageKey,
            SizeBytes = processed.JpegBytes.Length,
            Width = processed.Width,
            Height = processed.Height,
            Mime = "image/jpeg",
            Gps = processed.Gps,
            Altitude = processed.Altitude,
            Heading = processed.Heading,
            TakenAt = processed.TakenAt,
            Client = ResolveClient(http),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await mongo.IncidentPhotos.InsertOneAsync(photo, cancellationToken: ct);

        // Best-effort: a Redis hiccup must not fail an upload whose bytes are already stored + recorded.
        try
        {
            await dispatcher.DispatchAsync(new PhotoSubmitted(photo.Id, id), ct: ct);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("PhotoUpload").LogWarning(ex, "PhotoSubmitted dispatch failed for {PhotoId}", photo.Id);
        }

        return Results.Json(new { id = photo.Id, status = "pending" }, statusCode: StatusCodes.Status202Accepted);
    }

    // ── GET /v3/incidents/{id}/photos ──────────────────────────────────────────
    private static async Task<IResult> ListAsync(
        HttpContext http,
        string id,
        IncidentReads incidents,
        IObjectStorage storage,
        CancellationToken ct)
    {
        if (await incidents.GetByIdAsync(id, ct) is null)
            return Results.NotFound(new { success = false, error = "incident_not_found" });

        var photos = await incidents.PublicPhotosByIncidentsAsync([id], ct);
        var data = photos.Select(p => new
        {
            id = p.Id,
            publicUrl = storage.PublicUrl(p.StorageKey),
            width = p.Width,
            height = p.Height,
            takenAt = p.TakenAt?.ToString("o", CultureInfo.InvariantCulture),
            gps = p.Gps is { } g ? new { latitude = g.Latitude, longitude = g.Longitude } : null,
        });

        http.Response.Headers.CacheControl = ListCacheControl;
        return Results.Json(new { success = true, data });
    }

    /// <summary>Uploading client family: the explicit <c>X-Client</c> header, else a coarse User-Agent guess.</summary>
    private static string ResolveClient(HttpContext http)
    {
        var explicitClient = http.Request.Headers["X-Client"].ToString();
        if (!string.IsNullOrWhiteSpace(explicitClient))
            return explicitClient.Length > 60 ? explicitClient[..60] : explicitClient;

        var ua = http.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(ua))
            return "unknown";
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "android";
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) || ua.Contains("iOS", StringComparison.OrdinalIgnoreCase))
            return "ios";
        return "web";
    }
}
