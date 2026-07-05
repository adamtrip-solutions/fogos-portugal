namespace Fogos.Integration.Tests.Incidents;

/// <summary>
/// Two-pass status-transition scenarios: create a fire (seeds the social thread + Facebook post id),
/// then ingest a status change and assert the reacendimento/dominado post, threaded tweet chaining, and
/// the Facebook comment on the stored post.
/// </summary>
[Collection("fogos")]
public sealed class IncidentTransitionTests(ContainerFixture fixture)
{
    [SkippableFact]
    public async Task Dominado_posts_chains_thread_and_comments_facebook()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedAsync(h);

        // Pass 1 — create the fire as "Em Curso"; social handler seeds thread + facebookPostId.
        await IngestOneAsync(h, status: "Em Curso");
        await h.DrainAsync("default");
        var thread = await h.Threads.GetAsync("FIRE1", CancellationToken.None);
        Assert.NotNull(thread!.LastTweetId);
        Assert.NotNull(thread.FacebookPostId);
        var firstTweetId = thread.LastTweetId;

        // Pass 2 — status → Em Resolução (Dominado transition: current ∈ {7,8}, previous 5).
        await IngestOneAsync(h, status: "Em Resolução");
        await h.DrainAsync("default");

        Assert.Contains(h.Twitter.Posts, p => p.Text.Contains("Dominado"));
        var dominado = h.Twitter.Posts.Last(p => p.Text.Contains("Dominado"));
        Assert.Equal(firstTweetId, dominado.ReplyToId); // chained onto the thread tail
        Assert.Contains(h.Facebook.Comments, c => c.PostId == thread.FacebookPostId && c.Message.Contains("→"));

        var updated = await h.Threads.GetAsync("FIRE1", CancellationToken.None);
        Assert.NotEqual(firstTweetId, updated!.LastTweetId); // tail advanced
    }

    [SkippableFact]
    public async Task Reacendimento_posts_when_returning_to_em_curso()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        using var h = new IncidentPipelineHarness(fixture);
        await SeedAsync(h);

        await IngestOneAsync(h, status: "Em Curso");
        await h.DrainAsync("default");
        await IngestOneAsync(h, status: "Conclusão"); // 5 → 8 (dominado)
        await h.DrainAsync("default");
        await IngestOneAsync(h, status: "Em Curso");  // 8 → 5 (reacendimento)
        await h.DrainAsync("default");

        Assert.Contains(h.Twitter.Posts, p => p.Text.Contains("Reacendimento"));
        Assert.Contains(h.Discord.Posts, p => p.Text.Contains("Reacendimento")); // reacendimento also hits the Discord posts channel
    }

    private static async Task SeedAsync(IncidentPipelineHarness h)
    {
        await h.SeedConcelhoAsync("Ourém", "1408", "1408", "14");
        await h.SeedDistrictAsync("Santarém", "14");
        await h.SeedStationAsync(1, 39.65, -8.44);
    }

    private static async Task IngestOneAsync(IncidentPipelineHarness h, string status)
    {
        var json = "{ \"exceededTransferLimit\": false, \"features\": [ { \"attributes\": {" +
            "\"Numero\": \"FIRE1\", \"Concelho\": \"Ourém\", \"Freguesia\": \"Freixianda\"," +
            $"\"EstadoOcorrencia\": \"{status}\", \"CodNatureza\": 3101, \"Natureza\": \"3101 - Incêndio Florestal\"," +
            "\"MeiosAereos\": 1, \"MeiosTerrestres\": 5, \"Operacionais\": 20," +
            "\"Latitude\": 39.66, \"Longitude\": -8.45, \"DataOcorrencia\": 1754006400000 } } ] }";
        var raws = await h.ArcGisSource(json).FetchAsync();
        await h.Ingest.IngestAsync(raws);
    }
}
