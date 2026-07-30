namespace ColdChainX.Application.DTOs.Notifications
{
    public class NotificationResponse
    {
        public Guid NotiId { get; set; }
        public Guid UserId { get; set; }
        public Guid? SenderId { get; set; }
        public string? TemplateId { get; set; }
        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string Params { get; set; } = null!;
        public Guid? OrderId { get; set; }
        public string? Type { get; set; }
        public string? ReferenceId { get; set; }
        public string? DataJson { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string DeliveryStatus { get; set; } = "PENDING";
    }
}
