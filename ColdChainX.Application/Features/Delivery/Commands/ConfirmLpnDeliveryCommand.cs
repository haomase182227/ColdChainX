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

public class ConfirmLpnDeliveryCommand : IRequest<ApiResponse<LpnDeliveryStatusResponse>>
{
    public Guid TripId { get; set; }
    public Guid LpnId { get; set; }
    public string ReceiverName { get; set; } = null!;
    public string? ReceiverPhone { get; set; }
    public IFormFile EvidenceImage { get; set; } = null!;
    public Guid UserId { get; set; } // Set from JWT token by Controller
    public DateTime? CheckinAt { get; set; }
    public IFormFile? SignatureImage { get; set; }
    public decimal CodAmount { get; set; }
    public string? CodPaymentMethod { get; set; }
    public IFormFile? CodReceiptImage { get; set; }
    public string? NewSealNumber { get; set; }
}

public class ConfirmLpnDeliveryCommandHandler : IRequestHandler<ConfirmLpnDeliveryCommand, ApiResponse<LpnDeliveryStatusResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService _fileService;

    public ConfirmLpnDeliveryCommandHandler(IApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<ApiResponse<LpnDeliveryStatusResponse>> Handle(ConfirmLpnDeliveryCommand request, CancellationToken cancellationToken)
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

        // 6. Validate evidence image
        if (request.EvidenceImage == null || request.EvidenceImage.Length == 0)
            throw new ValidationException("Evidence image is required. Please attach a photo of the delivery.");

        const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB
        if (request.EvidenceImage.Length > MaxFileSizeBytes)
            throw new ValidationException($"Image file size ({request.EvidenceImage.Length / 1024.0 / 1024.0:F2}MB) exceeds the 10MB limit. Please compress the image and try again.");

        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(request.EvidenceImage.ContentType.ToLower()))
            throw new ValidationException($"Invalid file type '{request.EvidenceImage.ContentType}'. Only image files are accepted (jpg, jpeg, png, webp).");

        // 7. Validate Receiver info
        if (string.IsNullOrWhiteSpace(request.ReceiverName))
            throw new ValidationException("Receiver name is required.");
        if (request.ReceiverName.Length > 200)
            throw new ValidationException("Receiver name must not exceed 200 characters.");
        if (request.ReceiverPhone != null && request.ReceiverPhone.Length > 20)
            throw new ValidationException("Receiver phone must not exceed 20 characters.");

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
                    OutcomeType = "DELIVERED",
                    ReceiverName = request.ReceiverName,
                    ReceiverPhone = request.ReceiverPhone,
                    EvidenceImageUrl = imageUrl,
                    ConfirmedByDriverId = request.UserId,
                    ConfirmedAt = DateTime.UtcNow
                };

                _context.LpnDeliveryConfirmations.Add(confirmation);

                lpn.State = LpnState.DELIVERED;
                lpn.EvidenceImageUrl = imageUrl;
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
                    ReceiverName = confirmation.ReceiverName,
                    ReceiverPhone = confirmation.ReceiverPhone,
                    EvidenceImageUrl = confirmation.EvidenceImageUrl,
                    ConfirmedAt = confirmation.ConfirmedAt
                };

                return ApiResponse<LpnDeliveryStatusResponse>.SuccessResponse(response, "LPN delivery confirmed successfully.");
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

        var allDelivered = lpns.All(l => l.State == LpnState.DELIVERED);
        var allReturned = lpns.All(l => l.State == LpnState.DELIVERY_RETURNED);
        var anyShipping = lpns.Any(l => l.State != LpnState.DELIVERED && l.State != LpnState.DELIVERY_RETURNED);

        var order = await _context.TransportOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order == null) return;

        if (anyShipping)
        {
            return;
        }

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
