using ColdChainX.Application.DTOs.Chat;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Services;

namespace ColdChainX.UnitTests;

public sealed class ChatServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task CustomerCanSendFirstMessageWithoutReceiverId()
    {
        var customerRole = CreateRole("Customer");
        var salesRole = CreateRole("Sales");
        var adminRole = CreateRole("Admin");
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            CompanyName = "First message customer",
            TaxCode = "CHAT-FIRST",
            Email = "customer-chat@example.test",
            Status = "ACTIVE"
        };
        var customerUser = CreateUser(
            customerRole,
            "customer-chat",
            customer.Email!,
            DateTime.UtcNow.AddHours(-3));
        var adminUser = CreateUser(
            adminRole,
            "admin-chat",
            "admin-chat@example.test",
            DateTime.UtcNow.AddHours(-2));
        var salesUser = CreateUser(
            salesRole,
            "sales-chat",
            "sales-chat@example.test",
            DateTime.UtcNow.AddHours(-1));
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(),
            Customer = customer,
            CustomerId = customer.CustomerId,
            TrackingCode = "CHAT-FIRST-ORDER",
            ItemName = "Vaccines",
            Category = "PHARMA",
            Quantity = 1,
            PackingType = "BOX",
            TempCondition = "2-8C",
            Status = "APPROVED"
        };

        _database.Db.AddRange(customerRole, salesRole, adminRole, customerUser, adminUser, salesUser, order);
        await _database.Db.SaveChangesAsync();
        var service = new ChatService(_database.Db, new NoOpHubContext<ChatHub>());

        var result = await service.SendMessageAsync(
            order.OrderId,
            customerUser.UserId,
            new[] { "Customer" },
            customer.CustomerId,
            new SendChatMessageRequest { MessageContent = "I need help with this order" });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(salesUser.UserId, result.Data.ReceiverId);
        Assert.Equal("Sales", result.Data.ReceiverRole);
        var savedMessage = Assert.Single(_database.Db.ChatMessages);
        Assert.Equal(customerUser.UserId, savedMessage.SenderId);
        Assert.Equal(salesUser.UserId, savedMessage.ReceiverId);
    }

    [Fact]
    public async Task CustomerFirstMessageFailsClearlyWhenNoActiveStaffExists()
    {
        var customerRole = CreateRole("Customer");
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            CompanyName = "No staff customer",
            TaxCode = "CHAT-NO-STAFF",
            Email = "customer-no-staff@example.test",
            Status = "ACTIVE"
        };
        var customerUser = CreateUser(
            customerRole,
            "customer-no-staff",
            customer.Email!,
            DateTime.UtcNow);
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(),
            Customer = customer,
            CustomerId = customer.CustomerId,
            TrackingCode = "CHAT-NO-STAFF-ORDER",
            ItemName = "Vaccines",
            Category = "PHARMA",
            Quantity = 1,
            PackingType = "BOX",
            TempCondition = "2-8C",
            Status = "APPROVED"
        };

        _database.Db.AddRange(customerRole, customerUser, order);
        await _database.Db.SaveChangesAsync();
        var service = new ChatService(_database.Db, new NoOpHubContext<ChatHub>());

        var result = await service.SendMessageAsync(
            order.OrderId,
            customerUser.UserId,
            new[] { "Customer" },
            customer.CustomerId,
            new SendChatMessageRequest { ReceiverId = null, MessageContent = "Is anybody there?" });

        Assert.False(result.Success);
        Assert.Contains("No active", result.Message);
        Assert.Empty(_database.Db.ChatMessages);
    }

    private static Role CreateRole(string roleName)
        => new() { RoleId = Guid.NewGuid(), RoleName = roleName };

    private static User CreateUser(
        Role role,
        string username,
        string email,
        DateTime createdAt)
        => new()
        {
            UserId = Guid.NewGuid(),
            Username = username,
            FullName = username,
            Email = email,
            Role = role,
            RoleId = role.RoleId,
            Status = "ACTIVE",
            CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Unspecified)
        };
}
