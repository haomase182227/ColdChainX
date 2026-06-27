using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class HandoverCodCommand : IRequest<ApiResponse<CodHandoverResponse>>
{
    public Guid TripId { get; set; }
    public CodHandoverRequest Request { get; set; } = null!;
    public Guid UserId { get; set; } // Set from JWT token by Controller
}

public class HandoverCodCommandHandler : IRequestHandler<HandoverCodCommand, ApiResponse<CodHandoverResponse>>
{
    private readonly IApplicationDbContext _context;

    public HandoverCodCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<CodHandoverResponse>> Handle(HandoverCodCommand command, CancellationToken cancellationToken)
    {
        var request = command.Request;

        // 1. Fetch Trip and validate existence
        var trip = await _context.MasterTrips
            .FirstOrDefaultAsync(t => t.TripId == command.TripId, cancellationToken);
        if (trip == null)
            throw new NotFoundException($"Trip with ID '{command.TripId}' was not found.");

        // 2. Fetch all DeliveryEpods for orders on this trip
        var epods = await _context.DeliveryEpods
            .Include(e => e.Order)
            .Where(e => e.Order != null && e.Order.MasterTripId == command.TripId)
            .ToListAsync(cancellationToken);

        // 3. Reconcile cash and QR payments
        decimal expectedCash = epods
            .Where(e => e.PaymentMethod != null && e.PaymentMethod.ToUpper() == "CASH")
            .Sum(e => e.CodAmountPaid ?? 0);

        decimal expectedQr = epods
            .Where(e => e.PaymentMethod != null && e.PaymentMethod.ToUpper() == "QR")
            .Sum(e => e.CodAmountPaid ?? 0);

        decimal cashDiscrepancy = request.ActualCashReceived - expectedCash;
        decimal qrDiscrepancy = request.ActualQrReceived - expectedQr;

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 4. Update ePOD payment statuses and note details
                foreach (var epod in epods)
                {
                    epod.PaymentStatus = "COD_SETTLED";
                    var appendNote = $" [COD Handover Settled. Actual Cash: {request.ActualCashReceived}, Actual QR: {request.ActualQrReceived}. note: {request.Note}]";
                    epod.Note = $"{epod.Note}{appendNote}".Trim();
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var response = new CodHandoverResponse
                {
                    ExpectedCash = expectedCash,
                    ActualCash = request.ActualCashReceived,
                    ExpectedQr = expectedQr,
                    ActualQr = request.ActualQrReceived,
                    CashDiscrepancy = cashDiscrepancy,
                    QrDiscrepancy = qrDiscrepancy,
                    Status = "COD_SETTLED"
                };

                var msg = (cashDiscrepancy == 0 && qrDiscrepancy == 0)
                    ? "COD Handover completed and settled with zero discrepancy."
                    : $"COD Handover completed with discrepancy. Cash diff: {cashDiscrepancy}, QR diff: {qrDiscrepancy}.";

                return ApiResponse<CodHandoverResponse>.SuccessResponse(response, msg);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
