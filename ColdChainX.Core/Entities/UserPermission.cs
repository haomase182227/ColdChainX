namespace ColdChainX.Core.Entities;

public class UserPermission
{
    public Guid UserPermissionId { get; set; }

    public Guid UserId { get; set; }

    public Guid PermId { get; set; }

    public string Effect { get; set; } = null!;

    public DateTime? ValidFrom { get; set; }

    public DateTime? ValidTo { get; set; }

    public string? Reason { get; set; }

    public Guid GrantedBy { get; set; }

    public DateTime GrantedAt { get; set; }

    public Guid? RevokedBy { get; set; }

    public DateTime? RevokedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Permission Permission { get; set; } = null!;
}
