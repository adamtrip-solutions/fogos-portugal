namespace Fogos.Infrastructure.Storage;

/// <summary>
/// Provider-agnostic object storage. One S3-compatible implementation serves
/// Cloudflare R2 (prod) and MinIO (dev); anything S3-shaped can slot in later.
/// Documents persist only the storage key — public URLs are configuration.
/// </summary>
public interface IObjectStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    Task<Stream> GetAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Public CDN URL for a key (configured base + key). No I/O.</summary>
    string PublicUrl(string key);

    /// <summary>Time-limited signed GET URL for non-public objects (moderation queue).</summary>
    Task<string> PresignGetAsync(string key, TimeSpan validity, CancellationToken ct = default);
}
