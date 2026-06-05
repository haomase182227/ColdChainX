using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Permission
{
    public Guid PermId { get; set; }

    public string PermCode { get; set; } = null!;

    public string Module { get; set; } = null!;

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
}
