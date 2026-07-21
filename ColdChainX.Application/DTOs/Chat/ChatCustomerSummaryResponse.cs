namespace ColdChainX.Application.DTOs.Chat
{
    public class ChatCustomerSummaryResponse
    {
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = null!;
        public int ActiveOrderCount { get; set; }
        public DateTime? LatestOrderAt { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string? LastMessageContent { get; set; }
        public Guid? LastMessageOrderId { get; set; }
        public int UnreadCount { get; set; }
    }
}
