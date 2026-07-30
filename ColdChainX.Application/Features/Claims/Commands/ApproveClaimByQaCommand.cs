using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Claims.Commands;

public class ApproveClaimByQaCommand : IRequest<ApiResponse<object>>
{
    public Guid ClaimId { get; set; }
    public Guid QaUserId { get; set; }
    public string? FaultOwner { get; set; } = "COMPANY_COLDCHAIN"; // Mặc định lỗi hệ thống lạnh
    public string? InternalChargebackOption { get; set; } = "COMPANY_EXPENSE"; // "DRIVER_DEBT" hoặc "COMPANY_EXPENSE"
    public string? QaNote { get; set; }
    public bool IsTemperatureFaultConfirmed { get; set; } = true; // Dispatcher xác nhận lỗi nhiệt qua biểu đồ IoT Log
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
        string fault = request.FaultOwner ?? "COMPANY_COLDCHAIN";
        string chargeback = request.InternalChargebackOption ?? "COMPANY_EXPENSE";

        claim.Status = "PENDING_ACCOUNTANT_REVIEW";
        claim.FaultOwner = fault;
        claim.InternalChargebackOption = chargeback;
        claim.ResolutionNote = $"[Bước 2 - Dispatcher/QA Approved by {request.QaUserId}]: Đã kiểm tra biểu đồ nhiệt độ (IoT Log). Bấm [Duyệt lỗi] xác nhận lỗi thuộc về {fault} (Hạch toán: {chargeback}). Hồ sơ lập tức búng thẳng sang Dashboard của Kế Toán (Accountant)! Ghi chú: {request.QaNote}";
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
            FaultOwner = claim.FaultOwner,
            InternalChargebackOption = claim.InternalChargebackOption,
            IotLogCheckResult = request.IsTemperatureFaultConfirmed ? "CONFIRMED_FAULT_ON_IOT_CHART" : "NO_FAULT",
            Step2_DispatcherAction = "Dispatcher đã mở hệ thống check biểu đồ IoT Log xác nhận sai nhiệt độ -> Bấm [Duyệt lỗi] -> Đã đẩy thẳng hồ sơ bồi thường sang Dashboard của Kế Toán (Accountant)!",
            NextStep = "Bước 3: Khách và Tài xế gạch sổ ký tay thực nhận trên Phiếu Giao Nhận giấy rồi chụp upload. Bước 4: Kế toán chốt chi tiền bồi thường (Cash Refund)!"
        }, "Bước 2 hoàn tất: Dispatcher duyệt lỗi qua IoT Log thành công và búng hồ sơ sang Dashboard Kế toán.");
    }
}
