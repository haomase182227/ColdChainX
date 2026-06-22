namespace ColdChainX.Application.DTOs.Chat;

public class ChatParticipantResponse
{
    public Guid OrderId { get; set; }

    public Guid? CustomerId { get; set; }

    public Guid? CustomerUserId { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }
}
