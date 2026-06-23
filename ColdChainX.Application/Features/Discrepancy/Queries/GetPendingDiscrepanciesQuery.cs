using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ColdChainX.Application.Features.Discrepancy.Queries;

public class GetPendingDiscrepanciesQuery : IRequest<List<PendingDiscrepancyResponse>>
{
}

public class GetPendingDiscrepanciesQueryHandler : IRequestHandler<GetPendingDiscrepanciesQuery, List<PendingDiscrepancyResponse>>
{
    private readonly IApplicationDbContext _context;

    public GetPendingDiscrepanciesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PendingDiscrepancyResponse>> Handle(GetPendingDiscrepanciesQuery request, CancellationToken cancellationToken)
    {
        var pendingLpns = await _context.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.InboundAsns)
            .Include(l => l.Customer)
            .Where(l => l.State == LpnState.DISCREPANCY_HOLD)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        return pendingLpns.Select(l =>
        {
            var order = l.Order;
            var weightDiff = CalculateDiffPercent(order.ExpectedWeightKg, l.ActualWeightKg);
            var cbmDiff = CalculateDiffPercent(order.ExpectedCbm, l.ActualCbm);
            var diffPercent = Math.Max(weightDiff, cbmDiff);

            var asn = order.InboundAsns.OrderByDescending(a => a.CreatedAt).FirstOrDefault();

            return new PendingDiscrepancyResponse
            {
                LpnId = l.LpnId,
                LpnCode = l.LpnCode,
                OrderId = l.OrderId,
                TrackingCode = order.TrackingCode,
                CustomerName = l.Customer?.CompanyName,
                ItemName = order.ItemName,
                ExpectedWeightKg = order.ExpectedWeightKg,
                ActualWeightKg = l.ActualWeightKg,
                ExpectedCbm = order.ExpectedCbm,
                ActualCbm = l.ActualCbm,
                DiffPercent = diffPercent,
                DiscrepancyReason = l.DiscrepancyReason,
                AsnId = asn?.AsnId,
                AsnCode = asn?.AsnCode,
                ReceiptId = l.ReceiptId,
                EvidenceImageUrl = l.EvidenceImageUrl,
                CreatedAt = l.CreatedAt
            };
        }).ToList();
    }

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            return actual > 0 ? 100m : 0m;

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }
}
