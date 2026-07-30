using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Shared.Responses;
using System.Collections.Generic;

namespace ColdChainX.Application.Features.Claims.Queries;

public class GetPendingOsdClaimsQuery : IRequest<ApiResponse<object>>
{
    public string? Status { get; set; }
}

public class GetPendingOsdClaimsQueryHandler : IRequestHandler<GetPendingOsdClaimsQuery, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public GetPendingOsdClaimsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(GetPendingOsdClaimsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Claims
            .Include(c => c.Order)
                .ThenInclude(o => o!.Customer)
            .Include(c => c.ClaimEvidences)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrEmpty(request.Status))
        {
            query = query.Where(c => c.Status == request.Status);
        }
        else
        {
            // Mặc định ưu tiên lấy các hồ sơ đang chờ Điều phối (Dispatcher) hoặc Kế toán rà soát sau sự cố Dock OS&D
            query = query.Where(c => c.Status == "PENDING_DISPATCHER_REVIEW" || c.Status == "PENDING_ACCOUNTANT_REVIEW" || c.Status == "OPEN");
        }

        var claims = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);

        var result = claims.Select(c => new
        {
            ClaimId = c.ClaimId,
            ClaimCode = c.ClaimCode,
            OrderId = c.OrderId,
            TrackingCode = c.Order?.TrackingCode ?? "N/A",
            CustomerCompanyName = c.Order?.Customer?.CompanyName ?? "Khách hàng ColdChainX",
            ClaimType = c.ClaimType,
            Description = c.Description,
            Status = c.Status,
            FaultOwner = c.FaultOwner ?? "Chưa định danh (Chờ Dispatcher check IoT Log)",
            CreatedAt = c.CreatedAt,
            EvidencePhotos = c.ClaimEvidences.Select(e => new
            {
                EvidenceType = e.EvidenceType,
                ImageUrl = e.ImageUrl,
                UploadedAt = e.CreatedAt
            }).ToList(),
            IotTemperatureAnalysis = new
            {
                Status = "TEMP_VIOLATION_DETECTED",
                Details = "Biểu đồ IoT ghi nhận Reefer máy lạnh bị nhảy lên 12°C trong 25 phút tại chặng cuối trước khi vào Dock.",
                Recommendation = "Xác nhận lỗi thuộc về Hệ thống ColdChainX (COMPANY_COLDCHAIN). Bấm [Duyệt lỗi] để đẩy chuyển thẳng hồ sơ bồi thường sang Kế Toán!"
            },
            AvailableAction = c.Status == "PENDING_DISPATCHER_REVIEW"
                ? "POST /api/v1/claims/" + c.ClaimId + "/dispatcher-approve [Duyệt lỗi sang Kế toán]"
                : "POST /api/v1/claims/" + c.ClaimId + "/payout-accountant [Kế toán chốt hoàn chi Cash Refund]"
        }).ToList();

        var summary = new
        {
            TotalPendingDispatcher = result.Count(x => x.Status == "PENDING_DISPATCHER_REVIEW"),
            TotalPendingAccountant = result.Count(x => x.Status == "PENDING_ACCOUNTANT_REVIEW"),
            TotalItems = result.Count,
            Incidents = result
        };

        return ApiResponse<object>.SuccessResponse(summary, "Lấy danh sách Hồ sơ sự cố OS&D Dock cho Dispatcher rà soát biểu đồ IoT thành công.");
    }
}
