namespace ColdChainX.Application.DTOs.Chat
{
    public class ChatUnreadCountResponse
    {
        public Guid OrderId { get; set; }
        public int UnreadCount { get; set; }
    }
}
