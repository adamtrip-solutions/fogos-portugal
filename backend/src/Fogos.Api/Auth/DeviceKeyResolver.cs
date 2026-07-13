using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Fogos.Domain.Auth;
using Fogos.Domain.Devices;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Api.Auth;

/// <summary>
/// Resolves an <c>X-Device-Key</c> header (<c>fdv1.{deviceId}.{deviceSecret}</c>) to its mobile app
/// <see cref="Device"/>. Mirrors <see cref="ApiKeyResolver"/>: a short in-memory cache (60s, positive and
/// negative) keyed by the device id, so a burst of requests on one device hits Mongo at most once per TTL.
/// The stored <see cref="Device.SecretHash"/> is compared against the SHA-256 of the presented secret in
/// constant time; a revoked device (or a web-push device that carries no secret) never resolves. Revocation
/// therefore takes effect within the cache TTL (≤60s) — the accepted lag documented in the mobile contract.
/// </summary>
public sealed class DeviceKeyResolver(MongoContext mongo)
{
    /// <summary>The mandatory scheme prefix of the header value.</summary>
    public const string Prefix = "fdv1.";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    /// Returns the authenticated app device, or null when the header is malformed, the device is unknown, the
    /// secret does not match, the device is not an app device (no secret), or it has been revoked. A null
    /// result is always a hard 401 for the caller — a presented credential never downgrades to anonymous.
    /// </summary>
    public async Task<Device?> ResolveAsync(string headerValue, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(headerValue) || !headerValue.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        // "{deviceId}.{deviceSecret}" — neither the GUID id nor the base62 secret contains a dot, so the
        // remainder splits cleanly into exactly the two parts.
        var rest = headerValue[Prefix.Length..];
        var dot = rest.IndexOf('.');
        if (dot <= 0 || dot >= rest.Length - 1)
            return null;

        var deviceId = rest[..dot];
        var secret = rest[(dot + 1)..];

        // Device ids are GUID "N" strings (exactly 32 hex chars). Rejecting anything else before the lookup
        // keeps sprayed garbage ids out of the cache, which is keyed by attacker-controlled input.
        if (deviceId.Length != 32)
            return null;

        var device = await LookupAsync(deviceId, ct);
        if (device is null || device.Revoked || string.IsNullOrEmpty(device.SecretHash))
            return null;

        var presented = Encoding.UTF8.GetBytes(ApiKeyGenerator.Hash(secret));
        var stored = Encoding.UTF8.GetBytes(device.SecretHash);
        return CryptographicOperations.FixedTimeEquals(presented, stored) ? device : null;
    }

    private async Task<Device?> LookupAsync(string deviceId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_cache.TryGetValue(deviceId, out var entry) && entry.Expires > now)
            return entry.Device;

        var device = await mongo.Devices
            .Find(Builders<Device>.Filter.Eq(x => x.Id, deviceId))
            .FirstOrDefaultAsync(ct);

        _cache[deviceId] = new CacheEntry(device, now + CacheTtl);
        return device;
    }

    private readonly record struct CacheEntry(Device? Device, DateTimeOffset Expires);
}
