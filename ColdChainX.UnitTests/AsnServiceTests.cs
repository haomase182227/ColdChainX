using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Services;
using ColdChainX.Application.DTOs.Asns;
using ColdChainX.API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class AsnServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly AsnService _service;

        public AsnServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _service = new AsnService(_db, null!, null!);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task GetInboundSchedules_WithoutFilters_ReturnsAllScheduledASNs()
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer { CustomerId = customerId, CompanyName = "Company A", TaxCode = "TAX-A", Email = "a@a.com" };
            _db.Customers.Add(customer);

            var location = new Location { LocationId = Guid.NewGuid(), Address = "Location A", Status = "ACTIVE" };
            _db.Locations.Add(location);

            var order = new TransportOrder
            {
                OrderId = Guid.NewGuid(),
                TrackingCode = "TRK-01",
                CustomerId = customerId,
                ItemName = "Item 1",
                PackingType = "BOX",
                Category = "FOOD",
                Quantity = 10,
                TempCondition = "COLD",
                Status = "ASSIGNED",
                DestLocationNavigation = location
            };
            _db.TransportOrders.Add(order);

            var asn = new InboundAsn
            {
                AsnId = Guid.NewGuid(),
                AsnCode = "ASN-01",
                OrderId = order.OrderId,
                RequestedDropoffTime = DateTime.UtcNow.AddDays(1),
                QrCodeValue = "QR",
                Status = "SCHEDULED",
                CreatedAt = DateTime.UtcNow
            };
            _db.InboundAsns.Add(asn);
            await _db.SaveChangesAsync();

            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                orderId: null,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Single(result.Data.Data);
            Assert.Equal("ASN-01", result.Data.Data.First().AsnCode);
            Assert.Equal("Company A", result.Data.Data.First().CustomerName);
        }

        [Fact]
        public async Task GetInboundSchedules_WithCustomerFilter_RestrictsResults()
        {
            var customerId1 = Guid.NewGuid();
            var customerId2 = Guid.NewGuid();
            var customer1 = new Customer { CustomerId = customerId1, CompanyName = "Company A", TaxCode = "TAX-A", Email = "a@a.com" };
            var customer2 = new Customer { CustomerId = customerId2, CompanyName = "Company B", TaxCode = "TAX-B", Email = "b@b.com" };
            _db.Customers.AddRange(customer1, customer2);

            var order1 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-01", CustomerId = customerId1, ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            var order2 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-02", CustomerId = customerId2, ItemName = "Item 2", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.AddRange(order1, order2);

            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = order1.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = order2.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(2), QrCodeValue = "QR2", Status = "SCHEDULED" };
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            var result = await _service.GetInboundSchedulesAsync(
                customerId: customerId1,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                orderId: null,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-01", result.Data.Data.First().AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithStatusFilter_RestrictsResults()
        {
            var order = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-01", ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.Add(order);

            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = order.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = order.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(2), QrCodeValue = "QR2", Status = "ARRIVED" };
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: "ARRIVED",
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                orderId: null,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-02", result.Data.Data.First().AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithDateRangeFilter_FiltersCorrectly()
        {
            var order = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-01", ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.Add(order);

            var baseTime = new DateTime(2026, 6, 20, 12, 0, 0);
            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = order.OrderId, RequestedDropoffTime = baseTime.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = order.OrderId, RequestedDropoffTime = baseTime.AddDays(3), QrCodeValue = "QR2", Status = "SCHEDULED" };
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: baseTime.AddDays(2),
                dateTo: baseTime.AddDays(4),
                searchQuery: null,
                warehouseId: null,
                orderId: null,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-02", result.Data.Data.First().AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithSameDayDateRange_IncludesItemsAfterMidnight()
        {
            var order = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-DAY", ItemName = "Item", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.Add(order);
            _db.InboundAsns.Add(new InboundAsn
            {
                AsnId = Guid.NewGuid(),
                AsnCode = "ASN-DAY",
                OrderId = order.OrderId,
                RequestedDropoffTime = new DateTime(2026, 8, 13, 8, 0, 0),
                QrCodeValue = "QR-DAY",
                Status = "SCHEDULED"
            });
            await _db.SaveChangesAsync();

            var selectedDate = new DateTime(2026, 8, 13);
            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: selectedDate,
                dateTo: selectedDate,
                searchQuery: null,
                warehouseId: null,
                orderId: null,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal("ASN-DAY", Assert.Single(result.Data!.Data).AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithWarehouseFilter_DirectAsnWarehouseId_ReturnsMatchedASNs()
        {
            var warehouseId = Guid.NewGuid();
            var warehouse = new Warehouse
            {
                WarehouseId = warehouseId,
                WarehouseCode = "WH-HCM",
                WarehouseName = "HCM Central Warehouse",
                WarehouseType = "STORAGE",
                Address = "HCM City",
                Status = "ACTIVE"
            };
            _db.Warehouses.Add(warehouse);

            var location1 = new Location { LocationId = Guid.NewGuid(), Address = "District 1, HCM City", Status = "ACTIVE" };
            var location2 = new Location { LocationId = Guid.NewGuid(), Address = "Ha Noi City", Status = "ACTIVE" };
            _db.Locations.AddRange(location1, location2);

            var order1 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-01", ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED", DestLocationNavigation = location1 };
            var order2 = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-02", ItemName = "Item 2", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED", DestLocationNavigation = location2 };
            _db.TransportOrders.AddRange(order1, order2);

            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = order1.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = order2.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(2), QrCodeValue = "QR2", Status = "SCHEDULED" };
            asn1.WarehouseId = warehouseId;
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: warehouseId,
                orderId: null,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            var item = result.Data.Data.First();
            Assert.Equal("ASN-01", item.AsnCode);
            Assert.Equal(warehouseId, item.WarehouseId);
            Assert.Equal("HCM Central Warehouse", item.WarehouseName);
        }

        [Fact]
        public async Task GetSchedule_WithWarehouseFilter_ReturnsOnlyDirectWarehouseMatches()
        {
            var targetWarehouseId = Guid.NewGuid();
            var otherWarehouseId = Guid.NewGuid();
            var targetDate = new DateOnly(2026, 8, 13);
            var targetOrder = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-TARGET", ItemName = "Target", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "CONTRACT_SIGNED" };
            var otherOrder = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-OTHER", ItemName = "Other", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "CONTRACT_SIGNED" };
            _db.TransportOrders.AddRange(targetOrder, otherOrder);
            _db.InboundAsns.AddRange(
                new InboundAsn
                {
                    AsnId = Guid.NewGuid(),
                    AsnCode = "ASN-TARGET",
                    OrderId = targetOrder.OrderId,
                    RequestedDropoffTime = new DateTime(2026, 8, 13, 8, 0, 0),
                    QrCodeValue = "QR-TARGET",
                    Status = "SCHEDULED",
                    WarehouseId = targetWarehouseId
                },
                new InboundAsn
                {
                    AsnId = Guid.NewGuid(),
                    AsnCode = "ASN-OTHER",
                    OrderId = otherOrder.OrderId,
                    RequestedDropoffTime = new DateTime(2026, 8, 13, 8, 0, 0),
                    QrCodeValue = "QR-OTHER",
                    Status = "SCHEDULED",
                    WarehouseId = otherWarehouseId
                });
            await _db.SaveChangesAsync();

            var result = await _service.GetScheduleAsync(targetDate, status: null, warehouseId: targetWarehouseId);

            Assert.True(result.Success);
            Assert.Equal("ASN-TARGET", Assert.Single(result.Data!).AsnCode);
        }

        [Theory]
        [InlineData("ASN-20260812072927-4076")]
        [InlineData("Sữa chua uống men sống")]
        public async Task GetInboundSchedules_RuntimeFilters_FindTargetByCodeOrItem(string searchQuery)
        {
            var warehouseId = Guid.Parse("b05ec512-9b0a-4ad3-a0fc-819b375a957e");
            var orderId = Guid.Parse("5f10efc6-a539-47ab-a765-b1428f827bcc");
            var order = new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = "PROSHIP-2026980218",
                ItemName = "Sữa chua uống men sống",
                Category = "FOOD",
                PackingType = "BOX",
                TempCondition = "COLD",
                Status = "CONTRACT_SIGNED"
            };
            _db.TransportOrders.Add(order);
            _db.InboundAsns.Add(new InboundAsn
            {
                AsnId = Guid.Parse("76ac17c8-9c8d-49d7-931b-4292d9f921ee"),
                AsnCode = "ASN-20260812072927-4076",
                OrderId = orderId,
                RequestedDropoffTime = new DateTime(2026, 8, 13, 8, 0, 0),
                QrCodeValue = "QR-TARGET",
                Status = "SCHEDULED",
                WarehouseId = warehouseId
            });
            await _db.SaveChangesAsync();

            var selectedDate = new DateTime(2026, 8, 13);
            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: selectedDate,
                dateTo: selectedDate,
                searchQuery: searchQuery,
                warehouseId: warehouseId,
                orderId: null,
                pageNumber: 1,
                pageSize: 50);

            var target = Assert.Single(result.Data!.Data);
            Assert.Equal("ASN-20260812072927-4076", target.AsnCode);
            Assert.Equal("Sữa chua uống men sống", target.ItemName);
            Assert.Equal(warehouseId, target.WarehouseId);
        }

        [Fact]
        public async Task GetInboundSchedules_WithOrderIdFilter_ReturnsOnlyMatchedOrderASN()
        {
            var orderId1 = Guid.NewGuid();
            var orderId2 = Guid.NewGuid();
            var order1 = new TransportOrder { OrderId = orderId1, TrackingCode = "TRK-01", ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            var order2 = new TransportOrder { OrderId = orderId2, TrackingCode = "TRK-02", ItemName = "Item 2", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.AddRange(order1, order2);

            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = orderId1, RequestedDropoffTime = DateTime.UtcNow.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = orderId2, RequestedDropoffTime = DateTime.UtcNow.AddDays(2), QrCodeValue = "QR2", Status = "SCHEDULED" };
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                orderId: orderId1,
                pageNumber: 1,
                pageSize: 10);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-01", result.Data.Data.First().AsnCode);
            Assert.Equal(orderId1, result.Data.Data.First().OrderId);
        }

        [Fact]
        public async Task CreateAsn_WithoutExistingAsn_CreatesOneScheduledAsn()
        {
            var (customerId, orderId, warehouseId) = await SeedCreateAsnPrerequisitesAsync();

            var result = await _service.CreateAsnAsync(new CreateAsnRequest
            {
                OrderId = orderId,
                RequestedDropoffTime = DateTime.UtcNow.AddDays(1),
                Phone = "0900000000",
                WarehouseId = warehouseId
            }, customerId);

            Assert.True(result.Success);
            Assert.Equal(200, result.StatusCode);
            Assert.NotNull(result.Data);
            Assert.Equal(orderId, result.Data.OrderId);
            Assert.Equal(warehouseId, result.Data.WarehouseId);
            Assert.Equal(customerId, result.Data.CustomerId);
            Assert.Equal("SCHEDULED", result.Data.Status);
            Assert.Single(await _db.InboundAsns.Where(a => a.OrderId == orderId).ToListAsync());

            var customerAsns = await _service.GetAsnsByCustomerIdAsync(customerId);
            var customerAsn = Assert.Single(customerAsns.Data!);
            Assert.Equal(orderId, customerAsn.OrderId);
            Assert.Equal(warehouseId, customerAsn.WarehouseId);
            Assert.Equal(customerId, customerAsn.CustomerId);
        }

        [Fact]
        public async Task CreateAsn_WithExistingAsnForOrder_ReturnsConflictWithoutInserting()
        {
            var (customerId, orderId, warehouseId) = await SeedCreateAsnPrerequisitesAsync();
            _db.InboundAsns.Add(new InboundAsn
            {
                AsnId = Guid.NewGuid(),
                AsnCode = "ASN-EXISTING",
                OrderId = orderId,
                RequestedDropoffTime = DateTime.UtcNow.AddDays(1),
                QrCodeValue = "QR-EXISTING",
                Status = "RECEIVED",
                WarehouseId = warehouseId,
                CustomerId = customerId,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            var result = await _service.CreateAsnAsync(new CreateAsnRequest
            {
                OrderId = orderId,
                RequestedDropoffTime = DateTime.UtcNow.AddDays(2),
                Phone = "0900000001",
                WarehouseId = warehouseId
            }, customerId);

            Assert.False(result.Success);
            Assert.Equal(409, result.StatusCode);
            Assert.Equal("Đơn hàng này đã có lịch giao kho.", result.Message);
            Assert.Single(await _db.InboundAsns.Where(a => a.OrderId == orderId).ToListAsync());
        }

        [Fact]
        public async Task CreateAsnController_WithExistingAsnForOrder_ReturnsHttp409()
        {
            var (customerId, orderId, warehouseId) = await SeedCreateAsnPrerequisitesAsync();
            _db.InboundAsns.Add(new InboundAsn
            {
                AsnId = Guid.NewGuid(),
                AsnCode = "ASN-EXISTING-CONTROLLER",
                OrderId = orderId,
                RequestedDropoffTime = DateTime.UtcNow.AddDays(1),
                QrCodeValue = "QR-EXISTING-CONTROLLER",
                Status = "SCHEDULED",
                WarehouseId = warehouseId,
                CustomerId = customerId
            });
            await _db.SaveChangesAsync();

            var controller = new AsnController(_service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            new[]
                            {
                                new System.Security.Claims.Claim("CustomerId", customerId.ToString()),
                                new System.Security.Claims.Claim(ClaimTypes.Role, "Customer")
                            },
                            "TestAuth"))
                    }
                }
            };

            var actionResult = await controller.CreateAsn(new CreateAsnRequest
            {
                OrderId = orderId,
                RequestedDropoffTime = DateTime.UtcNow.AddDays(2),
                WarehouseId = warehouseId
            });

            var conflict = Assert.IsType<ObjectResult>(actionResult);
            Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
            Assert.Single(await _db.InboundAsns.Where(a => a.OrderId == orderId).ToListAsync());
        }

        private async Task<(Guid CustomerId, Guid OrderId, Guid WarehouseId)> SeedCreateAsnPrerequisitesAsync()
        {
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var warehouseId = Guid.NewGuid();
            var routeId = Guid.NewGuid();
            var scheduleId = Guid.NewGuid();

            var customer = new Customer
            {
                CustomerId = customerId,
                CompanyName = "Create ASN Customer",
                TaxCode = $"TAX-{customerId:N}",
                Email = $"{customerId:N}@example.com"
            };
            var warehouse = new Warehouse
            {
                WarehouseId = warehouseId,
                WarehouseCode = $"WH-{warehouseId:N}"[..20],
                WarehouseName = "Create ASN Warehouse",
                WarehouseType = "STORAGE",
                Status = "ACTIVE"
            };
            var route = new RouteMaster
            {
                RouteId = routeId,
                RouteCode = $"RT-{routeId:N}"[..20],
                OriginCity = "Ho Chi Minh City",
                DestCity = "Ha Noi",
                TransitTime = "24h",
                Status = "ACTIVE"
            };
            var schedule = new RouteSchedule
            {
                ScheduleId = scheduleId,
                RouteId = routeId,
                ScheduleName = "Create ASN schedule",
                DepartureDate = DateTime.UtcNow.AddDays(2),
                DepartureTime = new TimeSpan(8, 0, 0),
                CutOffTime = new TimeSpan(6, 0, 0),
                Status = "ACTIVE",
                Route = route
            };
            route.RouteSchedules.Add(schedule);

            var order = new TransportOrder
            {
                OrderId = orderId,
                TrackingCode = $"TRK-{orderId:N}"[..24],
                CustomerId = customerId,
                ItemName = "Create ASN item",
                Category = "FOOD",
                Quantity = 1,
                PackingType = "BOX",
                TempCondition = "COLD",
                Status = "CONTRACT_SIGNED",
                ScheduleId = scheduleId,
                Schedule = schedule
            };

            _db.Customers.Add(customer);
            _db.Warehouses.Add(warehouse);
            _db.RouteMasters.Add(route);
            _db.RouteSchedules.Add(schedule);
            _db.TransportOrders.Add(order);
            await _db.SaveChangesAsync();

            return (customerId, orderId, warehouseId);
        }
    }
}
