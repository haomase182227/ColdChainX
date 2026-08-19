using Microsoft.AspNetCore.Http;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Fleet;

public class InlineVehicleDocumentRequest
{
    public string DocumentNumber { get; set; } = null!;
    public string? Issuer { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? ExpireDate { get; set; }
}

public class InlineDriverLicenseRequest
{
    public string LicenseNumber { get; set; } = null!;
    public string LicenseClass { get; set; } = null!;
    public DateOnly IssueDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
}

// ── Tạo xe (kèm giấy tờ tùy chọn) ─────────────────────────────
/// <summary>
/// Tạo xe với ba kích thước lòng thùng cùng đơn vị centimet (cm).
/// Max CBM được hệ thống tự tính và không nhận từ request.
/// </summary>
public class CreateVehicleRequest
{
    public string TruckPlate { get; set; } = null!;

    public string? Brand { get; set; }
    public int? ManufactureYear { get; set; }
    public string? ChassisNumber { get; set; }
    public string? EngineNumber { get; set; }
    public decimal? StandardFuelLiters { get; set; }
    public string VehicleType { get; set; } = null!;
    public decimal MaxWeight { get; set; }

    /// <summary>Chiều dài lòng thùng, đơn vị centimet (cm).</summary>
    public decimal InnerLengthCm { get; set; }

    /// <summary>Chiều rộng lòng thùng, đơn vị centimet (cm).</summary>
    public decimal InnerWidthCm { get; set; }

    /// <summary>Chiều cao lòng thùng, đơn vị centimet (cm).</summary>
    public decimal InnerHeightCm { get; set; }
    public decimal MinTemp { get; set; }
    public decimal MaxTemp { get; set; }
    public string? CurrentLocation { get; set; }
    public double CurrentOdometer { get; set; }
    public double NextMaintenanceOdometer { get; set; }

    public InlineVehicleDocumentRequest? Registration { get; set; }
    public InlineVehicleDocumentRequest? Insurance { get; set; }
    public InlineVehicleDocumentRequest? CityPermit { get; set; }
}

public class CreateDriverRequest
{
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string IdentityNumber { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public DateOnly DateOfBirth { get; set; }
    public DateOnly JoinDate { get; set; }

    public InlineDriverLicenseRequest? License { get; set; }
}

public class UpdateDriverRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? IdentityNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? JoinDate { get; set; }
    public string? Status { get; set; }
}

public class CreateVehicleDocumentRequest
{
    public string DocumentType { get; set; } = null!;
    public string DocumentNumber { get; set; } = null!;
    public string? Issuer { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? ExpireDate { get; set; }
}

public class UpdateVehicleDocumentRequest : CreateVehicleDocumentRequest
{
}

public class CreateDriverLicenseRequest
{
    public string LicenseNumber { get; set; } = null!;
    public string LicenseClass { get; set; } = null!;
    public DateOnly IssueDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
}

public class UpdateDriverLicenseRequest : CreateDriverLicenseRequest
{
}

public class ImportExcelRequest
{
    public IFormFile ExcelFile { get; set; } = null!;
}

public class SyncOdometerRequest
{
    public string TruckPlate { get; set; } = null!;

    public double Odometer { get; set; }

    public string? LocationText { get; set; }

    public OdometerSyncReason Reason { get; set; } = OdometerSyncReason.ROUTINE_SYNC;

    public string? Note { get; set; }

    public IFormFile? OdometerPhoto { get; set; }
}

public class CreateMaintenanceTicketRequest
{
    public string MaintenanceType { get; set; } = "ROUTINE_AND_PTI";
    public string GarageName { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public class CompleteMaintenanceTicketRequest
{
    public decimal Cost { get; set; }
    public DateOnly CompletionDate { get; set; }
}
