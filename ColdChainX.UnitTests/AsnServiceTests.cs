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
            _service = new AsnService(_db);
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task GetInboundSchedules_WithoutFilters_ReturnsAllScheduledASNs()
        {
            // Arrange
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

            // Act
            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                pageNumber: 1,
                pageSize: 10);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Single(result.Data.Data);
            Assert.Equal("ASN-01", result.Data.Data.First().AsnCode);
            Assert.Equal("Company A", result.Data.Data.First().CustomerName);
        }

        [Fact]
        public async Task GetInboundSchedules_WithCustomerFilter_RestrictsResults()
        {
            // Arrange
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

            // Act
            var result = await _service.GetInboundSchedulesAsync(
                customerId: customerId1,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                pageNumber: 1,
                pageSize: 10);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-01", result.Data.Data.First().AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithStatusFilter_RestrictsResults()
        {
            // Arrange
            var order = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-01", ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.Add(order);

            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = order.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = order.OrderId, RequestedDropoffTime = DateTime.UtcNow.AddDays(2), QrCodeValue = "QR2", Status = "ARRIVED" };
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: "ARRIVED",
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: null,
                pageNumber: 1,
                pageSize: 10);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-02", result.Data.Data.First().AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithDateRangeFilter_FiltersCorrectly()
        {
            // Arrange
            var order = new TransportOrder { OrderId = Guid.NewGuid(), TrackingCode = "TRK-01", ItemName = "Item 1", Category = "FOOD", PackingType = "BOX", TempCondition = "COLD", Status = "ASSIGNED" };
            _db.TransportOrders.Add(order);

            var baseTime = new DateTime(2026, 6, 20, 12, 0, 0);
            var asn1 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-01", OrderId = order.OrderId, RequestedDropoffTime = baseTime.AddDays(1), QrCodeValue = "QR1", Status = "SCHEDULED" };
            var asn2 = new InboundAsn { AsnId = Guid.NewGuid(), AsnCode = "ASN-02", OrderId = order.OrderId, RequestedDropoffTime = baseTime.AddDays(3), QrCodeValue = "QR2", Status = "SCHEDULED" };
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: baseTime.AddDays(2),
                dateTo: baseTime.AddDays(4),
                searchQuery: null,
                warehouseId: null,
                pageNumber: 1,
                pageSize: 10);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            Assert.Equal("ASN-02", result.Data.Data.First().AsnCode);
        }

        [Fact]
        public async Task GetInboundSchedules_WithWarehouseFilter_AddressMatching_ReturnsMatchedASNs()
        {
            // Arrange
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
            _db.InboundAsns.AddRange(asn1, asn2);
            await _db.SaveChangesAsync();

            // Act
            var result = await _service.GetInboundSchedulesAsync(
                customerId: null,
                status: null,
                dateFrom: null,
                dateTo: null,
                searchQuery: null,
                warehouseId: warehouseId,
                pageNumber: 1,
                pageSize: 10);

            // Assert
            Assert.True(result.Success);
            Assert.Equal(1, result.Data.TotalRecords);
            var item = result.Data.Data.First();
            Assert.Equal("ASN-01", item.AsnCode);
            Assert.Equal(warehouseId, item.WarehouseId);
            Assert.Equal("HCM Central Warehouse", item.WarehouseName);
        }
    }
}
