namespace ColdChainX.Application.DTOs.Chat
{
    public class SendChatMessageRequest
    {
        public Guid ReceiverId { get; set; }
        public string MessageContent { get; set; } = null!;
    }
}
