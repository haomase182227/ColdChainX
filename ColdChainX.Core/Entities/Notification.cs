using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Notification
{
    public Guid NotiId { get; set; }

    public Guid UserId { get; set; }

    public Guid? SenderId { get; set; }

    public string TemplateId { get; set; } = null!;

    public string Params { get; set; } = null!;

    public Guid? OrderId { get; set; }

    public bool? IsRead { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder? Order { get; set; }

    public virtual User? Sender { get; set; }

    public virtual NotificationTemplate Template { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
