using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Inbound.Queries;

public class GetPendingReturnSlipsQuery : IRequest<ApiResponse<object>>
{
}

public class GetPendingReturnSlipsQueryHandler : IRequestHandler<GetPendingReturnSlipsQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public GetPendingReturnSlipsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(GetPendingReturnSlipsQuery request, CancellationToken cancellationToken)
    {
        var slips = await (
            from slip in _context.InboundReturnSlips
            join returnedItem in _context.ReturnedItems
                on slip.ReturnSlipId equals returnedItem.ReturnId
            where returnedItem.ProcessingStatus == "PENDING_INBOUND"
            orderby slip.CreatedAt descending
            select new
            {
                slip.SlipCode,
                slip.ReturnSlipId,
                slip.Reason,
                LpnCode = slip.Lpn.LpnCode,
                slip.ReturnedQty,
                Status = returnedItem.ProcessingStatus,
                OrderStatus = slip.Order.Status,
                LpnState = slip.Lpn.State.ToString(),
                Label = $"{slip.SlipCode} (LPN: {slip.Lpn.LpnCode})"
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<object>.SuccessResponse(slips, "Lấy danh sách mã Slip đang chờ xử lý thành công.");
    }
}
