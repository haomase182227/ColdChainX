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

using ColdChainX.Application.DTOs.Common;

namespace ColdChainX.Application.Features.Discrepancy.Queries;

public class GetPendingDiscrepanciesQuery : IRequest<PagedResult<PendingDiscrepancyResponse>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetPendingDiscrepanciesQueryHandler : IRequestHandler<GetPendingDiscrepanciesQuery, PagedResult<PendingDiscrepancyResponse>>
{
    private readonly IApplicationDbContext _context;

    public GetPendingDiscrepanciesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<PendingDiscrepancyResponse>> Handle(GetPendingDiscrepanciesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Lpns
            .Include(l => l.Order)
                .ThenInclude(o => o.InboundAsns)
            .Include(l => l.Customer)
            .Where(l => l.State == LpnState.DISCREPANCY_HOLD)
            .OrderByDescending(l => l.CreatedAt);

        var totalRecords = await query.CountAsync(cancellationToken);

        var pendingLpns = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = pendingLpns.Select(l =>
        {
            var order = l.Order;
            var weightDiff = CalculateDiffPercent(order.OrderDimension?.ExpectedWeightKg ?? 0m, l.ActualWeightKg);
            var cbmDiff = CalculateDiffPercent(order.OrderDimension?.ExpectedCbm ?? 0m, l.ActualCbm);
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
                ExpectedWeightKg = order.OrderDimension?.ExpectedWeightKg ?? 0m,
                ActualWeightKg = l.ActualWeightKg,
                ExpectedCbm = order.OrderDimension?.ExpectedCbm ?? 0m,
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

        return PagedResult<PendingDiscrepancyResponse>.Create(items, totalRecords, request.PageNumber, request.PageSize);
    }

    private static decimal CalculateDiffPercent(decimal expected, decimal actual)
    {
        if (expected <= 0)
            return actual > 0 ? 100m : 0m;

        return Math.Round(Math.Abs(actual - expected) / expected * 100m, 2);
    }
}
