using System.Text.RegularExpressions;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>Publisher dry-run / off / thread-splitting behaviour (no containers needed).</summary>
public sealed class PublishingTests
{
    private static TwitterPublisher BuildTwitter(PublisherMode mode, RecordingOps ops)
    {
        var publishing = Options.Create(new PublishingOptions { Channels = { ["twitter"] = mode } });
        var twitter = Options.Create(new TwitterOptions());
        var factory = new StubHttpClientFactory(new StubHttpMessageHandler(_ => new HttpResponseMessage()));
        return new TwitterPublisher(factory, publishing, twitter, ops, NullLogger<TwitterPublisher>.Instance);
    }

    [Fact]
    public void Splitter_returns_single_unsuffixed_element_when_under_limit()
    {
        const string text = "Curto e direto.";
        var parts = TwitterTextSplitter.Split(text);
        Assert.Single(parts);
        Assert.Equal(text, parts[0]);
        Assert.DoesNotContain("(1/", parts[0]);
    }

    [Fact]
    public void Splitter_produces_numbered_parts_within_limit_for_long_text()
    {
        var words = Enumerable.Repeat("incendio", 100);
        var text = string.Join(" ", words); // ~899 chars, well over 280
        var parts = TwitterTextSplitter.Split(text);

        Assert.True(parts.Count > 1, "long text should split into multiple parts");

        var total = parts.Count;
        for (var i = 0; i < parts.Count; i++)
        {
            Assert.True(parts[i].Length <= TwitterTextSplitter.DefaultMaxLength, $"part {i} exceeds 280");
            Assert.Matches(new Regex($@"\({i + 1}/{total}\)$"), parts[i]);
        }

        // Stripping suffixes and rejoining reproduces the original word sequence.
        var reconstructed = string.Join(" ", parts.Select(p => Regex.Replace(p, @" \(\d+/\d+\)$", "")));
        Assert.Equal(text, reconstructed);
    }

    [Fact]
    public async Task Twitter_dryrun_returns_fake_id_and_captures()
    {
        var ops = new RecordingOps();
        var publisher = BuildTwitter(PublisherMode.DryRun, ops);

        var result = await publisher.PublishAsync(new SocialPost { Text = "Um alerta simples." });

        Assert.True(result.Success);
        Assert.StartsWith("dryrun-", result.ExternalId);
        Assert.Single(ops.Captures);
        Assert.Equal("twitter", ops.Captures.First().Channel);
    }

    [Fact]
    public async Task Twitter_off_is_silent_noop()
    {
        var ops = new RecordingOps();
        var publisher = BuildTwitter(PublisherMode.Off, ops);

        var result = await publisher.PublishAsync(new SocialPost { Text = "Nada acontece." });

        Assert.True(result.Success);
        Assert.Null(result.ExternalId);
        Assert.Empty(ops.Captures);
    }

    [Fact]
    public async Task Twitter_dryrun_thread_splits_and_chains_replies()
    {
        var ops = new RecordingOps();
        var publisher = BuildTwitter(PublisherMode.DryRun, ops);
        var text = string.Join(" ", Enumerable.Repeat("incendio", 100));

        var result = await publisher.PublishAsync(new SocialPost { Text = text });

        // One capture per thread part, in order.
        var captures = ops.Captures.OrderBy(c => ExtractIndex(c.Payload)).Select(c => c.Payload).ToList();
        Assert.True(captures.Count > 1);

        // Parse id + replyTo from each capture and assert the reply chain.
        string? previousId = null;
        foreach (var payload in captures)
        {
            var id = ExtractField(payload, "id");
            var replyTo = ExtractField(payload, "replyTo");
            if (previousId is null)
                Assert.Equal("-", replyTo); // first tweet is standalone
            else
                Assert.Equal(previousId, replyTo); // each part replies to the previous
            previousId = id;
        }

        // The publisher returns the last tweet id (thread tail).
        Assert.Equal(previousId, result.ExternalId);
    }

    private static int ExtractIndex(string payload) =>
        int.Parse(Regex.Match(payload, @"tweet (\d+)/").Groups[1].Value);

    private static string ExtractField(string payload, string field) =>
        Regex.Match(payload, $@"{field}=(\S+)").Groups[1].Value;
}
