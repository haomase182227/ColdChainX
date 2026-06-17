using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using ColdChainX.Application.DTOs.WarehouseReceipt;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class WarehouseReceiptService : IWarehouseReceiptService
    {
        private const string InWarehouseStatus = "IN_WAREHOUSE";
        private const decimal MinCharge = 300000m;
        private const decimal LastMileFreeKm = 10m;
        private const decimal LastMileUnitPrice = 15000m;
        private const decimal VatRate = 0.08m;
        private const string DefaultOriginCity = "Ho Chi Minh";
        private const decimal HubLat = 10.732537m;
        private const decimal HubLon = 106.732148m;

        private readonly IApplicationDbContext _db;
        private readonly IWarehouseReceiptRepository _receiptRepository;
        private readonly ILocationService _locationService;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;
        private readonly IWarehouseAttachmentRepository _attachmentRepository;
        private readonly ComplianceRulesEngine _complianceEngine;

        public WarehouseReceiptService(
            IApplicationDbContext db,
            IWarehouseReceiptRepository receiptRepository,
            ILocationService locationService,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            IWarehouseAttachmentRepository attachmentRepository,
            ComplianceRulesEngine complianceEngine)
        {
            _db = db;
            _receiptRepository = receiptRepository;
            _locationService = locationService;
            _pdfService = pdfService;
            _environment = environment;
            _attachmentRepository = attachmentRepository;
            _complianceEngine = complianceEngine;
        }

        public async Task<ApiResponse<WarehouseReceiptResponse>> ProcessInboundQCAsync(
            Guid orderId,
            Guid warehouseId,
            InboundQCRequest request,
            Guid receiverId)
        {
            var order = await _db.TransportOrders
                .Include(o => o.Customer)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return ApiResponse<WarehouseReceiptResponse>.Failure("Order not found");

            if (order.Status == InWarehouseStatus || order.Status == "DELIVERED" || order.Status == "CANCELLED")
                return ApiResponse<WarehouseReceiptResponse>.Failure($"Order is already in '{order.Status}' status");

            var warehouse = await _db.Warehouses.FindAsync(warehouseId);
            if (warehouse == null)
                return ApiResponse<WarehouseReceiptResponse>.Failure("Warehouse not found");

            var receiverExists = await _db.Users.AnyAsync(u => u.UserId == receiverId);
            if (!receiverExists)
                return ApiResponse<WarehouseReceiptResponse>.Failure("Receiver (clerk) user not found");

            // Perform core temperature QC check
            bool tempPassed = true;
            string qcNote = "";
            if (!string.IsNullOrWhiteSpace(order.TempCondition))
            {
                if (double.TryParse(order.TempCondition, NumberStyles.Any, CultureInfo.InvariantCulture, out double targetTemp))
                {
                    if (targetTemp < 0)
                    {
                        if ((double)request.RecordedTemperature > targetTemp + 3.0)
                        {
                            tempPassed = false;
                            qcNote = $"[QC FAILED] Core temperature {request.RecordedTemperature}°C exceeds frozen threshold of {targetTemp}°C";
                        }
                    }
                    else
                    {
                        if ((double)request.RecordedTemperature > 8.0 || (double)request.RecordedTemperature < 0.0)
                        {
                            tempPassed = false;
                            qcNote = $"[QC FAILED] Core temperature {request.RecordedTemperature}°C is outside chill range (2-8°C)";
                        }
                    }
                }
            }

            // Perform Odor Matrix validation (Warning only)
            string? warningMessage = null;
            var activeReceipts = await _receiptRepository.GetActiveReceiptsByWarehouseIdAsync(warehouseId);
            if (activeReceipts.Any())
            {
                var incomingCategory = order.Category?.Trim().ToLowerInvariant();
                var currentCategoriesInWarehouse = await _db.WarehouseReceipts
                    .Where(r => r.WarehouseId == warehouseId && r.ReferenceDocNo == "COMPLETED")
                    .Join(_db.TransportOrders, r => r.OrderId, o => o.OrderId, (r, o) => o.Category)
                    .Where(cat => cat != null)
                    .Select(cat => cat.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToListAsync();

                if (IsStrongOdor(incomingCategory) && currentCategoriesInWarehouse.Any(IsOdorSensitive))
                {
                    warningMessage = $"[ODOR WARNING] Incoming strong odor category '{order.Category}' is stored near odor-sensitive goods in warehouse.";
                }
                else if (IsOdorSensitive(incomingCategory) && currentCategoriesInWarehouse.Any(IsStrongOdor))
                {
                    warningMessage = $"[ODOR WARNING] Incoming odor-sensitive category '{order.Category}' is stored in warehouse currently holding strong odor products.";
                }
            }

            var receipt = await _receiptRepository.GetByOrderIdAsync(orderId);
            if (receipt == null)
            {
                receipt = new WarehouseReceipt
                {
                    ReceiptId = Guid.NewGuid(),
                    ReceiptCode = $"REC-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
                    OrderId = orderId,
                    WarehouseId = warehouseId,
                    ReceiptType = "INBOUND",
                    RecordedTemperature = request.RecordedTemperature,
                    DelivererName = request.DelivererName.Trim(),
                    ReceiverId = receiverId,
                    TotalExpectedQty = order.Quantity,
                    Note = string.IsNullOrWhiteSpace(qcNote) ? request.Note?.Trim() : $"{qcNote}. {request.Note?.Trim()}",
                    ReferenceDocNo = "PENDING_MEASUREMENT",
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                await _receiptRepository.AddAsync(receipt);
            }
            else
            {
                receipt.RecordedTemperature = request.RecordedTemperature;
                receipt.DelivererName = request.DelivererName.Trim();
                receipt.ReceiverId = receiverId;
                receipt.Note = string.IsNullOrWhiteSpace(qcNote) ? request.Note?.Trim() : $"{qcNote}. {request.Note?.Trim()}";
                receipt.ReferenceDocNo = "PENDING_MEASUREMENT";
            }

            await _receiptRepository.SaveChangesAsync();

            var response = MapToResponse(receipt, order.TrackingCode, warehouse.WarehouseName, warningMessage);
            return ApiResponse<WarehouseReceiptResponse>.SuccessResponse(response, "Inbound QC recorded successfully");
        }

        public async Task<ApiResponse<WarehouseReceiptResponse>> UpdateMeasurementsAsync(
            Guid orderId,
            UpdateMeasurementsRequest request)
        {
            var receipt = await _receiptRepository.GetByOrderIdAsync(orderId);
            if (receipt == null)
                return ApiResponse<WarehouseReceiptResponse>.Failure("Warehouse receipt not found for this order. Please run Step 1 (QC receive) first.");

            if (receipt.ReferenceDocNo == "COMPLETED")
                return ApiResponse<WarehouseReceiptResponse>.Failure("Inbound is already completed and cannot be updated.");

            var order = await _db.TransportOrders
                .Include(o => o.DestLocationNavigation)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order == null)
                return ApiResponse<WarehouseReceiptResponse>.Failure("Order not found");

            var warehouse = await _db.Warehouses.FindAsync(receipt.WarehouseId);
            var warehouseName = warehouse?.WarehouseName ?? "Unknown Warehouse";

            // Clean existing items for this receipt (if re-submitting)
            var existingItems = await _db.WarehouseReceiptItems
                .Where(i => i.ReceiptId == receipt.ReceiptId)
                .ToListAsync();
            if (existingItems.Any())
            {
                _db.WarehouseReceiptItems.RemoveRange(existingItems);
            }

            decimal totalActualQty = 0;
            int index = 1;
            foreach (var item in request.Items)
            {
                var barcode = $"BAR-{orderId.ToString().Substring(0, 8).ToUpper()}-{index:D2}";
                var qrCode = $"QR-{orderId.ToString().Substring(0, 8).ToUpper()}-{index:D2}";

                var receiptItem = new WarehouseReceiptItem
                {
                    ItemId = Guid.NewGuid(),
                    ReceiptId = receipt.ReceiptId,
                    ItemName = item.ItemName.Trim(),
                    ItemCode = item.ItemCode?.Trim(),
                    Unit = item.Unit.Trim(),
                    ExpectedQty = order.Quantity,
                    ActualQty = item.ActualQty,
                    ActualWeightKg = item.WeightKg,
                    LengthCm = item.LengthCm,
                    WidthCm = item.WidthCm,
                    HeightCm = item.HeightCm,
                    Barcode = barcode,
                    QrCode = qrCode,
                    ConditionStatus = string.IsNullOrWhiteSpace(item.ConditionStatus) ? "GOOD" : item.ConditionStatus.Trim(),
                    Note = item.Note?.Trim(),
                    BatchNumber = item.BatchNumber?.Trim(),
                    ManufacturedDate = item.ManufacturedDate,
                    ExpiryDate = item.ExpiryDate,
                    CountryOfOrigin = item.CountryOfOrigin.Trim(),
                    ProductCategory = item.ProductCategory
                };

                await _receiptRepository.AddItemAsync(receiptItem);
                totalActualQty += item.ActualQty;
                index++;
            }

            receipt.TotalActualQty = totalActualQty;
            receipt.ReferenceDocNo = "PENDING_COMPLETE";
            await _receiptRepository.SaveChangesAsync();

            var response = MapToResponse(receipt, order.TrackingCode, warehouseName, null);
            return ApiResponse<WarehouseReceiptResponse>.SuccessResponse(response, "Actual package measurements and labels saved successfully");
        }

        public async Task<ApiResponse<WarehouseReceiptResponse>> CompleteInboundAsync(Guid orderId)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var receipt = await _db.WarehouseReceipts
                        .Include(r => r.WarehouseReceiptItems)
                        .FirstOrDefaultAsync(r => r.OrderId == orderId);

                    if (receipt == null)
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Warehouse receipt not found");

                    if (receipt.ReferenceDocNo == "COMPLETED")
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Inbound is already completed");

                    if (!receipt.WarehouseReceiptItems.Any())
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Measurements are missing. Please complete Step 2 first.");

                    // Load receipt-level attachments
                    var receiptAttachments = await _attachmentRepository.GetAttachmentsByReceiptIdAsync(receipt.ReceiptId);

                    // Collect item IDs
                    var itemIds = receipt.WarehouseReceiptItems.Select(i => i.ItemId).ToList();

                    // Load item attachments in a single batch
                    var itemAttachments = await _attachmentRepository.GetAttachmentsByReceiptItemIdsAsync(itemIds);

                    // Merge attachments uniquely
                    var allAttachments = new Dictionary<Guid, WarehouseEvidenceAttachment>();
                    foreach (var att in receiptAttachments)
                    {
                        allAttachments[att.AttachmentId] = att;
                    }
                    foreach (var att in itemAttachments)
                    {
                        allAttachments[att.AttachmentId] = att;
                    }

                    // Execute Compliance validation
                    var complianceResult = _complianceEngine.ValidateReceipt(receipt, allAttachments.Values);

                    if (!complianceResult.Passed)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Inbound completion blocked due to compliance validation failure.");

                        if (complianceResult.MissingRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Missing:");
                            foreach (var req in complianceResult.MissingRequirements.Select(GetSubCategoryDisplay).Distinct())
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        if (complianceResult.PendingRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Pending:");
                            foreach (var req in complianceResult.PendingRequirements.Select(GetSubCategoryDisplay).Distinct())
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        if (complianceResult.FailedRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Failed:");
                            foreach (var req in complianceResult.FailedRequirements.Select(GetSubCategoryDisplay).Distinct())
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        return ApiResponse<WarehouseReceiptResponse>.Failure(sb.ToString().TrimEnd());
                    }

                    var order = await _db.TransportOrders
                        .Include(o => o.Customer)
                        .Include(o => o.DestLocationNavigation)
                        .FirstOrDefaultAsync(o => o.OrderId == orderId);
                    if (order == null)
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Order not found");

                    var warehouse = await _db.Warehouses.FindAsync(receipt.WarehouseId);
                    var warehouseName = warehouse?.WarehouseName ?? "Unknown Warehouse";

                    var clerk = await _db.Users.FindAsync(receipt.ReceiverId);
                    var clerkName = clerk?.FullName ?? "Unknown Clerk";

                    // Calculate actual total weight and volume (CBM) from measurements
                    decimal totalActualWeight = 0;
                    decimal totalActualCbm = 0;
                    foreach (var item in receipt.WarehouseReceiptItems)
                    {
                        totalActualWeight += (item.ActualWeightKg ?? 0) * item.ActualQty;
                        var cbm = ((item.LengthCm ?? 0) * (item.WidthCm ?? 0) * (item.HeightCm ?? 0) * item.ActualQty) / 1000000m;
                        totalActualCbm += cbm;
                    }

                    // Update order dimensions
                    order.ActualWeightKg = totalActualWeight;
                    order.ActualCbm = totalActualCbm;
                    order.Status = InWarehouseStatus;

                    // Recalculate freight and generate adjustment invoice if necessary
                    var originalQuotation = await _db.Quotations
                        .Where(q => q.OrderId == orderId)
                        .OrderByDescending(q => q.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (originalQuotation != null && order.DestLocationNavigation != null)
                    {
                        var pricing = await ResolvePricingAsync(order);
                        if (pricing != null)
                        {
                            var baseFreight = Math.Max(totalActualWeight * pricing.PriceKg, totalActualCbm * pricing.PriceCbm);
                            if (baseFreight < MinCharge)
                                baseFreight = MinCharge;

                            var distanceKm = await _locationService.GetDistanceKmAsync(
                                HubLat,
                                HubLon,
                                order.DestLocationNavigation.Latitude,
                                order.DestLocationNavigation.Longitude);
                            var lastMileSurcharge = distanceKm > LastMileFreeKm
                                ? Math.Round((distanceKm - LastMileFreeKm) * LastMileUnitPrice, 0)
                                : 0m;
                            var vasAmount = originalQuotation.VasAmount.GetValueOrDefault();
                            var subtotal = baseFreight + lastMileSurcharge + vasAmount;
                            var vatAmount = Math.Round(subtotal * VatRate, 0);
                            var finalAmount = subtotal + vatAmount;

                            // If final cost differs, create an adjustment invoice
                            if (finalAmount != originalQuotation.FinalAmount)
                            {
                                var diffSubTotal = subtotal - (originalQuotation.BaseFreight + originalQuotation.LastMileSurcharge.GetValueOrDefault() + originalQuotation.VasAmount.GetValueOrDefault());
                                var diffVat = vatAmount - originalQuotation.VatAmount;
                                var diffGrandTotal = finalAmount - originalQuotation.FinalAmount;

                                var adjustmentInvoice = new Invoice
                                {
                                    InvoiceId = Guid.NewGuid(),
                                    InvoiceCode = $"INV-ADJ-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
                                    CustomerId = order.CustomerId.GetValueOrDefault(),
                                    SubTotal = diffSubTotal,
                                    TaxRate = VatRate,
                                    TaxAmount = diffVat,
                                    GrandTotal = diffGrandTotal,
                                    PaidAmount = 0,
                                    IssuedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                                    DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15)),
                                    Status = "UNPAID",
                                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                                };

                                var adjustmentLine = new InvoiceLine
                                {
                                    LineId = Guid.NewGuid(),
                                    InvoiceId = adjustmentInvoice.InvoiceId,
                                    OrderId = orderId,
                                    ChargeType = "INBOUND_MEASUREMENT_ADJUSTMENT",
                                    Description = $"Adjustment based on actual measured weight/volume at Hub: {totalActualWeight}kg / {totalActualCbm}cbm vs expected",
                                    Quantity = 1,
                                    UnitPrice = diffGrandTotal,
                                    Amount = diffGrandTotal,
                                    TaxRate = VatRate
                                };

                                _db.Invoices.Add(adjustmentInvoice);
                                _db.InvoiceLines.Add(adjustmentLine);
                            }
                        }
                    }

                    // Resolve or create RECEIVING Zone in target warehouse
                    var receivingZone = await _db.WarehouseZones
                        .FirstOrDefaultAsync(z => z.WarehouseId == receipt.WarehouseId && z.ZoneCode == "RECEIVING");
                    if (receivingZone == null)
                    {
                        receivingZone = new WarehouseZone
                        {
                            ZoneId = Guid.NewGuid(),
                            WarehouseId = receipt.WarehouseId,
                            ZoneCode = "RECEIVING",
                            ZoneName = "Receiving Stage Zone",
                            ZoneType = "RECEIVING",
                            StorageType = "FLOOR",
                            MaxCapacityPallets = 1000,
                            CurrentPallets = 0,
                            Status = "ACTIVE",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        _db.WarehouseZones.Add(receivingZone);
                        await _db.SaveChangesAsync();
                    }

                    // Pessimistic Lock on Zone
                    if (_db.Database.IsRelational())
                    {
                        receivingZone = await _db.WarehouseZones
                            .FromSqlRaw("SELECT * FROM warehouse_zones WHERE zone_id = {0} FOR UPDATE", receivingZone.ZoneId)
                            .FirstOrDefaultAsync();
                    }

                    // Resolve or create RCV-STAGE-01 Location in RECEIVING Zone
                    var receivingLocation = await _db.WarehouseLocations
                        .FirstOrDefaultAsync(l => l.ZoneId == receivingZone!.ZoneId && l.LocationCode == "RCV-STAGE-01");
                    if (receivingLocation == null)
                    {
                        receivingLocation = new WarehouseLocation
                        {
                            LocationId = Guid.NewGuid(),
                            ZoneId = receivingZone!.ZoneId,
                            LocationCode = "RCV-STAGE-01",
                            MaxCapacityPallets = 1000,
                            CurrentPallets = 0,
                            Status = "ACTIVE",
                            Description = "Default Inbound Receiving Stage Location",
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        _db.WarehouseLocations.Add(receivingLocation);
                        await _db.SaveChangesAsync();
                    }

                    // Pessimistic Lock on Location
                    if (_db.Database.IsRelational())
                    {
                        receivingLocation = await _db.WarehouseLocations
                            .FromSqlRaw("SELECT * FROM warehouse_locations WHERE location_id = {0} FOR UPDATE", receivingLocation.LocationId)
                            .FirstOrDefaultAsync();
                    }

                    // Perform core temperature QC check again for stock hold determination
                    bool tempPassed = true;
                    if (receipt.RecordedTemperature.HasValue && !string.IsNullOrWhiteSpace(order.TempCondition))
                    {
                        if (double.TryParse(order.TempCondition, NumberStyles.Any, CultureInfo.InvariantCulture, out double targetTemp))
                        {
                            if (targetTemp < 0)
                            {
                                if ((double)receipt.RecordedTemperature.Value > targetTemp + 3.0)
                                {
                                    tempPassed = false;
                                }
                            }
                            else
                            {
                                if ((double)receipt.RecordedTemperature.Value > 8.0 || (double)receipt.RecordedTemperature.Value < 0.0)
                                {
                                    tempPassed = false;
                                }
                            }
                        }
                    }

                    var stockStatus = tempPassed ? "AVAILABLE" : "HOLD";

                    // Initialize stock and movements for each item
                    foreach (var item in receipt.WarehouseReceiptItems)
                    {
                        var itemCode = string.IsNullOrWhiteSpace(item.ItemCode) ? "UNKNOWN-ITEM" : item.ItemCode.Trim();
                        var batchNo = string.IsNullOrWhiteSpace(item.BatchNumber) ? "NO-BATCH" : item.BatchNumber.Trim();
                        var expiryDate = item.ExpiryDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(100)); // Default fallback if missing
                        var mfgDate = item.ManufacturedDate;

                        // Find or create InventoryBatch
                        var batch = await _db.InventoryBatches
                            .FirstOrDefaultAsync(b => b.ItemCode == itemCode && b.BatchNumber == batchNo);
                        if (batch == null)
                        {
                            batch = new InventoryBatch
                            {
                                BatchId = Guid.NewGuid(),
                                ItemCode = itemCode,
                                BatchNumber = batchNo,
                                ManufacturedDate = mfgDate,
                                ExpiryDate = expiryDate,
                                Status = "ACTIVE",
                                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                            };
                            _db.InventoryBatches.Add(batch);
                            await _db.SaveChangesAsync();
                        }

                        var customerId = order.CustomerId.GetValueOrDefault();
                        // Find or create InventoryStock under default receiving location
                        var stock = await _db.InventoryStocks
                            .FirstOrDefaultAsync(s => s.LocationId == receivingLocation!.LocationId 
                                                      && s.CustomerId == customerId
                                                      && s.ItemCode == itemCode 
                                                      && s.BatchId == batch.BatchId);

                        decimal? requiredTempMin = null;
                        decimal? requiredTempMax = null;

                        if (!string.IsNullOrWhiteSpace(order.TempCondition))
                        {
                            var normalizedCond = order.TempCondition.Trim();
                            var parts = normalizedCond.Split(new[] { '-', 't', 'o', '~' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2 && 
                                decimal.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal minVal) &&
                                decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal maxVal))
                            {
                                requiredTempMin = minVal;
                                requiredTempMax = maxVal;
                            }
                            else if (decimal.TryParse(normalizedCond, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal singleTemp))
                            {
                                if (singleTemp < 0)
                                {
                                    requiredTempMax = singleTemp;
                                    requiredTempMin = -30m;
                                }
                                else
                                {
                                    requiredTempMin = singleTemp;
                                    requiredTempMax = singleTemp;
                                }
                            }
                        }

                        if (stock == null)
                        {
                            stock = new InventoryStock
                            {
                                StockId = Guid.NewGuid(),
                                LocationId = receivingLocation!.LocationId,
                                CustomerId = customerId,
                                ItemCode = itemCode,
                                ItemName = item.ItemName.Trim(),
                                Unit = item.Unit.Trim(),
                                BatchId = batch.BatchId,
                                QuantityOnHand = item.ActualQty,
                                QuantityAllocated = 0,
                                InboundDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                                Status = stockStatus,
                                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                                PalletCount = 1,
                                RequiredTempMin = requiredTempMin,
                                RequiredTempMax = requiredTempMax
                            };
                            _db.InventoryStocks.Add(stock);
                            receivingLocation.CurrentPallets += 1;
                            receivingZone!.CurrentPallets += 1;
                        }
                        else
                        {
                            stock.QuantityOnHand += item.ActualQty;
                            stock.PalletCount += 1;
                            stock.RequiredTempMin = requiredTempMin;
                            stock.RequiredTempMax = requiredTempMax;
                            stock.Status = stockStatus; // Update to reflect current receipt hold status
                            stock.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
                            receivingLocation!.CurrentPallets += 1;
                            receivingZone!.CurrentPallets += 1;
                        }
                        await _db.SaveChangesAsync();

                        // Create Inventory Hold if Temp QC failed
                        if (!tempPassed)
                        {
                            var hold = new InventoryHold
                            {
                                HoldId = Guid.NewGuid(),
                                StockId = stock.StockId,
                                HoldQuantity = item.ActualQty,
                                ReasonCode = "QC_TEMP_VIOLATION",
                                Notes = $"[AUTOMATIC QC HOLD] Temperature check failed during receiving: {receipt.RecordedTemperature}°C vs condition {order.TempCondition}.",
                                Status = "HOLD",
                                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                                CreatedBy = receipt.ReceiverId
                            };
                            _db.InventoryHolds.Add(hold);
                            await _db.SaveChangesAsync();
                        }

                        // Log Inbound InventoryMovement
                        var movement = new InventoryMovement
                        {
                            MovementId = Guid.NewGuid(),
                            StockId = stock.StockId,
                            ItemCode = itemCode,
                            BatchId = batch.BatchId,
                            MovementType = "INBOUND",
                            Quantity = item.ActualQty,
                            ToLocationId = receivingLocation!.LocationId,
                            ReferenceDocumentId = receipt.ReceiptId,
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                            CreatedBy = receipt.ReceiverId
                        };
                        _db.InventoryMovements.Add(movement);
                    }

                    // Generate HTML and PDF Receipt
                    receipt.PdfUrl = await GenerateReceiptPdfAsync(order, receipt, warehouseName, clerkName);
                    receipt.ReferenceDocNo = "COMPLETED";

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var response = MapToResponse(receipt, order.TrackingCode, warehouseName, null);
                    return ApiResponse<WarehouseReceiptResponse>.SuccessResponse(response, "Inbound completed and e-Warehouse Receipt PDF generated successfully");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return ApiResponse<WarehouseReceiptResponse>.Failure($"Failed to complete inbound: {ex.Message}");
                }
            });
        }

        private async Task<string> GenerateReceiptPdfAsync(
            TransportOrder order,
            WarehouseReceipt receipt,
            string warehouseName,
            string clerkName)
        {
            var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "WarehouseReceiptTemplate.html");
            if (!File.Exists(templatePath))
                throw new InvalidOperationException("WarehouseReceiptTemplate.html template was not found");

            var html = await File.ReadAllTextAsync(templatePath);

            var itemsRows = "";
            int no = 1;
            foreach (var item in receipt.WarehouseReceiptItems)
            {
                itemsRows += $@"
                <tr>
                     <td>{no}</td>
                     <td>{item.ItemName}</td>
                     <td>{item.ItemCode ?? "-"}</td>
                     <td>{item.BatchNumber ?? "-"}</td>
                     <td>{item.ExpiryDate?.ToString("dd/MM/yyyy") ?? "-"}</td>
                     <td>{item.Unit}</td>
                     <td>{item.ExpectedQty:0.##}</td>
                     <td>{item.ActualQty:0.##}</td>
                     <td>{item.ActualWeightKg?.ToString("0.##") ?? "-"}</td>
                     <td>{item.LengthCm:0}x{item.WidthCm:0}x{item.HeightCm:0}</td>
                     <td><span style='color: {(item.ConditionStatus == "GOOD" ? "green" : "red")}; font-weight: bold;'>{item.ConditionStatus}</span></td>
                     <td>{item.Note ?? "-"}</td>
                </tr>";
                no++;
            }

            var warningBlock = "";
            if (receipt.Note != null && receipt.Note.Contains("[QC FAILED]"))
            {
                warningBlock = $@"
                <div class='qc-alert qc-failed'>
                     <strong>⚠️ CẢNH BÁO QC CHẤT LƯỢNG:</strong> {receipt.Note}
                </div>";
            }

            var replacements = new Dictionary<string, string?>
            {
                ["Receipt_Code"] = receipt.ReceiptCode,
                ["Receipt_Date"] = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                ["Customer_Name"] = order.Customer?.CompanyName ?? "Khách hàng vãng lai",
                ["Deliverer_Name"] = receipt.DelivererName,
                ["Tracking_Code"] = order.TrackingCode,
                ["Warehouse_Name"] = warehouseName,
                ["Receiver_Name"] = clerkName,
                ["Recorded_Temperature"] = receipt.RecordedTemperature?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-",
                ["QC_Warning_Block"] = warningBlock,
                ["Items_Table_Rows"] = itemsRows
            };

            foreach (var replacement in replacements)
                html = html.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return await _pdfService.SaveWarehouseReceiptPdfAsync(html, receipt.ReceiptCode);
        }

        private async Task<RoutePricing?> ResolvePricingAsync(TransportOrder order)
        {
            var destinationAddress = order.DestLocationNavigation?.Address ?? string.Empty;
            var destinationCity = ExtractDestinationCity(destinationAddress);

            var allRoutePrices = await _db.PricingMatrices
                .AsNoTracking()
                .Where(p => p.PricingUnit == "KG" || p.PricingUnit == "CBM")
                .ToListAsync();

            var originKey = NormalizeRouteKey(DefaultOriginCity);
            var destinationKey = NormalizeRouteKey(destinationCity);
            var prices = allRoutePrices
                .Where(p => NormalizeRouteKey(p.OriginCity) == originKey
                            && NormalizeRouteKey(p.DestCity) == destinationKey)
                .OrderByDescending(p => p.EffectiveDate)
                .ToList();

            var priceKg = prices.FirstOrDefault(p => p.PricingUnit == "KG")?.UnitPrice;
            var priceCbm = prices.FirstOrDefault(p => p.PricingUnit == "CBM")?.UnitPrice;

            if (!priceKg.HasValue || !priceCbm.HasValue)
                return null;

            return new RoutePricing(priceKg.Value, priceCbm.Value, destinationCity);
        }

        private static string ExtractDestinationCity(string address)
        {
            var normalized = RemoveDiacritics(address).ToLowerInvariant();

            if (normalized.Contains("ha noi")) return "Ha Noi";
            if (normalized.Contains("da nang")) return "Da Nang";
            if (normalized.Contains("can tho")) return "Can Tho";
            if (normalized.Contains("kien giang")) return "Kien Giang";
            if (normalized.Contains("dong nai")) return "Dong Nai";
            if (normalized.Contains("binh duong")) return "Binh Duong";
            if (normalized.Contains("ho chi minh") || normalized.Contains("hcm") || normalized.Contains("tp.hcm") || normalized.Contains("sai gon")) return "Ho Chi Minh";

            return "Ho Chi Minh";
        }

        private static string NormalizeRouteKey(string? value)
        {
            return RemoveDiacritics(value ?? string.Empty)
                .ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace(".", string.Empty)
                .Replace("-", string.Empty);
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool IsStrongOdor(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return false;
            var cat = category.ToLowerInvariant();
            return cat.Contains("seafood") || cat.Contains("durian") || cat.Contains("meat") || cat.Contains("spices") || cat.Contains("mui");
        }

        private static bool IsOdorSensitive(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return false;
            var cat = category.ToLowerInvariant();
            return cat.Contains("dairy") || cat.Contains("egg") || cat.Contains("chocolate") || cat.Contains("sua") || cat.Contains("trung");
        }

        private static string GetSubCategoryDisplay(string req)
        {
            foreach (var val in Enum.GetValues<AttachmentSubCategory>())
            {
                if (req.Contains($"'{val}'") || req.Contains(val.ToString()))
                {
                    return val.ToString();
                }
            }
            return req;
        }

        private static WarehouseReceiptResponse MapToResponse(
            WarehouseReceipt receipt,
            string trackingCode,
            string warehouseName,
            string? warningMessage)
        {
            return new WarehouseReceiptResponse
            {
                ReceiptId = receipt.ReceiptId,
                ReceiptCode = receipt.ReceiptCode,
                ReferenceDocNo = receipt.ReferenceDocNo,
                OrderId = receipt.OrderId,
                OrderTrackingCode = trackingCode,
                WarehouseId = receipt.WarehouseId,
                WarehouseName = warehouseName,
                ReceiptType = receipt.ReceiptType,
                Reason = receipt.Reason,
                TotalExpectedQty = receipt.TotalExpectedQty,
                TotalActualQty = receipt.TotalActualQty,
                RecordedTemperature = receipt.RecordedTemperature,
                DelivererName = receipt.DelivererName,
                ReceiverId = receipt.ReceiverId,
                Note = receipt.Note,
                PdfUrl = receipt.PdfUrl,
                Status = receipt.ReferenceDocNo,
                WarningMessage = warningMessage,
                CreatedAt = receipt.CreatedAt,
                Items = receipt.WarehouseReceiptItems?.Select(i => new WarehouseReceiptItemDto
                {
                    ItemId = i.ItemId,
                    ItemName = i.ItemName,
                    ItemCode = i.ItemCode,
                    Unit = i.Unit,
                    ExpectedQty = i.ExpectedQty,
                    ActualQty = i.ActualQty,
                    ActualWeightKg = i.ActualWeightKg,
                    LengthCm = i.LengthCm,
                    WidthCm = i.WidthCm,
                    HeightCm = i.HeightCm,
                    Barcode = i.Barcode,
                    QrCode = i.QrCode,
                    ConditionStatus = i.ConditionStatus,
                    Note = i.Note,
                    BatchNumber = i.BatchNumber,
                    ManufacturedDate = i.ManufacturedDate,
                    ExpiryDate = i.ExpiryDate
                }).ToList() ?? new List<WarehouseReceiptItemDto>()
            };
        }

        private sealed record RoutePricing(decimal PriceKg, decimal PriceCbm, string DestinationCity);
    }
}
