using System.Security.Cryptography;
using System.Text.Json;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Fogos.Api.Auth;

/// <summary>A freshly minted refresh token: the opaque plaintext (returned once) and its family id.</summary>
public sealed record IssuedRefreshToken(string PlainText, string Family);

/// <summary>Result of consuming a refresh token during rotation.</summary>
public enum RefreshOutcome { Valid, Reuse, Unknown }

public sealed record RefreshConsumeResult(RefreshOutcome Outcome, string? ClientId, string? Family);

/// <summary>
/// Opaque 256-bit refresh tokens with single-use rotation. Each token is stored as
/// <c>auth:refresh:{sha256}</c> → {clientId, family}; a parallel <c>auth:refresh:famof:{sha256}</c>
/// survives consumption so a replay of a spent token is detected as reuse and revokes the whole
/// family (<c>auth:refresh:family:{family}</c> membership set).
/// </summary>
public sealed class RefreshTokenStore(IConnectionMultiplexer redis, IOptions<AuthOptions> options)
{
    private readonly int _days = options.Value.RefreshTokenDays;

    // KEYS[1]=token record, KEYS[2]=famof marker. Consume atomically:
    //   returns {1, record} when valid (record deleted), {2, family} on replay, {0} when unknown.
    private const string ConsumeScript = """
        local rec = redis.call('GET', KEYS[1])
        if rec then
            redis.call('DEL', KEYS[1])
            return {1, rec}
        end
        local fam = redis.call('GET', KEYS[2])
        if fam then return {2, fam} end
        return {0, ''}
        """;

    /// <summary>Mints and stores a new refresh token in the given family.</summary>
    public async Task<IssuedRefreshToken> IssueAsync(string clientId, string family)
    {
        var plain = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hash = Sha256Hex(plain);
        var ttl = TimeSpan.FromDays(_days);
        var db = redis.GetDatabase();

        var record = JsonSerializer.Serialize(new StoredToken(clientId, family));
        await db.StringSetAsync(RecordKey(hash), record, ttl);
        await db.StringSetAsync(FamOfKey(hash), family, ttl);
        await db.SetAddAsync(FamilyKey(family), hash);
        await db.KeyExpireAsync(FamilyKey(family), ttl);

        return new IssuedRefreshToken(plain, family);
    }

    /// <summary>Consumes a token (single use). On reuse of a spent token, revokes the whole family.</summary>
    public async Task<RefreshConsumeResult> ConsumeAsync(string plainText)
    {
        var hash = Sha256Hex(plainText);
        var db = redis.GetDatabase();

        var result = (RedisResult[])(await db.ScriptEvaluateAsync(
            ConsumeScript,
            [RecordKey(hash), FamOfKey(hash)]))!;

        var code = (int)result[0];
        if (code == 1)
        {
            var stored = JsonSerializer.Deserialize<StoredToken>((string)result[1]!)!;
            return new RefreshConsumeResult(RefreshOutcome.Valid, stored.ClientId, stored.Family);
        }

        if (code == 2)
        {
            var family = (string)result[1]!;
            await RevokeFamilyAsync(family);
            return new RefreshConsumeResult(RefreshOutcome.Reuse, null, family);
        }

        return new RefreshConsumeResult(RefreshOutcome.Unknown, null, null);
    }

    /// <summary>Deletes every token in a family — the nuclear rotation-reuse response.</summary>
    public async Task RevokeFamilyAsync(string family)
    {
        var db = redis.GetDatabase();
        var members = await db.SetMembersAsync(FamilyKey(family));
        foreach (var member in members)
        {
            var hash = (string)member!;
            await db.KeyDeleteAsync(RecordKey(hash));
            await db.KeyDeleteAsync(FamOfKey(hash));
        }
        await db.KeyDeleteAsync(FamilyKey(family));
    }

    public static string NewFamily() => Guid.NewGuid().ToString("N");

    private static string RecordKey(string hash) => $"auth:refresh:{hash}";
    private static string FamOfKey(string hash) => $"auth:refresh:famof:{hash}";
    private static string FamilyKey(string family) => $"auth:refresh:family:{family}";

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record StoredToken(string ClientId, string Family);
}
