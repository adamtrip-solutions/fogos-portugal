using System.Net;
using System.Text.Json;
using Fogos.Domain.Photos;
using Fogos.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Photos;

[Collection("fogos")]
public sealed class PhotoUploadTests(ContainerFixture fixture)
{
    private static MultipartFormDataContent Multipart(byte[] bytes, string field = "photo", string filename = "photo.jpg")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(file, field, filename);
        return content;
    }

    private static async Task<JsonDocument> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [SkippableFact]
    public async Task Upload_happy_path_returns_202_pending_and_stores_object()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("UP1"));
        var client = fixture.Factory.CreateClient();

        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: 11);
        var response = await client.PostAsync("/v3/incidents/UP1/photos", Multipart(jpeg));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = await Body(response);
        var photoId = body.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(photoId));
        Assert.Equal("pending", body.RootElement.GetProperty("status").GetString());

        // Document: Pending, not public, GPS + signature + dimensions recorded.
        var doc = await ctx.IncidentPhotos.Find(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photoId)).SingleAsync();
        Assert.Equal(ModerationStatus.Pending, doc.Status);
        Assert.False(doc.Public);
        Assert.Equal("UP1", doc.IncidentId);
        Assert.StartsWith("incidents/UP1/", doc.StorageKey);
        Assert.EndsWith(".jpg", doc.StorageKey);
        Assert.False(string.IsNullOrEmpty(doc.Signature));
        Assert.Equal(320, doc.Width);
        Assert.Equal(240, doc.Height);
        Assert.NotNull(doc.Gps);
        Assert.Equal(PhotoFixtures.Lat, doc.Gps!.Value.Latitude, precision: 5);
        Assert.Equal(PhotoFixtures.Lng, doc.Gps!.Value.Longitude, precision: 5);
        Assert.NotNull(doc.TakenAt);

        // Binary landed in MinIO under the recorded key.
        var storage = fixture.Factory.Services.GetRequiredService<IObjectStorage>();
        Assert.True(await storage.ExistsAsync(doc.StorageKey));

        // publicUrl shape: once approved+public, the GET listing exposes CDN-base + key.
        await ctx.IncidentPhotos.UpdateOneAsync(
            Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photoId),
            Builders<IncidentPhoto>.Update.Set(x => x.Status, ModerationStatus.Approved).Set(x => x.Public, true));

        var list = await client.GetAsync("/v3/incidents/UP1/photos");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains("s-maxage=300", list.Headers.CacheControl?.ToString());
        using var listBody = await Body(list);
        var row = listBody.RootElement.GetProperty("data").EnumerateArray().Single();
        Assert.Equal($"https://cdn.example.test/{doc.StorageKey}", row.GetProperty("publicUrl").GetString());
        Assert.Equal(320, row.GetProperty("width").GetInt32());
        Assert.Equal(PhotoFixtures.Lat, row.GetProperty("gps").GetProperty("latitude").GetDouble(), precision: 5);
    }

    [SkippableFact]
    public async Task Upload_accepts_the_alternate_file_field_name()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("UP2"));
        var client = fixture.Factory.CreateClient();

        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: 12);
        var response = await client.PostAsync("/v3/incidents/UP2/photos", Multipart(jpeg, field: "file"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [SkippableFact]
    public async Task Upload_without_gps_is_422()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("NG1"));
        var client = fixture.Factory.CreateClient();

        var response = await client.PostAsync("/v3/incidents/NG1/photos", Multipart(PhotoFixtures.Jpeg(exif: null)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = await Body(response);
        Assert.Equal("missing_gps_exif", body.RootElement.GetProperty("error").GetString());
    }

    [SkippableFact]
    public async Task Upload_of_non_image_is_415_and_corrupt_image_is_400()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("NI1"));
        var client = fixture.Factory.CreateClient();

        var notImage = await client.PostAsync("/v3/incidents/NI1/photos", Multipart("plain text pretending"u8.ToArray()));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notImage.StatusCode);

        var corrupt = new byte[64];
        corrupt[0] = 0xFF; corrupt[1] = 0xD8; corrupt[2] = 0xFF; // JPEG magic, garbage body
        var undecodable = await client.PostAsync("/v3/incidents/NI1/photos", Multipart(corrupt));
        Assert.Equal(HttpStatusCode.BadRequest, undecodable.StatusCode);
    }

    [SkippableFact]
    public async Task Upload_missing_file_is_400_and_unknown_incident_is_404()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("MF1"));
        var client = fixture.Factory.CreateClient();

        var empty = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        var missing = await client.PostAsync("/v3/incidents/MF1/photos", empty);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var unknown = await client.PostAsync("/v3/incidents/NOPE/photos", Multipart(PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile())));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [SkippableFact]
    public async Task Duplicate_signature_for_same_incident_is_409()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("DUP1"));
        var client = fixture.Factory.CreateClient();

        var jpeg = PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: 33);
        var first = await client.PostAsync("/v3/incidents/DUP1/photos", Multipart(jpeg));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsync("/v3/incidents/DUP1/photos", Multipart(jpeg));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var body = await Body(second);
        Assert.Equal("duplicate", body.RootElement.GetProperty("error").GetString());
    }

    [SkippableFact]
    public async Task Gate_trip_returns_429_with_retry_after_and_the_failing_gate()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await fixture.FlushRedisAsync();

        using var lowLimits = fixture.CreateFactory(new Dictionary<string, string?>
        {
            ["PhotoGate:PerIpPerMinute"] = "1",
        });
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.DeleteManyAsync(Builders<Fogos.Domain.Incidents.Incident>.Filter.Empty);
        await ctx.IncidentPhotos.DeleteManyAsync(Builders<IncidentPhoto>.Filter.Empty);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("GT1"));
        var client = lowLimits.CreateClient();

        var first = await client.PostAsync("/v3/incidents/GT1/photos", Multipart(PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: 51)));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        var second = await client.PostAsync("/v3/incidents/GT1/photos", Multipart(PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: 52)));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("Retry-After"), "429 must carry Retry-After");
        var retryAfter = int.Parse(second.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 60);

        using var body = await Body(second);
        Assert.Equal("rate_limited", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("PerIpPerMinute", body.RootElement.GetProperty("gate").GetString());

        await fixture.FlushRedisAsync(); // don't leak the tripped window into other tests
    }

    [SkippableFact]
    public async Task Listing_shows_only_approved_public_photos()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("LS1"));
        await ctx.IncidentPhotos.InsertManyAsync(
        [
            SeedData.Photo("LS1", ModerationStatus.Approved, @public: true),
            SeedData.Photo("LS1", ModerationStatus.Approved, @public: false),
            SeedData.Photo("LS1", ModerationStatus.Pending, @public: false),
        ]);
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/v3/incidents/LS1/photos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await Body(response);
        Assert.Single(body.RootElement.GetProperty("data").EnumerateArray());
    }
}
