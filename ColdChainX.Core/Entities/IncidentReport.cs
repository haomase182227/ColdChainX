using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class IncidentReport
{
    public Guid IncidentId { get; set; }

    public Guid? TripId { get; set; }

    public string IncidentType { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string? RiskLevel { get; set; }

    public string Description { get; set; } = null!;

    public decimal? CurrentLatitude { get; set; }

    public decimal? CurrentLongitude { get; set; }

    public decimal DriverPaidAmount { get; set; }

    public decimal? ReimbursedAmount { get; set; }

    public bool RequiresRescue { get; set; }

    public string? TemperatureSource { get; set; }

    public decimal? LatestTemperature { get; set; }

    public DateTime? TemperatureMeasuredAt { get; set; }

    public decimal TemperatureTolerance { get; set; } = 2m;

    public bool TemperatureThresholdBreached { get; set; }

    public DateTime? ContainmentConfirmedAt { get; set; }

    public int? RemainingSafeTimeMinutes { get; set; }

    public string? SafeTimeCalculation { get; set; }

    public bool DirectDeliveryLocked { get; set; }

    public Guid? PreviousIncidentId { get; set; }

    public DateTime? SlaDueAt { get; set; }

    public DateTime? LastSlaEscalatedAt { get; set; }

    public string? RescuePlanType { get; set; }

    public string? RescuePlanDetails { get; set; }

    public string? RedispatchPlan { get; set; }

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

    public Guid? TransloadConfirmedBy { get; set; }

    public DateTime? TransloadConfirmedAt { get; set; }

    public string? TransloadNote { get; set; }

    public string? TransloadDetailsJson { get; set; }

    public decimal? ApprovedAmount { get; set; }

    public string ExpenseStatus { get; set; } = "NOT_REQUIRED";

    public Guid? ExpenseApprovedBy { get; set; }

    public DateTime? ExpenseApprovedAt { get; set; }

    public string? ExpenseApprovalNote { get; set; }

    public Guid? ReimbursedBy { get; set; }

    public DateTime? ReimbursedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? ResolutionNote { get; set; }

    public virtual User ReportedByNavigation { get; set; } = null!;

    public virtual MasterTrip? Trip { get; set; }

    public virtual ICollection<IncidentEvidence> IncidentEvidences { get; set; } = new List<IncidentEvidence>();
}
