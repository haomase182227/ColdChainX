using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Application.Interfaces;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;

namespace ColdChainX.Infrastructure.Services.Firebase;

public sealed class FirebaseMessagingClient : IFirebaseMessagingClient
{
    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FirebaseMessagingClient> _logger;

    public FirebaseMessagingClient(
        FirebaseMessaging messaging,
        ILogger<FirebaseMessagingClient> logger)
    {
        _messaging = messaging;
        _logger = logger;
    }

    public bool IsConfigured => true;

    public string? ConfigurationError => null;

    public async Task<IReadOnlyList<FirebaseTokenSendResult>> SendToTokensAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        if (tokens.Count == 0)
            return Array.Empty<FirebaseTokenSendResult>();
        if (tokens.Count > 500)
            throw new ArgumentOutOfRangeException(nameof(tokens), "Firebase multicast supports at most 500 tokens per batch.");

        try
        {
            var response = await _messaging.SendEachForMulticastAsync(
                BuildMulticastMessage(tokens, title, body, data),
                cancellationToken);

            return response.Responses.Select(ToResult).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase multicast request failed for {TokenCount} devices.", tokens.Count);
            var result = ToResult(ex);
            return tokens.Select(_ => result).ToList();
        }
    }

    public async Task<FirebaseTokenSendResult> SendToTopicAsync(
        string topic,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _messaging.SendAsync(
                BuildMessage(title, body, data, topic: topic),
                cancellationToken);
            return new FirebaseTokenSendResult { Success = true };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase topic request failed.");
            return ToResult(ex);
        }
    }

    private static MulticastMessage BuildMulticastMessage(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data)
    {
        var message = BuildMessage(title, body, data);
        return new MulticastMessage
        {
#pragma warning disable CS0618 // Mobile clients provide FCM registration tokens, not Firebase Installation IDs.
            Tokens = tokens,
#pragma warning restore CS0618
            Notification = message.Notification,
            Data = message.Data,
            Android = message.Android,
            Apns = message.Apns
        };
    }

    private static Message BuildMessage(
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        string? topic = null)
    {
        return new Message
        {
            Topic = topic,
            Notification = new Notification
            {
                Title = title,
                Body = body
            },
            Data = new Dictionary<string, string>(data),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification
                {
                    ChannelId = "coldchainx_operational",
                    Sound = "default"
                }
            },
            Apns = new ApnsConfig
            {
                Aps = new Aps
                {
                    Sound = "default",
                    ContentAvailable = true
                }
            }
        };
    }

    private static FirebaseTokenSendResult ToResult(SendResponse response)
        => response.IsSuccess
            ? new FirebaseTokenSendResult { Success = true }
            : ToResult(response.Exception);

    private static FirebaseTokenSendResult ToResult(Exception? exception)
    {
        var messagingError = (exception as FirebaseMessagingException)?.MessagingErrorCode;
        var errorCode = messagingError?.ToString() ?? "FirebaseSendFailed";

        return new FirebaseTokenSendResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = "Firebase notification delivery failed.",
            IsInvalidToken = messagingError is
                MessagingErrorCode.Unregistered or
                MessagingErrorCode.InvalidArgument or
                MessagingErrorCode.SenderIdMismatch,
            IsTemporaryFailure = messagingError is
                MessagingErrorCode.Internal or
                MessagingErrorCode.Unavailable or
                MessagingErrorCode.QuotaExceeded
        };
    }
}
