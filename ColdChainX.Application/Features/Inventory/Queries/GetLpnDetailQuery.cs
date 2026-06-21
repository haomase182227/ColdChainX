using ColdChainX.Application.Features.Inventory.DTOs;
using ColdChainX.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Application.Features.Inventory.Queries;

public class GetLpnDetailQuery : IRequest<LpnDto?>
{
    public Guid Id { get; set; }

    public GetLpnDetailQuery(Guid id)
    {
        Id = id;
    }
}

public class GetLpnDetailQueryHandler : IRequestHandler<GetLpnDetailQuery, LpnDto?>
{
    private readonly IApplicationDbContext _context;

    public GetLpnDetailQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LpnDto?> Handle(GetLpnDetailQuery request, CancellationToken cancellationToken)
    {
        var lpn = await _context.Lpns
            .Where(x => x.LpnId == request.Id)
            .Select(x => new LpnDto
            {
                LpnId = x.LpnId,
                LpnCode = x.LpnCode,
                ItemName = x.ItemName,
                BatchNumber = x.BatchNumber,
                StorageLocation = x.StorageLocation,
                Quantity = x.Quantity,
                ExpectedWeightKg = x.ExpectedWeightKg,
                ActualWeightKg = x.ActualWeightKg,
                State = x.State.ToString(),
                Condition = x.DiscrepancyReason,
                InboundTime = x.InboundTime,
                SlaDeadline = x.SlaDeadline
            })
            .FirstOrDefaultAsync(cancellationToken);

        return lpn;
    }
}
