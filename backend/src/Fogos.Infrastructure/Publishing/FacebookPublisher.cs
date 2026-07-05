using System.Text.Json;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Facebook Graph API page publisher: <c>/{page}/feed</c> (text), <c>/{page}/photos</c> (image),
/// and <c>/{post}/comments</c> (comment on a stored post, used to document status transitions).
/// </summary>
public sealed class FacebookPublisher(
    IHttpClientFactory httpFactory,
    IOptions<PublishingOptions> publishing,
    IOptions<FacebookOptions> facebook,
    IOpsNotifier ops,
    ILogger<FacebookPublisher> logger) : SocialPublisherBase(publishing, ops, logger), IFacebookPublisher
{
    public const string HttpClientName = "facebook";

    private readonly IOptions<PublishingOptions> _publishing = publishing;

    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "facebook", CancellationToken ct = default) =>
        PublishCoreAsync(channelKey, post, ct);

    protected override async Task<PublishResult> SendAsync(SocialPost post, string channelKey, CancellationToken ct)
    {
        var opts = facebook.Value;
        if (string.IsNullOrWhiteSpace(opts.PageId) || string.IsNullOrWhiteSpace(opts.AccessToken))
            return await FailAsync("Facebook publish skipped: page id / access token not configured.", ct);

        var client = httpFactory.CreateClient(HttpClientName);
        var root = GraphRoot(opts);

        if (post.HasImage)
        {
            var bytes = post.ImageBytes ?? await File.ReadAllBytesAsync(post.ImagePath!, ct);
            using var form = new MultipartFormDataContent
            {
                { new StringContent(opts.AccessToken), "access_token" },
                { new StringContent(post.Text), "caption" },
            };
            form.Add(new ByteArrayContent(bytes), "source", "capture.png");
            using var photoResponse = await client.PostAsync($"{root}/{opts.PageId}/photos", form, ct);
            var photoJson = await photoResponse.Content.ReadAsStringAsync(ct);
            if (!photoResponse.IsSuccessStatusCode)
                return await FailAsync($"Facebook /photos returned {(int)photoResponse.StatusCode}: {photoJson}", ct);
            // Photo endpoint returns post_id (note: not id).
            return PublishResult.Ok(ReadString(photoJson, "post_id"));
        }

        var url = $"{root}/{opts.PageId}/feed?access_token={Uri.EscapeDataString(opts.AccessToken)}&message={Uri.EscapeDataString(post.Text)}";
        using var response = await client.PostAsync(url, content: null, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return await FailAsync($"Facebook /feed returned {(int)response.StatusCode}: {json}", ct);
        return PublishResult.Ok(ReadString(json, "id"));
    }

    public async Task<PublishResult> CommentOnPostAsync(string postId, string message, string channelKey = "facebook", CancellationToken ct = default)
    {
        switch (_publishing.Value.ModeFor(channelKey))
        {
            case PublisherMode.Off:
                return PublishResult.Skipped;

            case PublisherMode.DryRun:
                await Ops.DryRunCaptureAsync(channelKey, $"comment on {postId}: {message}", ct);
                return PublishResult.Ok(PublishResult.DryRunId());

            default:
                if (string.IsNullOrWhiteSpace(postId))
                    return await FailAsync("Facebook comment skipped: no post id.", ct);
                try
                {
                    var opts = facebook.Value;
                    var client = httpFactory.CreateClient(HttpClientName);
                    var form = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("access_token", opts.AccessToken),
                        new KeyValuePair<string, string>("message", message),
                    });
                    using var response = await client.PostAsync($"{GraphRoot(opts)}/{postId}/comments", form, ct);
                    var json = await response.Content.ReadAsStringAsync(ct);
                    if (!response.IsSuccessStatusCode)
                        return await FailAsync($"Facebook /comments returned {(int)response.StatusCode}: {json}", ct);
                    return PublishResult.Ok(ReadString(json, "id"));
                }
                catch (Exception ex)
                {
                    return await FailAsync($"Facebook comment failed: {ex.Message}", ct);
                }
        }
    }

    private static string GraphRoot(FacebookOptions opts) =>
        string.IsNullOrWhiteSpace(opts.ApiVersion)
            ? "https://graph.facebook.com"
            : $"https://graph.facebook.com/{opts.ApiVersion.TrimStart('/')}";

    private static string? ReadString(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
