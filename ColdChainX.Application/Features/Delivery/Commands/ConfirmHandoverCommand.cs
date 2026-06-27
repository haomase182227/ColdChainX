using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

/// <summary>
/// Bước 2 — Nghiệm thu hàng + Ký nhận tại điểm dừng (Handover Confirmation).
/// Xử lý: upload chữ ký khách + ảnh bằng chứng, cập nhật trạng thái LPN/Order,
/// sinh Biên bản Giao nhận PDF (handover receipt) và gửi thông báo SignalR.
/// </summary>
public class ConfirmHandoverCommand : IRequest<ApiResponse<HandoverConfirmResponse>>
{
    public Guid StopId { get; set; }
    public HandoverConfirmRequest Request { get; set; } = null!;
    public Guid UserId { get; set; }
}

public class ConfirmHandoverCommandHandler : IRequestHandler<ConfirmHandoverCommand, ApiResponse<HandoverConfirmResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;
    private readonly IPdfGeneratorService _pdfGeneratorService;
    private readonly IDeliveryEventService _deliveryEvents;

    public ConfirmHandoverCommandHandler(
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

    public async Task<ApiResponse<HandoverConfirmResponse>> Handle(
        ConfirmHandoverCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. Load TripStop with navigation properties and validate driver has checked in
        var stop = await _context.TripStops
            .Include(ts => ts.Location)
            .Include(ts => ts.Trip)
                .ThenInclude(t => t!.Vehicle)
            .FirstOrDefaultAsync(ts => ts.StopId == command.StopId, cancellationToken);

        if (stop == null)
            throw new NotFoundException($"Stop '{command.StopId}' was not found.");

        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Cannot confirm handover. Driver must check in at this stop first (POST /api/stops/{stopId}/check-ins).");

        // 2. Load Order and validate it belongs to this stop's location
        var order = await _context.TransportOrders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, cancellationToken);

        if (order == null)
            throw new NotFoundException($"Order '{request.OrderId}' was not found.");

        if (order.DestLocation != stop.LocationId)
            throw new ValidationException("This order's destination does not match the current stop's location.");

        if (order.MasterTripId == null)
            throw new ValidationException("Order has not been assigned to a trip yet.");

        var trip = stop.Trip;
        if (trip == null)
            throw new NotFoundException("Trip data not found for this stop.");

        // 3. Validate driver is assigned to this trip
        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == command.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found for current user.");

        var isAssigned = await _context.TripDrivers
            .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssigned)
            throw new ForbiddenException("You are not authorized to confirm handover for this trip.");

        // 4. Idempotency guard — prevent duplicate handover confirmations
        var existingEpod = await _context.DeliveryEpods
            .FirstOrDefaultAsync(e => e.OrderId == order.OrderId && e.HandoverConfirmedAt != null, cancellationToken);
        if (existingEpod != null)
            throw new ConflictException($"Handover for order '{order.TrackingCode}' has already been confirmed at {existingEpod.HandoverConfirmedAt:O} (ePOD: {existingEpod.EpodId}). Cannot confirm again.");

        // 5. Fetch LPNs for this order on this trip
        var lpns = await _context.Lpns
            .Where(l => l.OrderId == order.OrderId && l.TripId == trip.TripId)
            .ToListAsync(cancellationToken);

        if (lpns.Count == 0)
            throw new ValidationException("No LPNs found for this order on this trip. Ensure dispatch was completed.");

        // Auto-accept all LPNs if driver did not specify any
        if (request.Lpns == null || request.Lpns.Count == 0)
        {
            request.Lpns = lpns.Select(l => new HandoverConfirmLpnInput
            {
                LpnId = l.LpnId,
                IsAccepted = true
            }).ToList();
        }

        // 6. Validate each submitted LPN input
        foreach (var lpnInput in request.Lpns)
        {
            if (!lpns.Any(l => l.LpnId == lpnInput.LpnId))
                throw new ValidationException($"LPN '{lpnInput.LpnId}' does not belong to order '{order.TrackingCode}' on this trip.");

            if (!lpnInput.IsAccepted)
            {
                if (string.IsNullOrWhiteSpace(lpnInput.RejectionReason))
                    throw new ValidationException($"RejectionReason is required when LPN '{lpnInput.LpnId}' is rejected.");

                if (lpnInput.EvidencePhotoFile == null)
                    throw new ValidationException($"Evidence photo (EvidencePhotoFile) is required when LPN '{lpnInput.LpnId}' is rejected.");
            }
        }

        // 7. Upload signature (required) and optional handover photo — in parallel
        var signatureTask = _fileService.UploadFileAsync(request.SignatureFile);
        var handoverPhotoTask = request.HandoverPhotoFile != null
            ? _fileService.UploadFileAsync(request.HandoverPhotoFile)
            : Task.FromResult<string>(null!);

        await Task.WhenAll(signatureTask, handoverPhotoTask);
        var signatureUrl = signatureTask.Result;
        var handoverPhotoUrl = handoverPhotoTask.Result;

        // 8. Upload per-LPN evidence/condition photos in parallel
        var lpnUploadTasks = request.Lpns.Select(async input =>
        {
            string? evidenceUrl = input.EvidencePhotoFile != null
                ? await _fileService.UploadFileAsync(input.EvidencePhotoFile)
                : null;
            string? conditionUrl = input.ConditionPhotoFile != null
                ? await _fileService.UploadFileAsync(input.ConditionPhotoFile)
                : null;
            return (input.LpnId, evidenceUrl, conditionUrl);
        });
        var lpnUploadResults = await Task.WhenAll(lpnUploadTasks);
        var lpnUrlMap = lpnUploadResults.ToDictionary(r => r.LpnId, r => (r.evidenceUrl, r.conditionUrl));

        // 9. Get latest IoT temperature for this trip (cold chain standard fallback: 4.5°C)
        var latestTelemetry = await _context.TelemetryLogs
            .Where(t => t.TripId == trip.TripId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
        var recordedTemp = latestTelemetry?.Temperature ?? 4.5m;

        // 10. Calculate expected COD with safe divide-by-zero guard
        var expectedCod = CalculateExpectedCod(order, lpns, request.Lpns);

        var epodId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // 11. All DB writes inside a transaction (Handover stage)
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Update LPN states + create ReturnedItem records
                var hasReturnedLpns = UpdateLpnStates(
                    order, lpns, request.Lpns, lpnUrlMap, recordedTemp, epodId);

                // Create ePOD record (handover stage — payment step pending)
                var epod = new DeliveryEpod
                {
                    EpodId = epodId,
                    OrderId = order.OrderId,
                    CheckinTime = stop.ActualArrivalTime ?? now,
                    SignedAt = now,
                    HandoverConfirmedAt = now,
                    ReceiverName = request.ReceiverName,
                    ReceiverPhone = request.ReceiverPhone,
                    SignImageUrl = signatureUrl,
                    // Coordinates from Location (check-in validated GPS position)
                    SignLatitude = stop.Location?.Latitude,
                    SignLongitude = stop.Location?.Longitude,
                    DeliveryRating = request.DeliveryRating,
                    Note = request.Note,
                    Status = "HANDOVER_CONFIRMED",
                    CodAmount = expectedCod,
                    PaymentStatus = "AWAITING_PAYMENT",
                    CreatedAt = now
                };
                _context.DeliveryEpods.Add(epod);
                await _context.SaveChangesAsync(cancellationToken);

                // Generate Handover Receipt PDF (Biên bản giao nhận hàng)
                var pdfData = BuildHandoverPdfData(
                    order, trip, driver, stop.Location, lpns,
                    request, signatureUrl, handoverPhotoUrl, lpnUrlMap, recordedTemp, now);

                var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("Epod", pdfData);
                var pdfUrl = await _fileService.UploadFileAsync(
                    pdfBytes, $"handover_{order.TrackingCode}_{now:yyyyMMddHHmmss}.pdf");

                epod.HandoverPdfUrl = pdfUrl;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // 12. SignalR via IDeliveryEventService — Alert if any LPN was returned
                if (hasReturnedLpns)
                {
                    var rejectedCount = request.Lpns.Count(l => !l.IsAccepted);
                    await _deliveryEvents.NotifyHandoverPartialReturnAsync(
                        order.OrderId, order.TrackingCode, epodId,
                        rejectedCount, request.Lpns.Count,
                        order.Status, pdfUrl, cancellationToken);
                }

                return ApiResponse<HandoverConfirmResponse>.SuccessResponse(new HandoverConfirmResponse
                {
                    EpodId = epodId,
                    HandoverConfirmedAt = now,
                    OrderStatus = order.Status,
                    CodAmountDue = expectedCod,
                    HandoverPdfUrl = pdfUrl,
                    NextStep = $"POST /api/epods/{epodId}/payments — Thu tiền COD từ khách"
                }, "Nghiệm thu hàng và ký nhận thành công. Vui lòng thu tiền COD ở bước tiếp theo.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// COD tính theo tỷ lệ số lượng hàng được nhận / tổng số lượng đơn hàng.
    /// Guard: nếu CargoValue = 0 hoặc Quantity = 0 → COD = 0 (không thu hộ).
    /// </summary>
    private static decimal CalculateExpectedCod(
        TransportOrder order,
        List<Lpn> lpns,
        List<HandoverConfirmLpnInput> lpnInputs)
    {
        if (order.CargoValue <= 0 || order.Quantity <= 0)
            return 0m;

        var acceptedIds = lpnInputs.Where(i => i.IsAccepted).Select(i => i.LpnId).ToHashSet();
        if (acceptedIds.Count == 0) return 0m;

        var acceptedQty = lpns.Where(l => acceptedIds.Contains(l.LpnId)).Sum(l => l.Quantity);
        return Math.Round(order.CargoValue * ((decimal)acceptedQty / order.Quantity), 2);
    }

    /// <summary>
    /// Cập nhật trạng thái từng LPN, gán nhiệt độ ghi nhận, tạo ReturnedItem nếu từ chối.
    /// Cập nhật Order.Status tổng quát: DELIVERED / RETURNED / PARTIALLY_DELIVERED.
    /// Trả về true nếu có hàng trả lại.
    /// </summary>
    private bool UpdateLpnStates(
        TransportOrder order,
        List<Lpn> lpns,
        List<HandoverConfirmLpnInput> lpnInputs,
        Dictionary<Guid, (string? evidenceUrl, string? conditionUrl)> urlMap,
        decimal recordedTemp,
        Guid epodId)
    {
        bool hasReturns = false;
        var now = DateTime.UtcNow;

        foreach (var input in lpnInputs)
        {
            var lpn = lpns.First(l => l.LpnId == input.LpnId);
            lpn.RecordedTemperature = recordedTemp;
            lpn.UpdatedAt = now;

            if (input.IsAccepted)
            {
                lpn.State = LpnState.DELIVERED;
                if (urlMap.TryGetValue(input.LpnId, out var urls) && urls.conditionUrl != null)
                    lpn.EvidenceImageUrl = urls.conditionUrl;
            }
            else
            {
                hasReturns = true;
                lpn.State = LpnState.RETURN_PENDING;
                lpn.DiscrepancyReason = input.RejectionReason;
                if (urlMap.TryGetValue(input.LpnId, out var urls))
                    lpn.EvidenceImageUrl = urls.evidenceUrl;

                _context.ReturnedItems.Add(new ReturnedItem
                {
                    ReturnId = Guid.NewGuid(),
                    EpodId = epodId,
                    ItemName = order.ItemName,
                    ItemCode = lpn.LpnCode,
                    Unit = order.PackingType ?? "PALLET",
                    ReturnedQty = lpn.Quantity,
                    ReasonType = input.RejectionReason!.ToUpper(),
                    ReasonNote = input.RejectionNotes,
                    ProcessingStatus = "PENDING",
                    ReturnedAt = now
                });
            }
        }

        var allAccepted = lpnInputs.All(i => i.IsAccepted);
        var allRejected = lpnInputs.All(i => !i.IsAccepted);
        order.Status = allAccepted ? "DELIVERED" : allRejected ? "RETURNED" : "PARTIALLY_DELIVERED";

        return hasReturns;
    }

    private static object BuildHandoverPdfData(
        TransportOrder order,
        MasterTrip trip,
        Driver driver,
        Location? location,
        List<Lpn> lpns,
        HandoverConfirmRequest request,
        string signatureUrl,
        string? handoverPhotoUrl,
        Dictionary<Guid, (string? evidenceUrl, string? conditionUrl)> urlMap,
        decimal recordedTemp,
        DateTime now)
    {
        return new
        {
            DocumentType = "Biên bản giao nhận hàng lạnh",
            CompanyName = "ColdChainX Logistics",
            DeliveryDate = now.ToString("dd/MM/yyyy HH:mm"),
            DestinationAddress = location?.Address ?? "N/A",
            VehiclePlateNumber = trip.Vehicle?.TruckPlate ?? "N/A",
            DriverName = driver.FullName,
            CustomerName = order.Customer?.CompanyName ?? "Khách hàng",
            ReceiverName = request.ReceiverName,
            ReceiverPhone = request.ReceiverPhone ?? "N/A",
            OrderCode = order.TrackingCode,
            RecordedTemperatureCelsius = recordedTemp,
            SignatureUrl = signatureUrl,
            HandoverPhotoUrl = handoverPhotoUrl,
            DeliveryNote = request.Note,
            DeliveryRating = request.DeliveryRating,
            Items = lpns.Select((l, i) =>
            {
                var input = request.Lpns.First(li => li.LpnId == l.LpnId);
                urlMap.TryGetValue(l.LpnId, out var urls);
                return new
                {
                    Index = i + 1,
                    LpnCode = l.LpnCode,
                    ItemName = order.ItemName,
                    Unit = order.PackingType ?? "PALLET",
                    Quantity = l.Quantity,
                    WeightKg = l.ActualWeightKg,
                    Status = input.IsAccepted ? "Đã nhận ✓" : "Từ chối ✗",
                    RejectionReason = input.IsAccepted ? null : input.RejectionReason,
                    RejectionNotes = input.IsAccepted ? null : input.RejectionNotes,
                    PhotoUrl = input.IsAccepted ? urls.conditionUrl : urls.evidenceUrl
                };
            }).ToList()
        };
    }
}
