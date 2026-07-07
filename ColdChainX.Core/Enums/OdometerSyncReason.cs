using System.Text.Json.Serialization;

namespace ColdChainX.Core.Enums;

public enum OdometerSyncReason
{
    ROUTINE_SYNC = 0,
    PRE_TRIP_INSPECTION = 1,
    POST_TRIP_REPORT = 2,
    MANUAL_CORRECTION = 3,
    OTHER = 4
}
