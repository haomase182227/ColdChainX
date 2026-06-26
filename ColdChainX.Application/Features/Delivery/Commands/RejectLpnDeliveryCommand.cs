using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class RejectLpnDeliveryCommand : IRequest<ApiResponse<LpnDeliveryStatusResponse>>
{
    public Guid TripId { get; set; }
    public Guid LpnId { get; set; }
    public string RejectReason { get; set; } = null!;
    public string? RejectNote { get; set; }
    public IFormFile EvidenceImage { get; set; } = null!;
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class RejectLpnDeliveryCommandHandler : IRequestHandler<RejectLpnDeliveryCommand, ApiResponse<LpnDeliveryStatusResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;

    public RejectLpnDeliveryCommandHandler(IApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<ApiResponse<LpnDeliveryStatusResponse>> Handle(RejectLpnDeliveryCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch LPN and validate existence
        var lpn = await _context.Lpns
            .Include(l => l.Order)
            .FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
            throw new NotFoundException($"LPN with ID '{request.LpnId}' was not found.");

        // 2. Validate LPN belongs to specified trip
        if (lpn.TripId != request.TripId)
            throw new InvalidOperationException($"LPN '{lpn.LpnCode}' does not belong to trip '{request.TripId}'.");

        // 3. Fetch Trip and validate existence
        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Trip with ID '{request.TripId}' was not found.");

        // 4. Validate driver is assigned to this trip
        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == request.UserId, cancellationToken);
        if (driver == null)
            throw new ForbiddenException("Driver profile not found for current user.");

        var isAssignedDriver = await _context.TripDrivers
            .AnyAsync(td => td.TripId == request.TripId && td.DriverId == driver.DriverId, cancellationToken);
        if (!isAssignedDriver)
            throw new ForbiddenException("You are not authorized to confirm deliveries for this trip.");

        // 5. Check LPN state (Only SHIPPING allowed, handle double-submit Conflict)
        // 5. Check LPN state (Only SHIPPING allowed, handle double-submit Conflict)
        if (lpn.State != LpnState.SHIPPING)
        {
            var existing = await _context.LpnDeliveryConfirmations
                .FirstOrDefaultAsync(c => c.LpnId == request.LpnId, cancellationToken);
            if (existing != null)
            {
                throw new ConflictException($"LPN '{lpn.LpnCode}' has already been confirmed as {existing.OutcomeType} at {existing.ConfirmedAt:yyyy-MM-ddTHH:mm:ssZ}. Cannot confirm again.");
            }
            throw new InvalidOperationException($"LPN '{lpn.LpnCode}' is not eligible for delivery confirmation. Current state: {lpn.State}. Only SHIPPING LPNs can be confirmed.");
        }

        // Verify stop check-in
        DateTime? stopCheckinAt = null;
        if (lpn.Order?.DestLocation != null)
        {
            var stop = await _context.TripStops
                .FirstOrDefaultAsync(ts => ts.TripId == request.TripId && ts.LocationId == lpn.Order.DestLocation, cancellationToken);
            if (stop != null)
            {
                if (stop.ActualArrivalTime == null)
                {
                    var location = await _context.Locations
                        .FirstOrDefaultAsync(l => l.LocationId == stop.LocationId, cancellationToken);
                    var addressName = location != null ? location.Address : "the destination";
                    throw new ValidationException($"Cannot reject delivery. You must check in at the delivery stop '{addressName}' first.");
                }
                stopCheckinAt = stop.ActualArrivalTime;
            }
        }

        // 6. Validate evidence image
        if (request.EvidenceImage == null || request.EvidenceImage.Length == 0)
            throw new ValidationException("Evidence image is required. Please attach a photo of the delivery.");

        const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
        if (request.EvidenceImage.Length > MaxFileSizeBytes)
            throw new ValidationException($"Image file size ({request.EvidenceImage.Length / 1024.0 / 1024.0:F2}MB) exceeds the 10MB limit. Please compress the image and try again.");

        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(request.EvidenceImage.ContentType.ToLower()))
            throw new ValidationException($"Invalid file type '{request.EvidenceImage.ContentType}'. Only image files are accepted (jpg, jpeg, png, webp).");

        // 7. Validate Reject Reason and Note
        if (string.IsNullOrWhiteSpace(request.RejectReason))
            throw new ValidationException("Reject reason is required.");
        
        var allowedReasons = new[] { "DAMAGED", "WRONG_ITEM", "REFUSED_BY_CUSTOMER", "TEMPERATURE_DEVIATION", "OTHER" };
        if (!allowedReasons.Contains(request.RejectReason.ToUpper()))
            throw new ValidationException($"Invalid reject reason '{request.RejectReason}'. Allowed values: DAMAGED, WRONG_ITEM, REFUSED_BY_CUSTOMER, TEMPERATURE_DEVIATION, OTHER.");

        if (request.RejectReason.ToUpper() == "OTHER" && string.IsNullOrWhiteSpace(request.RejectNote))
            throw new ValidationException("A rejection note is required when reject reason is 'OTHER'. Please describe the issue.");

        if (request.RejectReason.Length > 50)
            throw new ValidationException("Reject reason must not exceed 50 characters.");

        if (request.RejectNote != null && request.RejectNote.Length > 500)
            throw new ValidationException("Reject note must not exceed 500 characters.");

        // 8. Upload to Cloudinary
        string imageUrl;
        try
        {
            imageUrl = await _fileService.UploadFileAsync(request.EvidenceImage);
        }
        catch (Exception)
        {
            throw new ExternalServiceException("Image upload service is temporarily unavailable. Please try again.");
        }

        if (string.IsNullOrEmpty(imageUrl))
            throw new ExternalServiceException("Image upload returned empty URL. Please try again.");

        // Fetch latest temperature from TelemetryLogs or fallback to 4.5
        var latestTelemetry = await _context.TelemetryLogs
            .Where(t => t.TripId == request.TripId)
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
        var recordedTemp = latestTelemetry != null ? latestTelemetry.Temperature : 4.5m;

        // 9. Database transaction and save using execution strategy
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var confirmation = new LpnDeliveryConfirmation
                {
                    ConfirmationId = Guid.NewGuid(),
                    LpnId = request.LpnId,
                    TripId = request.TripId,
                    OrderId = lpn.OrderId,
                    OutcomeType = "REJECTED",
                    RejectReason = request.RejectReason.ToUpper(),
                    RejectNote = request.RejectNote,
                    EvidenceImageUrl = imageUrl,
                    ConfirmedByDriverId = request.UserId,
                    ConfirmedAt = DateTime.UtcNow,
                    CheckinAt = stopCheckinAt,
                    RecordedTemperature = recordedTemp
                };

                _context.LpnDeliveryConfirmations.Add(confirmation);

                lpn.State = LpnState.DELIVERY_RETURNED;
                lpn.EvidenceImageUrl = imageUrl;
                lpn.RecordedTemperature = recordedTemp;
                lpn.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                // Sync Order status
                await SyncOrderDeliveryStatusAsync(lpn.OrderId, cancellationToken);

                // Sync Trip status
                await TryCompleteTripAsync(request.TripId, cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var response = new LpnDeliveryStatusResponse
                {
                    LpnId = lpn.LpnId,
                    LpnCode = lpn.LpnCode,
                    State = lpn.State.ToString(),
                    OutcomeType = confirmation.OutcomeType,
                    RejectReason = confirmation.RejectReason,
                    RejectNote = confirmation.RejectNote,
                    EvidenceImageUrl = confirmation.EvidenceImageUrl,
                    ConfirmedAt = confirmation.ConfirmedAt,
                    RecordedTemperature = confirmation.RecordedTemperature
                };

                return ApiResponse<LpnDeliveryStatusResponse>.SuccessResponse(response, "LPN delivery rejected successfully.");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task SyncOrderDeliveryStatusAsync(Guid orderId, CancellationToken ct)
    {
        var lpns = await _context.Lpns.Where(l => l.OrderId == orderId).ToListAsync(ct);
        if (lpns.Count == 0) return;

        var anyShipping = lpns.Any(l => l.State == LpnState.SHIPPING);
        if (anyShipping) return;

        // Fetch confirmations to verify COD payments
        var lpnIds = lpns.Select(l => l.LpnId).ToList();
        var confirmations = await _context.LpnDeliveryConfirmations
            .Where(c => lpnIds.Contains(c.LpnId))
            .ToListAsync(ct);

        var hasUnverifiedCod = lpns.Any(l => l.State == LpnState.DELIVERED &&
            confirmations.Any(c => c.LpnId == l.LpnId && c.CodAmount > 0 && !c.IsCodVerified));

        if (hasUnverifiedCod)
        {
            return; // Gate: keep order status as SHIPPING until accountant approves COD payments
        }

        var order = await _context.TransportOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order == null) return;

        var allDelivered = lpns.All(l => l.State == LpnState.DELIVERED);
        var allReturned = lpns.All(l => l.State == LpnState.DELIVERY_RETURNED);

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

    private async Task TryCompleteTripAsync(Guid tripId, CancellationToken ct)
    {
        var hasShippingLpn = await _context.Lpns
            .AnyAsync(l => l.TripId == tripId && l.State == LpnState.SHIPPING, ct);

        if (!hasShippingLpn)
        {
            var trip = await _context.MasterTrips
                .FirstOrDefaultAsync(t => t.TripId == tripId, ct);
            if (trip != null)
            {
                trip.Status = "COMPLETED";
                trip.CompletedAt = DateTime.UtcNow;
            }
        }
    }
}
