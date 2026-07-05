using Fogos.Infrastructure.Notifications;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Publishing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fogos.Integration.Tests;

/// <summary>FCM topic-condition building + notifier batching (no real Firebase).</summary>
public sealed class FcmTests
{
    [Fact]
    public void ChunkConditions_splits_over_five_topics_into_multiple_conditions()
    {
        var topics = Enumerable.Range(1, 7).Select(i => $"topic-{i}").ToArray();
        var conditions = FcmTopics.ChunkConditions(topics);

        Assert.Equal(2, conditions.Count);
        Assert.Equal(5, CountTopics(conditions[0]));
        Assert.Equal(2, CountTopics(conditions[1]));
        Assert.Contains("'topic-1' in topics", conditions[0]);
        Assert.Contains(" || ", conditions[0]);
    }

    [Fact]
    public void Incident_topics_include_important_only_when_requested()
    {
        var topics = new FcmTopics(prefix: "dev-", legacyEnabled: false);
        Assert.Equal(["dev-incident-42"], topics.Incident("42", includeImportant: false));
        Assert.Contains("dev-incident-important", topics.Incident("42", includeImportant: true));
    }

    [Fact]
    public async Task Notifier_On_sends_one_message_per_condition()
    {
        var sender = new RecordingFcmSender();
        var notifier = BuildNotifier(PublisherMode.On, sender, out _);

        var topics = Enumerable.Range(1, 7).Select(i => $"t{i}").ToArray();
        var ok = await notifier.SendNotificationAsync("Titulo", "Corpo", topics);

        Assert.True(ok);
        Assert.Equal(2, sender.Sends.Count); // 7 topics → two ≤5 conditions
        Assert.All(sender.Sends, s => Assert.True(CountTopics(s.Condition!) <= FcmTopics.MaxTopicsPerCondition));
        Assert.All(sender.Sends, s => Assert.StartsWith("FogosPortugal - ", s.Title));
    }

    [Fact]
    public async Task Notifier_DryRun_captures_and_never_sends()
    {
        var sender = new RecordingFcmSender();
        var notifier = BuildNotifier(PublisherMode.DryRun, sender, out var ops);

        var topics = Enumerable.Range(1, 7).Select(i => $"t{i}").ToArray();
        var ok = await notifier.SendNotificationAsync("Titulo", "Corpo", topics);

        Assert.True(ok);
        Assert.Empty(sender.Sends);
        Assert.Equal(2, ops.Captures.Count);
        Assert.All(ops.Captures, c => Assert.Equal("fcm", c.Channel));
    }

    private static FcmNotifier BuildNotifier(PublisherMode mode, RecordingFcmSender sender, out RecordingOps ops)
    {
        ops = new RecordingOps();
        var publishing = Options.Create(new PublishingOptions { Channels = { ["fcm"] = mode } });
        var fcm = Options.Create(new FcmOptions());
        var env = new FakeHostEnvironment("Production");
        return new FcmNotifier(sender, publishing, fcm, ops, env, NullLogger<FcmNotifier>.Instance);
    }

    private static int CountTopics(string condition) =>
        condition.Split(" || ", StringSplitOptions.RemoveEmptyEntries).Length;
}
