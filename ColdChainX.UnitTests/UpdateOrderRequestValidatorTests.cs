using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Application.Validators;

namespace ColdChainX.UnitTests;

public class UpdateOrderRequestValidatorTests
{
    private readonly UpdateOrderRequestValidator _validator = new();

    [Fact]
    public async Task Validate_WhenOptionalValuesAreValid_Passes()
    {
        var result = await _validator.ValidateAsync(new UpdateOrderRequest
        {
            Category = "MEAT_SEAFOOD",
            PackagingType = "Carton Box,Foam Box",
            TempCondition = -18m,
            Quantity = 2,
            ScheduleId = Guid.NewGuid(),
            DropoffStopId = Guid.NewGuid(),
            ReceiverPhone = "0901234567"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_WhenValuesAreOutsideOrderRules_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateOrderRequest
        {
            ItemName = " ",
            Category = "INVALID",
            PackagingType = "Wooden Crate",
            TempCondition = 20m,
            Quantity = 0,
            CustomerProvidedTotalCbm = -1m,
            ScheduleId = Guid.Empty,
            DropoffStopId = Guid.Empty,
            ReceiverPhone = "abc"
        });

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.ItemName));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.Category));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.PackagingType));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.TempCondition));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.Quantity));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.CustomerProvidedTotalCbm));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.ScheduleId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.DropoffStopId));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderRequest.ReceiverPhone));
    }
}
