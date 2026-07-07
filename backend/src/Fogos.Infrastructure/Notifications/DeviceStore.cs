using Fogos.Domain.Alerts;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// The single write path for removing a device and reconciling the subscriptions that pointed at it.
/// Deleting a device (explicit unsubscribe, a 404/410 from the push service, or the inactivity purge) means
/// its <b>anonymous</b> subscriptions are gone with it — the browser that owned them is the only thing that
/// knew them — while <b>owned</b> subscriptions survive as poll-only, with their <c>DeviceId</c> cleared.
/// </summary>
public sealed class DeviceStore(MongoContext mongo)
{
    /// <summary>Deletes the device document and cascades: drop its anonymous subs, null DeviceId on owned subs.</summary>
    public async Task DeleteWithCascadeAsync(string deviceId, CancellationToken ct = default)
    {
        var f = Builders<AlertSubscription>.Filter;

        // Anonymous subscriptions (no owner) bound to this device: gone with the device.
        await mongo.AlertSubscriptions.DeleteManyAsync(
            f.Eq(x => x.DeviceId, deviceId) & f.Eq(x => x.OwnerUserId, null), ct);

        // Owned subscriptions survive, but the device link is cleared (poll-only from here).
        await mongo.AlertSubscriptions.UpdateManyAsync(
            f.Eq(x => x.DeviceId, deviceId) & f.Ne(x => x.OwnerUserId, null),
            Builders<AlertSubscription>.Update.Unset(x => x.DeviceId),
            cancellationToken: ct);

        await mongo.Devices.DeleteOneAsync(Builders<Device>.Filter.Eq(x => x.Id, deviceId), ct);
    }
}
