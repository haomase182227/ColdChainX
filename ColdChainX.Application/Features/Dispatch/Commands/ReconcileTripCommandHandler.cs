using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using ColdChainX.Core.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Features.Dispatch.Commands
{
    public class ReconcileTripCommandHandler : IRequestHandler<ReconcileTripCommand, ApiResponse<bool>>
    {
        private readonly IApplicationDbContext _db;

        public ReconcileTripCommandHandler(IApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<bool>> Handle(ReconcileTripCommand request, CancellationToken cancellationToken)
        {
            using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var trip = await _db.MasterTrips
                    .Include(t => t.TripDrivers)
                        .ThenInclude(d => d.Driver)
                    .Include(t => t.TransportOrders)
                        .ThenInclude(o => o.DeliveryEpods)
                    .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);

                if (trip == null)
                    return ApiResponse<bool>.Failure("Trip not found.", 404);

                if (trip.Status != "COMPLETED")
                    return ApiResponse<bool>.Failure("Trip must be COMPLETED to be reconciled.", 400);

                decimal expectedCod = trip.TransportOrders
                    .SelectMany(o => o.DeliveryEpods)
                    .Where(e => e.PaymentMethod == "CASH" && e.PaymentStatus == "PAID") // Or collected
                    .Sum(e => e.CodAmountPaid ?? 0); 

                if (request.RemittedAmount < expectedCod)
                {
                    decimal shortage = expectedCod - request.RemittedAmount;

                    var tripDriver = trip.TripDrivers.FirstOrDefault(d => d.DriverRole == "PRIMARY") ?? trip.TripDrivers.FirstOrDefault();
                    Guid? driverUserId = tripDriver?.Driver?.UserId;
                    
                    var penalty = new PenaltyBill
                    {
                        BillCode = $"SHTG-{DateTime.UtcNow:yyyyMMddHHmmss}",
                        TotalAmount = shortage,
                        HandlingFee = shortage, // Store in handling fee for shortage
                        StorageFee = 0,
                        Reason = $"COD Shortage for Trip {trip.TripId}. Expected: {expectedCod}, Remitted: {request.RemittedAmount}. Note: {request.Note}",
                        IsPaid = false,
                        CreatedAt = DateTime.UtcNow,
                        PaidBy = driverUserId
                    };
                    
                    _db.PenaltyBills.Add(penalty);
                }

                trip.Status = "RECONCILED";

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return ApiResponse<bool>.SuccessResponse(true, "Trip reconciled successfully.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ApiResponse<bool>.Failure($"Reconciliation failed: {ex.Message}");
            }
        }
    }
}
