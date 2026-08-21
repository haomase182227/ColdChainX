using System.Reflection;
using ColdChainX.API.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace ColdChainX.UnitTests;

public class OrderControllerAuthorizationTests
{
    [Fact]
    public void AdminUpdateOrder_AllowsOnlyAdminAndSales()
    {
        var action = typeof(OrderController).GetMethod(nameof(OrderController.AdminUpdateOrder));

        var authorize = Assert.Single(action!.GetCustomAttributes<AuthorizeAttribute>());
        var roles = authorize.Roles!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(["Admin", "Sales"], roles);
        Assert.DoesNotContain("Customer", roles);
    }
}
