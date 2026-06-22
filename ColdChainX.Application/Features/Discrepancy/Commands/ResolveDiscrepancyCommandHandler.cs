using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Discrepancy.Commands;

public class ResolveDiscrepancyCommandHandler : IRequestHandler<ResolveDiscrepancyCommand, ResolveDiscrepancyResponse>
{
    private readonly IApplicationDbContext _context;

    public ResolveDiscrepancyCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ResolveDiscrepancyResponse> Handle(ResolveDiscrepancyCommand request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns.FirstOrDefaultAsync(l => l.LpnId == request.LpnId, cancellationToken);
        if (lpn == null)
        {
            return new ResolveDiscrepancyResponse { Success = false, Message = "LPN not found." };
        }

        if (lpn.State != LpnState.DISCREPANCY_HOLD)
        {
            return new ResolveDiscrepancyResponse { Success = false, Message = $"LPN is not in DISCREPANCY_HOLD state. Current state: {lpn.State}" };
        }

        if (request.Accept)
        {
            lpn.State = LpnState.RECEIVING;
            lpn.UpdatedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync(cancellationToken);

            return new ResolveDiscrepancyResponse 
            { 
                Success = true, 
                Message = "Discrepancy accepted by Sales. LPN is now RECEIVING and waiting putaway."
            };
        }
        else
        {
            // Reject -> Create Penalty Bill and Return
            lpn.State = LpnState.RETURN_PENDING;
            lpn.UpdatedAt = DateTime.UtcNow;

            var bill = new PenaltyBill
            {
                PenaltyBillId = Guid.NewGuid(),
                BillCode = $"PB-{DateTime.UtcNow:yyyyMMddHHmmss}",
                LpnId = lpn.LpnId,
                OrderId = lpn.OrderId,
                CustomerId = lpn.CustomerId,
                HandlingFee = request.PenaltyAmount,
                StorageFee = 0,
                TotalAmount = request.PenaltyAmount,
                Reason = request.PenaltyReason,
                IsPaid = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.PenaltyBills.Add(bill);
            await _context.SaveChangesAsync(cancellationToken);

            return new ResolveDiscrepancyResponse 
            { 
                Success = true, 
                Message = "Discrepancy rejected. Penalty Bill created and LPN set to RETURN_PENDING.",
                PenaltyBillId = bill.PenaltyBillId
            };
        }
    }
}
