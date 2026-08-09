using ColdChainX.Application.DTOs.Quotations;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Hubs;
using ColdChainX.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;

namespace ColdChainX.UnitTests;

public sealed class QuotationTimestampWorkflowTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task SendThenAcceptQuotation_PersistsOnlyRealEventTimestampsAndPreservesWorkflow()
    {
        var customer = new Customer
        {
            CustomerId = Guid.NewGuid(), CompanyName = "Timestamp customer", TaxCode = "TAX-TIMESTAMP",
            Email = "timestamp@example.test", Status = "ACTIVE"
        };
        var customerUser = new User
        {
            UserId = Guid.NewGuid(), Username = "timestamp-customer", FullName = "Timestamp customer",
            Email = customer.Email, Status = "ACTIVE"
        };
        var order = new TransportOrder
        {
            OrderId = Guid.NewGuid(), Customer = customer, CustomerId = customer.CustomerId,
            TrackingCode = "TRACK-TIMESTAMP", ItemName = "Vaccines", Category = "PHARMA",
            Quantity = 1, PackingType = "PALLET", TempCondition = "2-8C", Status = "APPROVED",
            CreatedAt = DbNow().AddHours(-2)
        };
        var quotation = new Quotation
        {
            QuoteId = Guid.NewGuid(), Order = order, OrderId = order.OrderId,
            BaseFreight = 1_000m, VatPercentage = 8m, VatAmount = 80m, FinalAmount = 1_080m,
            PricingSource = "AUTO", Status = "DRAFT", CreatedAt = DbNow().AddHours(-1)
        };
        _database.Db.AddRange(customer, customerUser, order, quotation);
        await _database.Db.SaveChangesAsync();
        var service = new QuotationService(
            _database.Db,
            null!,
            new PdfServiceStub(),
            new WebHostEnvironmentStub { ContentRootPath = FindApiContentRoot() },
            new NoOpHubContext<NotificationHub>());

        var beforeSend = DbNow();
        var sent = await service.SendQuotationAsync(quotation.QuoteId, Guid.NewGuid());
        var afterSend = DbNow();

        Assert.True(sent.Success, sent.Message);
        Assert.Equal("SENT", sent.Data!.Status);
        Assert.NotNull(sent.Data.SentAt);
        Assert.InRange(sent.Data.SentAt.Value, beforeSend, afterSend);
        Assert.Null(sent.Data.AcceptedAt);
        Assert.Equal("QUOTING", order.Status);

        var beforeAccept = DbNow();
        var accepted = await service.AcceptQuotationAsync(
            quotation.QuoteId,
            new AcceptQuotationRequest(),
            customerUser.UserId);
        var afterAccept = DbNow();

        Assert.True(accepted.Success, accepted.Message);
        Assert.Equal("ACCEPTED", accepted.Data!.QuoteStatus);
        Assert.Equal("CONTRACT_PENDING", accepted.Data.OrderStatus);
        Assert.NotNull(quotation.AcceptedAt);
        Assert.InRange(quotation.AcceptedAt.Value, beforeAccept, afterAccept);
        Assert.NotNull(quotation.SentAt);
        Assert.Single(_database.Db.CustomerContracts.Where(c => c.OrderId == order.OrderId));
    }

    private static string FindApiContentRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "ColdChainX.API");
            if (File.Exists(Path.Combine(candidate, "Templates", "QuotationTemplate.html")))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("ColdChainX.API content root was not found.");
    }

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private sealed class WebHostEnvironmentStub : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ColdChainX.API";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Testing";
    }

    private sealed class PdfServiceStub : IPdfService
    {
        public Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber) => Task.FromResult("/contract.pdf");
        public Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber) => Task.FromResult("/quotation.pdf");
        public Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode) => Task.FromResult("/receipt.pdf");
        public Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId) => Task.FromResult("/waybill.pdf");
        public Task<byte[]> GeneratePdfFromHtmlAsync(string htmlContent) => Task.FromResult(Array.Empty<byte>());
        public Task<string> SavePdfFromUrlAsync(string url, string fileName, string folderPath = "pdf") => Task.FromResult("/file.pdf");
        public Task<string> SaveLifoMapPdfAsync(string htmlContent, string tripId) => Task.FromResult("/lifo.pdf");
        public Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId) => Task.FromResult("/load.pdf");
        public Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode) => Task.FromResult("/invoice.pdf");
        public Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber) => Task.FromResult("/appendix.pdf");
        public Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode) => Task.FromResult("/return.pdf");
        public Task<string> GenerateManifestPdfAsync(Guid tripId) => Task.FromResult("/manifest.pdf");
        public Task<string> GenerateOutboundTicketPdfAsync(Guid tripId) => Task.FromResult("/outbound.pdf");
    }
}

internal sealed class NoOpHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public IHubClients Clients { get; } = new NoOpHubClients();
    public IGroupManager Groups { get; } = new NoOpGroupManager();

    private sealed class NoOpHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoOpClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoOpClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
