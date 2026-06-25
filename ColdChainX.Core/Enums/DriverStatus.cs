namespace ColdChainX.Core.Enums
{
    /// <summary>
    /// Operational status for drivers (separate from UserStatus which is for account state).
    /// </summary>
    public enum DriverStatus
    {
        Available,
        Planning,
        OnTrip,
        Offline,
        Inactive,

        /// <summary>
        /// Mandatory rest — the driver has exceeded the daily (10h) or weekly (48h)
        /// driving-hour limit and cannot be assigned until the calendar day/week rolls over.
        /// </summary>
        RELAX
    }
}
