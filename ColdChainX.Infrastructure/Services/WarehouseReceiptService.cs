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
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class WarehouseReceiptService : IWarehouseReceiptService
    {
        private const string InWarehouseStatus = "IN_WAREHOUSE";
        private const decimal MinCharge = 300000m;
        private const decimal LastMileFreeKm = 10m;
        private const decimal LastMileUnitPrice = 15000m;
        private const decimal VatRate = 0.08m;
        private const string DefaultOriginCity = "Ho Chi Minh";

        private readonly ApplicationDbContext _db;
        private readonly IWarehouseReceiptRepository _receiptRepository;
        private readonly ILocationService _locationService;
        private readonly IPdfService _pdfService;
        private readonly IWebHostEnvironment _environment;

        public WarehouseReceiptService(
            ApplicationDbContext db,
            IWarehouseReceiptRepository receiptRepository,
            ILocationService locationService,
            IPdfService pdfService,
            IWebHostEnvironment environment)
        {
            _db = db;
            _receiptRepository = receiptRepository;
            _locationService = locationService;
            _pdfService = pdfService;
            _environment = environment;
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
                    // If targetTemp is positive (e.g., 2 to 8 degrees chill range), we check if it is within range.
                    // If targetTemp is negative (e.g. -18 frozen), we check if it is below that.
                    if (targetTemp < 0)
                    {
                        if ((double)request.RecordedTemperature > targetTemp + 3.0) // Allow small buffer, e.g. -15
                        {
                            tempPassed = false;
                            qcNote = $"[QC FAILED] Core temperature {request.RecordedTemperature}°C exceeds frozen threshold of {targetTemp}°C";
                        }
                    }
                    else
                    {
                        // Assume chill range e.g. 2 to 8, targetTemp represents the upper bound or single value.
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
                    Note = item.Note?.Trim()
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
            var receipt = await _db.WarehouseReceipts
                .Include(r => r.WarehouseReceiptItems)
                .FirstOrDefaultAsync(r => r.OrderId == orderId);

            if (receipt == null)
                return ApiResponse<WarehouseReceiptResponse>.Failure("Warehouse receipt not found");

            if (receipt.ReferenceDocNo == "COMPLETED")
                return ApiResponse<WarehouseReceiptResponse>.Failure("Inbound is already completed");

            if (!receipt.WarehouseReceiptItems.Any())
                return ApiResponse<WarehouseReceiptResponse>.Failure("Measurements are missing. Please complete Step 2 first.");

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
                        GoongLocationService.HubLat,
                        GoongLocationService.HubLon,
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

            // Generate HTML and PDF Receipt
            receipt.PdfUrl = await GenerateReceiptPdfAsync(order, receipt, warehouseName, clerkName);
            receipt.ReferenceDocNo = "COMPLETED";

            await _db.SaveChangesAsync();

            var response = MapToResponse(receipt, order.TrackingCode, warehouseName, null);
            return ApiResponse<WarehouseReceiptResponse>.SuccessResponse(response, "Inbound completed and e-Warehouse Receipt PDF generated successfully");
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
                Status = receipt.ReferenceDocNo, // Using ReferenceDocNo as Status representation
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
                    Note = i.Note
                }).ToList() ?? new List<WarehouseReceiptItemDto>()
            };
        }

        private sealed record RoutePricing(decimal PriceKg, decimal PriceCbm, string DestinationCity);
    }
}
