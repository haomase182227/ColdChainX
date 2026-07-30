namespace ColdChainX.Application.DTOs.Notifications;

public class NotificationTestRequest
{
    public Guid UserId { get; set; }

    public string? Title { get; set; }

    public string? Body { get; set; }

    public string? Type { get; set; }

    public string? ReferenceId { get; set; }
}
