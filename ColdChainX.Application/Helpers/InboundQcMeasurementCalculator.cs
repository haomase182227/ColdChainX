using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Helpers;

public static class InboundQcMeasurementCalculator
{
    public static decimal CalculateCbm(decimal lengthCm, decimal widthCm, decimal heightCm, int quantity)
        => Math.Round(lengthCm * widthCm * heightCm * Math.Max(quantity, 1) / 1_000_000m, 4);

    public static decimal CalculateExpectedCbm(OrderDimension dimensions, int quantity)
        => CalculateCbm(dimensions.LengthCm, dimensions.WidthCm, dimensions.HeightCm, quantity);
}
