using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Fleet;

// ── Sub-object: một giấy tờ xe ──────────────────────────────────
public class InlineVehicleDocumentRequest
{
    public string DocumentNumber { get; set; } = null!;
    public string? Issuer { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? ExpireDate { get; set; }
}

// ── Sub-object: bằng lái ─────────────────────────────────────────
public class InlineDriverLicenseRequest
{
    public string LicenseNumber { get; set; } = null!;
    public string LicenseClass { get; set; } = null!;
    public DateOnly IssueDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
}

// ── Tạo xe (kèm giấy tờ tùy chọn) ─────────────────────────────
public class CreateVehicleRequest
{
    /// <summary>Biển số xe (bắt buộc)</summary>
    public string TruckPlate { get; set; } = null!;

    public string? Brand { get; set; }
    public decimal? StandardFuelLiters { get; set; }
    public string VehicleType { get; set; } = null!;
    public decimal MaxWeight { get; set; }
    public decimal MaxCbm { get; set; }
    public decimal MinTemp { get; set; }
    public decimal MaxTemp { get; set; }
    public string? CurrentLocation { get; set; }
    public double CurrentOdometer { get; set; }
    public double NextMaintenanceOdometer { get; set; }

    // Giấy tờ kèm theo (optional)
    public InlineVehicleDocumentRequest? Registration { get; set; }
    public InlineVehicleDocumentRequest? Insurance { get; set; }
    public InlineVehicleDocumentRequest? CityPermit { get; set; }
    public InlineVehicleDocumentRequest? FoodSafety { get; set; }
}

// ── Tạo tài xế (kèm bằng lái tùy chọn) ──────────────────────
public class CreateDriverRequest
{
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string IdentityNumber { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public DateOnly DateOfBirth { get; set; }
    public DateOnly JoinDate { get; set; }

    // Bằng lái kèm theo (optional)
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

// Dùng cho endpoint POST /api/drivers/{id}/licenses
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
    public double Odometer { get; set; }
    public string? LocationText { get; set; }
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
