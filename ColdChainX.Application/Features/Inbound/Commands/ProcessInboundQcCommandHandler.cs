using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ColdChainX.Application.Features.Inbound.Commands;

public class ProcessInboundQcCommandHandler : IRequestHandler<ProcessInboundQcCommand, ProcessInboundQcResponse>
{
    private const decimal DiscrepancyThresholdPercent = 5m;

    private readonly IApplicationDbContext _context;
    private readonly ILogger<ProcessInboundQcCommandHandler> _logger;
    private readonly IFileService _fileService;
    private readonly IMediator _mediator;
    private readonly IContractAppendixService _appendixService;

    public ProcessInboundQcCommandHandler(
        IApplicationDbContext context,
        ILogger<ProcessInboundQcCommandHandler> logger,
        IFileService fileService,
        IMediator mediator,
        IContractAppendixService appendixService)
    {
        _context = context;
        _logger = logger;
        _fileService = fileService;
        _mediator = mediator;
        _appendixService = appendixService;
    }

    public async Task<ProcessInboundQcResponse> Handle(ProcessInboundQcCommand request, CancellationToken cancellationToken)
    {
        if (request.AsnId == Guid.Empty)
            return Failure("AsnId is required.");

        if (request.ReceiverId == Guid.Empty)
            return Failure("ReceiverId is required.");

        if (request.ActualWeightKg <= 0 || request.LengthCm <= 0 || request.WidthCm <= 0 || request.HeightCm <= 0)
            return Failure("Actual weight and dimensions must be greater than 0.");

        var receiver = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == request.ReceiverId, cancellationToken);

        if (receiver == null)
            return Failure("Receiver user was not found.");

        var warehouseId = request.WarehouseId != Guid.Empty
            ? request.WarehouseId
            : receiver.WarehouseId;

        if (!warehouseId.HasValue || warehouseId.Value == Guid.Empty)
            return Failure("WarehouseId is required and could not be determined.");

        var uploadedUrls = new List<string>();
        if (request.EvidenceImages != null && request.EvidenceImages.Any())
        {
            foreach (var file in request.EvidenceImages)
            {
                if (file.Length > 10 * 1024 * 1024)
                {
                    return Failure($"File {file.FileName} exceeds the 10MB size limit.");
                }

                var url = await _fileService.UploadFileAsync(file);
                uploadedUrls.Add(url);
            }
        }

        var evidenceImageUrl = uploadedUrls.Any() ? string.Join(",", uploadedUrls) : null;

        var asn = await _context.InboundAsns
            .Include(a => a.Order)
            .FirstOrDefaultAsync(a => a.AsnId == request.AsnId, cancellationToken);

        if (asn?.Order == null)
            return Failure("ASN or linked order was not found.");

        if (asn.WarehouseId.HasValue && asn.WarehouseId.Value != warehouseId.Value)
            return Failure("ASN does not belong to current receiver warehouse.");

        var order = asn.Order;
        var now = DbNow();
        var actualCbm = CalculateCbm(request.LengthCm, request.WidthCm, request.HeightCm, order.Quantity);
        var weightDiff = CalculateDiffPercent(order.ExpectedWeightKg, request.ActualWeightKg);
        var cbmDiff = CalculateDiffPercent(order.ExpectedCbm, actualCbm);
        var maxDiff = Math.Max(weightDiff, cbmDiff);
        var hasDiscrepancy = maxDiff > DiscrepancyThresholdPercent;

