using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Incident;

public sealed class AssessIncidentRiskRequest
{
    public IncidentRiskLevel RiskLevel { get; set; }
    public TemperatureReadingSource TemperatureSource { get; set; } = TemperatureReadingSource.NONE;
    public decimal? MeasuredTemperature { get; set; }
    public DateTime? MeasuredAt { get; set; }
    public bool TemperatureStable { get; set; }
    public bool? CanSafelyRepairOnSite { get; set; }
    public bool ContainmentConfirmed { get; set; }
    public string? Note { get; set; }
}

public sealed class IncidentRiskAssessmentResponse
{
    public Guid IncidentId { get; set; }
    public string RequestedRiskLevel { get; set; } = null!;
    public string EffectiveRiskLevel { get; set; } = null!;
    public string IncidentStatus { get; set; } = null!;
    public bool EscalatedToCritical { get; set; }
    public string DecisionReason { get; set; } = null!;
    public decimal TargetTemperature { get; set; }
    public decimal TemperatureTolerance { get; set; }
    public decimal? LatestTemperature { get; set; }
    public DateTime? TemperatureMeasuredAt { get; set; }
    public string TemperatureSource { get; set; } = null!;
    public bool HasTrustedTemperatureSource { get; set; }
    public bool TemperatureThresholdBreached { get; set; }
    public bool DirectDeliveryLocked { get; set; }
    public bool RequiresRescue { get; set; }
    public int? RemainingSafeTimeMinutes { get; set; }
    public string SafeTimeCalculation { get; set; } = null!;
}
