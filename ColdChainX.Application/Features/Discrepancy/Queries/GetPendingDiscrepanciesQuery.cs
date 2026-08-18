using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Helpers;
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
            .Include(l => l.Order)
                .ThenInclude(o => o.OrderDimension)
            .Include(l => l.PackageVariantLines)
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
            var expectedWeight = l.PackageVariantLines.Count > 0
                ? l.PackageVariantLines.Sum(line => line.ExpectedWeightKg)
                : order.OrderDimension?.ExpectedWeightKg ?? 0m;
            var expectedCbm = l.PackageVariantLines.Count > 0
                ? l.PackageVariantLines.Sum(line => line.ExpectedCbm)
                : order.OrderDimension == null
                    ? 0m
                    : InboundQcMeasurementCalculator.CalculateExpectedCbm(order.OrderDimension, order.Quantity);
            var weightDiff = CalculateDiffPercent(expectedWeight, l.ActualWeightKg);
            var cbmDiff = CalculateDiffPercent(expectedCbm, l.ActualCbm);
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
                ExpectedWeightKg = expectedWeight,
                ActualWeightKg = l.ActualWeightKg,
                ExpectedCbm = expectedCbm,
                ActualCbm = l.ActualCbm,
                DiffPercent = diffPercent,
                DiscrepancyReason = l.DiscrepancyReason,
                AsnId = asn?.AsnId,
                AsnCode = asn?.AsnCode,
                ReceiptId = l.ReceiptId,
                EvidenceImageUrl = l.EvidenceImageUrl,
                CreatedAt = l.CreatedAt,
                PackageLines = l.PackageVariantLines.Select(line => new LpnPackageVariantLineResponse
                {
                    LpnPackageVariantLineId = line.LpnPackageVariantLineId,
                    OrderPackageVariantId = line.OrderPackageVariantId,
                    VariantName = line.VariantName,
                    PackingType = line.PackingType,
                    Quantity = line.Quantity,
                    ExpectedWeightKg = line.ExpectedWeightKg,
                    ActualWeightKg = line.ActualWeightKg,
                    ExpectedCbm = line.ExpectedCbm,
                    ActualCbm = line.ActualCbm,
                    LengthCm = line.LengthCm,
                    WidthCm = line.WidthCm,
                    HeightCm = line.HeightCm,
                    DiffPercent = line.DiffPercent,
                    HasDiscrepancy = line.HasDiscrepancy
                }).ToList()
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
