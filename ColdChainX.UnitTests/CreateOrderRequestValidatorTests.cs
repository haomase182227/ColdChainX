using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Validators;

namespace ColdChainX.UnitTests;

public class CreateOrderRequestValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    [Fact]
    public void Validate_MultiplePackageSizes_DoesNotRequireLegacyDimensionFields()
    {
        var request = BuildValidRequest();
        request.PackageVariants.Add(BuildPackage("Small", 10, 8m, 40m, 30m, 25m));
        request.PackageVariants.Add(BuildPackage("Large", 5, 22m, 80m, 50m, 40m));

        var result = _validator.Validate(request);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors.Select(error => error.ErrorMessage)));
    }

    [Fact]
    public void Validate_PackageSizeWithoutUnitWeight_ReturnsVariantError()
    {
        var request = BuildValidRequest();
        request.PackageVariants.Add(BuildPackage("Small", 10, 0m, 40m, 30m, 25m));

        var result = _validator.Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "PackageVariants[0].ExpectedUnitWeightKg");
    }

    [Fact]
    public void Validate_MoreThanTwentyPackageSizes_ReturnsLimitError()
    {
        var request = BuildValidRequest();
        for (var index = 0; index < 21; index++)
            request.PackageVariants.Add(BuildPackage($"Size {index + 1}", 1, 1m, 10m, 10m, 10m));

        var result = _validator.Validate(request);

        Assert.Contains(result.Errors, error => error.PropertyName == "PackageVariants");
    }

    private static CreateOrderRequest BuildValidRequest()
    {
        return new CreateOrderRequest
        {
            ItemName = "Frozen salmon",
            Category = "MEAT_SEAFOOD",
            TempCondition = -18m,
            DestAddressText = "123 Le Loi, District 1, HCMC",
            ScheduleId = Guid.NewGuid(),
            DropoffStopId = Guid.NewGuid()
        };
    }

    private static CreateOrderPackageVariantRequest BuildPackage(
        string name,
        int quantity,
        decimal unitWeightKg,
        decimal lengthCm,
        decimal widthCm,
        decimal heightCm)
    {
        return new CreateOrderPackageVariantRequest
        {
            VariantName = name,
            PackagingType = "Foam Box",
            Quantity = quantity,
            ExpectedUnitWeightKg = unitWeightKg,
            LengthCm = lengthCm,
            WidthCm = widthCm,
            HeightCm = heightCm
        };
    }
}
