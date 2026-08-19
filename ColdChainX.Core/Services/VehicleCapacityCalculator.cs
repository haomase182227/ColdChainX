namespace ColdChainX.Core.Services;

public static class VehicleCapacityCalculator
{
    private const decimal RefrigerationUnitVolumeRate = 0.10m;
    private const decimal CubicCentimetersPerCubicMeter = 1_000_000m;

    /// <summary>
    /// Calculates the vehicle's maximum cargo volume in m³ from inner dimensions in cm.
    /// Ten percent of the physical volume is reserved for the refrigeration unit.
    /// </summary>
    public static decimal CalculateMaxCbm(
        decimal innerLengthCm,
        decimal innerWidthCm,
        decimal innerHeightCm)
    {
        if (innerLengthCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(innerLengthCm));
        if (innerWidthCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(innerWidthCm));
        if (innerHeightCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(innerHeightCm));

        var physicalCbm = innerLengthCm
            * innerWidthCm
            * innerHeightCm
            / CubicCentimetersPerCubicMeter;

        return Math.Round(
            physicalCbm * (1m - RefrigerationUnitVolumeRate),
            2,
            MidpointRounding.AwayFromZero);
    }
}
