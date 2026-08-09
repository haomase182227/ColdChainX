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

public class RejectClaimByQaCommand : IRequest<ApiResponse<object>>
{
    [JsonIgnore]
    public Guid ClaimId { get; set; }
    
    [JsonIgnore]
    public Guid QaUserId { get; set; }
    
    public string Note { get; set; } = null!; // Bắt buộc phải có lý do từ chối
}

public class RejectClaimByQaCommandHandler : IRequestHandler<RejectClaimByQaCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public RejectClaimByQaCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(RejectClaimByQaCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.Claims
            .Include(c => c.Order)
            .FirstOrDefaultAsync(c => c.ClaimId == request.ClaimId, cancellationToken);

        if (claim == null)
            throw new NotFoundException($"Không tìm thấy yêu cầu bồi thường '{request.ClaimId}'.");

        claim.Status = "REJECTED";
        claim.ResolutionNote = $"[Bước 2 - Dispatcher/QA Rejected by {request.QaUserId}]: TỪ CHỐI BỒI THƯỜNG. Lý do: {request.Note}";
        claim.ResolvedAt = DateTime.UtcNow;

        if (claim.Order != null)
        {
            claim.Order.Status = "OSD_CLAIM_REJECTED_BY_DISPATCHER";
        }

        await _context.SaveChangesAsync(cancellationToken);

        return ApiResponse<object>.SuccessResponse(new
        {
            ClaimId = claim.ClaimId,
            ClaimCode = claim.ClaimCode,
            Status = claim.Status,
            Step2_DispatcherAction = "Dispatcher đã từ chối khiếu nại này. Hồ sơ khép lại và sẽ không được chuyển sang Kế toán."
        }, "Đã từ chối khiếu nại bồi thường thành công.");
    }
}
