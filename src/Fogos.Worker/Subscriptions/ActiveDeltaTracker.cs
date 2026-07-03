namespace Fogos.Worker.Subscriptions;

/// <summary>How a change affected the active-incident set.</summary>
public enum DeltaKind
{
    None,
    Added,
    Updated,
    Removed,
}

/// <summary>
/// Pure, testable active-set semantics for <c>activeIncidentsChanged</c>. Warmed from the DB
/// at startup, it classifies each incident change against the last-seen active state:
/// insert active → Added; update to active → Updated (or Added if it wasn't active);
/// update active→false or delete → Removed.
/// </summary>
public sealed class ActiveDeltaTracker
{
    private readonly HashSet<string> _active;

    public ActiveDeltaTracker(IEnumerable<string> initiallyActive) => _active = [.. initiallyActive];

    public IReadOnlySet<string> ActiveIds => _active;

    public DeltaKind Insert(string id, bool active)
    {
        if (!active)
            return DeltaKind.None;
        _active.Add(id);
        return DeltaKind.Added;
    }

    public DeltaKind Update(string id, bool active)
    {
        var wasActive = _active.Contains(id);
        if (active)
        {
            _active.Add(id);
            return wasActive ? DeltaKind.Updated : DeltaKind.Added;
        }

        if (!wasActive)
            return DeltaKind.None;
        _active.Remove(id);
        return DeltaKind.Removed;
    }

    public DeltaKind Delete(string id) => _active.Remove(id) ? DeltaKind.Removed : DeltaKind.None;
}
