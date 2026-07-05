using System.Net;
using System.Text.Json;
using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Photos;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Storage;
using Fogos.Worker.Handlers;
using Fogos.Worker.Jobs.Photos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace Fogos.Integration.Tests.Photos;

[Collection("fogos")]
public sealed class PhotoModerationTests(ContainerFixture fixture)
{
    private const string OperatorKey = "fgs_live_operator_photos_0900";

    private async Task<string> SeedOperatorAsync() =>
        await SeedData.InsertApiKeyAsync(fixture, OperatorKey, ApiTier.Operator,
            name: "photo moderator", scopes: [ApiScopes.ModeratePhotos]);

    private async Task<(string PhotoId, string StorageKey)> UploadAsync(string incidentId, byte seed)
    {
        var client = fixture.Factory.CreateClient();
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(PhotoFixtures.Jpeg(PhotoFixtures.GpsProfile(), seed: seed)), "photo", "photo.jpg" },
        };
        var response = await client.PostAsync($"/v3/incidents/{incidentId}/photos", content);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Accepted == response.StatusCode, $"upload failed: {response.StatusCode} {raw}");
        using var body = JsonDocument.Parse(raw);
        var photoId = body.RootElement.GetProperty("id").GetString()!;

        var ctx = SeedData.Context(fixture);
        var doc = await ctx.IncidentPhotos.Find(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photoId)).SingleAsync();
        return (photoId, doc.StorageKey);
    }

    [SkippableFact]
    public async Task Approve_with_publish_makes_photo_public_and_listable_everywhere()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("MOD1"));
        await SeedOperatorAsync();
        var (photoId, storageKey) = await UploadAsync("MOD1", seed: 61);

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!){ moderatePhoto(photoId:$id, decision:APPROVE, publish:true){ id status public moderation { decision by } } }",
            new { id = photoId });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var result = doc.RootElement.GetProperty("data").GetProperty("moderatePhoto");
        Assert.Equal("APPROVED", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("public").GetBoolean());
        Assert.Equal("approve", result.GetProperty("moderation").GetProperty("decision").GetString());
        Assert.Equal("photo moderator", result.GetProperty("moderation").GetProperty("by").GetString());

        // Persisted state.
        var stored = await ctx.IncidentPhotos.Find(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photoId)).SingleAsync();
        Assert.Equal(ModerationStatus.Approved, stored.Status);
        Assert.True(stored.Public);

        // REST listing shows it with a CDN publicUrl.
        var client = fixture.Factory.CreateClient();
        var list = await client.GetAsync("/v3/incidents/MOD1/photos");
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var row = listBody.RootElement.GetProperty("data").EnumerateArray().Single();
        Assert.Equal(photoId, row.GetProperty("id").GetString());
        Assert.Equal($"https://cdn.example.test/{storageKey}", row.GetProperty("publicUrl").GetString());

        // GraphQL incident.photos shows it too.
        var graph = await fixture.GraphQLAsync("query($id:ID!){ incident(id:$id){ photos { id publicUrl } } }", new { id = "MOD1" });
        var photos = graph.RootElement.GetProperty("data").GetProperty("incident").GetProperty("photos");
        Assert.Equal(photoId, photos.EnumerateArray().Single().GetProperty("id").GetString());
    }

    [SkippableFact]
    public async Task Approve_with_publish_false_keeps_photo_out_of_public_listings()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("MOD2"));
        await SeedOperatorAsync();
        var (photoId, _) = await UploadAsync("MOD2", seed: 62);

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!){ moderatePhoto(photoId:$id, decision:APPROVE, publish:false){ status public } }",
            new { id = photoId });

        var result = doc.RootElement.GetProperty("data").GetProperty("moderatePhoto");
        Assert.Equal("APPROVED", result.GetProperty("status").GetString());
        Assert.False(result.GetProperty("public").GetBoolean());

        var client = fixture.Factory.CreateClient();
        var list = await client.GetAsync("/v3/incidents/MOD2/photos");
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(listBody.RootElement.GetProperty("data").EnumerateArray());
    }

    [SkippableFact]
    public async Task Approved_photo_fan_out_is_captured_in_dry_run()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("MOD3"));
        await SeedOperatorAsync();
        var (photoId, _) = await UploadAsync("MOD3", seed: 63);

        await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!){ moderatePhoto(photoId:$id, decision:APPROVE, publish:true){ id } }",
            new { id = photoId });

        // Run the Worker-side handler directly (dry-run publishers + recording ops).
        var ops = new RecordingOps();
        var handler = BuildHandler(ops);
        await handler.HandleAsync(new PhotoApproved(photoId, "MOD3"), CancellationToken.None);

        var channels = ops.Captures.Select(c => c.Channel).ToHashSet();
        Assert.Superset(new HashSet<string> { "twitter", "facebook", "telegram", "discordPosts", "fcm" }, channels);
        Assert.Equal(5, ops.Captures.Count);

        var tweet = ops.Captures.Single(c => c.Channel == "twitter").Payload;
        Assert.Contains("Foi publicada uma nova foto no incêndio em Rua de Teste", tweet);
        Assert.Contains("https://fogosportugal.pt/fogo/MOD3/detalhe", tweet);
        Assert.Contains("Tirada às", tweet);            // TakenAt extra
        Assert.Contains("km do local do incêndio", tweet); // distance extra

        // The image bytes were fetched from storage (base publishers mark image posts with "[img]").
        var telegram = ops.Captures.Single(c => c.Channel == "telegram").Payload;
        Assert.StartsWith("[img]", telegram);

        var discord = ops.Captures.Single(c => c.Channel == "discordPosts").Payload;
        Assert.Contains("https://cdn.example.test/incidents/MOD3/", discord);

        var push = ops.Captures.Single(c => c.Channel == "fcm").Payload;
        Assert.Contains("Foi publicada uma nova foto deste incêndio.", push);
        Assert.Contains("incident-MOD3", push);
    }

    [SkippableFact]
    public async Task Reject_deletes_document_and_stored_object()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("MOD4"));
        await SeedOperatorAsync();
        var (photoId, storageKey) = await UploadAsync("MOD4", seed: 64);

        var storage = fixture.Factory.Services.GetRequiredService<IObjectStorage>();
        Assert.True(await storage.ExistsAsync(storageKey));

        var doc = await fixture.GraphQLAsync(OperatorKey,
            "mutation($id:ID!){ moderatePhoto(photoId:$id, decision:REJECT){ id status } }",
            new { id = photoId });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Equal("REJECTED", doc.RootElement.GetProperty("data").GetProperty("moderatePhoto").GetProperty("status").GetString());

        Assert.Equal(0, await ctx.IncidentPhotos.CountDocumentsAsync(Builders<IncidentPhoto>.Filter.Eq(x => x.Id, photoId)));
        Assert.False(await storage.ExistsAsync(storageKey));
    }

    [SkippableFact]
    public async Task Moderation_requires_the_scope_and_pendingPhotos_returns_presigned_urls()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await ctx.Incidents.InsertOneAsync(SeedData.Incident("MOD5"));
        await SeedOperatorAsync();
        var registeredKey = "fgs_live_registered_nophoto_10";
        await SeedData.InsertApiKeyAsync(fixture, registeredKey, ApiTier.Registered);
        var (photoId, _) = await UploadAsync("MOD5", seed: 65);

        // Anonymous caller: authorization error, no data.
        var anonymous = await fixture.GraphQLAsync("{ pendingPhotos { id } }");
        Assert.True(anonymous.RootElement.TryGetProperty("errors", out _), anonymous.RootElement.ToString());

        // Scope-less registered key: still denied.
        var registered = await fixture.GraphQLAsync(registeredKey, "{ pendingPhotos { id } }");
        Assert.True(registered.RootElement.TryGetProperty("errors", out _), registered.RootElement.ToString());

        var anonMutation = await fixture.GraphQLAsync(
            "mutation($id:ID!){ moderatePhoto(photoId:$id, decision:APPROVE){ id } }", new { id = photoId });
        Assert.True(anonMutation.RootElement.TryGetProperty("errors", out _));

        // Operator: pending rows with 15-min presigned URLs (signed, not the CDN base).
        var doc = await fixture.GraphQLAsync(OperatorKey,
            "{ pendingPhotos { id incidentId width height presignedUrl gps { latitude longitude } } }");
        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var rows = doc.RootElement.GetProperty("data").GetProperty("pendingPhotos").EnumerateArray().ToList();
        var row = Assert.Single(rows);
        Assert.Equal(photoId, row.GetProperty("id").GetString());
        Assert.Equal("MOD5", row.GetProperty("incidentId").GetString());
        var url = row.GetProperty("presignedUrl").GetString()!;
        Assert.Contains("X-Amz-Signature", url);
        Assert.Contains("incidents/MOD5/", url);
        Assert.DoesNotContain("cdn.example.test", url);
    }

    [SkippableFact]
    public async Task Pending_moderation_job_posts_once_then_respects_cooldown()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await fixture.FlushRedisAsync();
        var ctx = SeedData.Context(fixture);
        await ctx.IncidentPhotos.InsertOneAsync(SeedData.Photo("JOB1", ModerationStatus.Pending, @public: false));

        var ops = new RecordingOps();
        var job = new CheckPendingPhotoModerationJob(
            fixture.Factory.Services.GetRequiredService<MongoContext>(),
            fixture.Factory.Services.GetRequiredService<IConnectionMultiplexer>(),
            ops,
            NullLogger<CheckPendingPhotoModerationJob>.Instance);

        await job.RunAsync(CancellationToken.None);
        await job.RunAsync(CancellationToken.None); // within the 2h cooldown → silent

        var info = Assert.Single(ops.Infos);
        Assert.Contains("1 foto(s) à espera de moderação", info);

        // Zero pending → no notice even after the cooldown clears.
        await fixture.FlushRedisAsync();
        await ctx.IncidentPhotos.DeleteManyAsync(Builders<IncidentPhoto>.Filter.Empty);
        await job.RunAsync(CancellationToken.None);
        Assert.Single(ops.Infos);
    }

    /// <summary>Hand-wires the Worker fan-out handler over the shared containers with dry-run publishers.</summary>
    private ApprovedPhotoSocialHandler BuildHandler(RecordingOps ops)
    {
        var services = fixture.Factory.Services;
        var mongo = services.GetRequiredService<MongoContext>();
        var storage = services.GetRequiredService<IObjectStorage>();
        var clock = services.GetRequiredService<IClock>();

        var publishing = Options.Create(new PublishingOptions()); // everything defaults to DryRun
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage()));

        var twitter = new TwitterPublisher(factory, publishing, Options.Create(new TwitterOptions()), ops, NullLogger<TwitterPublisher>.Instance);
        var telegram = new TelegramPublisher(factory, publishing, Options.Create(new TelegramOptions()), ops, NullLogger<TelegramPublisher>.Instance);
        var facebook = new FacebookPublisher(factory, publishing, Options.Create(new FacebookOptions()), ops, NullLogger<FacebookPublisher>.Instance);
        var discord = new DiscordPostPublisher(factory, publishing, Options.Create(new DiscordPostOptions()), ops, NullLogger<DiscordPostPublisher>.Instance);
        var fcm = new FcmNotifier(new RecordingFcmSender(), publishing, Options.Create(new FcmOptions()), ops,
            new FakeHostEnvironment("Production"), NullLogger<FcmNotifier>.Instance);

        return new ApprovedPhotoSocialHandler(
            mongo, storage, twitter, telegram, facebook, discord, fcm, clock,
            Options.Create(new IncidentPipelineOptions()));
    }
}
