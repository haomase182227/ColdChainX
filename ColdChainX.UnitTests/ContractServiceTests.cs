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
using ColdChainX.Application.DTOs.Contracts;
using ColdChainX.Infrastructure.Hubs;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class ContractServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly MockFileService _fileService;
        private readonly MockPdfService _pdfService;
        private readonly MockWebHostEnvironment _environment;
        private readonly MockHubContext<NotificationHub> _hubContext;
        private readonly ContractService _service;

        public ContractServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _fileService = new MockFileService();
            _pdfService = new MockPdfService();
            _environment = new MockWebHostEnvironment();
            _hubContext = new MockHubContext<NotificationHub>();

            _service = new ContractService(
                _db,
                _pdfService,
                _environment,
                _hubContext,
                _fileService
            );
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task GetContractByOrderId_ExistingContract_ReturnsContractInfo()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var contractId = Guid.NewGuid();

            var contract = new CustomerContract
            {
                ContractId = contractId,
                CustomerId = customerId,
                OrderId = orderId,
                ContractNumber = "HD-2026-000001",
                FileUrl = "http://test.com/file.pdf",
                SignedFileUrl = null,
                SentAt = null,
                UploadedSignedAt = null,
                VerifiedAt = null,
                Status = "PENDING_SIGNATURE",
                ExpiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1))
            };
            _db.CustomerContracts.Add(contract);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.GetContractByOrderIdAsync(orderId);

            // Assert
            Assert.True(result.Success, result.Message);
            Assert.Equal("Contract retrieved successfully", result.Message);
            Assert.NotNull(result.Data);
            Assert.Equal(contractId, result.Data.ContractId);
            Assert.Equal(orderId, result.Data.OrderId);
            Assert.Equal("HD-2026-000001", result.Data.ContractNumber);
            Assert.Equal("PENDING_SIGNATURE", result.Data.Status);
        }

        [Fact]
        public async Task GetContractByOrderId_NonExistentContract_ReturnsFailure()
        {
            // Act
            var result = await _service.GetContractByOrderIdAsync(Guid.NewGuid());

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Contract not found", result.Message);
        }

        #region Mock Classes

        private class MockFileService : IFileService
        {
            public Task<string> UploadFileAsync(Microsoft.AspNetCore.Http.IFormFile file) => Task.FromResult("http://test.com/file.jpg");
            public Task<string> UploadFileAsync(System.IO.Stream stream, string fileName) => Task.FromResult($"/uploads/{fileName}");
            public Task<string> UploadFileAsync(byte[] fileBytes, string fileName) => Task.FromResult($"/uploads/{fileName}");
            public Task DeleteFileAsync(string fileUrl) => Task.CompletedTask;
            public string GetSignedUrl(string publicId) => $"http://test.com/{publicId}";
        }

        private class MockPdfService : IPdfService
        {
            public Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber) => Task.FromResult("http://test.com/contract.pdf");
            public Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber) => Task.FromResult("http://test.com/quote.pdf");
            public Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode) => Task.FromResult("http://test.com/receipt.pdf");
            public Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId) => Task.FromResult("http://test.com/waybill.pdf");
            public Task<string> SaveLifoMapPdfAsync(string htmlContent, string tripId) => Task.FromResult("http://test.com/lifo.pdf");
            public Task<string> SavePdfFromUrlAsync(string url, string fileId, string prefix) => Task.FromResult("http://test.com/url.pdf");
            public Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId) => Task.FromResult("http://test.com/loadplan.pdf");
            public Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode) => Task.FromResult("http://test.com/invoice.pdf");
            public Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber) => Task.FromResult("http://test.com/appendix.pdf");
            public Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode) => Task.FromResult("http://test.com/returnslip.pdf");
            public Task<string> GenerateManifestPdfAsync(Guid tripId) => Task.FromResult("http://test.com/manifest.pdf");
            public Task<string> GenerateOutboundTicketPdfAsync(Guid tripId) => Task.FromResult("http://test.com/outbound-ticket.pdf");
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
