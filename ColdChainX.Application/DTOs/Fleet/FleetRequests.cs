using Microsoft.AspNetCore.Http;
using ColdChainX.Core.Enums;

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
    public decimal InnerLengthCm { get; set; }
    public decimal InnerWidthCm { get; set; }
    public decimal InnerHeightCm { get; set; }
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
    /// <summary>
    /// Biển số xe cần đồng bộ (ví dụ: "29C-12345").
    /// </summary>
    public string TruckPlate { get; set; } = null!;

    /// <summary>
    /// Chỉ số công tơ mét hiện tại của xe (km).
    /// </summary>
    public double Odometer { get; set; }

    /// <summary>
    /// Địa điểm đồng bộ odometer (ví dụ: tên kho bãi, địa chỉ thực tế).
    /// </summary>
    public string? LocationText { get; set; }

    /// <summary>
    /// Lý do đồng bộ công tơ mét. Các giá trị chấp nhận:
    /// - ROUTINE_SYNC (Đồng bộ định kỳ)
    /// - PRE_TRIP_INSPECTION (Kiểm tra trước chuyến đi)
    /// - POST_TRIP_REPORT (Báo cáo sau chuyến đi)
    /// - MANUAL_CORRECTION (Điều chỉnh thủ công)
    /// - OTHER (Lý do khác, ghi chú ở trường Note)
    /// </summary>
    public OdometerSyncReason Reason { get; set; } = OdometerSyncReason.ROUTINE_SYNC;

    /// <summary>
    /// Ghi chú/chi tiết thêm về lý do đồng bộ (Bắt buộc nhập nếu lý do là OTHER).
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Tệp ảnh minh chứng chụp công tơ mét thực tế (upload trực tiếp từ thiết bị).
    /// </summary>
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
