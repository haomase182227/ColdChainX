using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Dispatch;

public sealed class TripDetailsDto
{
    public Guid TripId { get; set; }
    public Guid? RouteId { get; set; }
    public Guid? ScheduleId { get; set; }
    public DateTime? DepartureDate { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid OriginLocationId { get; set; }
    public Guid DestinationLocationId { get; set; }
    public string? SealNumber { get; set; }
    public decimal? TotalDistanceKm { get; set; }
    public decimal? EstimatedDurationHours { get; set; }
    public decimal TargetTemperature { get; set; }
    public bool RequiresInspection { get; set; }
    public DateTime PlannedStartTime { get; set; }
    public DateTime PlannedEndTime { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public TripCargoSummaryDto Summary { get; set; } = new();
    public TripRouteDetailsDto? Route { get; set; }
    public TripScheduleDetailsDto? Schedule { get; set; }
    public TripLocationDetailsDto Origin { get; set; } = new();
    public TripLocationDetailsDto Destination { get; set; } = new();
    public TripVehicleDetailsDto? Vehicle { get; set; }
    public List<TripDriverDetailsDto> Drivers { get; set; } = new();
    public List<TripStopDetailsDto> Stops { get; set; } = new();
    public List<TripLifoLoadItemDto> LoadPlan { get; set; } = new();
    public List<TripOrderDetailsDto> Orders { get; set; } = new();
    public List<TripLpnDetailsDto> Lpns { get; set; } = new();
    public List<TripSealDetailsDto> Seals { get; set; } = new();
    public List<TripIncidentDetailsDto> Incidents { get; set; } = new();
}

public sealed class TripCargoSummaryDto
{
    public int TotalOrders { get; set; }
    public int TotalLpns { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalWeightKg { get; set; }
    public decimal TotalCbm { get; set; }
    public int DeliveredLpns { get; set; }
    public int ReturnedLpns { get; set; }
    public Dictionary<string, int> LpnStateCounts { get; set; } = new();
}

public sealed class TripRouteDetailsDto
{
    public Guid RouteId { get; set; }
    public string RouteCode { get; set; } = string.Empty;
    public string OriginCity { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public string TransitTime { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class TripScheduleDetailsDto
{
    public Guid ScheduleId { get; set; }
    public Guid RouteId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public DateTime DepartureDate { get; set; }
    public TimeSpan DepartureTime { get; set; }
    public TimeSpan CutOffTime { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class TripLocationDetailsDto
{
    public Guid LocationId { get; set; }
    public Guid? CustomerId { get; set; }
    public string Address { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string? Status { get; set; }
}

public sealed class TripVehicleDetailsDto
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public int? ManufactureYear { get; set; }
    public string? ChassisNumber { get; set; }
    public string? EngineNumber { get; set; }
    public decimal? StandardFuelLiters { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public decimal MaxWeight { get; set; }
    public decimal MaxCbm { get; set; }
    public decimal? InnerLengthCm { get; set; }
    public decimal? InnerWidthCm { get; set; }
    public decimal? InnerHeightCm { get; set; }
    public decimal MinTemp { get; set; }
    public decimal MaxTemp { get; set; }
    public string? CurrentLocation { get; set; }
    public double CurrentOdometer { get; set; }
    public double NextMaintenanceOdometer { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
    public string? Status { get; set; }
    public List<TripIotDeviceDto> IotDevices { get; set; } = new();
    public List<TripVehicleDocumentDto> Documents { get; set; } = new();
}

public sealed class TripIotDeviceDto
{
    public Guid DeviceId { get; set; }
    public string? DeviceCode { get; set; }
    public int? BatteryLevel { get; set; }
    public DateTime? LastPingTime { get; set; }
    public bool IsOnline { get; set; }
    public string? Status { get; set; }
}

public sealed class TripVehicleDocumentDto
{
    public Guid DocumentId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly? ExpireDate { get; set; }
    public string? Status { get; set; }
}

public sealed class TripDriverDetailsDto
{
    public Guid TripDriverId { get; set; }
    public Guid DriverId { get; set; }
    public Guid? UserId { get; set; }
    public string DriverRole { get; set; } = string.Empty;
    public decimal AssignedDurationHours { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public DateOnly JoinDate { get; set; }
    public string? CurrentLocation { get; set; }
    public string? Status { get; set; }
    public List<TripDriverLicenseDto> Licenses { get; set; } = new();
}

public sealed class TripDriverLicenseDto
{
    public Guid LicenseId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string LicenseClass { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string? Status { get; set; }
}

public sealed class TripStopDetailsDto
{
    public Guid StopId { get; set; }
    public Guid? LocationId { get; set; }
    public int StopSequence { get; set; }
    public string StopType { get; set; } = string.Empty;
    public DateTime PlannedArrivalTime { get; set; }
    public DateTime PlannedDepartureTime { get; set; }
    public DateTime? ActualArrivalTime { get; set; }
    public DateTime? ActualDepartureTime { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
    public TripLocationDetailsDto? Location { get; set; }
    public List<Guid> OrderIds { get; set; } = new();
    public List<string> OrderTrackingCodes { get; set; } = new();
    public List<Guid> LpnIds { get; set; } = new();
    public List<string> LpnCodes { get; set; } = new();
}

public sealed class TripOrderDetailsDto
{
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string PackingType { get; set; } = string.Empty;
    public string TempCondition { get; set; } = string.Empty;
    public bool HasStrongOdor { get; set; }
    public bool IsStackable { get; set; }
    public Guid? PickupLocationId { get; set; }
    public Guid? DestinationLocationId { get; set; }
    public Guid? MasterTripId { get; set; }
    public Guid? ScheduleId { get; set; }
    public Guid? DropoffStopId { get; set; }
    public string? PickupAddress { get; set; }
    public string? DestinationAddress { get; set; }
    public int? DeliveryStopSequence { get; set; }
    public int? FirstLifoLoadOrder { get; set; }
    public List<int> LifoLoadOrders { get; set; } = new();
    public string? LifoZone { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public TripCustomerDetailsDto? Customer { get; set; }
    public TripLocationDetailsDto? PickupLocation { get; set; }
    public TripLocationDetailsDto? DestinationLocation { get; set; }
    public TripScheduleDetailsDto? Schedule { get; set; }
    public TripOrderDimensionDto? Dimension { get; set; }
    public List<TripOrderPackageLineDto> PackageLines { get; set; } = new();
    public List<Guid> LpnIds { get; set; } = new();
    public List<string> LpnCodes { get; set; } = new();
    public List<TripTransportDocumentDto> Documents { get; set; } = new();
    public List<TripDeliveryEpodDto> DeliveryEpods { get; set; } = new();
}

public sealed class TripCustomerDetailsDto
{
    public Guid CustomerId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Email { get; set; }
    public int? PaymentTerm { get; set; }
    public string? Status { get; set; }
}

public sealed class TripOrderDimensionDto
{
    public decimal ExpectedWeightKg { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ExpectedCbm { get; set; }
    public decimal? ActualCbm { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public string? CbmEstimationMethod { get; set; }
    public string? CbmEstimationConfidence { get; set; }
    public decimal? CustomerProvidedTotalCbm { get; set; }
    public int? TotalPackageQuantity { get; set; }
}

public sealed class TripOrderPackageLineDto
{
    public Guid OrderPackageLineId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal CapacityKg { get; set; }
    public int Quantity { get; set; }
}

public sealed class TripTransportDocumentDto
{
    public Guid DocumentId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public Guid? VerifiedBy { get; set; }
    public string? RejectReason { get; set; }
    public Guid UploadedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public sealed class TripDeliveryEpodDto
{
    public Guid EpodId { get; set; }
    public DateTime CheckinTime { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? SignImageUrl { get; set; }
    public decimal? SignLatitude { get; set; }
    public decimal? SignLongitude { get; set; }
    public int? DeliveryRating { get; set; }
    public string? Note { get; set; }
    public string? PdfUrl { get; set; }
    public string? Status { get; set; }
    public decimal? CodAmount { get; set; }
    public decimal? CodAmountPaid { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentStatus { get; set; }
    public string? PaymentEvidenceImageUrl { get; set; }
    public DateTime? HandoverConfirmedAt { get; set; }
    public string? HandoverPdfUrl { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
}

public sealed class TripLpnDetailsDto
{
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? RouteId { get; set; }
    public Guid? TripId { get; set; }
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal ActualCbm { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public List<TripInboundQcPackageLineDto> ActualPackageLines { get; set; } = new();
    public decimal? RequiredTemperature { get; set; }
    public decimal? RecordedTemperature { get; set; }
    public string? StorageLocation { get; set; }
    public string State { get; set; } = string.Empty;
    public string? DiscrepancyReason { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public bool IsFastTrack { get; set; }
    public DateTime? InboundTime { get; set; }
    public DateTime? SlaDeadline { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string OrderTrackingCode { get; set; } = string.Empty;
    public string OrderItemName { get; set; } = string.Empty;
    public string OrderCategory { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public int? LifoLoadOrder { get; set; }
    public int? DeliveryStopSequence { get; set; }
    public string? LifoZone { get; set; }
    public string? LifoReason { get; set; }
    public TripCustomerDetailsDto? Customer { get; set; }
    public TripWarehouseDetailsDto? Warehouse { get; set; }
    public TripWarehouseReceiptDto? Receipt { get; set; }
    public TripLpnDeliveryConfirmationDto? DeliveryConfirmation { get; set; }
}

public sealed class TripInboundQcPackageLineDto
{
    public Guid InboundQcPackageLineId { get; set; }
    public Guid AsnId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal ActualWeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }
    public decimal ActualCbm { get; set; }
}

public sealed class TripWarehouseDetailsDto
{
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string WarehouseCode { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string WarehouseType { get; set; } = string.Empty;
    public decimal? DefaultMinTemp { get; set; }
    public decimal? DefaultMaxTemp { get; set; }
    public string? Status { get; set; }
}

public sealed class TripWarehouseReceiptDto
{
    public Guid ReceiptId { get; set; }
    public string ReceiptCode { get; set; } = string.Empty;
    public string? ReferenceDocNo { get; set; }
    public string ReceiptType { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public decimal? TotalExpectedQuantity { get; set; }
    public decimal? TotalActualQuantity { get; set; }
    public decimal? RecordedTemperature { get; set; }
    public string DelivererName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? PdfUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public sealed class TripLpnDeliveryConfirmationDto
{
    public Guid ConfirmationId { get; set; }
    public string OutcomeType { get; set; } = string.Empty;
    public string? ReceiverName { get; set; }
    public string? ReceiverPhone { get; set; }
    public string? RejectReason { get; set; }
    public string? RejectNote { get; set; }
    public string EvidenceImageUrl { get; set; } = string.Empty;
    public Guid ConfirmedByDriverId { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public DateTime? CheckinAt { get; set; }
    public string? SignatureImageUrl { get; set; }
    public decimal CodAmount { get; set; }
    public string? CodPaymentMethod { get; set; }
    public string? CodReceiptImageUrl { get; set; }
    public string? NewSealNumber { get; set; }
    public decimal? RecordedTemperature { get; set; }
    public bool IsCodVerified { get; set; }
    public DateTime? CodVerifiedAt { get; set; }
    public Guid? CodVerifiedByUserId { get; set; }
}

public sealed class TripSealDetailsDto
{
    public Guid SealId { get; set; }
    public Guid? StopId { get; set; }
    public string SealCode { get; set; } = string.Empty;
    public DateTime? AppliedAt { get; set; }
    public string? AppliedImageUrl { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovedImageUrl { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public sealed class TripIncidentDetailsDto
{
    public Guid IncidentId { get; set; }
    public string IncidentType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? CurrentLatitude { get; set; }
    public decimal? CurrentLongitude { get; set; }
    public decimal DriverPaidAmount { get; set; }
    public decimal? ReimbursedAmount { get; set; }
    public bool RequiresRescue { get; set; }
    public string? Status { get; set; }
    public Guid ReportedBy { get; set; }
    public DateTime? ReportedAt { get; set; }
    public Guid? HandledBy { get; set; }
    public DateTime? HandledAt { get; set; }
    public string? HandlingNote { get; set; }
    public Guid? BrokenVehicleId { get; set; }
    public Guid? ReplacementVehicleId { get; set; }
    public Guid? MaintenanceTicketId { get; set; }
    public DateTime? RescueDispatchedAt { get; set; }
    public DateTime? TransloadConfirmedAt { get; set; }
    public string? TransloadNote { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string ExpenseStatus { get; set; } = string.Empty;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public List<TripIncidentEvidenceDto> Evidence { get; set; } = new();
}

public sealed class TripIncidentEvidenceDto
{
    public Guid EvidenceId { get; set; }
    public string EvidenceType { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
}

public sealed class TripLifoLoadItemDto
{
    public int LoadOrder { get; set; }
    public Guid LpnId { get; set; }
    public string LpnCode { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public decimal Cbm { get; set; }
    public string TempCondition { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public Guid DeliveryLocationId { get; set; }
    public string DeliveryAddress { get; set; } = string.Empty;
    public int DeliveryStopSequence { get; set; }
    public string Reason { get; set; } = string.Empty;
}
