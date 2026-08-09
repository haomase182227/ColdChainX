using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Permission
{
    public Guid PermId { get; set; }

    public string PermCode { get; set; } = null!;

    public string Module { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();

    public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}
