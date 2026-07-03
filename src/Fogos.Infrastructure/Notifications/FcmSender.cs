using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FcmOptions = Fogos.Infrastructure.Options.FcmOptions;

namespace Fogos.Infrastructure.Notifications;

/// <summary>
/// FirebaseAdmin-backed sender. The Firebase app is initialized lazily on first send (a named app so
/// it never collides with anything else in the process) and only when credentials are configured —
/// so constructing this in DryRun/Off deployments never fails.
/// </summary>
public sealed class FcmSender(IOptions<FcmOptions> options, ILogger<FcmSender> logger) : IFcmSender
{
    private const string AppName = "fogos-fcm";
    private readonly Lock _gate = new();
    private FirebaseApp? _app;
    private FirebaseMessaging? _messaging;

    public async Task<string> SendAsync(FcmSend message, CancellationToken ct = default)
    {
        var messaging = EnsureMessaging();

        var msg = new Message
        {
            Condition = message.Condition,
            Topic = message.Topic,
            Data = message.Data is null ? null : new Dictionary<string, string>(message.Data),
            Notification = message.DataOnly
                ? null
                : new Notification { Title = message.Title, Body = message.Body },
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig
            {
                Headers = message.DataOnly
                    ? new Dictionary<string, string> { ["apns-priority"] = "10", ["apns-push-type"] = "background" }
                    : new Dictionary<string, string> { ["apns-priority"] = "5" },
                Aps = message.DataOnly ? new Aps { ContentAvailable = true } : new Aps(),
            },
        };

        return await messaging.SendAsync(msg, ct);
    }

    private FirebaseMessaging EnsureMessaging()
    {
        if (_messaging is not null)
            return _messaging;

        lock (_gate)
        {
            if (_messaging is not null)
                return _messaging;

            var credential = LoadCredential()
                ?? throw new InvalidOperationException("FCM send requested but no credentials are configured.");

            _app = FirebaseApp.GetInstance(AppName)
                   ?? FirebaseApp.Create(new AppOptions
                   {
                       Credential = credential,
                       ProjectId = options.Value.ProjectId,
                   }, AppName);

            _messaging = FirebaseMessaging.GetMessaging(_app);
            logger.LogInformation("FirebaseMessaging initialized for project {Project}", options.Value.ProjectId);
            return _messaging;
        }
    }

    private GoogleCredential? LoadCredential()
    {
        var opts = options.Value;
        if (!string.IsNullOrWhiteSpace(opts.CredentialsJson))
            return GoogleCredential.FromJson(opts.CredentialsJson);
        if (!string.IsNullOrWhiteSpace(opts.CredentialsPath) && File.Exists(opts.CredentialsPath))
            return GoogleCredential.FromFile(opts.CredentialsPath);
        return null;
    }
}