        var existingLpn = await _context.Lpns
            .AsNoTracking()
            .Where(l => l.OrderId == order.OrderId)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingLpn != null)
            return Failure($"Order already has LPN {existingLpn.LpnCode}. Use putaway or discrepancy flow instead.");

        var receipt = await _context.WarehouseReceipts
            .FirstOrDefaultAsync(r => r.OrderId == order.OrderId
                                      && r.WarehouseId == warehouseId.Value
                                      && r.ReferenceDocNo != "COMPLETED",
                cancellationToken);

        if (receipt == null)
        {
            receipt = new WarehouseReceipt
            {
                ReceiptId = Guid.NewGuid(),
                ReceiptCode = GenerateCode("REC"),
                ReferenceDocNo = hasDiscrepancy ? "DISCREPANCY_HOLD" : "PENDING_PUTAWAY",
                OrderId = order.OrderId,
                WarehouseId = warehouseId.Value,
                ReceiptType = "INBOUND",
                Reason = hasDiscrepancy ? "QC discrepancy hold" : null,
                TotalExpectedQty = order.Quantity,
                TotalActualQty = order.Quantity,
                RecordedTemperature = request.Temperature,
                DelivererName = "",
                ReceiverId = request.ReceiverId,
                Note = hasDiscrepancy ? "Generated during QC with variance greater than 5%." : "Generated during QC.",
                CreatedAt = now
            };

            _context.WarehouseReceipts.Add(receipt);
        }
        else
        {
            receipt.ReferenceDocNo = hasDiscrepancy ? "DISCREPANCY_HOLD" : "PENDING_PUTAWAY";
            receipt.RecordedTemperature = request.Temperature;
            receipt.TotalExpectedQty = order.Quantity;
            receipt.TotalActualQty = order.Quantity;
            receipt.Note = hasDiscrepancy ? "QC discrepancy hold." : "QC passed and waiting putaway.";
        }

        var lpn = new Lpn
        {
            LpnId = Guid.NewGuid(),
            LpnCode = GenerateCode("LPN"),
            OrderId = order.OrderId,
            CustomerId = order.CustomerId,
            ReceiptId = receipt.ReceiptId,
            RouteId = order.RouteId,
            TripId = order.MasterTripId,
            Quantity = order.Quantity,
            ActualWeightKg = request.ActualWeightKg,
            ActualCbm = actualCbm,
            RequiredTemperature = ParseTemperature(order.TempCondition),
            RecordedTemperature = request.Temperature,
            State = hasDiscrepancy ? LpnState.DISCREPANCY_HOLD : LpnState.RECEIVING,
            DiscrepancyReason = hasDiscrepancy
                ? $"Actual cargo differs from expected by {maxDiff:0.##}% (weight {weightDiff:0.##}%, cbm {cbmDiff:0.##}%)."
                : null,
            EvidenceImageUrl = evidenceImageUrl,
            SlaDeadline = now.AddHours(24),
            CreatedAt = now
        };

        _context.Lpns.Add(lpn);

        asn.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "QC_PASSED";
        order.ActualWeightKg = request.ActualWeightKg;
        order.ActualCbm = actualCbm;
        order.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "RECEIVING";

        // Save changes to generate ReceiptId in DB before generating PDF
        await _context.SaveChangesAsync(cancellationToken);

        string? pdfUrl = null;
        if (hasDiscrepancy && receipt != null)
        {
            var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Discrepancy.Queries.GenerateDiscrepancyPdfQuery(receipt.ReceiptId), cancellationToken);
                
            var pdfFileName = $"discrepancy-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf";
            pdfUrl = await _fileService.UploadFileAsync(pdfBytes, pdfFileName);
            
            receipt.PdfUrl = pdfUrl;
            await _context.SaveChangesAsync(cancellationToken);

            // Automatically generate a pre-tax contract appendix in DRAFT status
            var salesUserId = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role != null && u.Role.RoleName.ToLower() == "sales")
                .Select(u => u.UserId)
                .FirstOrDefaultAsync(cancellationToken);

            if (salesUserId == Guid.Empty)
            {
                salesUserId = await _context.Users
                    .Include(u => u.Role)
                    .Where(u => u.Role != null && (u.Role.RoleName.ToLower() == "admin" || u.Role.RoleName.ToLower() == "manager"))
                    .Select(u => u.UserId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (salesUserId == Guid.Empty)
            {
                salesUserId = request.ReceiverId;
            }

            var isWeightHigher = request.ActualWeightKg > order.ExpectedWeightKg;
            var isCbmHigher = actualCbm > order.ExpectedCbm;
            var weightSign = isWeightHigher ? "+" : "-";
            var cbmSign = isCbmHigher ? "+" : "-";

            var appendixReason = $"Phát hiện chênh lệch thực tế khi kiểm đếm QC tại Hub (Trọng lượng chênh lệch: {weightSign}{weightDiff:0.##}%, Thể tích chênh lệch: {cbmSign}{cbmDiff:0.##}%).";

            var appendixResult = await _appendixService.GenerateAppendixAsync(
                order.OrderId,
                null,
                appendixReason,
                salesUserId);

            if (!appendixResult.Success)
            {
                _logger.LogError("Failed to automatically generate contract appendix: {Message}", appendixResult.Message);
            }

            var appendixIdStr = appendixResult.Success ? appendixResult.Data!.AppendixId.ToString() : "";
            var appendixNumberStr = appendixResult.Success ? appendixResult.Data!.AppendixNumber : "";

            // Send notification to Sales, Admin, and Manager
            await EnsureNotificationTemplateAsync("NOTI_QC_DISCREPANCY", cancellationToken);
            var salesUsers = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role != null && (u.Role.RoleName.ToLower() == "sales" || u.Role.RoleName.ToLower() == "admin" || u.Role.RoleName.ToLower() == "manager"))
                .ToListAsync(cancellationToken);

            foreach (var user in salesUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    NotiId = Guid.NewGuid(),
                    UserId = user.UserId,
                    SenderId = request.ReceiverId,
                    TemplateId = "NOTI_QC_DISCREPANCY",
                    Params = JsonSerializer.Serialize(new { 
                        Tracking_Code = order.TrackingCode, 
                        Pdf_URL = pdfUrl ?? "",
                        Appendix_Id = appendixIdStr,
                        Appendix_Number = appendixNumberStr
                    }),
                    OrderId = order.OrderId,
                    IsRead = false,
                    CreatedAt = now
                });
            }
            await _context.SaveChangesAsync(cancellationToken);
        }

        if (hasDiscrepancy)
        {
            _logger.LogWarning(
                "Inbound QC discrepancy detected lpn={LpnCode} order={OrderId} maxDiff={MaxDiffPercent}",
                lpn.LpnCode,
                order.OrderId,
                maxDiff);
        }

        return new ProcessInboundQcResponse
        {
            Success = true,
            Message = hasDiscrepancy
                ? "QC completed. LPN created and placed on DISCREPANCY_HOLD."
                : "QC passed successfully. LPN ready for putaway.",
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            State = lpn.State.ToString(),
            ReceiptId = receipt?.ReceiptId,
            DiffPercent = maxDiff,
            PdfUrl = pdfUrl
        };
    }

    private async Task EnsureNotificationTemplateAsync(string templateId, CancellationToken cancellationToken)
    {
        var existing = await _context.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == templateId, cancellationToken);

        var typeId = await _context.Messagetypes
            .Where(t => t.TypeName == "ORDER_STATUS")
            .Select(t => (Guid?)t.TypeId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!typeId.HasValue)
        {
            var type = new Messagetype
            {
                TypeId = Guid.NewGuid(),
                TypeName = "ORDER_STATUS",
                Description = "Cập nhật trạng thái đơn hàng, báo giá, hợp đồng"
            };
            _context.Messagetypes.Add(type);
            await _context.SaveChangesAsync(cancellationToken);
            typeId = type.TypeId;
        }

        var expectedTitle = "Đơn hàng {{Tracking_Code}} bị giữ lại do chênh lệch QC";
        var expectedBody = "Phát hiện chênh lệch >5% tại Inbound QC. Biên bản bất thường: {{Pdf_URL}}. Phụ lục hợp đồng nháp: {{Appendix_Number}} (ID: {{Appendix_Id}})";

        if (existing != null)
        {
            if (existing.BodyTemplate != expectedBody || existing.TitleTemplate != expectedTitle)
            {
                existing.TitleTemplate = expectedTitle;
                existing.BodyTemplate = expectedBody;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            _context.NotificationTemplates.Add(new NotificationTemplate
            {
                TemplateId = templateId,
                TypeId = typeId.Value,
                TitleTemplate = expectedTitle,
                BodyTemplate = expectedBody,
                Channel = "IN_APP",
                Status = "ACTIVE"
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static ProcessInboundQcResponse Failure(string message)
        => new() { Success = false, Message = message };

    private static decimal CalculateCbm(decimal lengthCm, decimal widthCm, decimal heightCm, int quantity)
        => Math.Round(lengthCm * widthCm * heightCm * Math.Max(quantity, 1) / 1_000_000m, 4);

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            return actual > 0 ? 100m : 0m;

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }

    private static ProductCategory ParseProductCategory(string? value)
        => Enum.TryParse<ProductCategory>(NormalizeCategory(value), true, out var category)
            ? category
            : ProductCategory.FOOD;

    private static string NormalizeCategory(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Replace(" ", "_")
            .Replace("-", "_")
            .ToUpperInvariant();

    private static decimal? ParseTemperature(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value
            .Replace("Â°C", "", StringComparison.OrdinalIgnoreCase)
            .Replace("°C", "", StringComparison.OrdinalIgnoreCase)
            .Replace("C", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(normalized, out var temp) ? temp : null;
    }

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static string GenerateCode(string prefix)
        => $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
}
