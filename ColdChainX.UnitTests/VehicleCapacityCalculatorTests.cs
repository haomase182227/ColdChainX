using ColdChainX.Application.DTOs;
using ColdChainX.Application.DTOs.Fleet;
using ColdChainX.Core.Services;

namespace ColdChainX.UnitTests;

public class VehicleCapacityCalculatorTests
{
    [Fact]
    public void CalculateMaxCbm_ConvertsCentimetersToCubicMeters_AndReservesTenPercent()
    {
        var maxCbm = VehicleCapacityCalculator.CalculateMaxCbm(950m, 250m, 240m);

        Assert.Equal(51.30m, maxCbm);
    }

    [Fact]
    public void CalculateMaxCbm_RoundsToDatabaseScale()
    {
        var maxCbm = VehicleCapacityCalculator.CalculateMaxCbm(333m, 222m, 111m);

        Assert.Equal(7.39m, maxCbm);
    }

    [Theory]
    [InlineData(0, 200, 200)]
    [InlineData(200, 0, 200)]
    [InlineData(200, 200, 0)]
    public void CalculateMaxCbm_NonPositiveDimension_Throws(
        int innerLengthCm,
        int innerWidthCm,
        int innerHeightCm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VehicleCapacityCalculator.CalculateMaxCbm(
                innerLengthCm,
                innerWidthCm,
                innerHeightCm));
    }
}

public class VehicleRequestContractTests
{
    [Fact]
    public void CreateAndUpdateRequests_DoNotExposeMaxCbm()
    {
        Assert.Null(typeof(CreateVehicleRequest).GetProperty("MaxCbm"));
        Assert.Null(typeof(VehicleUpdateRequest).GetProperty("MaxCbm"));
    }
}
