using System.Collections.Concurrent;
using Fogos.Domain.Users;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Api.Auth;

/// <summary>
/// Maps a validated <see cref="ClerkClaims"/> to the local <see cref="User"/> record, creating it on
/// first sight. A 60s in-memory cache (mirrors <see cref="ApiKeyResolver"/>) keeps the auth middleware
/// off Mongo on the hot path; the miss path is a race-safe <c>FindOneAndUpdate</c> upsert so two
/// concurrent first requests converge on a single document.
/// </summary>
public sealed class UserProvisioningService(MongoContext mongo)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public async Task<User> GetOrProvisionAsync(ClerkClaims claims, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue(claims.ClerkUserId, out var entry) && entry.Expires > now)
            return entry.User;

        var filter = Builders<User>.Filter.Eq(u => u.ClerkUserId, claims.ClerkUserId);

        // The filter's equality seeds clerkUserId on insert, so we must not SetOnInsert it too
        // (Mongo rejects that as a path conflict). Identity fields are set-on-insert; LastSeenAt and
        // profile fields refresh every time (throttled to once per TTL by the cache above).
        var update = Builders<User>.Update
            .SetOnInsert(u => u.Role, UserRole.User)
            .SetOnInsert(u => u.CreatedAt, now)
            .Set(u => u.LastSeenAt, now);
        if (!string.IsNullOrEmpty(claims.Email))
            update = update.Set(u => u.Email, claims.Email);
        if (!string.IsNullOrEmpty(claims.Name))
            update = update.Set(u => u.DisplayName, claims.Name);

        var user = await mongo.Users.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<User> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            ct);

        _cache[claims.ClerkUserId] = new CacheEntry(user, now + CacheTtl);
        return user;
    }

    private readonly record struct CacheEntry(User User, DateTimeOffset Expires);
}
