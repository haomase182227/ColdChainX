using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Messagetype
{
    public Guid TypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<NotificationTemplate> NotificationTemplates { get; set; } = new List<NotificationTemplate>();
}
