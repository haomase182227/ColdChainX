namespace ColdChainX.Application.DTOs.Notifications;

public class DeviceTokenRegistrationResponse
{
    public Guid DeviceTokenId { get; set; }

    public string Platform { get; set; } = null!;

    public string? DeviceId { get; set; }

    public bool IsActive { get; set; }

    public DateTime UpdatedAt { get; set; }
}
