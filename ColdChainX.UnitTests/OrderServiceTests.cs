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
        public async Task GetOrders_LoadsCustomerContactWithoutCapturingOrderServiceInProjection()
        {
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            const string customerEmail = "customer@example.com";

            _db.Customers.Add(new Customer
            {
                CustomerId = customerId,
                CompanyName = "Projection Test Customer",
                TaxCode = "PROJECTION-001",
                Email = customerEmail,
                Status = "ACTIVE"
            });
            _db.Users.Add(new User
            {
                UserId = Guid.NewGuid(),
                Username = "projection-customer",
                Email = customerEmail,
                FullName = "Nguyen Van Contact",
                Phone = "0901234567",
                Status = "ACTIVE"
            });
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-PROJECTION-001",
                CustomerId = customerId,
                ItemName = "Frozen cargo",
                Category = "MEAT_SEAFOOD",
                Quantity = 1,
                PackingType = "Carton Box",
                TempCondition = "-18",
                Status = "PENDING_REVIEW",
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            var result = await _service.GetOrdersAsync(1, 10);

            Assert.True(result.Success, result.Message);
            var page = Assert.IsType<ColdChainX.Application.DTOs.Common.PagedResult<OrderResponse>>(result.Data);
            var order = Assert.Single(page.Data);
            Assert.Equal("Nguyen Van Contact", order.CustomerContactName);
            Assert.Equal("0901234567", order.CustomerPhone);

            var detailResult = await _service.GetOrderByIdAsync(orderId);
            Assert.True(detailResult.Success, detailResult.Message);
            var detail = Assert.IsType<OrderResponse>(detailResult.Data);
            Assert.Equal("Nguyen Van Contact", detail.CustomerContactName);
            Assert.Equal("0901234567", detail.CustomerPhone);
        }

        [Fact]
        public async Task ReviewOrder_Approve_SetsStatusToApproved()
        {
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

            var result = await _service.ReviewOrderAsync(orderId, request, Guid.NewGuid());

            Assert.True(result.Success, result.Message);
            Assert.Equal("APPROVED", result.Data.Status);

            var updatedOrder = await _db.TransportOrders.FindAsync(orderId);
            Assert.NotNull(updatedOrder);
            Assert.Equal("APPROVED", updatedOrder.Status);
        }

        [Fact]
        public async Task ReviewOrder_Reject_SetsStatusToRejected()
        {
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
                Action = "COMPLIANCE_REJECT",
                CustomerNote = "Documents incomplete"
            };

            var result = await _service.ReviewOrderAsync(orderId, request, Guid.NewGuid());

            Assert.True(result.Success, result.Message);
            Assert.Equal("REJECTED", result.Data.Status);

            var updatedOrder = await _db.TransportOrders.FindAsync(orderId);
            Assert.NotNull(updatedOrder);
            Assert.Equal("REJECTED", updatedOrder.Status);
        }

        [Fact]
        public async Task UpdateOrder_WhenPendingReview_UpdatesOrderAndKeepsPendingReviewStatus()
        {
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-UPDATE-01",
                CustomerId = customerId,
                ItemName = "Old cargo",
                Category = "FOOD",
                Quantity = 5,
                PackingType = "PALLET",
                TempCondition = "5",
                Status = "PENDING_REVIEW"
            });
            await _db.SaveChangesAsync();

            var request = new UpdateOrderRequest
            {
                ItemName = "Updated cargo",
                Quantity = 10
            };

            var result = await _service.UpdateOrderAsync(orderId, request, customerId);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Data);
            Assert.Equal("Updated cargo", result.Data.ItemName);
            Assert.Equal(10, result.Data.Quantity);
            Assert.Equal("PENDING_REVIEW", result.Data.Status);
        }

        [Fact]
        public async Task UpdateOrder_WithPackageLines_ReplacesLinesAndSynchronizesTotals()
        {
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-PACKAGE-UPDATE-01",
                CustomerId = customerId,
                ItemName = "Frozen seafood",
                Category = "MEAT_SEAFOOD",
                Quantity = 1,
                PackingType = "Carton Box",
                TempCondition = "-18",
                Status = "PENDING_REVIEW",
                OrderDimension = new OrderDimension
                {
                    OrderId = orderId,
                    ExpectedWeightKg = 5m,
                    ActualWeightKg = 5m,
                    ExpectedCbm = 0.02m,
                    ActualCbm = 0.02m,
                    TotalPackageQuantity = 1
                },
                OrderPackageLines =
                {
                    new OrderPackageLine
                    {
                        OrderPackageLineId = Guid.NewGuid(),
                        OrderId = orderId,
                        Label = "Old",
                        CapacityKg = 5m,
                        Quantity = 1
                    }
                }
            });
            await _db.SaveChangesAsync();

            var request = new UpdateOrderRequest
            {
                PackageLinesJson = """
                    [
                      { "label": "Small", "capacityKg": 5, "quantity": 2 },
                      { "label": "Large", "capacityKg": 10, "quantity": 1 }
                    ]
                    """
            };

            var result = await _service.UpdateOrderAsync(orderId, request, customerId);

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, result.Data!.Quantity);
            Assert.Equal(20m, result.Data.ExpectedWeightKg);
            Assert.Equal(0.0553m, result.Data.ExpectedCbm);

            var savedOrder = await _db.TransportOrders
                .Include(order => order.OrderDimension)
                .Include(order => order.OrderPackageLines)
                .SingleAsync(order => order.OrderId == orderId);
            Assert.Equal(3, savedOrder.Quantity);
            Assert.Equal(3, savedOrder.OrderDimension!.TotalPackageQuantity);
            Assert.Equal(2, savedOrder.OrderPackageLines.Count);
            Assert.Equal(20m, savedOrder.OrderDimension.ExpectedWeightKg);
            Assert.Equal("DENSITY_FACTOR", savedOrder.OrderDimension.CbmEstimationMethod);
        }

        [Fact]
        public async Task UpdateOrder_WhenPricingChanges_RebuildsDraftQuotation()
        {
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var routeId = Guid.NewGuid();
            var scheduleId = Guid.NewGuid();
            var locationId = Guid.NewGuid();
            var oldQuoteId = Guid.NewGuid();

            _db.RouteMasters.Add(new RouteMaster
            {
                RouteId = routeId,
                RouteCode = "PRICE-ROUTE",
                OriginCity = "HCM",
                DestCity = "Hanoi",
                TransitTime = "2 days",
                Status = "ACTIVE"
            });
            _db.RouteSchedules.Add(new RouteSchedule
            {
                ScheduleId = scheduleId,
                RouteId = routeId,
                ScheduleName = "Pricing schedule",
                DepartureDate = DateTime.UtcNow.AddDays(2),
                DepartureTime = new TimeSpan(8, 0, 0),
                CutOffTime = new TimeSpan(6, 0, 0),
                Status = "ACTIVE"
            });
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                CustomerId = customerId,
                Address = "Customer destination",
                Status = "ACTIVE"
            });
            _db.WeightTiers.Add(new WeightTier
            {
                Id = Guid.NewGuid(),
                RouteId = routeId,
                MinWeightKg = 0m,
                PricePerKg = 100m
            });
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-REPRICE-01",
                CustomerId = customerId,
                ItemName = "Frozen cargo",
                Category = "MEAT_SEAFOOD",
                Quantity = 1,
                PackingType = "Carton Box",
                TempCondition = "-18",
                Status = "PENDING_REVIEW",
                ScheduleId = scheduleId,
                DestLocation = locationId,
                OrderDimension = new OrderDimension
                {
                    OrderId = orderId,
                    ExpectedWeightKg = 20m,
                    ActualWeightKg = 20m,
                    ExpectedCbm = 0.02m,
                    ActualCbm = 0.02m,
                    LengthCm = 10m,
                    WidthCm = 10m,
                    HeightCm = 10m
                }
            });
            _db.Quotations.Add(new Quotation
            {
                QuoteId = oldQuoteId,
                OrderId = orderId,
                BaseFreight = 2_000m,
                VatAmount = 160m,
                FinalAmount = 2_160m,
                PricingSource = "AUTO",
                Status = "DRAFT"
            });
            await _db.SaveChangesAsync();

            var result = await _service.UpdateOrderAsync(
                orderId,
                new UpdateOrderRequest { ExpectedWeightKg = 50m },
                customerId);

            Assert.True(result.Success, result.Message);
            var quotation = await _db.Quotations.SingleAsync(item => item.OrderId == orderId && item.Status == "DRAFT");
            Assert.NotEqual(oldQuoteId, quotation.QuoteId);
            Assert.Equal(5_000m, quotation.BaseFreight);
            Assert.Equal(5_400m, quotation.FinalAmount);
        }

        [Fact]
        public async Task AdminUpdateOrder_WhenScheduleChanges_RepricesUsingNewRoute()
        {
            var orderId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var oldRouteId = Guid.NewGuid();
            var newRouteId = Guid.NewGuid();
            var oldScheduleId = Guid.NewGuid();
            var newScheduleId = Guid.NewGuid();
            var locationId = Guid.NewGuid();

            _db.RouteMasters.AddRange(
                new RouteMaster
                {
                    RouteId = oldRouteId,
                    RouteCode = "OLD",
                    OriginCity = "HCM",
                    DestCity = "Danang",
                    TransitTime = "1 day",
                    Status = "ACTIVE"
                },
                new RouteMaster
                {
                    RouteId = newRouteId,
                    RouteCode = "NEW",
                    OriginCity = "HCM",
                    DestCity = "Hanoi",
                    TransitTime = "2 days",
                    Status = "ACTIVE"
                });
            _db.RouteSchedules.AddRange(
                new RouteSchedule
                {
                    ScheduleId = oldScheduleId,
                    RouteId = oldRouteId,
                    ScheduleName = "Old schedule",
                    DepartureDate = DateTime.UtcNow.AddDays(2),
                    DepartureTime = new TimeSpan(8, 0, 0),
                    CutOffTime = new TimeSpan(6, 0, 0),
                    Status = "ACTIVE"
                },
                new RouteSchedule
                {
                    ScheduleId = newScheduleId,
                    RouteId = newRouteId,
                    ScheduleName = "New schedule",
                    DepartureDate = DateTime.UtcNow.AddDays(3),
                    DepartureTime = new TimeSpan(8, 0, 0),
                    CutOffTime = new TimeSpan(6, 0, 0),
                    Status = "ACTIVE"
                });
            _db.WeightTiers.AddRange(
                new WeightTier { Id = Guid.NewGuid(), RouteId = oldRouteId, MinWeightKg = 0m, PricePerKg = 100m },
                new WeightTier { Id = Guid.NewGuid(), RouteId = newRouteId, MinWeightKg = 0m, PricePerKg = 200m });
            _db.Locations.Add(new Location
            {
                LocationId = locationId,
                CustomerId = customerId,
                Address = "Customer destination",
                Status = "ACTIVE"
            });
            _db.TransportOrders.Add(new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "TRK-REPRICE-ROUTE-01",
                CustomerId = customerId,
                ItemName = "Frozen cargo",
                Category = "MEAT_SEAFOOD",
                Quantity = 1,
                PackingType = "Carton Box",
                TempCondition = "-18",
                Status = "PENDING_REVIEW",
                ScheduleId = oldScheduleId,
                DestLocation = locationId,
                OrderDimension = new OrderDimension
                {
                    OrderId = orderId,
                    ExpectedWeightKg = 50m,
                    ActualWeightKg = 50m,
                    ExpectedCbm = 0.02m,
                    ActualCbm = 0.02m
                }
            });
            _db.Quotations.Add(new Quotation
            {
                QuoteId = Guid.NewGuid(),
                OrderId = orderId,
                BaseFreight = 5_000m,
                VatAmount = 400m,
                FinalAmount = 5_400m,
                PricingSource = "AUTO",
                Status = "DRAFT"
            });
            await _db.SaveChangesAsync();

            var result = await _service.AdminUpdateOrderAsync(
                orderId,
                new UpdateOrderRequest { ScheduleId = newScheduleId },
                Guid.NewGuid());

            Assert.True(result.Success, result.Message);
            var quotation = await _db.Quotations.SingleAsync(item => item.OrderId == orderId && item.Status == "DRAFT");
            Assert.Equal(10_000m, quotation.BaseFreight);
            Assert.Equal(10_800m, quotation.FinalAmount);
        }

        #region Mock Classes

        private class MockLocationService : ILocationService
        {
            public Task<(decimal Latitude, decimal Longitude)> GetCoordinatesAsync(string addressText) => Task.FromResult((0m, 0m));
            public Task<decimal> GetDistanceKmAsync(decimal originLat, decimal originLon, decimal destinationLat, decimal destinationLon) => Task.FromResult(0m);
            public Task<ColdChainX.Application.DTOs.Dispatch.GoongDirectionsResult> GetDirectionsAsync(List<(decimal Lat, decimal Lon, string Address)> waypoints) => Task.FromResult(new ColdChainX.Application.DTOs.Dispatch.GoongDirectionsResult());
        }

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
            public Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId) => Task.FromResult("http://test.com/loadplan.pdf");
            public Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode) => Task.FromResult("http://test.com/invoice.pdf");
            public Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber) => Task.FromResult("http://test.com/appendix.pdf");
            public Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode) => Task.FromResult("http://test.com/returnslip.pdf");
            public Task<string> GenerateManifestPdfAsync(Guid tripId) => Task.FromResult("http://test.com/manifest.pdf");
            public Task<string> GenerateOutboundTicketPdfAsync(Guid tripId) => Task.FromResult("http://test.com/outbound-ticket.pdf");
            public Task<byte[]> GeneratePdfFromHtmlAsync(string htmlContent)
            {
                return Task.FromResult(new byte[] { 1, 2, 3 });
            }

            public Task<string> SavePdfFromUrlAsync(string url, string fileName, string folderPath = "pdf")
            {
                return Task.FromResult("http://example.com/" + fileName);
            }
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
