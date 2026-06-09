namespace ColdChainX.Application.DTOs.Notifications
{
    public class NotificationResponse
    {
        public Guid NotiId { get; set; }
        public Guid UserId { get; set; }
        public Guid? SenderId { get; set; }
        public string TemplateId { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string Params { get; set; } = null!;
        public Guid? OrderId { get; set; }
        public bool IsRead { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
