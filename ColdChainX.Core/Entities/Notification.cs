using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Notification
{
    public Guid NotiId { get; set; }

    public Guid UserId { get; set; }

    public Guid? SenderId { get; set; }

    public string? TemplateId { get; set; }

    public string Params { get; set; } = null!;

    public Guid? OrderId { get; set; }

    public bool? IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public string? Title { get; set; }

    public string? Body { get; set; }

    public string? Type { get; set; }

    public string? ReferenceId { get; set; }

    public string? DataJson { get; set; }

    public DateTime? SentAt { get; set; }

    public string DeliveryStatus { get; set; } = "PENDING";

    public string? FailureReason { get; set; }

    public virtual TransportOrder? Order { get; set; }

    public virtual User? Sender { get; set; }

    public virtual NotificationTemplate? Template { get; set; }

    public virtual User User { get; set; } = null!;
}
