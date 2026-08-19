namespace ColdChainX.Core.Enums;

public enum IncidentRiskLevel
{
    LOW = 1,
    WARNING = 2,
    CRITICAL = 3
}

public enum TemperatureReadingSource
{
    NONE = 0,
    IOT = 1,
    CARGO_GAUGE = 2,
    BACKUP_THERMOMETER = 3,
    TIMESTAMPED_PHOTO = 4
}

public enum IncidentRescuePlanType
{
    DIRECT_RESCUE = 1,
    WAREHOUSE_RESCUE = 2,
    INTERNAL_COLD_STORAGE = 3,
    EXTERNAL_COLD_STORAGE = 4,
    MANUAL_ESCALATION = 5
}
