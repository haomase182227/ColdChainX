using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class ConfirmEpodDeliveryCommand : IRequest<ApiResponse<EpodConfirmResponse>>
{
    public EpodConfirmRequest Request { get; set; } = null!;
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class ConfirmEpodDeliveryCommandHandler : IRequestHandler<ConfirmEpodDeliveryCommand, ApiResponse<EpodConfirmResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;
    private readonly IPdfGeneratorService _pdfGeneratorService;

    public ConfirmEpodDeliveryCommandHandler(
        IApplicationDbContext context,
        IFileService fileService,
        IPdfGeneratorService pdfGeneratorService)
    {
        _context = context;
        _fileService = fileService;
        _pdfGeneratorService = pdfGeneratorService;
    }

    public async Task<ApiResponse<EpodConfirmResponse>> Handle(ConfirmEpodDeliveryCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. Fetch Order and validate
        var order = await _context.TransportOrders
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, cancellationToken);
        if (order == null)
            throw new NotFoundException($"Order with ID '{request.OrderId}' was not found.");

        if (order.MasterTripId == null)
            throw new ValidationException("Order has not been assigned to a trip yet.");

        // 2. Fetch Trip and validate
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
            .FirstOrDefaultAsync(t => t.TripId == order.MasterTripId.Value, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Trip with ID '{order.MasterTripId.Value}' was not found.");

        // 3. Validate driver is assigned to this trip
        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == command.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found for current user.");

        var isAssignedDriver = await _context.TripDrivers
            .AnyAsync(td => td.TripId == trip.TripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssignedDriver)
            throw new ForbiddenException("You are not authorized to confirm delivery for this trip.");

        // 4. Find the TripStop corresponding to delivery location and validate check-in
        var stop = await _context.TripStops
            .FirstOrDefaultAsync(ts => ts.TripId == trip.TripId && ts.LocationId == order.DestLocation, cancellationToken);
        if (stop == null)
            throw new NotFoundException("Delivery trip stop was not found.");

        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Cannot confirm ePOD. You must check in at the delivery stop first.");

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.LocationId == stop.LocationId, cancellationToken);

        // 5. Fetch LPNs for this order
        var lpns = await _context.Lpns
            .Where(l => l.OrderId == order.OrderId && l.TripId == trip.TripId)
            .ToListAsync(cancellationToken);

        if (lpns.Count == 0)
            throw new ValidationException("No LPNs found in this order for delivery.");

        // If no LPN status is specified, automatically accept all LPNs
        if (request.Lpns == null || request.Lpns.Count == 0)
        {
            request.Lpns = lpns.Select(l => new EpodConfirmLpnInput
            {
                LpnId = l.LpnId,
                IsAccepted = true
            }).ToList();
        }

        // Validate LPN inputs match database LPNs
        foreach (var lpnInput in request.Lpns)
        {
            var exists = lpns.Any(l => l.LpnId == lpnInput.LpnId);
            if (!exists)
                throw new ValidationException($"LPN with ID '{lpnInput.LpnId}' does not belong to this order and trip.");
        }

        // 6. Calculate expected COD and validate (Strict COD Validation)
        var expectedCod = CalculateExpectedCod(order, lpns, request.Lpns);
        var codAmountPaid = request.CodAmountPaid ?? 0m;
        if (Math.Round(codAmountPaid, 2) != Math.Round(expectedCod, 2))
        {
            throw new ValidationException($"COD Amount Paid ({codAmountPaid}) does not match Expected COD ({expectedCod}) based on accepted LPNs.");
        }

        // Fetch latest temperature from TelemetryLogs or fallback to 4.5
        var latestTelemetry = await _context.TelemetryLogs
            .Where(t => t.TripId == trip.TripId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
        var recordedTemp = latestTelemetry != null ? latestTelemetry.Temperature : 4.5m;

        // Initialize ePOD ID
        var epodId = Guid.NewGuid();

        // 7. Start database transaction
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // Helper method: Update LPN and Order States and create ReturnedItems
                UpdateLpnAndOrderStates(order, lpns, request.Lpns, recordedTemp, epodId);

                // Payment Status & Gated Order Status for QR payments
                var paymentStatus = "UNPAID";
                if (request.PaymentMethod.ToUpper() == "CASH")
                {
                    paymentStatus = "PAID";
                }
                else if (request.PaymentMethod.ToUpper() == "QR" && order.Status != "RETURNED")
                {
                    // Gate order status as SHIPPING until QR payment is verified
                    order.Status = "SHIPPING";
                }

                // Create DeliveryEpod entity
                var epod = new DeliveryEpod
                {
                    EpodId = epodId,
                    OrderId = order.OrderId,
                    CheckinTime = stop.ActualArrivalTime ?? DateTime.UtcNow,
                    SignedAt = DateTime.UtcNow,
                    ReceiverName = request.ReceiverName,
                    ReceiverPhone = request.ReceiverPhone,
                    SignImageUrl = request.SignImageUrl, // Direct URL string, no base64 handling
                    SignLatitude = request.SignLatitude,
                    SignLongitude = request.SignLongitude,
                    DeliveryRating = request.DeliveryRating,
                    Note = request.Note,
                    Status = "COMPLETED",
                    CreatedAt = DateTime.UtcNow,
                    CodAmount = expectedCod,
                    CodAmountPaid = codAmountPaid,
                    PaymentMethod = request.PaymentMethod.ToUpper(),
                    PaymentStatus = paymentStatus,
                    PaymentEvidenceImageUrl = request.PaymentEvidenceImageUrl // Direct URL string, no base64 handling
                };
                _context.DeliveryEpods.Add(epod);

                // Save changes initially so data is consistent
                await _context.SaveChangesAsync(cancellationToken);

                // 8. Generate ePOD PDF
                var pdfData = new
                {
                    DeliveryDate = DateTime.UtcNow.ToString("dd/MM/yyyy"),
                    DestinationAddress = location?.Address ?? "Địa điểm giao hàng",
                    CompanyName = "ColdChainX Logistics",
                    VehiclePlateNumber = trip.Vehicle?.TruckPlate ?? "Unknown Plate",
                    DriverName = driver.FullName,
                    CustomerName = order.Customer?.CompanyName ?? "Khách hàng",
                    OrderCode = order.TrackingCode,
                    Items = lpns.Select((l, idx) => {
                        var input = request.Lpns.First(li => li.LpnId == l.LpnId);
                        return new {
                            Index = idx + 1,
                            ItemName = order.ItemName,
                            LpnCode = l.LpnCode,
                            Unit = order.PackingType ?? "PALLET",
                            Quantity = l.Quantity,
                            StatusDescription = input.IsAccepted ? "Đã nhận" : "Từ chối"
                        };
                    }).ToList()
                };

                byte[] pdfBytes;
                try
                {
                    pdfBytes = await _pdfGeneratorService.GeneratePdfAsync("Epod", pdfData);
                }
                catch (Exception ex)
                {
                    throw new ExternalServiceException($"Failed to generate ePOD PDF: {ex.Message}");
                }

                // Upload generated PDF to Cloudinary
                string pdfUrl;
                try
                {
                    pdfUrl = await _fileService.UploadFileAsync(pdfBytes, $"epod_{order.TrackingCode}.pdf");
                }
                catch (Exception ex)
                {
                    throw new ExternalServiceException($"Failed to upload ePOD PDF to Cloudinary: {ex.Message}");
                }

                epod.PdfUrl = pdfUrl;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var response = new EpodConfirmResponse
                {
                    EpodId = epod.EpodId,
                    OrderStatus = order.Status,
                    PaymentStatus = epod.PaymentStatus,
                    PdfUrl = pdfUrl
                };

                return ApiResponse<EpodConfirmResponse>.SuccessResponse(response, "ePOD confirmation and COD logging processed successfully.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    /// <summary>
    /// Calculates the expected COD amount based on ONLY the accepted LPNs proportionally.
    /// </summary>
    private decimal CalculateExpectedCod(TransportOrder order, List<Lpn> lpns, List<EpodConfirmLpnInput> lpnInputs)
    {
        var acceptedLpnIds = lpnInputs.Where(li => li.IsAccepted).Select(li => li.LpnId).ToHashSet();
        if (acceptedLpnIds.Count == 0)
        {
            return 0m;
        }

        var acceptedQty = lpns.Where(l => acceptedLpnIds.Contains(l.LpnId)).Sum(l => l.Quantity);
        if (order.Quantity <= 0)
        {
            return 0m;
        }

        // Expected COD is proportional to the quantity of items in the accepted LPNs
        return order.CargoValue * ((decimal)acceptedQty / order.Quantity);
    }

    /// <summary>
    /// Updates states for LPNs and the order, and logs rejected LPNs as returned items.
    /// </summary>
    private void UpdateLpnAndOrderStates(
        TransportOrder order,
        List<Lpn> lpns,
        List<EpodConfirmLpnInput> lpnInputs,
        decimal recordedTemp,
        Guid epodId)
    {
        foreach (var lpnInput in lpnInputs)
        {
            var lpn = lpns.First(l => l.LpnId == lpnInput.LpnId);

            if (lpnInput.IsAccepted)
            {
                lpn.State = LpnState.DELIVERED;
                lpn.RecordedTemperature = recordedTemp;
                lpn.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(lpnInput.RejectionReason))
                    throw new ValidationException($"Rejection reason is required for LPN '{lpn.LpnCode}'.");

                lpn.State = LpnState.RETURN_PENDING;
                lpn.DiscrepancyReason = lpnInput.RejectionReason;
                lpn.EvidenceImageUrl = lpnInput.EvidenceImageUrl; // Direct URL string
                lpn.RecordedTemperature = recordedTemp;
                lpn.UpdatedAt = DateTime.UtcNow;

                // Create ReturnedItem record for the rejected LPN
                var returnedItem = new ReturnedItem
                {
                    ReturnId = Guid.NewGuid(),
                    EpodId = epodId,
                    ItemName = order.ItemName,
                    ItemCode = lpn.LpnCode,
                    Unit = order.PackingType ?? "PALLET",
                    ReturnedQty = lpn.Quantity,
                    ReasonType = lpnInput.RejectionReason.ToUpper(),
                    ReasonNote = lpnInput.RejectionNotes,
                    ProcessingStatus = "PENDING",
                    ReturnedAt = DateTime.UtcNow
                };
                _context.ReturnedItems.Add(returnedItem);
            }
        }

        // Determine TransportOrder overall status
        var allDelivered = lpnInputs.All(li => li.IsAccepted);
        var allReturned = lpnInputs.All(li => !li.IsAccepted);

        if (allDelivered)
        {
            order.Status = "DELIVERED";
        }
        else if (allReturned)
        {
            order.Status = "RETURNED";
        }
        else
        {
            order.Status = "PARTIALLY_DELIVERED";
        }
    }
}
