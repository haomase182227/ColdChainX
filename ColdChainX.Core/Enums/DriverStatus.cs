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
        Inactive
    }
}
