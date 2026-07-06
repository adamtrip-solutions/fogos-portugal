using System.Collections.Concurrent;
using Fogos.Domain.Auth;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Api.Auth;

/// <summary>
/// Resolves an <c>X-API-Key</c> (<c>fgs_live_…</c>) to its <see cref="ApiClient"/> by SHA-256 hex
/// lookup in <c>api_clients</c>, with a short in-memory cache (positive and negative) so a burst of
/// requests on one key hits Mongo at most once per TTL.
/// </summary>
public sealed class ApiKeyResolver(MongoContext mongo)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>The canonical SHA-256 hex hash of a plaintext key (delegates to the shared Domain generator).</summary>
    public static string Hash(string apiKey) => ApiKeyGenerator.Hash(apiKey);

    /// <summary>Returns the matching client (even when revoked, so the caller can 401), or null if unknown.</summary>
    public async Task<ApiClient?> ResolveAsync(string apiKey, CancellationToken ct = default)
    {
        var hash = Hash(apiKey);
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue(hash, out var entry) && entry.Expires > now)
            return entry.Client;

        var client = await mongo.ApiClients
            .Find(Builders<ApiClient>.Filter.Eq(x => x.KeyHash, hash))
            .FirstOrDefaultAsync(ct);

        _cache[hash] = new CacheEntry(client, now + CacheTtl);
        return client;
    }

    private readonly record struct CacheEntry(ApiClient? Client, DateTimeOffset Expires);
}
