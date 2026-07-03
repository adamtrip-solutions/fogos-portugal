using Amazon.S3;
using Amazon.S3.Model;
using Fogos.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Storage;

/// <summary>S3-compatible object storage (Cloudflare R2 in prod, MinIO in dev) via AWSSDK.S3.</summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _publicBaseUrl;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        var o = options.Value;
        _bucket = o.Bucket;
        _publicBaseUrl = o.PublicBaseUrl;

        var config = new AmazonS3Config
        {
            ServiceURL = o.Endpoint,
            ForcePathStyle = o.ForcePathStyle,
            AuthenticationRegion = o.Region,
        };
        _client = new AmazonS3Client(o.AccessKey, o.SecretKey, config);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true,
        };
        await _client.PutObjectAsync(request, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default) =>
        await _client.DeleteObjectAsync(_bucket, key, ct);

    public async Task<Stream> GetAsync(string key, CancellationToken ct = default)
    {
        var response = await _client.GetObjectAsync(_bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public string PublicUrl(string key) => _publicBaseUrl.TrimEnd('/') + "/" + key;

    public Task<string> PresignGetAsync(string key, TimeSpan validity, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(validity),
        };
        return _client.GetPreSignedURLAsync(request);
    }

    public void Dispose() => _client.Dispose();
}
