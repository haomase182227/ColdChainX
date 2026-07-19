namespace ColdChainX.Application.DTOs.Dispatch;

public static class CargoCompatibilityReasonCodes
{
    public const string DifferentSchedule = "DIFFERENT_SCHEDULE";
    public const string DifferentWarehouse = "DIFFERENT_WAREHOUSE";
    public const string InvalidLpnState = "INVALID_LPN_STATE";
    public const string CategoryMismatch = "CATEGORY_MISMATCH";
    public const string TemperatureMismatch = "TEMPERATURE_MISMATCH";
    public const string OdorConflict = "ODOR_CONFLICT";
    public const string MissingTemperature = "MISSING_TEMPERATURE";
}

public sealed class CargoCompatibilityConflictDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? LpnId { get; set; }
    public string? LpnCode { get; set; }
    public Guid? OtherLpnId { get; set; }
    public string? OtherLpnCode { get; set; }
}

public sealed class CargoCompatibilityValidationResult
{
    public bool IsValid => Conflicts.Count == 0;
    public List<CargoCompatibilityConflictDto> Conflicts { get; set; } = new();
}

public sealed class CompatibleLpnsSearchRequest
{
    public Guid ScheduleId { get; set; }
    public List<Guid> SelectedLpnIds { get; set; } = new();
}

public sealed class CompatibleLpnsSearchResponse
{
    public bool SelectedSetValid { get; set; }
    public List<CargoCompatibilityConflictDto> Conflicts { get; set; } = new();
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyCollection<CompatibleLpnItemDto> Items { get; set; } = Array.Empty<CompatibleLpnItemDto>();
}

public class DispatchLpnReadyDto
{
    public Guid LpnId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string LpnCode { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public Guid? ScheduleId { get; set; }
    public string? ScheduleName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? DestinationAddress { get; set; }
    public string? RouteName { get; set; }
    public DateTime? PlannedDispatchDate { get; set; }
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ActualCbm { get; set; }
    public string TempCondition { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal? RequiredTemperature { get; set; }
    public bool HasStrongOdor { get; set; }
    public bool IsStackable { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public sealed class CompatibleLpnItemDto : DispatchLpnReadyDto
{
    public bool IsCompatible { get; set; } = true;
}

public sealed class SimulatePackingVehicleDto
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public string? Status { get; set; }
    public decimal MaxWeight { get; set; }
    public decimal MaxCbm { get; set; }
    public decimal MinTemp { get; set; }
    public decimal MaxTemp { get; set; }
}
