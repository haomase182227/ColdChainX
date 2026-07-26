namespace ColdChainX.Core.Entities;

public class DeviceToken
{
    public Guid DeviceTokenId { get; set; }

    public Guid UserId { get; set; }

    public string Token { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public string? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    public string? AppVersion { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime LastUsedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
