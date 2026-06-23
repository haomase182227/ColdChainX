using ColdChainX.Application.Features.Inventory.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inventory.Queries;

public class GetLpnListQuery : IRequest<List<LpnDto>>
{
    public LpnState? Status { get; set; }
    public string? Keyword { get; set; }
}

public class GetLpnListQueryHandler : IRequestHandler<GetLpnListQuery, List<LpnDto>>
{
    private readonly IApplicationDbContext _context;

    public GetLpnListQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<LpnDto>> Handle(GetLpnListQuery request, CancellationToken cancellationToken)
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

        var lpns = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(100) // Simple pagination/limit
            .Select(x => new LpnDto
            {
                LpnId = x.LpnId,
                LpnCode = x.LpnCode,
                ItemName = x.Order.ItemName,
                BatchNumber = "N/A",
                StorageLocation = x.StorageLocation,
                Quantity = x.Quantity,
                ExpectedWeightKg = x.Order.ExpectedWeightKg,
                ActualWeightKg = x.ActualWeightKg,
                State = x.State.ToString(),
                Condition = x.DiscrepancyReason,
                InboundTime = x.InboundTime,
                SlaDeadline = x.SlaDeadline
            })
            .ToListAsync(cancellationToken);

        return lpns;
    }
}
