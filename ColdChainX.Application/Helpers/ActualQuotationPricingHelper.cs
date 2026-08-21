using System.Globalization;
using ColdChainX.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Helpers;

public static class ActualQuotationPricingHelper
{
    private const decimal MinChargeableWeightKg = 30m;

    public static async Task<ActualQuotationPricingResult> ResolveAsync(
        IApplicationDbContext context,
        Guid routeId,
        decimal actualWeightKg,
        decimal actualCbm,
        CancellationToken cancellationToken)
    {
        var configuredValue = await context.SystemConfigs
            .AsNoTracking()
            .Where(config => config.Key == "VolumetricConversionRate")
            .Select(config => config.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var volumetricRate = decimal.TryParse(
            configuredValue,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var configuredRate)
                ? configuredRate
                : 250m;

        var volumetricWeight = Math.Round(actualCbm * volumetricRate, 2);
        var chargeableWeight = Math.Max(
            Math.Max(actualWeightKg, volumetricWeight),
            MinChargeableWeightKg);
        var tier = await context.WeightTiers
            .AsNoTracking()
            .Where(item => item.RouteId == routeId
                && chargeableWeight >= item.MinWeightKg
                && (!item.MaxWeightKg.HasValue || chargeableWeight <= item.MaxWeightKg.Value))
            .OrderByDescending(item => item.MinWeightKg)
            .FirstOrDefaultAsync(cancellationToken);

        return tier == null
            ? ActualQuotationPricingResult.Failure(
                $"No weight tier covers the actual chargeable weight {chargeableWeight:0.##} kg for the selected route.")
            : ActualQuotationPricingResult.Success(
                chargeableWeight,
                volumetricWeight,
                tier.PricePerKg,
                Math.Round(chargeableWeight * tier.PricePerKg, 0));
    }
}

public sealed record ActualQuotationPricingResult(
    bool IsSuccess,
    decimal ChargeableWeightKg,
    decimal VolumetricWeightKg,
    decimal PricePerKg,
    decimal BaseFreight,
    string? Error)
{
    public static ActualQuotationPricingResult Success(
        decimal chargeableWeightKg,
        decimal volumetricWeightKg,
        decimal pricePerKg,
        decimal baseFreight)
        => new(true, chargeableWeightKg, volumetricWeightKg, pricePerKg, baseFreight, null);

    public static ActualQuotationPricingResult Failure(string error)
        => new(false, 0m, 0m, 0m, 0m, error);
}
