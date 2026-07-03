using Fogos.Worker.Subscriptions;

namespace Fogos.Integration.Tests;

/// <summary>
/// Pure unit tests for the change-stream bridge's active-set semantics — no containers,
/// so they exercise the subscription delta logic even where change-stream e2e is skipped.
/// </summary>
public sealed class ActiveDeltaTrackerTests
{
    [Fact]
    public void Insert_active_is_added_inactive_is_none()
    {
        var tracker = new ActiveDeltaTracker([]);
        Assert.Equal(DeltaKind.Added, tracker.Insert("a", active: true));
        Assert.Equal(DeltaKind.None, tracker.Insert("b", active: false));
        Assert.Contains("a", tracker.ActiveIds);
        Assert.DoesNotContain("b", tracker.ActiveIds);
    }

    [Fact]
    public void Update_to_active_is_updated_when_already_active_added_when_newly_active()
    {
        var tracker = new ActiveDeltaTracker(["a"]);
        Assert.Equal(DeltaKind.Updated, tracker.Update("a", active: true));
        Assert.Equal(DeltaKind.Added, tracker.Update("c", active: true));
    }

    [Fact]
    public void Update_flipping_to_inactive_is_removed_then_none()
    {
        var tracker = new ActiveDeltaTracker(["a"]);
        Assert.Equal(DeltaKind.Removed, tracker.Update("a", active: false));
        Assert.Equal(DeltaKind.None, tracker.Update("a", active: false));
        Assert.DoesNotContain("a", tracker.ActiveIds);
    }

    [Fact]
    public void Delete_removes_only_tracked_ids()
    {
        var tracker = new ActiveDeltaTracker(["a"]);
        Assert.Equal(DeltaKind.Removed, tracker.Delete("a"));
        Assert.Equal(DeltaKind.None, tracker.Delete("a"));
        Assert.Equal(DeltaKind.None, tracker.Delete("ghost"));
    }
}
