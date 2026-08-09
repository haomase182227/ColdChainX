using System.Reflection;
using System.Text.Json;
using ColdChainX.API.Controllers;
using ColdChainX.Application.Features.Payment.Queries;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.UnitTests;

public class DashboardAndFilteringTests : IDisposable
{
    private readonly SqliteTestDatabase _database;
    private readonly ApplicationDbContext _db;
    private readonly DashboardService _dashboardService;

    public DashboardAndFilteringTests()
    {
        _database = new SqliteTestDatabase();
        _db = _database.Db;
        _dashboardService = new DashboardService(_db);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task SalesOverview_UsesPersistedTimestampsForFunnelAndAverages()
    {
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(),
            TrackingCode = "PROSHIP-001",
            ItemName = "Vaccines",
            Category = "PHARMA",
            Quantity = 1,
            PackingType = "PALLET",
            TempCondition = "2-8C",
            Status = "CONTRACT_PENDING",
            CreatedAt = start.AddHours(1)
        };
        _db.TransportOrders.Add(order);
        _db.Quotations.Add(new Quotation
        {
            QuoteId = Guid.NewGuid(),
            OrderId = order.OrderId,
            Order = order,
            BaseFreight = 1_000_000,
            VatAmount = 80_000,
            FinalAmount = 1_080_000,
            PricingSource = "AUTO",
            Status = "ACCEPTED",
            CreatedAt = start.AddHours(2),
            SentAt = start.AddHours(5),
            AcceptedAt = start.AddHours(8)
        });
        _db.CustomerContracts.Add(new CustomerContract
        {
            ContractId = Guid.NewGuid(),
            OrderId = order.OrderId,
            Order = order,
            ContractNumber = "CTR-001",
            ExpiredDate = DateOnly.FromDateTime(start.AddYears(1)),
            FileUrl = string.Empty,
            Status = "ACTIVE",
            CreatedAt = start.AddHours(9),
            SentAt = start.AddHours(10),
            UploadedSignedAt = start.AddHours(12),
            VerifiedAt = start.AddHours(14)
        });
        await _db.SaveChangesAsync();

        var result = await _dashboardService.GetSalesOverviewAsync(start, start.AddDays(1), null);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(7, result.Data.Funnel.Count);
        Assert.Equal(1, result.Data.Funnel.Single(x => x.Key == "QUOTATION_SENT").Count);
        Assert.Equal(4m, result.Data.AverageProcessingTimes.OrderToQuotationSentHours);
        Assert.Equal(2m, result.Data.AverageProcessingTimes.SignedUploadToVerificationHours);
        Assert.Equal(start.AddHours(5), _db.Quotations.Single().SentAt);
    }

    [Fact]
    public async Task AccountantOverview_GroupsCashFlowAndCalculatesReceivables()
    {
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            CompanyName = "Accountant test customer",
            TaxCode = "TAX-ACCOUNTANT-TEST",
            Status = "ACTIVE"
        };
        _db.Customers.Add(customer);
        _db.Invoices.Add(new Invoice
        {
            InvoiceId = Guid.NewGuid(),
            InvoiceCode = "INV-001",
            CustomerId = customer.CustomerId,
            Customer = customer,
            SubTotal = 900m,
            TaxAmount = 100m,
            GrandTotal = 1_000m,
            PaidAmount = 400m,
            IssuedDate = new DateOnly(2026, 8, 1),
            DueDate = new DateOnly(2026, 8, 10),
            Status = "UNPAID"
        });
        _db.PaymentTransactions.AddRange(
            NewTransaction("PTX-IN", "IN", 500m, "COMPLETED", start.AddHours(1)),
            NewTransaction("PTX-OUT", "OUT", 100m, "COMPLETED", start.AddHours(2)),
            NewTransaction("PTX-PENDING", "IN", 50m, "PENDING_VERIFY", start.AddHours(3)));
        await _db.SaveChangesAsync();

        var result = await _dashboardService.GetAccountantOverviewAsync(start, start.AddDays(1), "DAY");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(600m, result.Data.Kpis.Receivables);
        Assert.Equal(500m, result.Data.Kpis.CashCollected);
        Assert.Equal(1, result.Data.Kpis.PendingVerificationTransactions);
        Assert.Single(result.Data.CashFlowSeries);
        Assert.Equal(500m, result.Data.CashFlowSeries.Single().CashIn);
        Assert.Equal(100m, result.Data.CashFlowSeries.Single().CashOut);
    }

    [Fact]
    public async Task PaymentTransactions_AppliesStatusTypeMethodAndDateFilters()
    {
        var start = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified);
        _db.PaymentTransactions.AddRange(
            NewTransaction("PTX-KEEP", "IN", 500m, "PENDING_VERIFY", start.AddHours(1), "PAYOS"),
            NewTransaction("PTX-STATUS", "IN", 500m, "COMPLETED", start.AddHours(1), "PAYOS"),
            NewTransaction("PTX-TYPE", "OUT", 500m, "PENDING_VERIFY", start.AddHours(1), "PAYOS"),
            NewTransaction("PTX-METHOD", "IN", 500m, "PENDING_VERIFY", start.AddHours(1), "CASH"),
            NewTransaction("PTX-DATE", "IN", 500m, "PENDING_VERIFY", start.AddDays(2), "PAYOS"));
        await _db.SaveChangesAsync();

        var handler = new GetAllPaymentTransactionsQueryHandler(_db);
        var result = await handler.Handle(new GetAllPaymentTransactionsQuery
        {
            Status = "pending_verify",
            TransactionType = "in",
            PaymentMethod = "payos",
            FromDate = start,
            ToDate = start,
            PageNumber = 1,
            PageSize = 10
        }, CancellationToken.None);

        Assert.True(result.Success);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        Assert.Equal(1, json.RootElement.GetProperty("TotalCount").GetInt32());
        Assert.Equal("PTX-KEEP", json.RootElement.GetProperty("Transactions")[0].GetProperty("TransactionCode").GetString());
    }

    [Theory]
    [InlineData(nameof(IncidentReportsController.ApproveExpense))]
    [InlineData(nameof(IncidentReportsController.ReimburseExpense))]
    public void IncidentExpenseEndpoints_AllowAccountant(string actionName)
    {
        var action = typeof(IncidentReportsController).GetMethod(actionName, BindingFlags.Instance | BindingFlags.Public);
        var authorize = Assert.Single(action!.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Contains("Accountant", authorize.Roles);
    }

    private static PaymentTransaction NewTransaction(
        string code,
        string type,
        decimal amount,
        string status,
        DateTime createdAt,
        string method = "BANK_TRANSFER")
        => new()
        {
            TransactionId = Guid.NewGuid(),
            TransactionCode = code,
            TransactionType = type,
            Amount = amount,
            PaymentMethod = method,
            Status = status,
            CreatedAt = createdAt
        };
}
