namespace Fogos.Infrastructure.Options;

/// <summary>
/// S3-compatible object storage settings. Cloudflare R2 in prod, MinIO in dev; the public
/// URL is a configured base (custom domain), never derived from the endpoint.
/// </summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "incident-photos";

    /// <summary>Public base URL (e.g. the CDN domain) that keys are appended to.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>R2 uses "auto"; MinIO ignores it but the SDK needs a value.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>MinIO and most self-hosted S3 need path-style addressing.</summary>
    public bool ForcePathStyle { get; set; } = true;
}
