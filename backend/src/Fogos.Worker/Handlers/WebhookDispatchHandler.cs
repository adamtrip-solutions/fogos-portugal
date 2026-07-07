using System.Text;
using System.Text.Json;
using Fogos.Domain.Events;
using Fogos.Domain.Time;
using Fogos.Domain.Webhooks;
using Fogos.Infrastructure.Mongo;
using Fogos.Infrastructure.Options;
using Fogos.Infrastructure.Ops;
using Fogos.Infrastructure.Queue;
using Fogos.Infrastructure.Reads;
using Fogos.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Fogos.Worker.Handlers;

/// <summary>
/// Delivers the five webhook events to every active subscribed endpoint: a JSON body
/// <c>{ event, eventId, occurredAt, data }</c> signed with <c>X-Fogos-Signature: sha256=…</c>. A 2xx is
/// success (and clears the failure counter); anything else (non-2xx or transport error) increments
/// <c>ConsecutiveFailures</c> and, at the threshold, disables the endpoint with an ops notice. No
/// in-request retry — each endpoint is attempted once per event, and one bad endpoint never throws the
/// handler (which would re-deliver to healthy siblings on stream redelivery).
/// </summary>
public sealed class WebhookDispatchHandler(
    WebhookReads webhooks,
    MongoContext mongo,
    IHttpClientFactory httpFactory,
    IOpsNotifier ops,
    IClock clock,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatchHandler> logger)
    : IEventHandler<IncidentCreated>, IEventHandler<IncidentEscalating>, IEventHandler<RekindleDetected>,
      IEventHandler<SituationReportCreated>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task HandleAsync(IncidentCreated evt, CancellationToken ct) =>
        DispatchAsync(WebhookEvents.IncidentCreated, evt.EventId, new { incidentId = evt.IncidentId }, ct);

    public Task HandleAsync(IncidentEscalating evt, CancellationToken ct) =>
        DispatchAsync(WebhookEvents.IncidentEscalating, evt.EventId, new { incidentId = evt.IncidentId }, ct);

    public Task HandleAsync(RekindleDetected evt, CancellationToken ct) =>
        DispatchAsync(WebhookEvents.IncidentRekindle, evt.EventId, new { incidentId = evt.IncidentId }, ct);

    public Task HandleAsync(SituationReportCreated evt, CancellationToken ct) =>
        DispatchAsync(WebhookEvents.ReportCreated, evt.EventId, new { reportId = evt.ReportId }, ct);

    private async Task DispatchAsync(string eventName, Guid eventId, object data, CancellationToken ct)
    {
        var endpoints = await webhooks.ActiveForEventAsync(eventName, ct);
        if (endpoints.Count == 0)
            return;

        var body = JsonSerializer.Serialize(
            new { @event = eventName, eventId = eventId.ToString("N"), occurredAt = clock.UtcNow, data },
            Json);

        var client = httpFactory.CreateClient(WebhookSigner.HttpClientName);
        foreach (var endpoint in endpoints)
            await DeliverAsync(client, endpoint, body, ct);
    }

    private async Task DeliverAsync(HttpClient client, WebhookEndpoint endpoint, string body, CancellationToken ct)
    {
        var ok = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(WebhookSigner.SignatureHeader, WebhookSigner.Sign(endpoint.Secret, body));

            using var response = await client.SendAsync(request, ct);
            ok = (int)response.StatusCode is >= 200 and < 300;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook delivery failed for {Url}", endpoint.Url);
        }

        if (ok)
        {
            // Reset the failure counter only when it isn't already zero.
            if (endpoint.ConsecutiveFailures > 0)
                await mongo.WebhookEndpoints.UpdateOneAsync(
                    Builders<WebhookEndpoint>.Filter.Eq(x => x.Id, endpoint.Id),
                    Builders<WebhookEndpoint>.Update.Set(x => x.ConsecutiveFailures, 0),
                    cancellationToken: ct);
            return;
        }

        var updated = await mongo.WebhookEndpoints.FindOneAndUpdateAsync(
            Builders<WebhookEndpoint>.Filter.Eq(x => x.Id, endpoint.Id),
            Builders<WebhookEndpoint>.Update.Inc(x => x.ConsecutiveFailures, 1),
            new FindOneAndUpdateOptions<WebhookEndpoint> { ReturnDocument = ReturnDocument.After },
            ct);

        if (updated is not null && updated.Active && updated.ConsecutiveFailures >= options.Value.DisableThreshold)
        {
            await mongo.WebhookEndpoints.UpdateOneAsync(
                Builders<WebhookEndpoint>.Filter.Eq(x => x.Id, endpoint.Id),
                Builders<WebhookEndpoint>.Update.Set(x => x.Active, false),
                cancellationToken: ct);
            await ops.ErrorAsync(
                $"🔌 Webhook desativado após {updated.ConsecutiveFailures} falhas consecutivas: {endpoint.Url} (cliente {endpoint.ClientId}).", ct);
        }
    }
}
