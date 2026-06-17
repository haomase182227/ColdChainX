using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using ColdChainX.Application.DTOs.Inventory;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Application.Services;
using Xunit;

namespace ColdChainX.UnitTests
{
    public class InventoryAdjustmentTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly InventoryService _inventoryService;

        private Guid _operatorUserId;
        private Guid _managerUserId;

        public InventoryAdjustmentTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new ApplicationDbContext(options);
            _inventoryService = new InventoryService(_db, NullLogger<InventoryService>.Instance);

            _operatorUserId = Guid.NewGuid();
            _managerUserId = Guid.NewGuid();
            SeedUsers();
        }

        public void Dispose()
        {
            _db.Dispose();
        }

        private void SeedUsers()
        {
            var operatorRole = new Role { RoleId = Guid.NewGuid(), RoleName = "Operator" };
            var managerRole = new Role { RoleId = Guid.NewGuid(), RoleName = "Manager" };

            _db.Roles.AddRange(operatorRole, managerRole);

            var opUser = new User
            {
                UserId = _operatorUserId,
                Username = "op01",
                PasswordHash = "hash",
                FullName = "Operator User",
                RoleId = operatorRole.RoleId,
                Status = "ACTIVE",
                Role = operatorRole
            };

            var mngrUser = new User
            {
                UserId = _managerUserId,
                Username = "mngr01",
                PasswordHash = "hash",
                FullName = "Manager User",
                RoleId = managerRole.RoleId,
                Status = "ACTIVE",
                Role = managerRole
            };

            _db.Users.AddRange(opUser, mngrUser);
            _db.SaveChanges();
        }

        private async Task<(Guid locId, Guid stockId, Guid batchId)> SeedStockAsync(decimal qty = 100m, int pallets = 2, int maxCapacity = 10)
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                CustomerId = customerId,
                CompanyName = "Test Customer",
                TaxCode = "TX-112",
                Email = "test@customer.com",
                Status = "ACTIVE"
            };
            _db.Customers.Add(customer);

            var warehouseId = Guid.NewGuid();
            var warehouse = new Warehouse
            {
                WarehouseId = warehouseId,
                WarehouseCode = "WH-TEST-01",
                WarehouseName = "Test Warehouse",
                WarehouseType = "STORAGE",
                Address = "Test Address",
                Status = "ACTIVE"
            };
            _db.Warehouses.Add(warehouse);

            var zoneId = Guid.NewGuid();
            var zone = new WarehouseZone
            {
                ZoneId = zoneId,
                WarehouseId = warehouseId,
                ZoneCode = "Z-TEST-01",
                ZoneName = "Test Zone",
                ZoneType = "STORAGE",
                StorageType = "RACK",
                MaxCapacityPallets = maxCapacity * 2,
                CurrentPallets = pallets,
                Status = "ACTIVE"
            };
            _db.WarehouseZones.Add(zone);

            var locId = Guid.NewGuid();
            var location = new WarehouseLocation
            {
                LocationId = locId,
                ZoneId = zoneId,
                LocationCode = "LOC-TEST-01",
                MaxCapacityPallets = maxCapacity,
                CurrentPallets = pallets,
                Status = "ACTIVE"
            };
            _db.WarehouseLocations.Add(location);

            var batchId = Guid.NewGuid();
            var batch = new InventoryBatch
            {
                BatchId = batchId,
                ItemCode = "ITEM-TEST-01",
                BatchNumber = "B-TEST-001",
                ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
                Status = "ACTIVE",
                CreatedAt = DateTime.UtcNow
            };
            _db.InventoryBatches.Add(batch);

            var stockId = Guid.NewGuid();
            var stock = new InventoryStock
            {
                StockId = stockId,
                LocationId = locId,
                CustomerId = customerId,
                ItemCode = "ITEM-TEST-01",
                ItemName = "Test Item",
                Unit = "PCS",
                BatchId = batchId,
                QuantityOnHand = qty,
                QuantityAllocated = 0,
                InboundDate = DateTime.UtcNow,
                Status = "AVAILABLE",
                PalletCount = pallets,
                Location = location,
                Batch = batch,
                Customer = customer
            };
            _db.InventoryStocks.Add(stock);
            await _db.SaveChangesAsync();

            return (locId, stockId, batchId);
        }

        [Fact]
        public async Task CreateAdjustmentRequest_Operator_CreatesPendingApproval()
        {
            // Arrange
            var (_, stockId, _) = await SeedStockAsync(100m, 2);
            var request = new InventoryAdjustmentRequest
            {
                StockId = stockId,
                AdjustmentType = InventoryAdjustmentType.DAMAGED,
                IsAbsoluteCount = false,
                Quantity = -10m,
                Pallets = -1,
                Reason = "Damaged during handling"
            };

            // Act
            var result = await _inventoryService.CreateAdjustmentRequestAsync(request, _operatorUserId);

            // Assert
            Assert.True(result.Success);
            Assert.NotEqual(Guid.Empty, result.Data);

            // Verify db state: stock is NOT updated, adjustment is PENDING_APPROVAL
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.Equal(100m, stock.QuantityOnHand);
            Assert.Equal(2, stock.PalletCount);

            var adj = await _db.InventoryAdjustments.FindAsync(result.Data);
            Assert.NotNull(adj);
            Assert.Equal(InventoryAdjustmentStatus.PENDING_APPROVAL, adj.Status);
            Assert.Null(adj.MovementId);
            Assert.Equal(-10m, adj.QuantityChanged);
            Assert.Equal(-1, adj.PalletsChanged);
            Assert.Equal(2, adj.PalletsBefore);
            Assert.Equal(1, adj.PalletsAfter);
        }

        [Fact]
        public async Task ApproveAdjustment_Manager_ExecutesStockAdjustmentAndLogsMovement()
        {
            // Arrange
            var (locId, stockId, _) = await SeedStockAsync(100m, 2);
            var request = new InventoryAdjustmentRequest
            {
                StockId = stockId,
                AdjustmentType = InventoryAdjustmentType.DAMAGED,
                IsAbsoluteCount = false,
                Quantity = -10m,
                Pallets = -1,
                Reason = "Damaged during handling"
            };
            var createResult = await _inventoryService.CreateAdjustmentRequestAsync(request, _operatorUserId);
            Assert.True(createResult.Success);
            var adjId = createResult.Data;

            // Act
            var approveResult = await _inventoryService.ApproveAdjustmentAsync(adjId, _managerUserId);

            // Assert
            Assert.True(approveResult.Success);

            // Verify stock updated
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.Equal(90m, stock.QuantityOnHand);
            Assert.Equal(1, stock.PalletCount);

            // Verify location capacity updated
            var loc = await _db.WarehouseLocations.FindAsync(locId);
            Assert.Equal(1, loc.CurrentPallets);

            // Verify adjustment status APPROVED
            var adj = await _db.InventoryAdjustments.FindAsync(adjId);
            Assert.Equal(InventoryAdjustmentStatus.APPROVED, adj.Status);
            Assert.NotNull(adj.MovementId);
            Assert.Equal(_managerUserId, adj.ApprovedBy);
            Assert.NotNull(adj.ApprovedAt);

            // Verify movement logged
            var movement = await _db.InventoryMovements.FindAsync(adj.MovementId);
            Assert.NotNull(movement);
            Assert.Equal("ADJUSTMENT", movement.MovementType);
            Assert.Equal(10m, movement.Quantity);
            Assert.Equal(locId, movement.FromLocationId);
            Assert.Null(movement.ToLocationId);
        }

        [Fact]
        public async Task RejectAdjustment_Manager_UpdatesStatusToRejected()
        {
            // Arrange
            var (_, stockId, _) = await SeedStockAsync(100m, 2);
            var request = new InventoryAdjustmentRequest
            {
                StockId = stockId,
                AdjustmentType = InventoryAdjustmentType.LOST,
                IsAbsoluteCount = false,
                Quantity = -5m,
                Pallets = 0,
                Reason = "Lost in warehouse"
            };
            var createResult = await _inventoryService.CreateAdjustmentRequestAsync(request, _operatorUserId);
            var adjId = createResult.Data;

            // Act
            var rejectResult = await _inventoryService.RejectAdjustmentAsync(adjId, "Invalid count reason", _managerUserId);

            // Assert
            Assert.True(rejectResult.Success);

            // Stock unchanged
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.Equal(100m, stock.QuantityOnHand);
            Assert.Equal(2, stock.PalletCount);

            // Status REJECTED
            var adj = await _db.InventoryAdjustments.FindAsync(adjId);
            Assert.Equal(InventoryAdjustmentStatus.REJECTED, adj.Status);
            Assert.Equal("Invalid count reason", adj.RejectionReason);
            Assert.Equal(_managerUserId, adj.ApprovedBy);
            Assert.NotNull(adj.ApprovedAt);
            Assert.Null(adj.MovementId);
        }

        [Fact]
        public async Task AdjustStock_AutoApproved_ExecutesImmediately()
        {
            // Arrange
            var (locId, stockId, _) = await SeedStockAsync(100m, 2);
            var request = new InventoryAdjustmentRequest
            {
                StockId = stockId,
                AdjustmentType = InventoryAdjustmentType.CYCLE_COUNT,
                IsAbsoluteCount = false,
                Quantity = 15m,
                Pallets = 1,
                Reason = "Cycle Count variance"
            };

            // Act
            var result = await _inventoryService.AdjustStockAsync(request, _managerUserId, autoApprove: true);

            // Assert
            Assert.True(result.Success);

            // Verify stock updated immediately
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.Equal(115m, stock.QuantityOnHand);
            Assert.Equal(3, stock.PalletCount);

            // Verify location capacity updated
            var loc = await _db.WarehouseLocations.FindAsync(locId);
            Assert.Equal(3, loc.CurrentPallets);

            // Verify adjustment is logged as approved
            var adj = await _db.InventoryAdjustments
                .Where(a => a.StockId == stockId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();
            Assert.NotNull(adj);
            Assert.Equal(InventoryAdjustmentStatus.APPROVED, adj.Status);
            Assert.Equal(15m, adj.QuantityChanged);
            Assert.Equal(1, adj.PalletsChanged);
            Assert.NotNull(adj.MovementId);
        }

        [Fact]
        public async Task ApproveAdjustment_ValidatesCapacityAndFailsIfExceeded()
        {
            // Arrange: Max capacity is 3 pallets, current is 2. Add 1 pallet -> 3 pallets total (fits capacity initially)
            var (locId, stockId, _) = await SeedStockAsync(100m, 2, maxCapacity: 3);
            var request = new InventoryAdjustmentRequest
            {
                StockId = stockId,
                AdjustmentType = InventoryAdjustmentType.FOUND,
                IsAbsoluteCount = false,
                Quantity = 50m,
                Pallets = 1,
                Reason = "Found stock"
            };
            var createResult = await _inventoryService.CreateAdjustmentRequestAsync(request, _operatorUserId);
            Assert.True(createResult.Success);
            var adjId = createResult.Data;

            // Before approval, modify the location's CurrentPallets to be 3 (exceeding capacity if 1 is added)
            var location = await _db.WarehouseLocations.FindAsync(locId);
            location.CurrentPallets = 3;
            await _db.SaveChangesAsync();

            // Act
            var approveResult = await _inventoryService.ApproveAdjustmentAsync(adjId, _managerUserId);

            // Assert
            Assert.False(approveResult.Success);
            Assert.Contains("Capacity exceeded", approveResult.Message);

            // Verify stock is untouched
            var stock = await _db.InventoryStocks.FindAsync(stockId);
            Assert.Equal(100m, stock.QuantityOnHand);
            Assert.Equal(2, stock.PalletCount);
        }
    }
}
