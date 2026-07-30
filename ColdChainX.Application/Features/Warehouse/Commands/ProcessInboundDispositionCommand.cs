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
    public string SlipCode { get; set; } = null!;
    public Guid WarehouseManagerId { get; set; }
    public string? Notes { get; set; }
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
            .FirstOrDefaultAsync(r => r.SlipCode == request.SlipCode, cancellationToken);

        if (returnSlip == null)
            throw new NotFoundException($"Không tìm thấy phiếu trả hàng có mã '{request.SlipCode}'.");

        var lpn = returnSlip.Lpn;
        if (lpn == null)
            throw new NotFoundException("Kiện hàng LPN tương ứng không tồn tại trong DB.");

        // Quét mã nhận tại HUB
        lpn.State = LpnState.RECEIVED_AT_HUB;
        lpn.UpdatedAt = DateTime.UtcNow;

        string dispositionAction;
        string? generatedClaimCode = null;

        // Kiểm tra lý do trả hàng để tự động phân luồng (Disposition)
        bool isNoShow = (returnSlip.Reason != null && (returnSlip.Reason.Contains("NOSHOW", StringComparison.OrdinalIgnoreCase) || returnSlip.Reason.Contains("No-Show", StringComparison.OrdinalIgnoreCase)))
                     || (returnSlip.Order?.Status == "DELIVERY_FAILED");

        if (isNoShow)
        {
            // Trường hợp No-Show: Hàng còn nguyên vẹn -> Chuyển thẳng sang Pending Redelivery
            lpn.State = LpnState.PENDING_REDELIVERY;
            dispositionAction = "PENDING_REDELIVERY (Chờ bốc xếp chuyển chuyến sau do khách No-Show)";
        }
        else
        {
            // Trường hợp Reject (Hỏng / lỗi nhiệt độ / OS&D): Khởi tạo Claim Urgent cho QA/KCS kiểm tra
            lpn.State = LpnState.DISCREPANCY_HOLD;
            dispositionAction = "URGENT_CLAIM_CREATED (Hàng lỗi/từ chối OS&D -> Đã khởi tạo Claim Urgent chờ QA/KCS kiểm tra)";
            generatedClaimCode = $"CLM-URGENT-{DateTime.UtcNow:yyyyMMddHHmmss}";

            var claim = new Claim
            {
                ClaimId = Guid.NewGuid(),
                ClaimCode = generatedClaimCode,
                OrderId = returnSlip.OrderId,
                LpnId = lpn.LpnId,
                ClaimType = "URGENT_REVERSE_LOGISTICS",
                Description = $"[URGENT] Hàng trả về do từ chối đồng kiểm OS&D/Nhiệt độ. Lý do: {returnSlip.Reason}. Ghi chú kho: {request.Notes}",
                Status = "PENDING_QA_REVIEW",
                CreatedAt = DateTime.UtcNow
            };
            _context.Claims.Add(claim);
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
