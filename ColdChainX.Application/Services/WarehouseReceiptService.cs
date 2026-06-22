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
        private readonly ComplianceRulesEngine _complianceEngine;

        public WarehouseReceiptService(
            IApplicationDbContext db,
            IWarehouseReceiptRepository receiptRepository,
            ILocationService locationService,
            IPdfService pdfService,
            IWebHostEnvironment environment,
            ComplianceRulesEngine complianceEngine)
        {
            _db = db;
            _receiptRepository = receiptRepository;
            _locationService = locationService;
            _pdfService = pdfService;
            _environment = environment;
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

            // Update Lpn since we only have 1 Lpn per receipt in this flow.
            var lpn = await _db.Lpns.FirstOrDefaultAsync(l => l.ReceiptId == receipt.ReceiptId);
            if (lpn != null && request.Items.Any())
            {
                var item = request.Items.First();
                lpn.Quantity = (int)item.ActualQty;
                lpn.ActualWeightKg = item.WeightKg;
                lpn.ActualCbm = (item.LengthCm * item.WidthCm * item.HeightCm) / 1000000m;
                lpn.StorageLocation = string.IsNullOrWhiteSpace(item.ConditionStatus) ? "GOOD" : item.ConditionStatus.Trim();
                lpn.DiscrepancyReason = item.Note?.Trim();
                // other mapping as necessary
                
                receipt.TotalActualQty = item.ActualQty;
            }

            receipt.ReferenceDocNo = "PENDING_COMPLETE";
            await _db.SaveChangesAsync();

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
                        .Include(r => r.Lpns)
                        .FirstOrDefaultAsync(r => r.OrderId == orderId);

                    if (receipt == null)
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Warehouse receipt not found");

                    if (receipt.ReferenceDocNo == "COMPLETED")
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Inbound is already completed");

                    if (!receipt.Lpns.Any())
                        return ApiResponse<WarehouseReceiptResponse>.Failure("Measurements are missing. Please complete Step 2 first.");

                    // Execute Compliance validation - dummy pass since attachments are gone
                    var complianceResult = new ColdChainX.Application.Models.ComplianceCheckResult { Passed = true, MissingRequirements = new List<string>(), PendingRequirements = new List<string>() };

                    if (!complianceResult.Passed)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Inbound completion blocked due to compliance validation failure.");

                        if (complianceResult.MissingRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Missing:");
                            foreach (var req in complianceResult.MissingRequirements)
                            {
                                sb.AppendLine($"* {req}");
                            }
                        }

                        if (complianceResult.PendingRequirements.Any())
                        {
                            sb.AppendLine();
                            sb.AppendLine("Pending:");
                            foreach (var req in complianceResult.PendingRequirements)
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
                    foreach (var item in receipt.Lpns)
                    {
                        totalActualWeight += item.ActualWeightKg * item.Quantity;
                        var cbm = item.ActualCbm * item.Quantity;
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

                    string? billingWarning = null;

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

                                 // Generate PDF for the adjustment invoice
                                 try
                                 {
                                     adjustmentInvoice.PdfUrl = await GenerateInvoicePdfAsync(order, adjustmentInvoice, adjustmentLine, warehouseName);
                                 }
                                 catch (Exception pdfEx)
                                 {
                                     billingWarning = $"[PDF ERROR] Failed to generate adjustment invoice PDF: {pdfEx.Message}";
                                 }

                                 _db.Invoices.Add(adjustmentInvoice);
                                 _db.InvoiceLines.Add(adjustmentLine);
                            }
                        }
                        else
                        {
                            billingWarning = $"[BILLING WARNING] Route pricing not found from '{DefaultOriginCity}' to '{order.DestLocationNavigation.Address ?? "Unknown Address"}'. Pricing recalculation skipped.";
                        }
                    }
                    else
                    {
                        if (originalQuotation == null)
                        {
                            billingWarning = "[BILLING WARNING] Original quotation not found. Pricing recalculation skipped.";
                        }
                        else if (order.DestLocationNavigation == null)
                        {
                            billingWarning = "[BILLING WARNING] Destination location not found. Pricing recalculation skipped.";
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
                    // Inventory module has been removed. No stock will be recorded.

                    // Generate HTML and PDF Receipt
                    try
                    {
                        receipt.PdfUrl = await GenerateReceiptPdfAsync(order, receipt, warehouseName, clerkName);
                    }
                    catch (Exception pdfEx)
                    {
                        billingWarning = billingWarning == null 
                            ? $"[PDF ERROR] Failed to generate e-receipt PDF: {pdfEx.Message}"
                            : $"{billingWarning} [PDF ERROR] Failed to generate e-receipt PDF: {pdfEx.Message}";
                    }
                    receipt.ReferenceDocNo = "COMPLETED";

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var response = MapToResponse(receipt, order.TrackingCode, warehouseName, billingWarning);
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
            foreach (var item in receipt.Lpns)
            {
                itemsRows += $@"
                <tr>
                     <td>{no}</td>
                     <td>{item.Order?.ItemName ?? "Unknown"}</td>
                     <td>{item.Order?.TrackingCode ?? "-"}</td>
                     <td>{"-"}</td>
                     <td>{"-"}</td>
                     <td>{item.Order?.PackingType ?? "-"}</td>
                     <td>{item.Order?.Quantity ?? 0}</td>
                     <td>{item.Quantity}</td>
                     <td>{item.ActualWeightKg.ToString("0.##")}</td>
                     <td>{"-"}</td>
                     <td>{item.LpnCode}</td>
                     <td>{item.StorageLocation ?? "-"}</td>
                     <td>{item.DiscrepancyReason ?? "-"}</td>
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

        private async Task<string> GenerateInvoicePdfAsync(
            TransportOrder order,
            Invoice invoice,
            InvoiceLine line,
            string warehouseName)
        {
            var templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "InvoiceTemplate.html");
            if (!File.Exists(templatePath))
                throw new InvalidOperationException("InvoiceTemplate.html template was not found");

            var html = await File.ReadAllTextAsync(templatePath);

            var linesRows = $@"
            <tr>
                 <td>{line.ChargeType}</td>
                 <td>{line.Description}</td>
                 <td>{line.Quantity:0.##}</td>
                 <td>{line.UnitPrice.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}</td>
                 <td>{line.Amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}</td>
            </tr>";

            var replacements = new Dictionary<string, string?>
            {
                ["Invoice_Code"] = invoice.InvoiceCode,
                ["Issued_Date"] = invoice.IssuedDate.ToString("dd/MM/yyyy"),
                ["Due_Date"] = invoice.DueDate.ToString("dd/MM/yyyy"),
                ["Customer_Name"] = order.Customer?.CompanyName ?? "Khách hàng vãng lai",
                ["Order_Tracking_Code"] = order.TrackingCode,
                ["Warehouse_Name"] = warehouseName,
                ["Status"] = invoice.Status == "UNPAID" ? "Chưa thanh toán" : "Đã thanh toán",
                ["Status_Class"] = invoice.Status == "UNPAID" ? "status-unpaid" : "status-paid",
                ["Sub_Total"] = invoice.SubTotal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                ["Tax_Rate"] = ((invoice.TaxRate ?? 0) * 100).ToString("0"),
                ["Tax_Amount"] = invoice.TaxAmount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                ["Grand_Total"] = invoice.GrandTotal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
                ["Invoice_Lines_Table_Rows"] = linesRows
            };

            foreach (var replacement in replacements)
                html = html.Replace($"{{{{{replacement.Key}}}}}", replacement.Value ?? string.Empty);

            return await _pdfService.SaveInvoicePdfAsync(html, invoice.InvoiceCode);
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
                Items = receipt.Lpns?.Select(i => new WarehouseReceiptItemDto
                {
                    ItemId = i.LpnId,
                    ItemName = i.Order?.ItemName ?? "Unknown",
                    ItemCode = i.Order?.TrackingCode,
                    Unit = i.Order?.PackingType ?? "N/A",
                    ExpectedQty = i.Order?.Quantity ?? 0,
                    ActualQty = i.Quantity,
                    ActualWeightKg = i.ActualWeightKg,
                    LengthCm = 0,
                    WidthCm = 0,
                    HeightCm = 0,
                    Barcode = i.LpnCode,
                    QrCode = "",
                    ConditionStatus = i.StorageLocation,
                    Note = i.DiscrepancyReason,
                    BatchNumber = "N/A",
                    ManufacturedDate = null,
                    ExpiryDate = null
                }).ToList() ?? new List<WarehouseReceiptItemDto>()
            };
        }

        private sealed record RoutePricing(decimal PriceKg, decimal PriceCbm, string DestinationCity);
    }
}
