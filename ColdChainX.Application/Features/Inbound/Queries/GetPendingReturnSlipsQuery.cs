using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Enums;
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
        var slips = await _context.InboundReturnSlips
            .Where(s => s.Lpn.State == LpnState.DELIVERY_RETURNED)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.SlipCode,
                s.ReturnSlipId,
                s.Reason,
                LpnCode = s.Lpn.LpnCode,
                Label = $"{s.SlipCode} (LPN: {s.Lpn.LpnCode})"
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<object>.SuccessResponse(slips, "Lấy danh sách mã Slip đang chờ xử lý thành công.");
    }
}
