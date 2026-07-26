namespace ColdChainX.Application.DTOs.Fleet;

public class VehicleFleetResponse
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public string? Brand { get; set; }
    public int? ManufactureYear { get; set; }
    public string? ChassisNumber { get; set; }
    public string? EngineNumber { get; set; }
    public decimal? StandardFuelLiters { get; set; }
    public string VehicleType { get; set; } = null!;
    public decimal MaxWeight { get; set; }
    public decimal MaxCbm { get; set; }
    public decimal? InnerLengthCm { get; set; }
    public decimal? InnerWidthCm { get; set; }
    public decimal? InnerHeightCm { get; set; }
    public decimal? UsableCbm { get; set; }
    public decimal MinTemp { get; set; }
    public decimal MaxTemp { get; set; }
    public string? CurrentLocation { get; set; }
    public double CurrentOdometer { get; set; }
    public double NextMaintenanceOdometer { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
    public int WarningDaysBeforeDue { get; set; }
    public double WarningKmBeforeDue { get; set; }
    public string? Status { get; set; }
    public IReadOnlyCollection<VehicleDocumentResponse> Documents { get; set; } = Array.Empty<VehicleDocumentResponse>();
}

public class DriverFleetResponse
{
    public Guid DriverId { get; set; }
    public Guid? UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string? Email { get; set; }
    public string IdentityNumber { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public DateOnly DateOfBirth { get; set; }
    public DateOnly JoinDate { get; set; }
    public string? Status { get; set; }
    public IReadOnlyCollection<DriverLicenseResponse> Licenses { get; set; } = Array.Empty<DriverLicenseResponse>();
}

public class VehicleDocumentResponse
{
    public Guid DocId { get; set; }
    public Guid? VehicleId { get; set; }
    public string DocumentType { get; set; } = null!;
    public string DocumentNumber { get; set; } = null!;
    public string? Issuer { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? ExpireDate { get; set; }
    public string? Status { get; set; }
}

public class DriverLicenseResponse
{
    public Guid LicenseId { get; set; }
    public Guid? DriverId { get; set; }
    public string LicenseNumber { get; set; } = null!;
    public string LicenseClass { get; set; } = null!;
    public DateOnly IssueDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string? Status { get; set; }
}

public class MaintenanceTicketResponse
{
    public Guid TicketId { get; set; }
    public string TicketCode { get; set; } = null!;
    public Guid? VehicleId { get; set; }
    public string MaintenanceType { get; set; } = null!;
    public double TriggeredAtOdometer { get; set; }
    public string GarageName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal? Cost { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? CompletionDate { get; set; }
    public string? Status { get; set; }
    public string? AttachmentUrl { get; set; }
}

public class MaintenanceForecastResponse
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = null!;
    public bool IsDueByDate { get; set; }
    public bool IsDueByKm { get; set; }
    public bool IsWarningByDate { get; set; }
    public bool IsWarningByKm { get; set; }
    public bool IsOverrunForecast { get; set; }
    public double HeadroomKm { get; set; }
    public int RemainingDays { get; set; }
    public string ForecastStatus { get; set; } = "SAFE"; // SAFE, WARNING, OVERDUE
    public string? Message { get; set; }
}

public class ImportResultResponse
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public IReadOnlyCollection<string> Errors { get; set; } = Array.Empty<string>();
}
