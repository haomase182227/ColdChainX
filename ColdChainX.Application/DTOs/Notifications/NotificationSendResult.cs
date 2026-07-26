namespace ColdChainX.Application.DTOs.Notifications;

public class NotificationSendResult
{
    public bool Success => SuccessfulSends > 0 && FailedSends == 0;

    public int TotalTokens { get; set; }

    public int SuccessfulSends { get; set; }

    public int FailedSends { get; set; }

    public string? ErrorMessage { get; set; }

    public List<Guid> NotificationIds { get; set; } = new();

    public void Add(NotificationSendResult other)
    {
        TotalTokens += other.TotalTokens;
        SuccessfulSends += other.SuccessfulSends;
        FailedSends += other.FailedSends;
        NotificationIds.AddRange(other.NotificationIds);

        if (!string.IsNullOrWhiteSpace(other.ErrorMessage))
        {
            ErrorMessage = string.IsNullOrWhiteSpace(ErrorMessage)
                ? other.ErrorMessage
                : $"{ErrorMessage}; {other.ErrorMessage}";
        }
    }
}

public class FirebaseTokenSendResult
{
    public bool Success { get; set; }

    public bool IsInvalidToken { get; set; }

    public bool IsTemporaryFailure { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
