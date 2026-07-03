namespace Fogos.Infrastructure.Scheduling;

/// <summary>
/// Redis <c>SET NX EX</c> single-flight lock — the <c>ShouldBeUnique</c> equivalent from the legacy
/// Laravel jobs. A holder gets an opaque token it must present to release, so a lock is never freed
/// by a different run (or a stale run whose TTL already lapsed and was re-acquired).
/// </summary>
public interface ISingleFlightLock
{
    /// <summary>Acquire <paramref name="key"/> for <paramref name="ttl"/>; returns a release token or null if held.</summary>
    Task<string?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Release <paramref name="key"/> only if <paramref name="token"/> still owns it.</summary>
    Task ReleaseAsync(string key, string token, CancellationToken ct = default);
}
