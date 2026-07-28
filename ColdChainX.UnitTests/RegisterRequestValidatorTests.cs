using ColdChainX.Application.DTOs;
using ColdChainX.Application.Validators;

namespace ColdChainX.UnitTests;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Theory]
    [InlineData("Admin")]
    [InlineData("Dispatcher")]
    [InlineData("Sales")]
    [InlineData("WarehouseWorker")]
    [InlineData("warehouseworker")]
    public void Validate_AllowsSupportedStaffRoles(string role)
    {
        var result = _validator.Validate(CreateRequest(role));

        Assert.DoesNotContain(result.Errors, error => error.PropertyName == nameof(RegisterRequest.Role));
    }

    [Theory]
    [InlineData("Loader")]
    [InlineData("WarehouseOperator")]
    [InlineData("Manager")]
    public void Validate_RejectsLegacyWarehouseRoles(string role)
    {
        var result = _validator.Validate(CreateRequest(role));

        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RegisterRequest.Role));
    }

    private static RegisterRequest CreateRequest(string role)
    {
        return new RegisterRequest
        {
            FullName = "Warehouse User",
            Email = "warehouse.user@coldchainx.test",
            Password = "Password@123",
            Role = role
        };
    }
}
