using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class RecordCodPaymentCommand : IRequest<ApiResponse<RecordCodPaymentResponse>>
{
    public Guid EpodId { get; set; }
    public RecordCodPaymentRequest Request { get; set; } = null!;
    public Guid UserId { get; set; }
}

public class RecordCodPaymentCommandHandler : IRequestHandler<RecordCodPaymentCommand, ApiResponse<RecordCodPaymentResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;
    private readonly IPdfGeneratorService _pdfGeneratorService;
    private readonly IDeliveryEventService _deliveryEvents;
    private readonly IPaymentGatewayService _paymentGateway;

    public RecordCodPaymentCommandHandler(
        IApplicationDbContext context,
        IFileService fileService,
        IPdfGeneratorService pdfGeneratorService,
        IDeliveryEventService deliveryEvents,
        IPaymentGatewayService paymentGateway)
    {
        _context = context;
        _fileService = fileService;
        _pdfGeneratorService = pdfGeneratorService;
        _deliveryEvents = deliveryEvents;
        _paymentGateway = paymentGateway;
    }

    public async Task<ApiResponse<RecordCodPaymentResponse>> Handle(
        RecordCodPaymentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        var epod = await _context.DeliveryEpods
            .Include(e => e.Order)
                .ThenInclude(o => o!.Customer)
            .Include(e => e.ReturnedItems)
            .FirstOrDefaultAsync(e => e.EpodId == command.EpodId, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"ePOD '{command.EpodId}' was not found.");

        if (epod.HandoverConfirmedAt == null)
            throw new ValidationException("Cannot record payment. Handover has not been confirmed yet (Step 2 is required first).");

        if (epod.PaymentStatus == "PAID" || epod.PaymentConfirmedAt != null)
            throw new ValidationException("Payment for this ePOD has already been recorded.");

        var method = request.PaymentMethod?.ToUpper();
        if (method != "CASH" && method != "QR")
            throw new ValidationException("Payment method must be 'CASH' or 'QR'.");

        var expectedCod = epod.CodAmount ?? 0m;
        if (request.CodAmountPaid <= 0 && expectedCod > 0)
        {
            request.CodAmountPaid = expectedCod;
        }
        var codDiscrepancy = request.CodAmountPaid - expectedCod;

        var order = epod.Order!;
        if (order.MasterTripId == null)
            throw new ValidationException("Order has no assigned trip.");

        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == command.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found.");

        var isAssigned = await _context.TripDrivers
            .AnyAsync(td => td.TripId == order.MasterTripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssigned)
            throw new ForbiddenException("You are not authorized to record payment for this trip.");

        string? paymentEvidenceUrl = null;
        if (request.PaymentEvidenceFile != null)
            paymentEvidenceUrl = await _fileService.UploadFileAsync(request.PaymentEvidenceFile);
        else if (method == "CASH")
            throw new ValidationException("Payment evidence photo (receipt) is required for cash payments.");

        var now = DateTime.UtcNow;

        if (method == "QR")
        {
            var payosOrderCode = Math.Abs((long)(epod.EpodId.GetHashCode()) % 9_000_000_000L + 1_000_000_000L);

            var trackingCode = epod.Order?.TrackingCode ?? epod.EpodId.ToString("N")[..8];
            var description = $"COD {trackingCode}"[..Math.Min(25, $"COD {trackingCode}".Length)];

            CreateQrResult qrResult;
            try
            {
                qrResult = await _paymentGateway.CreatePaymentLinkAsync(
                    payosOrderCode,
                    (int)request.CodAmountPaid,
                    description,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                throw new ApplicationException(
                    $"PayOS payment link creation failed: {ex.Message}. Please retry or switch to CASH payment.", ex);
            }

            epod.PaymentMethod = "QR";
            epod.CodAmountPaid = request.CodAmountPaid;
            epod.PaymentStatus = "AWAITING_QR";
            epod.PaymentEvidenceImageUrl = paymentEvidenceUrl;
            var discrepancyNote = Math.Abs(codDiscrepancy) > 0.01m
                ? $" [COD discrepancy: {codDiscrepancy:+0.##;-0.##} VND]" : "";
            epod.Note = $"{epod.Note} [PayOS:{payosOrderCode}]{discrepancyNote}".Trim();
            await _context.SaveChangesAsync(cancellationToken);

            return ApiResponse<RecordCodPaymentResponse>.SuccessResponse(new RecordCodPaymentResponse
            {
                EpodId = epod.EpodId,
                PaymentStatus = "AWAITING_QR",
                PaymentConfirmedAt = null,
                EpodPdfUrl = null,
                QrCodeUrl = qrResult.QrCodeUrl,
                CheckoutUrl = qrResult.CheckoutUrl,
                NextStep = $"Hiển thị mã QR cho khách quét. PayOS sẽ tự động xác nhận và sinh ePOD PDF khi thanh toán thành công."
            }, "Đã tạo mã QR PayOS thành công. Vui lòng hiển thị cho khách quét.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                epod.PaymentMethod = "CASH";
                epod.CodAmountPaid = request.CodAmountPaid;
                epod.PaymentStatus = "PAID";
                epod.PaymentConfirmedAt = now;
                epod.PaymentEvidenceImageUrl = paymentEvidenceUrl;
                epod.Status = "COMPLETED";
                if (Math.Abs(codDiscrepancy) > 0.01m)
                    epod.Note = $"{epod.Note} [COD discrepancy: {codDiscrepancy:+0.##;-0.##} VND — verify at handover]".Trim();

                var cashTx = new ColdChainX.Core.Entities.PaymentTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    TransactionCode = $"TX-CASH-{now:yyyyMMddHHmmss}-{order.OrderId.ToString("N")[..6].ToUpperInvariant()}",
                    OrderId = order.OrderId,
                    CustomerId = order.CustomerId,
                    TransactionType = "IN",
                    Amount = request.CodAmountPaid,
                    PaymentMethod = "CASH",
                    ReferenceCode = "CASH_RECEIPT",
                    EvidenceImageUrl = paymentEvidenceUrl,
                    Status = "COMPLETED",
                    Note = "Thu tiền COD mặt từ khách tại bãi sau đồng kiểm OS&D",
                    CreatedAt = now,
                    CompletedAt = now,
                    CreatedBy = command.UserId
                };
                _context.PaymentTransactions.Add(cashTx);

                await _context.SaveChangesAsync(cancellationToken);

                var trip = await _context.MasterTrips
                    .Include(t => t.Vehicle)
                    .FirstOrDefaultAsync(t => t.TripId == order.MasterTripId, cancellationToken);

                var lpns = await _context.Lpns
                    .Where(l => l.OrderId == order.OrderId && l.TripId == order.MasterTripId)
                    .ToListAsync(cancellationToken);

                var stop = await _context.TripStops
                    .FirstOrDefaultAsync(ts => ts.TripId == order.MasterTripId &&
                                               ts.LocationId == order.DestLocation, cancellationToken);

                var location = stop?.LocationId != null
                    ? await _context.Locations.FirstOrDefaultAsync(l => l.LocationId == stop.LocationId, cancellationToken)
                    : null;

                var pdfData = BuildEpodPdfData(epod, order, trip, driver, location, lpns, paymentEvidenceUrl, now);

                byte[] pdfBytes;
                try { pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("Epod", pdfData); }
                catch (Exception ex) { throw new ExternalServiceException($"Failed to generate ePOD PDF: {ex.Message}"); }

                string epodPdfUrl;
                try { epodPdfUrl = await _fileService.UploadFileAsync(pdfBytes, $"epod_{order.TrackingCode}_{now:yyyyMMddHHmmss}.pdf"); }
                catch (Exception ex) { throw new ExternalServiceException($"Failed to upload ePOD PDF: {ex.Message}"); }

                epod.PdfUrl = epodPdfUrl;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                await _deliveryEvents.NotifyCodPaymentConfirmedAsync(
                    order.OrderId, order.TrackingCode, epod.EpodId,
                    request.CodAmountPaid, "CASH",
                    order.Status, epodPdfUrl, epod.ReceiverName,
                    cancellationToken);

                return ApiResponse<RecordCodPaymentResponse>.SuccessResponse(new RecordCodPaymentResponse
                {
                    EpodId = epod.EpodId,
                    PaymentStatus = "PAID",
                    PaymentConfirmedAt = now,
                    EpodPdfUrl = epodPdfUrl,
                    NextStep = $"POST /api/stops/{{stopId}}/departures — Xuất phát điểm giao hàng tiếp theo"
                }, "Thu tiền COD thành công. ePOD hoàn chỉnh đã được sinh và gửi cho quản lý.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private static object BuildEpodPdfData(
        Core.Entities.DeliveryEpod epod,
        Core.Entities.TransportOrder order,
        Core.Entities.MasterTrip? trip,
        Core.Entities.Driver driver,
        Core.Entities.Location? location,
        List<Core.Entities.Lpn> lpns,
        string? paymentEvidenceUrl,
        DateTime now)
    {
        return new
        {
            EpodId = epod.EpodId.ToString("N"),
            DeliveryDate = now.ToString("dd/MM/yyyy HH:mm"),
            CompanyName = "ColdChainX Logistics",

            DestinationAddress = location?.Address ?? "Địa điểm giao hàng",
            VehiclePlateNumber = trip?.Vehicle?.TruckPlate ?? "N/A",
            DriverName = driver.FullName,
            CustomerName = order.Customer?.CompanyName ?? "Khách hàng",

            ReceiverName = epod.ReceiverName,
            ReceiverPhone = epod.ReceiverPhone,
            SignatureUrl = epod.SignImageUrl,
            HandoverTime = epod.HandoverConfirmedAt?.ToString("dd/MM/yyyy HH:mm"),

            RecordedTemperatureCelsius = lpns.FirstOrDefault()?.RecordedTemperature ?? 4.5m,

            Items = lpns.Select((l, idx) => new
            {
                Index = idx + 1,
                ItemName = order.ItemName,
                LpnCode = l.LpnCode,
                Unit = order.PackingType ?? "PALLET",
                Quantity = l.Quantity,
                WeightKg = l.ActualWeightKg,
                Status = l.State == ColdChainX.Core.Enums.LpnState.DELIVERED ? "Đã nhận ✓" : "Từ chối ✗",
                RejectionReason = l.DiscrepancyReason,
                EvidencePhotoUrl = l.EvidenceImageUrl
            }).ToList(),

            OrderCode = order.TrackingCode,
            CodAmountDue = epod.CodAmount ?? 0m,
            CodAmountPaid = epod.CodAmountPaid ?? 0m,
            PaymentMethod = epod.PaymentMethod,
            PaymentStatus = epod.PaymentStatus,
            PaymentConfirmedAt = now.ToString("dd/MM/yyyy HH:mm"),
            PaymentEvidenceUrl = paymentEvidenceUrl,

            ReturnedItems = epod.ReturnedItems.Select(ri => new
            {
                ItemName = ri.ItemName,
                ItemCode = ri.ItemCode,
                Quantity = ri.ReturnedQty,
                ReasonType = ri.ReasonType,
                ReasonNote = ri.ReasonNote
            }).ToList()
        };
    }
}
