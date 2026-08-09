using System.Text.Json;
using ColdChainX.Application.Features.Payment.Queries;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Services;

namespace ColdChainX.UnitTests;

public sealed class FilteringRelationalTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task GetContracts_NoFilters_PaginatesAfterOrderingAndReturnsCompleteDto()
    {
        var start = DbTime(2026, 8, 1);
        var (customer, order) = AddCustomerAndOrder("A", start);
        AddContract("CTR-1", "DRAFT", start, customer, order);
        AddContract("CTR-2", "ACTIVE", start.AddHours(1), customer, order);
        AddContract("CTR-3", "PENDING_SALES_VERIFICATION", start.AddHours(2), customer, order,
            sentAt: start.AddHours(3), uploadedAt: start.AddHours(4), verifiedAt: start.AddHours(5));
        await _database.Db.SaveChangesAsync();

        var service = NewContractService();
        var page1 = await service.GetContractsAsync(null, null, null, null, 1, 2);
        var page2 = await service.GetContractsAsync(null, null, null, null, 2, 2);

        Assert.True(page1.Success);
        Assert.NotNull(page1.Data);
        Assert.Equal(3, page1.Data.TotalCount);
        Assert.Equal(2, page1.Data.TotalPages);
        Assert.Equal(new[] { "CTR-3", "CTR-2" }, page1.Data.Items.Select(x => x.ContractNumber));
        var newest = page1.Data.Items.First();
        Assert.Equal(order.OrderId, newest.OrderId);
        Assert.Equal(order.TrackingCode, newest.TrackingCode);
        Assert.Equal(customer.CustomerId, newest.CustomerId);
        Assert.Equal(customer.CompanyName, newest.CustomerName);
        Assert.Equal("PENDING_SALES_VERIFICATION", newest.Status);
        Assert.NotNull(newest.CreatedAt);
        Assert.NotNull(newest.SentAt);
        Assert.NotNull(newest.UploadedSignedAt);
        Assert.NotNull(newest.VerifiedAt);
        Assert.Equal("CTR-1", Assert.Single(page2.Data!.Items).ContractNumber);
    }

    [Fact]
    public async Task GetContracts_AllFiltersAndInclusiveDateBoundaries_AreAppliedBeforePagination()
    {
        var start = DbTime(2026, 8, 1);
        var (customer, order) = AddCustomerAndOrder("B", start);
        var (otherCustomer, otherOrder) = AddCustomerAndOrder("C", start);
        AddContract("CTR-START", "ACTIVE", start, customer, order);
        AddContract("CTR-MIDDLE", "ACTIVE", start.AddHours(12), customer, order);
        AddContract("CTR-END", "ACTIVE", start.AddDays(1).AddTicks(-1), customer, order);
        AddContract("CTR-STATUS", "DRAFT", start.AddHours(6), customer, order);
        AddContract("CTR-CUSTOMER", "ACTIVE", start.AddHours(6), otherCustomer, otherOrder);
        AddContract("CTR-OUTSIDE", "ACTIVE", start.AddDays(1), customer, order);
        await _database.Db.SaveChangesAsync();

        var result = await NewContractService().GetContractsAsync(
            " active ", customer.CustomerId, start, start, 1, 2);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.TotalCount);
        Assert.Equal(2, result.Data.TotalPages);
        Assert.Equal(2, result.Data.Items.Count);

        var fromOnly = await NewContractService().GetContractsAsync(null, null, start.AddDays(1), null, 1, 20);
        Assert.Equal(new[] { "CTR-OUTSIDE" }, fromOnly.Data!.Items.Select(x => x.ContractNumber));

        var toOnly = await NewContractService().GetContractsAsync(null, null, null, start, 1, 20);
        Assert.DoesNotContain(toOnly.Data!.Items, x => x.ContractNumber == "CTR-OUTSIDE");
        Assert.Contains(toOnly.Data.Items, x => x.ContractNumber == "CTR-START");
        Assert.Contains(toOnly.Data.Items, x => x.ContractNumber == "CTR-END");
    }

    [Fact]
    public async Task GetContracts_InvalidInputs_FollowListConventionWithoutExceptions()
    {
        var start = DbTime(2026, 8, 1);
        var (customer, order) = AddCustomerAndOrder("D", start);
        AddContract("CTR-ONLY", "ACTIVE", start, customer, order);
        await _database.Db.SaveChangesAsync();

        var service = NewContractService();
        var invalidStatus = await service.GetContractsAsync("NOT_A_STATUS", null, null, null, 1, 10);
        var unknownCustomer = await service.GetContractsAsync(null, Guid.NewGuid(), null, null, 1, 10);
        var invertedRange = await service.GetContractsAsync(null, null, start.AddDays(1), start, 1, 10);
        var invalidPagination = await service.GetContractsAsync(null, null, null, null, 0, 0);

        Assert.Empty(invalidStatus.Data!.Items);
        Assert.Empty(unknownCustomer.Data!.Items);
        Assert.Empty(invertedRange.Data!.Items);
        Assert.Equal(1, invalidPagination.Data!.PageNumber);
        Assert.Equal(10, invalidPagination.Data.PageSize);
        Assert.Equal(1, invalidPagination.Data.TotalCount);
    }

    [Fact]
    public async Task GetQuotations_NoFilters_PreservesResponseAndRealEventTimestamps()
    {
        var start = DbTime(2026, 8, 1);
        var (_, order) = AddCustomerAndOrder("E", start);
        AddQuotation("Q-DRAFT", "DRAFT", start, order);
        AddQuotation("Q-SENT", "SENT", start.AddHours(1), order, sentAt: start.AddHours(2));
        AddQuotation("Q-ACCEPTED", "ACCEPTED", start.AddHours(2), order,
            sentAt: start.AddHours(3), acceptedAt: start.AddHours(4));
        await _database.Db.SaveChangesAsync();

        var result = await NewQuotationService().GetQuotationsAsync(1, 10);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.TotalRecords);
        Assert.Equal(new[] { "ACCEPTED", "SENT", "DRAFT" }, result.Data.Data.Select(x => x.Status));
        var draft = result.Data.Data.Single(x => x.Status == "DRAFT");
        Assert.Null(draft.SentAt);
        Assert.Null(draft.AcceptedAt);
        var accepted = result.Data.Data.Single(x => x.Status == "ACCEPTED");
        Assert.Equal(order.TrackingCode, accepted.TrackingCode);
        Assert.Equal(start.AddHours(3), accepted.SentAt);
        Assert.Equal(start.AddHours(4), accepted.AcceptedAt);
        Assert.Equal(1_080m, accepted.FinalAmount);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("SENT")]
    [InlineData("ACCEPTED")]
    public async Task GetQuotations_EachValidStatus_IsFilteredCaseInsensitively(string status)
    {
        var start = DbTime(2026, 8, 1);
        var (_, order) = AddCustomerAndOrder("F" + status[0], start);
        AddQuotation("Q-DRAFT", "DRAFT", start, order);
        AddQuotation("Q-SENT", "SENT", start.AddHours(1), order);
        AddQuotation("Q-ACCEPTED", "ACCEPTED", start.AddHours(2), order);
        await _database.Db.SaveChangesAsync();

        var result = await NewQuotationService().GetQuotationsAsync(1, 10, status.ToLowerInvariant());

        Assert.Equal(1, result.Data!.TotalRecords);
        Assert.Equal(status, Assert.Single(result.Data.Data).Status);
    }

    [Fact]
    public async Task GetQuotations_DateRangeCombinationAndPagination_UseFilteredCount()
    {
        var start = DbTime(2026, 8, 1);
        var (_, order) = AddCustomerAndOrder("G", start);
        AddQuotation("Q-START", "SENT", start, order);
        AddQuotation("Q-MIDDLE", "SENT", start.AddHours(12), order);
        AddQuotation("Q-END", "SENT", start.AddDays(1).AddTicks(-1), order);
        AddQuotation("Q-WRONG-STATUS", "DRAFT", start.AddHours(13), order);
        AddQuotation("Q-OUTSIDE", "SENT", start.AddDays(1), order);
        await _database.Db.SaveChangesAsync();

        var page2 = await NewQuotationService().GetQuotationsAsync(2, 2, "SENT", start, start);

        Assert.Equal(3, page2.Data!.TotalRecords);
        Assert.Equal(2, page2.Data.TotalPages);
        Assert.Equal(2, page2.Data.CurrentPage);
        Assert.Equal("Q-START", Assert.Single(page2.Data.Data).FileUrl);

        var invalid = await NewQuotationService().GetQuotationsAsync(0, 0, "INVALID", start.AddDays(1), start);
        Assert.Equal(0, invalid.Data!.TotalRecords);
        Assert.Empty(invalid.Data.Data);
        Assert.Equal(1, invalid.Data.CurrentPage);
        Assert.Equal(10, invalid.Data.PageSize);
    }

    [Fact]
    public async Task GetPaymentTransactions_NoFilters_PreservesLedgerAndNullableCompletedAt()
    {
        var start = DbTime(2026, 8, 1);
        _database.Db.PaymentTransactions.AddRange(
            Payment("P-1", "IN", "COMPLETED", "CASH", start, 100m, completedAt: start.AddHours(1)),
            Payment("P-2", "OUT", "PENDING_VERIFY", "BANK_TRANSFER", start.AddHours(2), 40m));
        await _database.Db.SaveChangesAsync();

        var result = await NewPaymentHandler().Handle(new GetAllPaymentTransactionsQuery(), CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("TotalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("Summary").GetProperty("TotalTransactionsCount").GetInt32());
        Assert.Equal(2, root.GetProperty("Transactions").GetArrayLength());
        var pending = root.GetProperty("Transactions").EnumerateArray()
            .Single(x => x.GetProperty("TransactionCode").GetString() == "P-2");
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("CompletedAt").ValueKind);
    }

    [Theory]
    [InlineData("status", "COMPLETED", 2)]
    [InlineData("transactionType", "OUT", 2)]
    [InlineData("paymentMethod", "CASH", 2)]
    public async Task GetPaymentTransactions_EachFilter_UsesExistingLedgerValues(
        string filter, string value, int expected)
    {
        var start = DbTime(2026, 8, 1);
        _database.Db.PaymentTransactions.AddRange(
            Payment("KEEP", "IN", "COMPLETED", "CASH", start, 100m),
            Payment("STATUS", "OUT", "PENDING_VERIFY", "CASH", start, 20m),
            Payment("TYPE", "OUT", "COMPLETED", "BANK_TRANSFER", start, 30m));
        await _database.Db.SaveChangesAsync();
        var query = new GetAllPaymentTransactionsQuery();
        if (filter == "status") query.Status = value.ToLowerInvariant();
        if (filter == "transactionType") query.TransactionType = value.ToLowerInvariant();
        if (filter == "paymentMethod") query.PaymentMethod = value.ToLowerInvariant();

        var result = await NewPaymentHandler().Handle(query, CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));

        Assert.Equal(expected, json.RootElement.GetProperty("TotalCount").GetInt32());
    }

    [Fact]
    public async Task GetPaymentTransactions_CombinedFiltersDatesAndPagination_UseFilteredUnion()
    {
        var start = DbTime(2026, 8, 1);
        _database.Db.PaymentTransactions.AddRange(
            Payment("START", "IN", "PENDING_VERIFY", "PAYOS", start, 100m),
            Payment("MIDDLE", "IN", "PENDING_VERIFY", "PAYOS", start.AddHours(12), 200m),
            Payment("END", "IN", "PENDING_VERIFY", "PAYOS", start.AddDays(1).AddTicks(-1), 300m),
            Payment("STATUS", "IN", "COMPLETED", "PAYOS", start, 1m),
            Payment("TYPE", "OUT", "PENDING_VERIFY", "PAYOS", start, 1m),
            Payment("METHOD", "IN", "PENDING_VERIFY", "CASH", start, 1m),
            Payment("OUTSIDE", "IN", "PENDING_VERIFY", "PAYOS", start.AddDays(1), 1m));
        await _database.Db.SaveChangesAsync();

        var result = await NewPaymentHandler().Handle(new GetAllPaymentTransactionsQuery
        {
            Status = "pending_verify",
            TransactionType = "in",
            PaymentMethod = "payos",
            FromDate = start,
            ToDate = start,
            PageNumber = 2,
            PageSize = 2
        }, CancellationToken.None);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Data));
        var root = json.RootElement;

        Assert.Equal(3, root.GetProperty("TotalCount").GetInt32());
        Assert.Equal(2, root.GetProperty("TotalPages").GetInt32());
        Assert.Equal("START", root.GetProperty("Transactions")[0].GetProperty("TransactionCode").GetString());
        Assert.Equal(600m, root.GetProperty("Summary").GetProperty("TotalCodReceived").GetDecimal());

        var invalid = await NewPaymentHandler().Handle(new GetAllPaymentTransactionsQuery
        {
            Status = "INVALID",
            FromDate = start.AddDays(1),
            ToDate = start,
            PageNumber = 0,
            PageSize = 0
        }, CancellationToken.None);
        using var invalidJson = JsonDocument.Parse(JsonSerializer.Serialize(invalid.Data));
        Assert.Equal(0, invalidJson.RootElement.GetProperty("TotalCount").GetInt32());
        Assert.Equal(1, invalidJson.RootElement.GetProperty("PageNumber").GetInt32());
        Assert.Equal(10, invalidJson.RootElement.GetProperty("PageSize").GetInt32());
    }

    private ContractService NewContractService()
        => new(_database.Db, null!, null!, null!, null!);

    private QuotationService NewQuotationService()
        => new(_database.Db, null!, null!, null!, null!);

    private GetAllPaymentTransactionsQueryHandler NewPaymentHandler()
        => new(_database.Db);

    private (Customer Customer, TransportOrder Order) AddCustomerAndOrder(string suffix, DateTime createdAt)
    {
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(),
            CompanyName = "Customer " + suffix,
            TaxCode = "TAX-" + suffix + "-" + Guid.NewGuid().ToString("N")[..6],
            Status = "ACTIVE",
            CreatedAt = createdAt
        };
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(),
            Customer = customer,
            CustomerId = customer.CustomerId,
            TrackingCode = "TRACK-" + suffix + "-" + Guid.NewGuid().ToString("N")[..6],
            ItemName = "Vaccines",
            Category = "PHARMA",
            Quantity = 1,
            PackingType = "PALLET",
            TempCondition = "2-8C",
            Status = "PENDING_REVIEW",
            CreatedAt = createdAt
        };
        _database.Db.AddRange(customer, order);
        return (customer, order);
    }

    private void AddContract(
        string number,
        string status,
        DateTime createdAt,
        Customer customer,
        TransportOrder order,
        DateTime? sentAt = null,
        DateTime? uploadedAt = null,
        DateTime? verifiedAt = null)
        => _database.Db.CustomerContracts.Add(new CustomerContract
        {
            ContractId = Guid.NewGuid(),
            Customer = customer,
            CustomerId = customer.CustomerId,
            Order = order,
            OrderId = order.OrderId,
            ContractNumber = number,
            FileUrl = "/contracts/" + number + ".pdf",
            Status = status,
            ExpiredDate = DateOnly.FromDateTime(createdAt.AddYears(1)),
            CreatedAt = createdAt,
            SentAt = sentAt,
            UploadedSignedAt = uploadedAt,
            VerifiedAt = verifiedAt
        });

    private void AddQuotation(
        string file,
        string status,
        DateTime createdAt,
        TransportOrder order,
        DateTime? sentAt = null,
        DateTime? acceptedAt = null)
        => _database.Db.Quotations.Add(new Quotation
        {
            QuoteId = Guid.NewGuid(),
            Order = order,
            OrderId = order.OrderId,
            BaseFreight = 1_000m,
            VatAmount = 80m,
            FinalAmount = 1_080m,
            PricingSource = "AUTO",
            FileUrl = file,
            Status = status,
            CreatedAt = createdAt,
            SentAt = sentAt,
            AcceptedAt = acceptedAt
        });

    private static PaymentTransaction Payment(
        string code,
        string type,
        string status,
        string method,
        DateTime createdAt,
        decimal amount,
        DateTime? completedAt = null)
        => new()
        {
            TransactionId = Guid.NewGuid(),
            TransactionCode = code,
            TransactionType = type,
            Status = status,
            PaymentMethod = method,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            Amount = amount
        };

    private static DateTime DbTime(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
}
