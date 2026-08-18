using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Helpers;
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

        var receiver = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == request.ReceiverId, cancellationToken);

        if (receiver == null)
            return Failure("Receiver user was not found.");

        var asn = await _context.InboundAsns
            .Include(a => a.Order)
                .ThenInclude(o => o.OrderDimension)
            .Include(a => a.Order)
                .ThenInclude(o => o.PackageVariants)
            .FirstOrDefaultAsync(a => a.AsnId == request.AsnId, cancellationToken);

        if (asn?.Order == null)
            return Failure("ASN or linked order was not found.");

        var warehouseId = request.WarehouseId != Guid.Empty
            ? request.WarehouseId
            : (receiver.WarehouseId ?? asn.WarehouseId);

        if (!warehouseId.HasValue || warehouseId.Value == Guid.Empty)
            return Failure("WarehouseId is required and could not be determined.");

        if (receiver.WarehouseId.HasValue && receiver.WarehouseId.Value != Guid.Empty && asn.WarehouseId.HasValue && asn.WarehouseId.Value != warehouseId.Value)
            return Failure("ASN does not belong to current receiver warehouse.");

        if (asn.WarehouseId.HasValue && asn.WarehouseId.Value != warehouseId.Value)
            return Failure("ASN does not belong to current receiver warehouse.");

        var order = asn.Order;
        if (order.OrderDimension == null)
            return Failure("Expected order measurements were not found.");

        var normalizedMeasurements = NormalizeMeasurements(order, request, out var measurementError);
        if (normalizedMeasurements == null)
            return Failure(measurementError!);

        var measurementResults = new List<QcMeasurementResult>();
        foreach (var input in normalizedMeasurements)
        {
            var measurement = input.Measurement;
            if (measurement.ActualWeightKg <= 0
                || measurement.LengthCm <= 0
                || measurement.WidthCm <= 0
                || measurement.HeightCm <= 0)
            {
                return Failure("Actual weight and dimensions must be greater than 0 for every package size.");
            }

            var uploadedUrls = new List<string>();
            foreach (var file in measurement.EvidenceImages)
            {
                if (file.Length <= 0 || file.Length > 10 * 1024 * 1024)
                    return Failure($"File {file.FileName} must be non-empty and cannot exceed 10MB.");

                uploadedUrls.Add(await _fileService.UploadFileAsync(file));
            }

            var quantity = measurement.Quantity;
            var expectedWeightKg = input.PackageVariant == null
                ? order.OrderDimension.ExpectedWeightKg
                : Math.Round(input.PackageVariant.ExpectedUnitWeightKg * quantity, 2);
            var expectedCbm = input.PackageVariant == null
                ? InboundQcMeasurementCalculator.CalculateExpectedCbm(order.OrderDimension, order.Quantity)
                : Math.Round(
                    input.PackageVariant.LengthCm
                    * input.PackageVariant.WidthCm
                    * input.PackageVariant.HeightCm
                    * quantity / 1_000_000m,
                    4);
            var actualCbm = InboundQcMeasurementCalculator.CalculateCbm(
                measurement.LengthCm,
                measurement.WidthCm,
                measurement.HeightCm,
                quantity);
            var weightDiff = CalculateDiffPercent(expectedWeightKg, measurement.ActualWeightKg);
            var cbmDiff = CalculateDiffPercent(expectedCbm, actualCbm);
            var maxMeasurementDiff = Math.Max(weightDiff, cbmDiff);

            measurementResults.Add(new QcMeasurementResult(
                input.PackageVariant,
                measurement,
                quantity,
                expectedWeightKg,
                expectedCbm,
                actualCbm,
                weightDiff,
                cbmDiff,
                maxMeasurementDiff,
                maxMeasurementDiff > DiscrepancyThresholdPercent,
                uploadedUrls.Count > 0 ? string.Join(",", uploadedUrls) : null));
        }

        var now = DbNow();
        var totalActualWeightKg = measurementResults.Sum(result => result.Measurement.ActualWeightKg);
        var totalActualCbm = measurementResults.Sum(result => result.ActualCbm);
        var maxDiff = measurementResults.Max(result => result.MaxDiff);
        var hasDiscrepancy = measurementResults.Any(result => result.HasDiscrepancy);

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
                RecordedTemperature = measurementResults.Select(result => result.Measurement.Temperature).FirstOrDefault(value => value.HasValue),
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
            receipt.RecordedTemperature = measurementResults.Select(result => result.Measurement.Temperature).FirstOrDefault(value => value.HasValue);
            receipt.TotalExpectedQty = order.Quantity;
            receipt.TotalActualQty = order.Quantity;
            receipt.Note = hasDiscrepancy ? "QC discrepancy hold." : "QC passed and waiting putaway.";
        }

        var lpns = measurementResults
            .GroupBy(result => NormalizeLpnGroupKey(result.Measurement.LpnGroupKey))
            .Select(group =>
            {
                var groupResults = group.ToList();
                var lpnId = Guid.NewGuid();
                var packageLines = groupResults.Select(result => new LpnPackageVariantLine
                {
                    LpnPackageVariantLineId = Guid.NewGuid(),
                    LpnId = lpnId,
                    OrderPackageVariantId = result.PackageVariant?.OrderPackageVariantId,
                    VariantName = result.PackageVariant?.VariantName ?? "Default size",
                    PackingType = result.PackageVariant?.PackingType ?? order.PackingType,
                    Quantity = result.Quantity,
                    ExpectedWeightKg = result.ExpectedWeightKg,
                    ActualWeightKg = result.Measurement.ActualWeightKg,
                    ExpectedCbm = result.ExpectedCbm,
                    ActualCbm = result.ActualCbm,
                    LengthCm = result.Measurement.LengthCm,
                    WidthCm = result.Measurement.WidthCm,
                    HeightCm = result.Measurement.HeightCm,
                    RecordedTemperature = result.Measurement.Temperature,
                    DiffPercent = result.MaxDiff,
                    HasDiscrepancy = result.HasDiscrepancy,
                    EvidenceImageUrl = result.EvidenceImageUrl,
                    CreatedAt = now
                }).ToList();
                var discrepantResults = groupResults.Where(result => result.HasDiscrepancy).ToList();

                return new Lpn
                {
                    LpnId = lpnId,
                    LpnCode = GenerateCode("LPN"),
                    OrderId = order.OrderId,
                    CustomerId = order.CustomerId,
                    ReceiptId = receipt.ReceiptId,
                    TripId = order.MasterTripId,
                    Quantity = groupResults.Sum(result => result.Quantity),
                    ActualWeightKg = groupResults.Sum(result => result.Measurement.ActualWeightKg),
                    ActualCbm = groupResults.Sum(result => result.ActualCbm),
                    LengthCm = groupResults.Count == 1 ? groupResults[0].Measurement.LengthCm : null,
                    WidthCm = groupResults.Count == 1 ? groupResults[0].Measurement.WidthCm : null,
                    HeightCm = groupResults.Count == 1 ? groupResults[0].Measurement.HeightCm : null,
                    RequiredTemperature = ParseTemperature(order.TempCondition),
                    RecordedTemperature = groupResults.Select(result => result.Measurement.Temperature).FirstOrDefault(value => value.HasValue),
                    State = discrepantResults.Count > 0 ? LpnState.DISCREPANCY_HOLD : LpnState.RECEIVING,
                    DiscrepancyReason = discrepantResults.Count > 0
                        ? $"Package size discrepancy: {string.Join(", ", discrepantResults.Select(result => result.PackageVariant?.VariantName ?? "Default size"))}."
                        : null,
                    EvidenceImageUrl = string.Join(",", groupResults
                        .Select(result => result.EvidenceImageUrl)
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    SlaDeadline = now.AddHours(24),
                    CreatedAt = now,
                    PackageVariantLines = packageLines
                };
            })
            .ToList();

        _context.Lpns.AddRange(lpns);
        var primaryLpn = lpns[0];

        asn.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "QC_PASSED";
        if (order.OrderDimension != null)
        {
            order.OrderDimension.ActualWeightKg = totalActualWeightKg;
            order.OrderDimension.ActualCbm = totalActualCbm;
        }
        order.Status = hasDiscrepancy ? "DISCREPANCY_HOLD" : "RECEIVING";

        if (!hasDiscrepancy)
        {
            await _context.SaveChangesAsync(cancellationToken);

            var receiptPdfBytes = await _mediator.Send(
                new ColdChainX.Application.Features.Inbound.Queries.GenerateReceiptPdfQuery(receipt.ReceiptId),
                cancellationToken);
            var receiptPdfUrl = await _fileService.UploadFileAsync(
                receiptPdfBytes,
                $"grn-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf");
            receipt.PdfUrl = receiptPdfUrl;
            _context.TransportDocuments.Add(new TransportDocument
            {
                DocId = Guid.NewGuid(),
                OrderId = order.OrderId,
                DocType = "WAREHOUSE_RECEIPT",
                ImageUrl = receiptPdfUrl,
                UploadedBy = request.ReceiverId,
                CreatedAt = now
            });
            await _context.SaveChangesAsync(cancellationToken);

            return new ProcessInboundQcResponse
            {
                Success = true,
                Message = $"QC passed successfully. {lpns.Count} LPN(s) are ready for putaway.",
                LpnId = primaryLpn.LpnId,
                LpnCode = primaryLpn.LpnCode,
                State = primaryLpn.State.ToString(),
                ReceiptId = receipt?.ReceiptId,
                DiffPercent = maxDiff,
                PdfUrl = receiptPdfUrl,
                Lpns = BuildLpnResponses(lpns)
            };
        }

        if (receipt == null)
            return Failure("Warehouse receipt could not be created for discrepancy QC.");

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);

                string? pdfUrl = null;
                if (hasDiscrepancy && receipt != null)
                {
                    var pdfBytes = await _mediator.Send(new ColdChainX.Application.Features.Discrepancy.Queries.GenerateDiscrepancyPdfQuery(receipt.ReceiptId), cancellationToken);

                    var pdfFileName = $"discrepancy-{order.TrackingCode}-{now:yyyyMMddHHmmss}.pdf";
                    pdfUrl = await _fileService.UploadFileAsync(pdfBytes, pdfFileName);

                    receipt.PdfUrl = pdfUrl;

                    var existingDoc = await _context.TransportDocuments
                        .FirstOrDefaultAsync(d => d.OrderId == order.OrderId && d.DocType == "DISCREPANCY_REPORT", cancellationToken);

            if (existingDoc == null)
            {
                _context.TransportDocuments.Add(new TransportDocument
                {
                    DocId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    DocType = "DISCREPANCY_REPORT",
                    ImageUrl = pdfUrl,
                    UploadedBy = request.ReceiverId,
                    CreatedAt = now
                });
            }
            else
            {
                existingDoc.ImageUrl = pdfUrl;
                existingDoc.UploadedBy = request.ReceiverId;
                existingDoc.CreatedAt = now;
            }

                    await _context.SaveChangesAsync(cancellationToken);

                    var salesUserId = await _context.Users
                        .Include(u => u.Role)
                        .Where(u => u.Role != null && u.Role.RoleName.ToLower() == "sales")
                        .Select(u => u.UserId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (salesUserId == Guid.Empty)
                    {
                        salesUserId = await _context.Users
                            .Include(u => u.Role)
                            .Where(u => u.Role != null && (u.Role.RoleName.ToLower() == "admin" || u.Role.RoleName.ToLower() == "warehouseworker"))
                            .Select(u => u.UserId)
                            .FirstOrDefaultAsync(cancellationToken);
                    }

                    if (salesUserId == Guid.Empty)
                    {
                        salesUserId = request.ReceiverId;
                    }

                    var aggregateWeightDiff = CalculateDiffPercent(order.OrderDimension.ExpectedWeightKg, totalActualWeightKg);
                    var aggregateCbmDiff = CalculateDiffPercent(order.OrderDimension.ExpectedCbm, totalActualCbm);
                    var weightSign = totalActualWeightKg >= order.OrderDimension.ExpectedWeightKg ? "+" : "-";
                    var cbmSign = totalActualCbm >= order.OrderDimension.ExpectedCbm ? "+" : "-";
                    var discrepantSizes = string.Join(", ", measurementResults
                        .Where(result => result.HasDiscrepancy)
                        .Select(result => result.PackageVariant?.VariantName ?? "Default size"));

                    var appendixReason = $"Inbound QC detected differences for package size(s): {discrepantSizes}. "
                                         + $"Aggregate weight difference: {weightSign}{aggregateWeightDiff:0.##}%, "
                                         + $"aggregate volume difference: {cbmSign}{aggregateCbmDiff:0.##}%.";

                    var appendixResult = await _appendixService.GenerateAppendixAsync(
                        order.OrderId,
                        null,
                        appendixReason,
                        salesUserId);

                    if (!appendixResult.Success || appendixResult.Data == null || string.IsNullOrWhiteSpace(appendixResult.Data.DraftHtmlContent))
                    {
                        var appendixFailureMessage = appendixResult.Success
                            ? "Contract appendix draft was generated without HTML content."
                            : appendixResult.Message;

                        _logger.LogError(
                            "Failed to automatically generate contract appendix for order {OrderId} tracking {TrackingCode}: {Message}",
                            order.OrderId,
                            order.TrackingCode,
                            appendixFailureMessage);

                        await transaction.RollbackAsync(cancellationToken);
                        return Failure($"Inbound QC discrepancy hold was not saved because contract appendix draft generation failed: {appendixFailureMessage}");
                    }

                    var appendixIdStr = appendixResult.Success ? appendixResult.Data!.AppendixId.ToString() : "";
                    var appendixNumberStr = appendixResult.Success ? appendixResult.Data!.AppendixNumber : "";

                    // Send notification to Sales, Admin, and WarehouseOperator
                    await EnsureNotificationTemplateAsync("NOTI_QC_DISCREPANCY", cancellationToken);
                    var salesUsers = await _context.Users
                        .Include(u => u.Role)
                        .Where(u => u.Role != null && (u.Role.RoleName.ToLower() == "sales" || u.Role.RoleName.ToLower() == "admin" || u.Role.RoleName.ToLower() == "warehouseworker"))
                        .ToListAsync(cancellationToken);

                    foreach (var user in salesUsers)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            NotiId = Guid.NewGuid(),
                            UserId = user.UserId,
                            SenderId = request.ReceiverId,
                            TemplateId = "NOTI_QC_DISCREPANCY",
                            Params = JsonSerializer.Serialize(new
                            {
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
                        primaryLpn.LpnCode,
                        order.OrderId,
                        maxDiff);
                }

                await transaction.CommitAsync(cancellationToken);

                return new ProcessInboundQcResponse
                {
                    Success = true,
                    Message = hasDiscrepancy
                        ? $"QC completed. {lpns.Count} LPN(s) created; discrepant sizes were placed on hold."
                        : $"QC passed successfully. {lpns.Count} LPN(s) are ready for putaway.",
                    LpnId = primaryLpn.LpnId,
                    LpnCode = primaryLpn.LpnCode,
                    State = primaryLpn.State.ToString(),
                    ReceiptId = receipt?.ReceiptId,
                    DiffPercent = maxDiff,
                    PdfUrl = pdfUrl,
                    Lpns = BuildLpnResponses(lpns)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to complete inbound QC discrepancy hold for order {OrderId} tracking {TrackingCode}. Database changes were rolled back.",
                    order.OrderId,
                    order.TrackingCode);

                await transaction.RollbackAsync(cancellationToken);
                return Failure($"Inbound QC discrepancy hold was not saved because contract appendix generation failed: {ex.Message}");
            }
        });
    }

    private static List<QcMeasurementInput>? NormalizeMeasurements(
        TransportOrder order,
        ProcessInboundQcCommand request,
        out string? error)
    {
        error = null;
        var variants = order.PackageVariants.OrderBy(variant => variant.CreatedAt).ToList();

        if (variants.Count == 0)
        {
            if (!HasPositiveLegacyMeasurement(request))
            {
                error = "Actual weight and dimensions must be greater than 0.";
                return null;
            }

            return new List<QcMeasurementInput>
            {
                new(null, new PackageVariantQcMeasurement
                {
                    Quantity = order.Quantity,
                    LpnGroupKey = "LPN-1",
                    ActualWeightKg = request.ActualWeightKg,
                    LengthCm = request.LengthCm,
                    WidthCm = request.WidthCm,
                    HeightCm = request.HeightCm,
                    Temperature = request.Temperature,
                    EvidenceImages = request.EvidenceImages ?? new List<Microsoft.AspNetCore.Http.IFormFile>()
                })
            };
        }

        if (request.PackageMeasurements.Count == 0)
        {
            if (variants.Count != 1 || !HasPositiveLegacyMeasurement(request))
            {
                error = "PackageMeasurements must contain one measurement for every package size.";
                return null;
            }

            return new List<QcMeasurementInput>
            {
                new(variants[0], new PackageVariantQcMeasurement
                {
                    OrderPackageVariantId = variants[0].OrderPackageVariantId,
                    Quantity = variants[0].Quantity,
                    LpnGroupKey = "LPN-1",
                    ActualWeightKg = request.ActualWeightKg,
                    LengthCm = request.LengthCm,
                    WidthCm = request.WidthCm,
                    HeightCm = request.HeightCm,
                    Temperature = request.Temperature,
                    EvidenceImages = request.EvidenceImages ?? new List<Microsoft.AspNetCore.Http.IFormFile>()
                })
            };
        }

        var variantById = variants.ToDictionary(variant => variant.OrderPackageVariantId);
        var rowCountByVariant = request.PackageMeasurements
            .GroupBy(measurement => measurement.OrderPackageVariantId)
            .ToDictionary(group => group.Key, group => group.Count());
        var result = new List<QcMeasurementInput>();
        foreach (var measurement in request.PackageMeasurements)
        {
            if (!variantById.TryGetValue(measurement.OrderPackageVariantId, out var variant))
            {
                error = $"Package size {measurement.OrderPackageVariantId} does not belong to this order.";
                return null;
            }

            if (measurement.Quantity <= 0)
            {
                if (rowCountByVariant[measurement.OrderPackageVariantId] != 1)
                {
                    error = "Quantity is required when a package size is split across multiple LPN groups.";
                    return null;
                }

                measurement.Quantity = variant.Quantity;
            }

            measurement.LpnGroupKey = NormalizeLpnGroupKey(measurement.LpnGroupKey);
            result.Add(new QcMeasurementInput(variant, measurement));
        }

        if (result.GroupBy(item => new
            {
                item.Measurement.OrderPackageVariantId,
                GroupKey = NormalizeLpnGroupKey(item.Measurement.LpnGroupKey)
            }).Any(group => group.Count() > 1))
        {
            error = "A package size can only appear once inside the same LPN group.";
            return null;
        }

        foreach (var variant in variants)
        {
            var measuredQuantity = result
                .Where(item => item.PackageVariant?.OrderPackageVariantId == variant.OrderPackageVariantId)
                .Sum(item => item.Measurement.Quantity);
            if (measuredQuantity != variant.Quantity)
            {
                error = $"Package size '{variant.VariantName ?? variant.OrderPackageVariantId.ToString()}' requires quantity {variant.Quantity}, but QC assigned {measuredQuantity}.";
                return null;
            }
        }

        return result;
    }

    private static bool HasPositiveLegacyMeasurement(ProcessInboundQcCommand request)
        => request.ActualWeightKg > 0
           && request.LengthCm > 0
           && request.WidthCm > 0
           && request.HeightCm > 0;

    private static string NormalizeLpnGroupKey(string? value)
        => string.IsNullOrWhiteSpace(value) ? "LPN-1" : value.Trim().ToUpperInvariant();

    private static List<ProcessInboundQcItemResponse> BuildLpnResponses(IReadOnlyList<Lpn> lpns)
        => lpns.Select(lpn => new ProcessInboundQcItemResponse
        {
            LpnId = lpn.LpnId,
            LpnCode = lpn.LpnCode,
            State = lpn.State.ToString(),
            Quantity = lpn.Quantity,
            ActualWeightKg = lpn.ActualWeightKg,
            ActualCbm = lpn.ActualCbm,
            DiffPercent = lpn.PackageVariantLines.Count == 0
                ? 0
                : lpn.PackageVariantLines.Max(line => line.DiffPercent),
            PackageLines = lpn.PackageVariantLines.Select(line => new ProcessInboundQcPackageLineResponse
            {
                LpnPackageVariantLineId = line.LpnPackageVariantLineId,
                OrderPackageVariantId = line.OrderPackageVariantId,
                PackageVariantName = line.VariantName,
                PackingType = line.PackingType,
                Quantity = line.Quantity,
                ActualWeightKg = line.ActualWeightKg,
                ActualCbm = line.ActualCbm,
                DiffPercent = line.DiffPercent,
                HasDiscrepancy = line.HasDiscrepancy
            }).ToList()
        }).ToList();

    private sealed record QcMeasurementInput(
        OrderPackageVariant? PackageVariant,
        PackageVariantQcMeasurement Measurement);

    private sealed record QcMeasurementResult(
        OrderPackageVariant? PackageVariant,
        PackageVariantQcMeasurement Measurement,
        int Quantity,
        decimal ExpectedWeightKg,
        decimal ExpectedCbm,
        decimal ActualCbm,
        decimal WeightDiff,
        decimal CbmDiff,
        decimal MaxDiff,
        bool HasDiscrepancy,
        string? EvidenceImageUrl);

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

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            throw new ArgumentOutOfRangeException(nameof(expected), "Expected measurement must be greater than 0.");

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

        var normalized = new string(value
                .Where(ch => char.IsDigit(ch) || ch == '-' || ch == '.' || ch == ',')
                .ToArray())
            .Replace(',', '.')
            .Trim();

        return decimal.TryParse(normalized, out var temp) ? temp : null;
    }

    private static DateTime DbNow()
        => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    private static string GenerateCode(string prefix)
        => $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
}
