using ColdChainX.Application.DTOs.Notifications;

namespace ColdChainX.Application.Interfaces;

public interface IFirebaseMessagingClient
{
    bool IsConfigured { get; }

    string? ConfigurationError { get; }

    Task<IReadOnlyList<FirebaseTokenSendResult>> SendToTokensAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default);

    Task<FirebaseTokenSendResult> SendToTopicAsync(
        string topic,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default);
}
