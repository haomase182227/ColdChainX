namespace ColdChainX.Application.DTOs.Chat
{
    public class ChatMessageResponse
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid SenderId { get; set; }
        public string? SenderName { get; set; }
        public string? SenderEmail { get; set; }
        public string? SenderRole { get; set; }
        public Guid ReceiverId { get; set; }
        public string? ReceiverName { get; set; }
        public string? ReceiverEmail { get; set; }
        public string? ReceiverRole { get; set; }
        public string MessageContent { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
    }
}
