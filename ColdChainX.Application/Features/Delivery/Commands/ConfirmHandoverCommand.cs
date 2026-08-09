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

        var stop = await _context.TripStops
            .Include(ts => ts.Location)
            .Include(ts => ts.Trip)
                .ThenInclude(t => t!.Vehicle)
            .FirstOrDefaultAsync(ts => ts.StopId == command.StopId, cancellationToken);

        if (stop == null)
            throw new NotFoundException($"Stop '{command.StopId}' was not found.");

        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Cannot confirm handover. Driver must check in at this stop first (POST /api/stops/{stopId}/check-ins).");

        var order = await _context.TransportOrders
            .Include(o => o.Customer)
            .Include(o => o.Quotations)
            .FirstOrDefaultAsync(o => o.MasterTripId == request.TripId && o.CustomerId == request.CustomerId && o.DestLocation == stop.LocationId, cancellationToken);

        if (order == null)
            throw new NotFoundException($"Không tìm thấy đơn hàng nào của khách hàng '{request.CustomerId}' trên chuyến đi '{request.TripId}' tại điểm dừng này.");

        var trip = stop.Trip;
        if (trip == null)
            throw new NotFoundException("Trip data not found for this stop.");

        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == command.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found for current user.");

        var isAssigned = await _context.TripDrivers
            .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssigned)
            throw new ForbiddenException("You are not authorized to confirm handover for this trip.");

        var existingEpod = await _context.DeliveryEpods
            .FirstOrDefaultAsync(e => e.OrderId == order.OrderId && e.HandoverConfirmedAt != null, cancellationToken);
        if (existingEpod != null)
            throw new ConflictException($"Handover for order '{order.TrackingCode}' has already been confirmed at {existingEpod.HandoverConfirmedAt:O} (ePOD: {existingEpod.EpodId}). Cannot confirm again.");

        var lpns = await _context.Lpns
            .Where(l => l.OrderId == order.OrderId && l.TripId == trip.TripId)
            .ToListAsync(cancellationToken);

        if (lpns.Count == 0)
            throw new ValidationException("No LPNs found for this order on this trip. Ensure dispatch was completed.");

        var signatureTask = _fileService.UploadFileAsync(request.SignatureFile);
        var handoverPhotoTask = request.HandoverPhotoFile != null
            ? _fileService.UploadFileAsync(request.HandoverPhotoFile)
            : Task.FromResult<string>(null!);

        await Task.WhenAll(signatureTask, handoverPhotoTask);
        var signatureUrl = signatureTask.Result;
        var handoverPhotoUrl = handoverPhotoTask.Result;

        var latestTelemetry = await _context.TelemetryLogs
            .Where(t => t.TripId == trip.TripId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
        var recordedTemp = latestTelemetry?.Temperature ?? 4.5m;

        var expectedCod = CalculateExpectedCod(order, lpns);

        var epodId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                UpdateLpnStates(order, lpns, recordedTemp);

                var epod = new DeliveryEpod
                {
                    EpodId = epodId,
                    OrderId = order.OrderId,
                    CheckinTime = stop.ActualArrivalTime ?? now,
                    SignedAt = now,
                    HandoverConfirmedAt = now,
                    SignImageUrl = signatureUrl,
                    SignLatitude = stop.Location?.Latitude,
                    SignLongitude = stop.Location?.Longitude,
                    Status = "HANDOVER_CONFIRMED",
                    CodAmount = expectedCod,
                    PaymentStatus = "AWAITING_PAYMENT",
                    CreatedAt = now
                };
                _context.DeliveryEpods.Add(epod);
                await _context.SaveChangesAsync(cancellationToken);

                var pdfData = BuildHandoverPdfData(
                    order, trip, driver, stop.Location, lpns,
                    request, signatureUrl, handoverPhotoUrl, recordedTemp, now);

                var pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("Epod", pdfData);
                var pdfUrl = await _fileService.UploadFileAsync(
                    pdfBytes, $"handover_{order.TrackingCode}_{now:yyyyMMddHHmmss}.pdf");

                epod.HandoverPdfUrl = pdfUrl;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ApiResponse<HandoverConfirmResponse>.SuccessResponse(new HandoverConfirmResponse
                {
                    EpodId = epodId,
                    HandoverConfirmedAt = now,
                    OrderStatus = order.Status,
                    CodAmountDue = expectedCod,
                    HandoverPdfUrl = pdfUrl,
                    NextStep = $"GET /api/Delivery/epods/{epodId}/payment-qr — Hiển thị mã QR thanh toán tổng COD chính xác cho khách hàng"
                }, "Nghiệm thu hàng và ký nhận thành công. Vui lòng thu tiền COD ở bước tiếp theo.");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }


    private static decimal CalculateExpectedCod(TransportOrder order, List<Lpn> lpns)
    {
        if (order.Quantity <= 0) return 0m;

        var acceptedQty = lpns.Sum(l => l.Quantity); // All accepted

        var quotation = order.Quotations
            .Where(q => q.Status == "ACCEPTED" || q.Status == "APPROVED" || q.Status == "DRAFT" || q.FinalAmount > 0)
            .OrderByDescending(q => q.CreatedAt)
            .FirstOrDefault();

        decimal baseAmount = quotation?.FinalAmount ?? 0m;
        if (baseAmount <= 0)
            throw new ValidationException($"Đơn hàng '{order.TrackingCode}' ({order.OrderId}) chưa có Báo giá (Quotation) hợp lệ hoặc giá trị bằng 0. Không thể tính tiền COD nghiệm thu!");

        return Math.Round((decimal)acceptedQty / order.Quantity * baseAmount, 2);
    }

    private void UpdateLpnStates(TransportOrder order, List<Lpn> lpns, decimal recordedTemp)
    {
        var now = DateTime.UtcNow;
        foreach (var lpn in lpns)
        {
            lpn.RecordedTemperature = recordedTemp;
            lpn.UpdatedAt = now;
            lpn.State = LpnState.DELIVERED;
        }
        order.Status = "DELIVERED";
    }

    private static object BuildHandoverPdfData(
        TransportOrder order, MasterTrip trip, Driver driver, Location? location,
        List<Lpn> lpns, HandoverConfirmRequest request, string signatureUrl,
        string? handoverPhotoUrl, decimal recordedTemp, DateTime now)
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
            ReceiverName = order.Customer?.CompanyName ?? "Khách hàng",
            ReceiverPhone = "N/A",
            OrderCode = order.TrackingCode,
            RecordedTemperatureCelsius = recordedTemp,
            SignatureUrl = signatureUrl,
            HandoverPhotoUrl = handoverPhotoUrl,
            Items = lpns.Select((l, i) => new
            {
                Index = i + 1,
                LpnCode = l.LpnCode,
                ItemName = order.ItemName,
                Unit = order.PackingType ?? "PALLET",
                Quantity = l.Quantity,
                WeightKg = l.ActualWeightKg,
                Status = "Đã nhận ✓",
                RejectionReason = (string?)null,
                RejectionNotes = (string?)null,
                PhotoUrl = (string?)null
            }).ToList()
        };
    }
}
