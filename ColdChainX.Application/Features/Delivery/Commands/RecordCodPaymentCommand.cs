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

/// <summary>
/// Bước 3 — Thu tiền COD sau khi bàn giao hàng và khách đã ký nhận.
/// Chỉ được gọi sau khi ConfirmHandoverCommand đã hoàn tất (HandoverConfirmedAt != null).
/// Sinh ePOD hoàn chỉnh (có chữ ký + ảnh bằng chứng + COD) và thông báo Admin/Sales.
/// </summary>
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

    public RecordCodPaymentCommandHandler(
        IApplicationDbContext context,
        IFileService fileService,
        IPdfGeneratorService pdfGeneratorService,
        IDeliveryEventService deliveryEvents)
    {
        _context = context;
        _fileService = fileService;
        _pdfGeneratorService = pdfGeneratorService;
        _deliveryEvents = deliveryEvents;
    }

    public async Task<ApiResponse<RecordCodPaymentResponse>> Handle(
        RecordCodPaymentCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. Load ePOD with full order/trip data
        var epod = await _context.DeliveryEpods
            .Include(e => e.Order)
                .ThenInclude(o => o!.Customer)
            .Include(e => e.ReturnedItems)
            .FirstOrDefaultAsync(e => e.EpodId == command.EpodId, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"ePOD '{command.EpodId}' was not found.");

        // 2. Guard: Handover must have been confirmed first (Step 2)
        if (epod.HandoverConfirmedAt == null)
            throw new ValidationException("Cannot record payment. Handover has not been confirmed yet (Step 2 is required first).");

        // 3. Guard: Idempotency — do not allow double payment
        if (epod.PaymentStatus == "PAID" || epod.PaymentConfirmedAt != null)
            throw new ValidationException("Payment for this ePOD has already been recorded.");

        // 4. Validate payment method
        var method = request.PaymentMethod?.ToUpper();
        if (method != "CASH" && method != "QR")
            throw new ValidationException("Payment method must be 'CASH' or 'QR'.");

        // 5. Record COD discrepancy (do NOT hard-reject — operational reality allows over/under)
        var expectedCod = epod.CodAmount ?? 0m;
        var codDiscrepancy = request.CodAmountPaid - expectedCod;
        // Will be appended to note for accountant review during COD handover reconciliation

        // 6. Validate driver is on this trip
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

        // 7. Upload payment evidence photo (if provided)
        string? paymentEvidenceUrl = null;
        if (request.PaymentEvidenceFile != null)
            paymentEvidenceUrl = await _fileService.UploadFileAsync(request.PaymentEvidenceFile);
        else if (method == "CASH")
            throw new ValidationException("Payment evidence photo (receipt) is required for cash payments.");

        var now = DateTime.UtcNow;

        // 8. For QR payments: set AWAITING_QR status and return immediately
        // (PayOS webhook will confirm and finalize ePOD PDF once bank confirms)
        if (method == "QR")
        {
            epod.PaymentMethod = "QR";
            epod.CodAmountPaid = request.CodAmountPaid;
            epod.PaymentStatus = "AWAITING_QR";
            epod.PaymentEvidenceImageUrl = paymentEvidenceUrl;
            if (Math.Abs(codDiscrepancy) > 0.01m)
                epod.Note = $"{epod.Note} [COD discrepancy: {codDiscrepancy:+0.##;-0.##} VND]".Trim();
            await _context.SaveChangesAsync(cancellationToken);

            return ApiResponse<RecordCodPaymentResponse>.SuccessResponse(new RecordCodPaymentResponse
            {
                EpodId = epod.EpodId,
                PaymentStatus = "AWAITING_QR",
                PaymentConfirmedAt = null,
                EpodPdfUrl = null,
                NextStep = "Chờ khách chuyển khoản. Webhook PayOS sẽ tự động xác nhận và sinh ePOD PDF."
            }, "Đã ghi nhận yêu cầu thanh toán QR. Vui lòng chờ xác nhận từ PayOS.");
        }

        // 9. CASH payment — confirm immediately and generate final ePOD PDF
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

                await _context.SaveChangesAsync(cancellationToken);

                // Fetch LPNs for ePOD PDF
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

                // Generate final ePOD PDF
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

                // 10. SignalR via IDeliveryEventService
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
            // Header
            EpodId = epod.EpodId.ToString("N"),
            DeliveryDate = now.ToString("dd/MM/yyyy HH:mm"),
            CompanyName = "ColdChainX Logistics",

            // Điểm giao hàng
            DestinationAddress = location?.Address ?? "Địa điểm giao hàng",
            VehiclePlateNumber = trip?.Vehicle?.TruckPlate ?? "N/A",
            DriverName = driver.FullName,
            CustomerName = order.Customer?.CompanyName ?? "Khách hàng",

            // Người nhận
            ReceiverName = epod.ReceiverName,
            ReceiverPhone = epod.ReceiverPhone,
            SignatureUrl = epod.SignImageUrl,
            HandoverTime = epod.HandoverConfirmedAt?.ToString("dd/MM/yyyy HH:mm"),

            // Nhiệt độ (từ IoT sensor)
            RecordedTemperatureCelsius = lpns.FirstOrDefault()?.RecordedTemperature ?? 4.5m,

            // Danh sách kiện hàng
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

            // COD & Thanh toán
            OrderCode = order.TrackingCode,
            CodAmountDue = epod.CodAmount ?? 0m,
            CodAmountPaid = epod.CodAmountPaid ?? 0m,
            PaymentMethod = epod.PaymentMethod,
            PaymentStatus = epod.PaymentStatus,
            PaymentConfirmedAt = now.ToString("dd/MM/yyyy HH:mm"),
            PaymentEvidenceUrl = paymentEvidenceUrl,

            // Trả hàng
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
