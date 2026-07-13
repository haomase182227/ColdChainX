using ColdChainX.Application.Features.Inventory.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

using ColdChainX.Application.DTOs.Common;

namespace ColdChainX.Application.Features.Inventory.Queries;

public class GetLpnListQuery : IRequest<PagedResult<LpnDto>>
{
    public LpnState? Status { get; set; }
    public string? Keyword { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GetLpnListQueryHandler : IRequestHandler<GetLpnListQuery, PagedResult<LpnDto>>
{
    private readonly IApplicationDbContext _context;

    public GetLpnListQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<LpnDto>> Handle(GetLpnListQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Lpns.AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.State == request.Status.Value);
        }
        else
        {
            // Mac dinh an LPN da xoa mem — chi hien thi khi loc tuong minh ?status=DELETED
            query = query.Where(x => x.State != LpnState.DELETED);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            query = query.Where(x =>
                x.LpnCode.Contains(request.Keyword) ||
                x.Order.ItemName.Contains(request.Keyword));
        }

        var totalRecords = await query.CountAsync(cancellationToken);

        var lpns = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new LpnDto
            {
                LpnId = x.LpnId,
                LpnCode = x.LpnCode,
                ItemName = x.Order.ItemName,
                BatchNumber = "N/A",
                ReceiptId = x.ReceiptId,
                HasWarehouseReceipt = x.Receipt.PdfUrl != null && x.Receipt.PdfUrl != "",
                WarehouseReceiptPdfUrl = x.Receipt.PdfUrl,
                WarehouseId = x.WarehouseId,
                WarehouseName = x.Warehouse != null ? x.Warehouse.WarehouseName : null,
                StorageLocation = x.StorageLocation,
                Quantity = x.Quantity,
                ExpectedWeightKg = x.Order != null && x.Order.OrderDimension != null ? x.Order.OrderDimension.ExpectedWeightKg : 0m,
                ActualWeightKg = x.ActualWeightKg,
                State = x.State.ToString(),
                Condition = x.DiscrepancyReason,
                InboundTime = x.InboundTime,
                SlaDeadline = x.SlaDeadline
            })
            .ToListAsync(cancellationToken);

        return PagedResult<LpnDto>.Create(lpns, totalRecords, request.PageNumber, request.PageSize);
    }
}
