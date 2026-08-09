using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Warehouse.Commands;

public class ProcessInboundDispositionCommand : IRequest<ApiResponse<object>>
{
    public string LpnCode { get; set; } = null!;
    
    public Guid ReturnWarehouseId { get; set; }
}

public class ProcessInboundDispositionCommandHandler : IRequestHandler<ProcessInboundDispositionCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public ProcessInboundDispositionCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(ProcessInboundDispositionCommand request, CancellationToken cancellationToken)
    {
        var returnSlip = await _context.InboundReturnSlips
            .Include(r => r.Lpn)
            .Include(r => r.Order)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(r => r.SlipCode == request.LpnCode || r.Lpn.LpnCode == request.LpnCode, cancellationToken);

        if (returnSlip == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng cho mã kiện '{request.LpnCode}'.");

        var lpn = returnSlip.Lpn;
        if (lpn == null)
            throw new NotFoundException("Kiện hàng LPN tương ứng không tồn tại trong DB.");

        lpn.State = LpnState.RECEIVED_AT_HUB;
        lpn.UpdatedAt = DateTime.UtcNow;

        string dispositionAction;
        string? generatedClaimCode = null;

        bool isNoShow = (returnSlip.Reason != null && (returnSlip.Reason.Contains("NOSHOW", StringComparison.OrdinalIgnoreCase) || returnSlip.Reason.Contains("No-Show", StringComparison.OrdinalIgnoreCase)))
                     || (returnSlip.Order?.Status == "DELIVERY_FAILED");

        if (isNoShow)
        {
            lpn.State = LpnState.IN_STOCK;
            lpn.TripId = null; // Xóa TripId cũ để sẵn sàng cho manual-dispatch
            if (request.ReturnWarehouseId != Guid.Empty)
            {
                lpn.WarehouseId = request.ReturnWarehouseId;
            }
            if (returnSlip.Order != null)
            {
                returnSlip.Order.Status = "READY_FOR_ROUTING";
            }
            
            lpn.InboundTime = DateTime.UtcNow;
            lpn.SlaDeadline = lpn.IsFastTrack ? DateTime.UtcNow.AddHours(12) : DateTime.UtcNow.AddHours(24);

            dispositionAction = "IN_STOCK (Hàng nguyên vẹn, nhập lại kho, gia hạn SLA chờ ghép chuyến mới)";
        }
        else
        {
            lpn.State = LpnState.DISCREPANCY_HOLD;
            dispositionAction = "DISCREPANCY_HOLD (Hàng lỗi/từ chối OS&D -> Cách ly chờ xử lý riêng)";
        }

        await _context.SaveChangesAsync(cancellationToken);

        var result = new
        {
            SlipCode = returnSlip.SlipCode,
            LpnCode = lpn.LpnCode,
            PreviousReason = returnSlip.Reason,
            NewLpnState = lpn.State.ToString(),
            DispositionAction = dispositionAction,
            ClaimCode = generatedClaimCode,
            Message = "Tiếp nhận hàng trả về hub và phân luồng tự động (Inbound Disposition) thành công."
        };

        return ApiResponse<object>.SuccessResponse(result, result.Message);
    }
}
