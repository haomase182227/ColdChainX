using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Application.DTOs.Outbound;
using ColdChainX.Application.DTOs.Inventory;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class OutboundOrderServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly InventoryService _inventoryService;
        private readonly MockWarehouseAttachmentRepository _attachmentRepository;
        private readonly ComplianceRulesEngine _complianceEngine;
        private readonly OutboundOrderService _service;

        public OutboundOrderServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _inventoryService = new InventoryService(_db, NullLogger<InventoryService>.Instance);
            _attachmentRepository = new MockWarehouseAttachmentRepository();
            _complianceEngine = new ComplianceRulesEngine();
            _service = new OutboundOrderService(
                _db,
                _inventoryService,
                _attachmentRepository,
                _complianceEngine,
                NullLogger<OutboundOrderService>.Instance
            );
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        [Fact]
        public async Task ShipOrder_FailedCompliance_BlocksShipment()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var order = new OutboundOrder
            {
                OutboundOrderId = orderId,
                OrderCode = "OB-COMP-01",
                CustomerId = customerId,
                ReceiverName = "John Doe",
                ReceiverPhone = "123456789",
                DestinationAddress = "123 Street",
                Status = OutboundOrderStatus.PICKED,
                CreatedAt = DateTime.UtcNow
            };
            _db.OutboundOrders.Add(order);

            // Add an item that belongs to SEAFOOD category
            var item = new OutboundOrderItem
            {
                OutboundOrderItemId = Guid.NewGuid(),
                OutboundOrderId = orderId,
                ItemCode = "ITEM-SF-1",
                ItemName = "Salmon Box",
                Unit = "BOX",
                Quantity = 10m
            };
            _db.OutboundOrderItems.Add(item);

            // Seed User and WarehouseReceipt to satisfy query filter on WarehouseReceiptItem
            var receiverId = Guid.NewGuid();
            var receiver = new User
            {
                UserId = receiverId,
                Username = "receiver_user_1",
                PasswordHash = "hash",
                FullName = "Receiver User 1",
                Status = "ACTIVE"
            };
            _db.Users.Add(receiver);

            var receiptId = Guid.NewGuid();
            var receipt = new WarehouseReceipt
            {
                ReceiptId = receiptId,
                ReceiptCode = "WR-SF-1",
                ReceiverId = receiverId,
                ReceiptType = "INBOUND",
                DelivererName = "Deliverer",
                OrderId = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid()
            };
            _db.WarehouseReceipts.Add(receipt);

            // Add reference receipt item of same item code under SEAFOOD category so engine knows its category
            var receiptItem = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ReceiptId = receiptId,
                ItemCode = "ITEM-SF-1",
                ItemName = "Salmon Box",
                Unit = "BOX",
                ActualQty = 10m,
                ProductCategory = ProductCategory.SEAFOOD,
                BatchNumber = "B-01",
                CountryOfOrigin = "Vietnam"
            };
            _db.WarehouseReceiptItems.Add(receiptItem);

            // Seed stock and allocation so ship logic has allocations
            var wh = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = "WH-OB-1", WarehouseName = "Main WH", WarehouseType = "COLD", Address = "Add", Status = "ACTIVE" };
            var zone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = wh.WarehouseId, ZoneCode = "COLD-1", ZoneName = "Cold Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 0 };
            var loc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = zone.ZoneId, LocationCode = "LOC-OB-1", Status = "ACTIVE", MaxCapacityPallets = 5, CurrentPallets = 0 };
            _db.Warehouses.Add(wh);
            _db.WarehouseZones.Add(zone);
            _db.WarehouseLocations.Add(loc);

            var batch = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-SF-1", BatchNumber = "B-01", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.Add(batch);

            var stock = new InventoryStock { StockId = Guid.NewGuid(), LocationId = loc.LocationId, CustomerId = customerId, ItemCode = "ITEM-SF-1", ItemName = "Salmon Box", Unit = "BOX", BatchId = batch.BatchId, QuantityOnHand = 100m, QuantityAllocated = 10m, PalletCount = 1, Status = "AVAILABLE", InboundDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            _db.InventoryStocks.Add(stock);

            var allocation = new InventoryAllocation { AllocationId = Guid.NewGuid(), ReferenceDocumentId = orderId, StockId = stock.StockId, AllocatedQuantity = 10m, Status = "ALLOCATED", CreatedAt = DateTime.UtcNow };
            _db.InventoryAllocations.Add(allocation);

            await _db.SaveChangesAsync();

            // Act: Attempt to ship. Seafood category requires QUARANTINE_CERTIFICATE (compliance document) but none is uploaded!
            var result = await _service.ShipOrderAsync(orderId, Guid.NewGuid());

            // Assert
            Assert.False(result.Success);
            Assert.Contains("compliance validation failure", result.Message);

            // Order status should remain PICKED
            var checkOrder = await _db.OutboundOrders.FindAsync(orderId);
            Assert.NotNull(checkOrder);
            Assert.Equal(OutboundOrderStatus.PICKED, checkOrder.Status);
        }

        [Fact]
        public async Task ShipOrder_SuccessfulCompliance_AllowsShipmentAndDeductsInventory()
        {
            // Arrange
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                CustomerId = customerId,
                CompanyName = "Test Customer Company",
                TaxCode = "TAX-12345",
                Status = "ACTIVE"
            };
            _db.Customers.Add(customer);

            var orderId = Guid.NewGuid();
            var order = new OutboundOrder
            {
                OutboundOrderId = orderId,
                OrderCode = "OB-COMP-02",
                CustomerId = customerId,
                ReceiverName = "John Doe",
                ReceiverPhone = "123456789",
                DestinationAddress = "123 Street",
                Status = OutboundOrderStatus.PICKED,
                CreatedAt = DateTime.UtcNow
            };
            _db.OutboundOrders.Add(order);

            var item = new OutboundOrderItem
            {
                OutboundOrderItemId = Guid.NewGuid(),
                OutboundOrderId = orderId,
                ItemCode = "ITEM-SF-2",
                ItemName = "Salmon Box",
                Unit = "BOX",
                Quantity = 10m
            };
            _db.OutboundOrderItems.Add(item);

            // Seed User and WarehouseReceipt to satisfy query filter on WarehouseReceiptItem
            var receiverId = Guid.NewGuid();
            var receiver = new User
            {
                UserId = receiverId,
                Username = "receiver_user_2",
                PasswordHash = "hash",
                FullName = "Receiver User 2",
                Status = "ACTIVE"
            };
            _db.Users.Add(receiver);

            var receiptId = Guid.NewGuid();
            var receipt = new WarehouseReceipt
            {
                ReceiptId = receiptId,
                ReceiptCode = "WR-SF-2",
                ReceiverId = receiverId,
                ReceiptType = "INBOUND",
                DelivererName = "Deliverer",
                OrderId = Guid.NewGuid(),
                WarehouseId = Guid.NewGuid()
            };
            _db.WarehouseReceipts.Add(receipt);

            var receiptItem = new WarehouseReceiptItem
            {
                ItemId = Guid.NewGuid(),
                ReceiptId = receiptId,
                ItemCode = "ITEM-SF-2",
                ItemName = "Salmon Box",
                Unit = "BOX",
                ActualQty = 10m,
                ProductCategory = ProductCategory.SEAFOOD,
                BatchNumber = "B-02",
                CountryOfOrigin = "Vietnam"
            };
            _db.WarehouseReceiptItems.Add(receiptItem);

            var wh = new Warehouse { WarehouseId = Guid.NewGuid(), WarehouseCode = "WH-OB-2", WarehouseName = "Main WH", WarehouseType = "COLD", Address = "Add", Status = "ACTIVE" };
            var zone = new WarehouseZone { ZoneId = Guid.NewGuid(), WarehouseId = wh.WarehouseId, ZoneCode = "COLD-2", ZoneName = "Cold Zone", ZoneType = "STORAGE", StorageType = "RACK", Status = "ACTIVE", MaxCapacityPallets = 10, CurrentPallets = 1 };
            var loc = new WarehouseLocation { LocationId = Guid.NewGuid(), ZoneId = zone.ZoneId, LocationCode = "LOC-OB-2", Status = "ACTIVE", MaxCapacityPallets = 5, CurrentPallets = 1 };
            _db.Warehouses.Add(wh);
            _db.WarehouseZones.Add(zone);
            _db.WarehouseLocations.Add(loc);

            var batch = new InventoryBatch { BatchId = Guid.NewGuid(), ItemCode = "ITEM-SF-2", BatchNumber = "B-02", ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = "ACTIVE", CreatedAt = DateTime.UtcNow };
            _db.InventoryBatches.Add(batch);

            var stock = new InventoryStock { StockId = Guid.NewGuid(), LocationId = loc.LocationId, CustomerId = customerId, ItemCode = "ITEM-SF-2", ItemName = "Salmon Box", Unit = "BOX", BatchId = batch.BatchId, QuantityOnHand = 100m, QuantityAllocated = 10m, PalletCount = 1, Status = "AVAILABLE", InboundDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, Location = loc };
            _db.InventoryStocks.Add(stock);

            var allocation = new InventoryAllocation { AllocationId = Guid.NewGuid(), ReferenceDocumentId = orderId, StockId = stock.StockId, AllocatedQuantity = 10m, Status = "ALLOCATED", CreatedAt = DateTime.UtcNow, Stock = stock };
            _db.InventoryAllocations.Add(allocation);

            // Add verified compliance documents so compliance validation succeeds
            await _attachmentRepository.AddAttachmentAsync(new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                OutboundOrderId = orderId,
                FileName = "issue_note.pdf",
                FilePath = "/uploads/issue_note.pdf",
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.WAREHOUSE_ISSUE_NOTE,
                Status = DocumentStatus.VERIFIED
            });
            await _attachmentRepository.AddAttachmentAsync(new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                OutboundOrderId = orderId,
                FileName = "condition.jpg",
                FilePath = "/uploads/condition.jpg",
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.GOODS_CONDITION_PHOTO,
                Status = DocumentStatus.VERIFIED
            });
            await _attachmentRepository.AddAttachmentAsync(new WarehouseEvidenceAttachment
            {
                AttachmentId = Guid.NewGuid(),
                OutboundOrderId = orderId,
                FileName = "temp.jpg",
                FilePath = "/uploads/temp.jpg",
                Category = AttachmentCategory.COMPLIANCE,
                SubCategory = AttachmentSubCategory.TEMPERATURE_PHOTO,
                Status = DocumentStatus.VERIFIED
            });

            await _db.SaveChangesAsync();

            // Act
            var result = await _service.ShipOrderAsync(orderId, Guid.NewGuid());

            // Assert
            Assert.True(result.Success, result.Message);

            var checkOrder = await _db.OutboundOrders.FindAsync(orderId);
            Assert.NotNull(checkOrder);
            Assert.Equal(OutboundOrderStatus.SHIPPED, checkOrder.Status);

            // Inventory stock should be reduced from 100m to 90m and allocated quantity reduced to 0
            var checkStock = await _db.InventoryStocks.FindAsync(stock.StockId);
            Assert.NotNull(checkStock);
            Assert.Equal(90m, checkStock.QuantityOnHand);
            Assert.Equal(0m, checkStock.QuantityAllocated);
        }
    }
}
