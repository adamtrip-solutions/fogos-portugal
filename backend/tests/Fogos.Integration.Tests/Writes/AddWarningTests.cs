using Fogos.Domain.Auth;
using Fogos.Domain.Events;
using Fogos.Domain.Warnings;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Fogos.Infrastructure.Reads;
using Fogos.Worker.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Integration.Tests.Writes;

/// <summary>
/// <c>addWarning</c> operator mutation: persists a broadcast warning with the caller as issuer, fans it out
/// (dry-run) via the worker handler — MANUAL with the "ALERTA:" banner, AGIF verbatim — rejects SITE, and
/// enforces the <c>write:warnings</c> scope.
/// </summary>
[Collection("fogos")]
public sealed class AddWarningTests(ContainerFixture fixture)
{
    private const string WarnKey = "fgs_live_operator_warnings";

    private async Task SeedWarningsOperatorAsync() =>
        await SeedData.InsertApiKeyAsync(fixture, WarnKey, ApiTier.Operator,
            name: "civil protection", scopes: [ApiScopes.WriteWarnings]);

    [SkippableFact]
    public async Task Manual_warning_persists_with_issuer_and_fans_out_with_alerta_banner()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await SeedWarningsOperatorAsync();

        var doc = await fixture.GraphQLAsync(WarnKey,
            "mutation($input:WarningInput!){ addWarning(kind:MANUAL, input:$input){ id kind message } }",
            new { input = new { message = "Evacuação imediata da zona X" } });

        Assert.False(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        var result = doc.RootElement.GetProperty("data").GetProperty("addWarning");
        var id = result.GetProperty("id").GetString()!;
        Assert.Equal("MANUAL", result.GetProperty("kind").GetString());

        var stored = await ctx.Warnings.Find(Builders<Warning>.Filter.Eq(x => x.Id, id)).SingleAsync();
        Assert.Equal(WarningKind.Manual, stored.Kind);
        Assert.Equal("civil protection", stored.IssuedBy);
        Assert.Equal("Evacuação imediata da zona X", stored.Message);

        // Worker fan-out, dry-run: all main channels, MANUAL carries the ALERTA banner.
        var ops = new RecordingOps();
        var handler = BuildHandler(ops);
        await handler.HandleAsync(new WarningCreated(id, WarningKind.Manual), CancellationToken.None);

        var channels = ops.Captures.Select(c => c.Channel).ToHashSet();
        Assert.Superset(new HashSet<string> { "twitter", "telegram", "facebook", "discordPosts" }, channels);

        var telegram = ops.Captures.Single(c => c.Channel == "telegram").Payload;
        Assert.Equal("ALERTA: \r\nEvacuação imediata da zona X", telegram);
    }

    [SkippableFact]
    public async Task Agif_warning_posts_the_message_verbatim()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var ctx = SeedData.Context(fixture);
        await SeedWarningsOperatorAsync();

        var doc = await fixture.GraphQLAsync(WarnKey,
            "mutation($input:WarningInput!){ addWarning(kind:AGIF, input:$input){ id kind } }",
            new { input = new { message = "AGIF: risco máximo de incêndio rural hoje" } });

        var id = doc.RootElement.GetProperty("data").GetProperty("addWarning").GetProperty("id").GetString()!;

        var ops = new RecordingOps();
        var handler = BuildHandler(ops);
        await handler.HandleAsync(new WarningCreated(id, WarningKind.Agif), CancellationToken.None);

        // No ALERTA banner — AGIF is passed through as-is (legacy addAgif).
        var telegram = ops.Captures.Single(c => c.Channel == "telegram").Payload;
        Assert.Equal("AGIF: risco máximo de incêndio rural hoje", telegram);
        Assert.DoesNotContain("ALERTA", telegram);
    }

    [SkippableFact]
    public async Task Site_warning_is_rejected()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        await SeedWarningsOperatorAsync();

        var doc = await fixture.GraphQLAsync(WarnKey,
            "mutation($input:WarningInput!){ addWarning(kind:SITE, input:$input){ id } }",
            new { input = new { message = "banner" } });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
        Assert.Contains("WARNING_KIND_UNSUPPORTED", doc.RootElement.GetProperty("errors").ToString());
    }

    [SkippableFact]
    public async Task Incidents_only_operator_cannot_add_a_warning()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        await SeedData.ResetAsync(fixture);
        var incidentsKey = "fgs_live_operator_incidents_nowarn";
        await SeedData.InsertApiKeyAsync(fixture, incidentsKey, ApiTier.Operator,
            name: "incident operator", scopes: [ApiScopes.WriteIncidents]);

        var doc = await fixture.GraphQLAsync(incidentsKey,
            "mutation($input:WarningInput!){ addWarning(kind:MANUAL, input:$input){ id } }",
            new { input = new { message = "should be denied" } });

        Assert.True(doc.RootElement.TryGetProperty("errors", out _), doc.RootElement.ToString());
    }

    private WarningSocialHandler BuildHandler(RecordingOps ops)
    {
        var services = fixture.Factory.Services;
        var warnings = new WarningReads(services.GetRequiredService<MongoContext>());

        var publishing = Options.Create(new PublishingOptions()); // DryRun defaults
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage()));
        var twitter = new TwitterPublisher(factory, publishing, Options.Create(new TwitterOptions()), ops, NullLogger<TwitterPublisher>.Instance);
        var telegram = new TelegramPublisher(factory, publishing, Options.Create(new TelegramOptions()), ops, NullLogger<TelegramPublisher>.Instance);
        var facebook = new FacebookPublisher(factory, publishing, Options.Create(new FacebookOptions()), ops, NullLogger<FacebookPublisher>.Instance);
        var discord = new DiscordPostPublisher(factory, publishing, Options.Create(new DiscordPostOptions()), ops, NullLogger<DiscordPostPublisher>.Instance);

        return new WarningSocialHandler(warnings, twitter, telegram, facebook, discord);
    }
}
