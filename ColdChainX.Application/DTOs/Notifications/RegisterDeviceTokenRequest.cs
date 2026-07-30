namespace ColdChainX.Application.DTOs.Notifications;

public class RegisterDeviceTokenRequest
{
    public string? DeviceToken { get; set; }

    public string? Platform { get; set; }

    public string? DeviceId { get; set; }

    public string? DeviceName { get; set; }

    public string? AppVersion { get; set; }
}
