namespace Fogos.Infrastructure.Queue;

/// <summary>
/// At-most-once guard backed by Redis <c>SET key NX EX</c>. A handler calls
/// <see cref="TryMarkAsync"/> keyed on something stable (e.g. <c>"tweet:{eventId}"</c>) and only
/// performs its side effect when it wins the race — surviving redelivery and multiple workers.
/// </summary>
public interface IProcessedMarker
{
    /// <summary>True when this caller claimed the key (first to see it); false if already marked.</summary>
    Task<bool> TryMarkAsync(string key, CancellationToken ct = default);
}
