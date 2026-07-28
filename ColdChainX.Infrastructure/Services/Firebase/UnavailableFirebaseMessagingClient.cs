using ColdChainX.Application.DTOs.Notifications;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services.Firebase;

public sealed class UnavailableFirebaseMessagingClient : IFirebaseMessagingClient
{
    public UnavailableFirebaseMessagingClient(string? configurationError)
    {
        ConfigurationError = string.IsNullOrWhiteSpace(configurationError)
            ? "Firebase is not configured."
            : configurationError;
    }

    public bool IsConfigured => false;

    public string? ConfigurationError { get; }

    public Task<IReadOnlyList<FirebaseTokenSendResult>> SendToTokensAsync(
        IReadOnlyList<string> tokens,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FirebaseTokenSendResult> results = tokens.Select(_ => Unavailable()).ToList();
        return Task.FromResult(results);
    }

    public Task<FirebaseTokenSendResult> SendToTopicAsync(
        string topic,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Unavailable());

    private FirebaseTokenSendResult Unavailable()
        => new()
        {
            Success = false,
            ErrorCode = "FirebaseNotConfigured",
            ErrorMessage = ConfigurationError,
            IsTemporaryFailure = true
        };
}
