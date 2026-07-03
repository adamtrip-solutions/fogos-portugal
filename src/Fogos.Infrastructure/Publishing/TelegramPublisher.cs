using System.Net.Http.Json;
using System.Text.Json;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fogos.Infrastructure.Publishing;

/// <summary>
/// Telegram bot publisher: <c>sendPhoto</c> when the post carries an image, otherwise
/// <c>sendMessage</c>; HTML parse mode; optional <c>message_thread_id</c> for forum channels.
/// </summary>
public sealed class TelegramPublisher(
    IHttpClientFactory httpFactory,
    IOptions<PublishingOptions> publishing,
    IOptions<TelegramOptions> telegram,
    IOpsNotifier ops,
    ILogger<TelegramPublisher> logger) : SocialPublisherBase(publishing, ops, logger), ITelegramPublisher
{
    public const string HttpClientName = "telegram";

    public Task<PublishResult> PublishAsync(SocialPost post, string channelKey = "telegram", CancellationToken ct = default) =>
        PublishCoreAsync(channelKey, post, ct);

    protected override async Task<PublishResult> SendAsync(SocialPost post, string channelKey, CancellationToken ct)
    {
        var opts = telegram.Value;
        if (string.IsNullOrWhiteSpace(opts.BotToken))
            return await FailAsync("Telegram publish skipped: bot token not configured.", ct);

        var client = httpFactory.CreateClient(HttpClientName);
        var baseUrl = $"https://api.telegram.org/bot{opts.BotToken}";

        HttpResponseMessage response;
        if (post.HasImage)
        {
            var bytes = post.ImageBytes ?? await File.ReadAllBytesAsync(post.ImagePath!, ct);
            using var form = new MultipartFormDataContent
            {
                { new StringContent(opts.ChatId), "chat_id" },
                { new StringContent(post.Text), "caption" },
                { new StringContent("HTML"), "parse_mode" },
            };
            if (!string.IsNullOrEmpty(post.TelegramThreadId))
                form.Add(new StringContent(post.TelegramThreadId), "message_thread_id");
            var photo = new ByteArrayContent(bytes);
            form.Add(photo, "photo", "capture.png");
            response = await client.PostAsync($"{baseUrl}/sendPhoto", form, ct);
        }
        else
        {
            var payload = new Dictionary<string, object>
            {
                ["chat_id"] = opts.ChatId,
                ["text"] = post.Text,
                ["parse_mode"] = "HTML",
            };
            if (!string.IsNullOrEmpty(post.TelegramThreadId))
                payload["message_thread_id"] = post.TelegramThreadId;
            response = await client.PostAsJsonAsync($"{baseUrl}/sendMessage", payload, ct);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return await FailAsync($"Telegram returned {(int)response.StatusCode}: {json}", ct);

        using var doc = JsonDocument.Parse(json);
        var messageId = doc.RootElement.TryGetProperty("result", out var result)
                        && result.TryGetProperty("message_id", out var mid)
            ? mid.GetRawText()
            : null;
        return PublishResult.Ok(messageId);
    }
}
