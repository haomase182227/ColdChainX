using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Application.DTOs.Orders;
using ColdChainX.Infrastructure.Hubs;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class OrderServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly MockLocationService _locationService;
        private readonly MockFileService _fileService;
        private readonly MockPdfService _pdfService;
        private readonly MockWebHostEnvironment _environment;
        private readonly MockHubContext<NotificationHub> _hubContext;
        private readonly OrderService _service;

        public OrderServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _locationService = new MockLocationService();
            _fileService = new MockFileService();
            _pdfService = new MockPdfService();
            _environment = new MockWebHostEnvironment();
            _hubContext = new MockHubContext<NotificationHub>();

            _service = new OrderService(
                _db,
                _locationService,
                _fileService,
                _pdfService,
                _environment,
                _hubContext
            );
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task ReviewOrder_Approve_SetsStatusToApproved()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            var order = new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-APPROVE-01",
                CustomerId = customerId,
                ItemName = "Cargo",
                Category = "FOOD",
                Quantity = 5,
                PackingType = "PALLET",
                TempCondition = "2 to 8",
                Status = "PENDING_REVIEW"
            };
            _db.TransportOrders.Add(order);

            var quotation = new Quotation
            {
                QuoteId = Guid.NewGuid(),
                OrderId = orderId,
                Status = "DRAFT",
                CreatedAt = DateTime.UtcNow,
                BaseFreight = 100m,
                LastMileSurcharge = 20m,
                VatAmount = 9.6m,
                FinalAmount = 129.6m,
                PricingSource = "AUTO"
            };
            _db.Quotations.Add(quotation);

            await _db.SaveChangesAsync();

            var request = new ReviewOrderRequest
            {
                Action = "APPROVE"
            };

            // Act
            var result = await _service.ReviewOrderAsync(orderId, request, Guid.NewGuid());

            // Assert
            Assert.True(result.Success, result.Message);
            Assert.Equal("APPROVED", result.Data.Status);

            var updatedOrder = await _db.TransportOrders.FindAsync(orderId);
            Assert.NotNull(updatedOrder);
            Assert.Equal("APPROVED", updatedOrder.Status);
        }

        [Fact]
        public async Task ReviewOrder_Reject_SetsStatusToRejected()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            var order = new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-REJECT-01",
                CustomerId = customerId,
                ItemName = "Cargo",
                Category = "FOOD",
                Quantity = 5,
                PackingType = "PALLET",
                TempCondition = "2 to 8",
                Status = "PENDING_REVIEW"
            };
            _db.TransportOrders.Add(order);

            await _db.SaveChangesAsync();

            var request = new ReviewOrderRequest
            {
                Action = "REJECT",
                RejectReason = "Documents incomplete"
            };

            // Act
            var result = await _service.ReviewOrderAsync(orderId, request, Guid.NewGuid());

            // Assert
            Assert.True(result.Success, result.Message);
            Assert.Equal("REJECTED", result.Data.Status);

            var updatedOrder = await _db.TransportOrders.FindAsync(orderId);
            Assert.NotNull(updatedOrder);
            Assert.Equal("REJECTED", updatedOrder.Status);
        }

        #region Mock Classes

        private class MockLocationService : ILocationService
        {
            public Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText) => Task.FromResult((0m, 0m));
            public Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon) => Task.FromResult(0m);
        }

        private class MockFileService : IFileService
        {
            public Task<string> UploadFileAsync(Microsoft.AspNetCore.Http.IFormFile file) => Task.FromResult("http://test.com/file.jpg");
            public Task<string> UploadFileAsync(System.IO.Stream stream, string fileName) => Task.FromResult($"/uploads/{fileName}");
            public Task<string> UploadFileAsync(byte[] fileBytes, string fileName) => Task.FromResult($"/uploads/{fileName}");
            public Task DeleteFileAsync(string fileUrl) => Task.CompletedTask;
        }

        private class MockPdfService : IPdfService
        {
            public Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber) => Task.FromResult("http://test.com/contract.pdf");
            public Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber) => Task.FromResult("http://test.com/quote.pdf");
            public Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode) => Task.FromResult("http://test.com/receipt.pdf");
            public Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId) => Task.FromResult("http://test.com/waybill.pdf");
            public Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId) => Task.FromResult("http://test.com/loadplan.pdf");
            public Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode) => Task.FromResult("http://test.com/invoice.pdf");
        }

        private class MockWebHostEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; } = "";
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
            public string ContentRootPath { get; set; } = "";
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ApplicationName { get; set; } = "ColdChainX";
            public string EnvironmentName { get; set; } = "Development";
        }

        private class MockHubContext<THub> : IHubContext<THub> where THub : Hub
        {
            public IHubClients Clients { get; } = new MockHubClients();
            public IGroupManager Groups { get; } = new MockGroupManager();
        }

        private class MockHubClients : IHubClients
        {
            public IClientProxy All => new MockClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new MockClientProxy();
            public IClientProxy Client(string connectionId) => new MockClientProxy();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new MockClientProxy();
            public IClientProxy Group(string groupName) => new MockClientProxy();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new MockClientProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new MockClientProxy();
            public IClientProxy User(string userId) => new MockClientProxy();
            public IClientProxy Users(IReadOnlyList<string> userIds) => new MockClientProxy();
        }

        private class MockClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        private class MockGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        #endregion
    }
}
