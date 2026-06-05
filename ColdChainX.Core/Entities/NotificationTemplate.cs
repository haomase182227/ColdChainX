using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class NotificationTemplate
{
    public string TemplateId { get; set; } = null!;

    public Guid TypeId { get; set; }

    public string TitleTemplate { get; set; } = null!;

    public string BodyTemplate { get; set; } = null!;

    public string Channel { get; set; } = null!;

    public string? Status { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual Messagetype Type { get; set; } = null!;
}
