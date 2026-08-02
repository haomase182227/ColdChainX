using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

using System.Text.Json.Serialization;

namespace ColdChainX.Application.Features.Claims.Commands;

public class ApproveClaimByQaCommand : IRequest<ApiResponse<object>>
{
    [JsonIgnore]
    public Guid ClaimId { get; set; }
    
    [JsonIgnore]
    public Guid QaUserId { get; set; }
    
    public string? Note { get; set; }
}

public class ApproveClaimByQaCommandHandler : IRequestHandler<ApproveClaimByQaCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public ApproveClaimByQaCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(ApproveClaimByQaCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.Claims
            .Include(c => c.Order)
            .FirstOrDefaultAsync(c => c.ClaimId == request.ClaimId, cancellationToken);

        if (claim == null)
            throw new NotFoundException($"Không tìm thấy yêu cầu bồi thường '{request.ClaimId}'.");

        // Bước 2: Dispatcher / QA đánh giá lỗi IoT Log, bấm [Duyệt lỗi] -> Đẩy thẳng cho Kế toán (Accountant)
        claim.Status = "PENDING_ACCOUNTANT_REVIEW";
        claim.ResolutionNote = $"[Bước 2 - Dispatcher/QA Approved by {request.QaUserId}]: Đã kiểm tra biểu đồ nhiệt độ (IoT Log). Bấm [Duyệt lỗi]. Hồ sơ lập tức búng thẳng sang Dashboard của Kế Toán (Accountant)! Ghi chú: {request.Note}";
        claim.ResolvedAt = DateTime.UtcNow;

        if (claim.Order != null)
        {
            claim.Order.Status = "OSD_CLAIM_APPROVED_BY_DISPATCHER";
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<object>.SuccessResponse(new
        {
            ClaimId = claim.ClaimId,
            ClaimCode = claim.ClaimCode,
            Status = claim.Status,
            IotLogCheckResult = "CONFIRMED_FAULT_ON_IOT_CHART",
            Step2_DispatcherAction = "Dispatcher đã mở hệ thống check biểu đồ IoT Log xác nhận sai nhiệt độ -> Bấm [Duyệt lỗi] -> Đã đẩy thẳng hồ sơ bồi thường sang Dashboard của Kế Toán (Accountant)!",
            NextStep = "Bước 3: Khách và Tài xế gạch sổ ký tay thực nhận trên Phiếu Giao Nhận giấy rồi chụp upload. Bước 4: Kế toán chốt chi tiền bồi thường (Cash Refund)!"
        }, "Bước 2 hoàn tất: Dispatcher duyệt lỗi qua IoT Log thành công và búng hồ sơ sang Dashboard Kế toán.");
    }
}
