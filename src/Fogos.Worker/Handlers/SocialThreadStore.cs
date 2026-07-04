using Fogos.Domain.Social;
using Fogos.Domain.Time;
using Fogos.Infrastructure.Mongo;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Read/upsert helper for per-incident <see cref="SocialThread"/> state (lastTweetId / facebookPostId /
/// sentImportant / sentBig). Replaces the legacy pattern of mutating those fields on the incident doc.
/// Upserts are keyed by incident id so a handler never has to pre-create the row.
/// </summary>
public sealed class SocialThreadStore(MongoContext mongo, IClock clock)
{
    private FilterDefinition<SocialThread> ById(string id) => Builders<SocialThread>.Filter.Eq(x => x.IncidentId, id);

    public Task<SocialThread?> GetAsync(string incidentId, CancellationToken ct) =>
        mongo.SocialThreads.Find(ById(incidentId)).FirstOrDefaultAsync(ct)!;

    public Task SetLastTweetIdAsync(string incidentId, string? tweetId, CancellationToken ct) =>
        Apply(incidentId, Builders<SocialThread>.Update.Set(x => x.LastTweetId, tweetId), ct);

    public Task SetFacebookPostIdAsync(string incidentId, string? postId, CancellationToken ct) =>
        Apply(incidentId, Builders<SocialThread>.Update.Set(x => x.FacebookPostId, postId), ct);

    /// <summary>
    /// Atomically claims the new-incident social fan-out: sets <see cref="SocialThread.SentNewIncidentPost"/>
    /// only if it wasn't already set, returning true for the single caller that wins. Idempotency guard for
    /// at-least-once redelivery of <c>IncidentCreated</c> — a loser must not publish.
    /// </summary>
    public async Task<bool> TryClaimNewIncidentPostAsync(string incidentId, CancellationToken ct)
    {
        var filter = Builders<SocialThread>.Filter.And(
            ById(incidentId),
            Builders<SocialThread>.Filter.Ne(x => x.SentNewIncidentPost, true));
        var update = Builders<SocialThread>.Update
            .Set(x => x.SentNewIncidentPost, true)
            .SetOnInsert(x => x.IncidentId, incidentId)
            .Set(x => x.UpdatedAt, clock.UtcNow);
        try
        {
            await mongo.SocialThreads.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The row already exists with the flag set (filter excluded it, so upsert tried to insert a
            // duplicate _id) — another delivery already claimed it.
            return false;
        }
    }

    public Task MarkImportantSentAsync(string incidentId, CancellationToken ct) =>
        Apply(incidentId, Builders<SocialThread>.Update.Set(x => x.SentImportantPost, true), ct);

    public Task MarkBigSentAsync(string incidentId, CancellationToken ct) =>
        Apply(incidentId, Builders<SocialThread>.Update.Set(x => x.SentBigIncidentPost, true), ct);

    private Task Apply(string incidentId, UpdateDefinition<SocialThread> update, CancellationToken ct)
    {
        var combined = Builders<SocialThread>.Update.Combine(
            update,
            Builders<SocialThread>.Update.SetOnInsert(x => x.IncidentId, incidentId),
            Builders<SocialThread>.Update.Set(x => x.UpdatedAt, clock.UtcNow));
        return mongo.SocialThreads.UpdateOneAsync(ById(incidentId), combined, new UpdateOptions { IsUpsert = true }, ct);
    }
}
