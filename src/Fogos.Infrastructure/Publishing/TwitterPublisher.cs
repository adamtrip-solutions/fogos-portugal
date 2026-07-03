using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// X/Twitter publisher. Splits over-length text into a reply-chained thread (media on the first
/// tweet only), signs each request with OAuth 1.0a user-context, and returns the last tweet id
/// (mirroring the legacy thread-tail threading model). Never throws.
/// </summary>
public sealed class TwitterPublisher(
    IHttpClientFactory httpFactory,
    IOptions<PublishingOptions> publishing,
    IOptions<TwitterOptions> twitter,
    IOpsNotifier ops,
    ILogger<TwitterPublisher> logger) : ITwitterPublisher
{
    public const string HttpClientName = "twitter";
    private const string TweetEndpoint = "https://api.twitter.com/2/tweets";
    private const string MediaEndpoint = "https://upload.twitter.com/1.1/media/upload.json";

    public async Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "twitter", CancellationToken ct = default)
    {
        var mode = publishing.Value.ModeFor(channelKey);
        if (mode == PublisherMode.Off)
            return PublishResult.Skipped;

        var parts = TwitterTextSplitter.Split(post.Text);
        var account = twitter.Value.AccountFor(channelKey);
        string? replyTo = post.ReplyToId;
        string? lastId = null;

        for (var i = 0; i < parts.Count; i++)
        {
            var withImage = i == 0 && post.HasImage;
            var result = mode == PublisherMode.DryRun
                ? await DryRunTweetAsync(channelKey, parts[i], i + 1, parts.Count, replyTo, withImage, ct)
                : await SendTweetAsync(account, parts[i], replyTo, withImage ? post : null, ct);

            if (!result.Success)
                return result; // abort the thread; caller sees the failure

            lastId = result.ExternalId;
            replyTo = result.ExternalId; // chain the next part onto this one
        }

        return PublishResult.Ok(lastId);
    }

    private async Task<PublishResult> DryRunTweetAsync(
        string channelKey, string text, int index, int total, string? replyTo, bool withImage, CancellationToken ct)
    {
        var id = PublishResult.DryRunId();
        var summary = $"tweet {index}/{total} id={id} replyTo={replyTo ?? "-"} image={(withImage ? "yes" : "no")}: {text}";
        await ops.DryRunCaptureAsync(channelKey, summary, ct);
        return PublishResult.Ok(id);
    }

    private async Task<PublishResult> SendTweetAsync(TwitterAccount account, string text, string? replyTo, SocialPost? imagePost, CancellationToken ct)
    {
        if (!account.IsConfigured)
        {
            await ops.ErrorAsync("Twitter publish skipped: account not configured while mode is On.", ct);
            return PublishResult.Fail("Twitter account not configured.");
        }

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);

            string? mediaId = null;
            if (imagePost is not null)
                mediaId = await UploadMediaAsync(client, account, imagePost, ct);

            var body = new Dictionary<string, object> { ["text"] = text };
            if (!string.IsNullOrEmpty(replyTo))
                body["reply"] = new Dictionary<string, object> { ["in_reply_to_tweet_id"] = replyTo };
            if (mediaId is not null)
                body["media"] = new Dictionary<string, object> { ["media_ids"] = new[] { mediaId } };

            using var request = new HttpRequestMessage(HttpMethod.Post, TweetEndpoint)
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Authorization = AuthorizationHeaderValue(
                OAuth1Signer.BuildAuthorizationHeader("POST", TweetEndpoint, [], account));

            using var response = await client.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return await FailAsync($"Twitter POST /2/tweets returned {(int)response.StatusCode}: {json}", ct);

            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
            return PublishResult.Ok(id);
        }
        catch (Exception ex)
        {
            return await FailAsync($"Twitter publish failed: {ex.Message}", ct);
        }
    }

    private async Task<string?> UploadMediaAsync(HttpClient client, TwitterAccount account, SocialPost post, CancellationToken ct)
    {
        var bytes = post.ImageBytes ?? await File.ReadAllBytesAsync(post.ImagePath!, ct);
        var base64 = Convert.ToBase64String(bytes);
        var form = new[] { new KeyValuePair<string, string>("media_data", base64) };

        using var request = new HttpRequestMessage(HttpMethod.Post, MediaEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = AuthorizationHeaderValue(
            OAuth1Signer.BuildAuthorizationHeader("POST", MediaEndpoint, form, account));

        using var response = await client.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("media_id_string", out var m) ? m.GetString() : null;
    }

    private static AuthenticationHeaderValue AuthorizationHeaderValue(string oauth)
    {
        // oauth already begins with "OAuth "; split into scheme + parameter for HttpClient.
        var space = oauth.IndexOf(' ');
        return new AuthenticationHeaderValue(oauth[..space], oauth[(space + 1)..]);
    }

    private async Task<PublishResult> FailAsync(string error, CancellationToken ct)
    {
        logger.LogWarning("{Error}", error);
        await ops.ErrorAsync(error, ct);
        return PublishResult.Fail(error);
    }
}
