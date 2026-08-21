using System.Reflection;
using ColdChainX.API.Authorization;
using ColdChainX.API.Controllers;
using ColdChainX.Shared.Constants;
using Microsoft.AspNetCore.Authorization;

namespace ColdChainX.UnitTests;

public class OrderControllerAuthorizationTests
{
    [Fact]
    public void AdminUpdateOrder_AllowsOnlyAdminAndSales()
    {
        var action = typeof(OrderController).GetMethod(nameof(OrderController.AdminUpdateOrder));
        Assert.NotNull(action);
        var method = action!;

        var authorize = Assert.Single(
            method.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Roles != null);
        var roles = authorize.Roles!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var permission = Assert.Single(method.GetCustomAttributes<HasPermissionAttribute>());

        Assert.Equal(["Admin", "Sales"], roles);
        Assert.DoesNotContain("Customer", roles);
        Assert.Equal($"{HasPermissionAttribute.PolicyPrefix}{PermissionCodes.OrderUpdateAny}", permission.Policy);
    }
}
