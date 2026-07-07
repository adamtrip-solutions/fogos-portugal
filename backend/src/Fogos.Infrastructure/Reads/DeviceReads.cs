using Fogos.Domain.Alerts;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Reads;

/// <summary>Read queries over registered push devices and the alert subscriptions bound to them.</summary>
public sealed class DeviceReads(MongoContext context)
{
    /// <summary>A device by its capability id (random GUID), or null when unknown.</summary>
    public async Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default) =>
        await context.Devices
            .Find(Builders<Device>.Filter.Eq(x => x.Id, deviceId))
            .FirstOrDefaultAsync(ct);

    /// <summary>Devices by their capability ids, keyed by id (one query per delivery batch).</summary>
    public async Task<IReadOnlyDictionary<string, Device>> GetByIdsAsync(
        IReadOnlyCollection<string> deviceIds, CancellationToken ct = default)
    {
        if (deviceIds.Count == 0)
            return new Dictionary<string, Device>();
        var found = await context.Devices
            .Find(Builders<Device>.Filter.In(x => x.Id, deviceIds))
            .ToListAsync(ct);
        return found.ToDictionary(d => d.Id);
    }

    /// <summary>A device by its push endpoint (unique), or null when unknown.</summary>
    public async Task<Device?> GetByEndpointAsync(string endpoint, CancellationToken ct = default) =>
        await context.Devices
            .Find(Builders<Device>.Filter.Eq(x => x.PushEndpoint, endpoint))
            .FirstOrDefaultAsync(ct);

    /// <summary>The alert subscriptions bound to a device, newest first (the deviceSubscriptions capability query).</summary>
    public async Task<IReadOnlyList<AlertSubscription>> SubscriptionsByDeviceAsync(string deviceId, CancellationToken ct = default) =>
        await context.AlertSubscriptions
            .Find(Builders<AlertSubscription>.Filter.Eq(x => x.DeviceId, deviceId))
            .Sort(Builders<AlertSubscription>.Sort.Descending(x => x.CreatedAt))
            .ToListAsync(ct);
}
