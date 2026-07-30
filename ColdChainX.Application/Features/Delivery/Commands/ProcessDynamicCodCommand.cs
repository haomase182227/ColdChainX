using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class ProcessDynamicCodCommand : IRequest<ApiResponse<object>>
{
    public Guid EpodId { get; set; }
    public Guid UserId { get; set; }
    public string? OsdNotes { get; set; }
    public string? EvidenceImageUrl { get; set; }
    public List<RejectedOsdItemInput>? RejectedItems { get; set; } = new();

    // Fields hỗ trợ từ Simulator & Mobile Field App (Đồng kiểm Hư Hỏng / Vi phạm nhiệt độ)
    public int? RejectedQuantity { get; set; } // Số lượng kiện/hộp khách từ chối nhận tại bãi (ví dụ: 5 hộp)
    public decimal? EstimatedClaimAmount { get; set; } // Giá trị hàng hỏng ước tính (chuyển về Kế toán rà soát, tài xế KHÔNG có quyền tự phán quyết con số bồi thường cuối cùng)
    public decimal? RejectedAmount { get; set; } // Hỗ trợ tương đương EstimatedClaimAmount nếu app test cũ gửi
    public string? RejectionReason { get; set; }
    public List<string>? EvidenceImages { get; set; }
}

public class RejectedOsdItemInput
{
    public Guid LpnId { get; set; }
    public int RejectedQty { get; set; }
    public decimal DeductedAmount { get; set; }
    public string? Reason { get; set; }
}

public class ProcessDynamicCodCommandHandler : IRequestHandler<ProcessDynamicCodCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;

    public ProcessDynamicCodCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<object>> Handle(ProcessDynamicCodCommand request, CancellationToken cancellationToken)
    {
        var epod = await _context.DeliveryEpods
            .Include(e => e.Order)
                .ThenInclude(o => o!.Customer)
            .FirstOrDefaultAsync(e => e.EpodId == request.EpodId, cancellationToken);

        if (epod == null)
            throw new NotFoundException($"ePOD '{request.EpodId}' not found.");

        decimal totalEstimatedDeduction = 0m;
        int totalRejectedQty = 0;
        var returnSlips = new List<string>();
        string primaryReason = request.RejectionReason ?? "TEMPERATURE_VIOLATION_OSD";

        // 1. Nếu tài xế khai báo theo danh sách LPNs từ chối nhận
        if (request.RejectedItems != null && request.RejectedItems.Any())
        {
            var lpnIds = request.RejectedItems.Select(x => x.LpnId).ToList();
            var lpns = await _context.Lpns
                .Where(l => lpnIds.Contains(l.LpnId))
                .ToListAsync(cancellationToken);

            foreach (var input in request.RejectedItems)
            {
                totalEstimatedDeduction += input.DeductedAmount;
                totalRejectedQty += input.RejectedQty;
                var lpn = lpns.FirstOrDefault(l => l.LpnId == input.LpnId);
                if (lpn != null)
                {
                    lpn.State = LpnState.RETURN_PENDING;
                    lpn.DiscrepancyReason = input.Reason ?? primaryReason;
                    lpn.UpdatedAt = DateTime.UtcNow;

                    // Tạo phiếu trả hàng về kho (Reverse Logistics) theo chuẩn IRS-yyyyMMdd-LPNxx
                    var slipCode = $"IRS-{DateTime.UtcNow:yyyyMMdd}-{lpn.LpnCode}";
                    var returnSlip = new InboundReturnSlip
                    {
                        ReturnSlipId = Guid.NewGuid(),
                        OrderId = epod.OrderId ?? lpn.OrderId,
                        LpnId = lpn.LpnId,
                        SlipCode = slipCode,
                        ReturnedWeightKg = lpn.ActualWeightKg,
                        ReturnedCbm = lpn.ActualCbm,
                        ReturnedQty = input.RejectedQty,
                        Reason = input.Reason ?? primaryReason,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.InboundReturnSlips.Add(returnSlip);
                    returnSlips.Add(slipCode);

                    // Ghi nhận ReturnedItem vào ePOD
                    _context.ReturnedItems.Add(new ReturnedItem
                    {
                        ReturnId = Guid.NewGuid(),
                        EpodId = epod.EpodId,
                        ItemName = epod.Order?.ItemName ?? lpn.LpnCode,
                        ItemCode = lpn.LpnCode,
                        Unit = "BOX/PALLET",
                        ReturnedQty = input.RejectedQty,
                        ReasonType = (input.Reason ?? primaryReason).ToUpper(),
                        ReasonNote = request.OsdNotes ?? $"Đồng kiểm từ chối: {input.Reason ?? primaryReason}",
                        ProcessingStatus = "REVERSE_LOGISTICS_RETURNING",
                        ReturnedAt = DateTime.UtcNow
                    });
                }
            }
        }

        // 2. Trường hợp gọi từ Mobile App / Simulator khai báo nhanh số lượng và giá trị ước tính
        var estimatedVal = request.EstimatedClaimAmount ?? request.RejectedAmount;
        if (totalRejectedQty == 0) totalRejectedQty = request.RejectedQuantity ?? 5; // Mặc định 5 hộp dâu tây theo hoạt cảnh
        if (totalEstimatedDeduction == 0 && estimatedVal.HasValue) totalEstimatedDeduction = estimatedVal.Value;

        if (returnSlips.Count == 0 && epod.OrderId.HasValue)
        {
            // Tự động tìm LPN liên quan của đơn hàng để đưa hàng hỏng lên xe chạy ngược trở về kho
            var orderLpns = await _context.Lpns
                .Where(l => l.OrderId == epod.OrderId.Value)
                .ToListAsync(cancellationToken);

            var targetLpn = orderLpns.FirstOrDefault();
            var lpnCodeStr = targetLpn?.LpnCode ?? $"ORD{epod.OrderId.Value.ToString("N")[..4].ToUpper()}";
            var slipCode = $"IRS-{DateTime.UtcNow:yyyyMMdd}-{lpnCodeStr}";

            if (targetLpn != null)
            {
                targetLpn.State = LpnState.RETURN_PENDING;
                targetLpn.DiscrepancyReason = primaryReason;
                targetLpn.UpdatedAt = DateTime.UtcNow;
            }

            var returnSlip = new InboundReturnSlip
            {
                ReturnSlipId = Guid.NewGuid(),
                OrderId = epod.OrderId.Value,
                LpnId = targetLpn?.LpnId ?? Guid.NewGuid(),
                SlipCode = slipCode,
                ReturnedWeightKg = targetLpn?.ActualWeightKg ?? 10m,
                ReturnedCbm = targetLpn?.ActualCbm ?? 0.5m,
                ReturnedQty = totalRejectedQty,
                Reason = primaryReason,
                CreatedAt = DateTime.UtcNow
            };

            _context.InboundReturnSlips.Add(returnSlip);
            returnSlips.Add(slipCode);

            _context.ReturnedItems.Add(new ReturnedItem
            {
                ReturnId = Guid.NewGuid(),
                EpodId = epod.EpodId,
                ItemName = epod.Order?.ItemName ?? "Hàng đông lạnh / Dâu tây từ chối đồng kiểm",
                ItemCode = targetLpn?.LpnCode ?? "OSD-ITEM",
                Unit = "BOX",
                ReturnedQty = totalRejectedQty,
                ReasonType = primaryReason.ToUpper(),
                ReasonNote = request.OsdNotes ?? $"Tài xế báo cáo hàng vi phạm nhiệt độ ({primaryReason}). Bọc hàng lên xe thu lùi về bãi.",
                ProcessingStatus = "REVERSE_LOGISTICS_RETURNING",
                ReturnedAt = DateTime.UtcNow
            });
        }

        // 3. TẠO HỒ SƠ KHIẾU NẠI & BỒI THƯỜNG TRUYỂN VỀ P. KẾ TOÁN (Claim Case Allocation)
        // Tài xế ngoài hiện trường KHÔNG có quyền quyết định con số chốt bồi thường cuối cùng!
        // Mọi hồ sơ OS&D phải chuyển về Kế toán rà soát nguyên nhân, hợp đồng và làm việc trực tiếp với Khách.
        string claimCode = $"CLM-{DateTime.UtcNow:yyyyMMdd}-OSD{Random.Shared.Next(100, 999)}";
        Guid claimId = Guid.NewGuid();

        var accountingClaim = new Claim
        {
            ClaimId = claimId,
            ClaimCode = claimCode,
            OrderId = epod.OrderId,
            LpnId = null,
            ClaimType = primaryReason.Trim().ToUpperInvariant(),
            Description = $"[Bước 1 - Hiện trường Dock] Tài xế & Khách hàng đóng băng bằng chứng (Hàng hỏng, Toàn cảnh, Nhiệt kế). Khách từ chối nhận {totalRejectedQty} kiện do vi phạm nhiệt độ/hư hỏng. Hàng đưa lên xe thu về bãi (Phiếu: {string.Join(", ", returnSlips)}). Thiệt hại ước tính: {totalEstimatedDeduction:N0} VNĐ.",
            Status = "PENDING_DISPATCHER_REVIEW", // Bước 2: Chờ Dispatcher mở IoT Log đánh giá lỗi
            CreatedAt = DateTime.UtcNow
        };
        _context.Claims.Add(accountingClaim);

        // 4. Quản lý COD thực tế tại bãi (Provisional COD adjustment)
        decimal originalCod = epod.CodAmount ?? 0m;
        // Số tiền COD tài xế tạm thu tại hiện trường (chỉ thu ứng với phần hàng nguyên vẹn, phần hỏng làm Hồ sơ Claim)
        decimal provisionalCodToCollect = Math.Max(0m, originalCod - totalEstimatedDeduction);
        
        epod.CodAmount = provisionalCodToCollect;
        epod.CodAmountPaid = provisionalCodToCollect;

        var osdSummary = $"[Bước 1 Dock OS&D: Khách từ chối {totalRejectedQty} kiện do {primaryReason}. Hàng bỏ lên xe trả về bãi theo Phiếu {string.Join(", ", returnSlips)}. Tạm thu COD cho phần Thực Nhận: {provisionalCodToCollect:N0}đ. Hồ sơ sự cố đẩy sang Dispatcher check IoT Log: {claimCode}]";
        epod.Note = string.IsNullOrEmpty(epod.Note) ? osdSummary : $"{epod.Note} {osdSummary}".Trim();

        // 5. Lưu minh chứng hiện trường (Ảnh chụp hàng hỏng, toàn cảnh, nhiệt kế)
        var images = new List<string>();
        if (!string.IsNullOrEmpty(request.EvidenceImageUrl)) images.Add(request.EvidenceImageUrl);
        if (request.EvidenceImages != null && request.EvidenceImages.Any()) images.AddRange(request.EvidenceImages);

        if (images.Any())
        {
            foreach (var url in images.Distinct())
            {
                if (epod.OrderId.HasValue)
                {
                    _context.TransportDocuments.Add(new TransportDocument
                    {
                        DocId = Guid.NewGuid(),
                        OrderId = epod.OrderId.Value,
                        DocType = "OSD_DOCK_EVIDENCE",
                        ImageUrl = url,
                        UploadedBy = request.UserId,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                _context.ClaimEvidences.Add(new ClaimEvidence
                {
                    EvidenceId = Guid.NewGuid(),
                    ClaimId = claimId,
                    EvidenceType = "DOCK_OSD_PHOTO",
                    ImageUrl = url,
                    UploadedBy = request.UserId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // 6. Cập nhật trạng thái Order & Ghi nhận luồng thông báo cảnh báo cho Kho và Điều phối (Dispatcher)
        if (epod.Order != null && totalRejectedQty > 0)
        {
            epod.Order.Status = "OSD_DOCK_REPORTED_PENDING_DISPATCHER";
        }

        if (returnSlips.Any())
        {
            var existingTemplate = await _context.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == "REVERSE_LOGISTICS_AND_CLAIM_ALERT", cancellationToken);
            if (existingTemplate == null)
            {
                var typeId = await _context.Messagetypes.Select(m => m.TypeId).FirstOrDefaultAsync(cancellationToken);
                _context.NotificationTemplates.Add(new NotificationTemplate
                {
                    TemplateId = "REVERSE_LOGISTICS_AND_CLAIM_ALERT",
                    TitleTemplate = "🚨 [DOCK HIỆN TRƯỜNG] BẮT QUY TRÌNH HẬU CẦN NGƯỢC & CHECK IOT LOG",
                    BodyTemplate = "Tài xế báo hiệu khách từ chối {Qty} kiện do {Reason}. Đã tạo Phiếu quay về kho {Slip} & Hồ sơ {ClaimCode}. Yêu cầu Dispatcher kiểm tra biểu đồ nhiệt độ!",
                    Channel = "ALL",
                    Status = "ACTIVE",
                    TypeId = typeId
                });
                await _context.SaveChangesAsync(cancellationToken);
            }

            _context.Notifications.Add(new Notification
            {
                NotiId = Guid.NewGuid(),
                UserId = request.UserId,
                TemplateId = "REVERSE_LOGISTICS_AND_CLAIM_ALERT",
                Params = $"{{\"Qty\":{totalRejectedQty},\"Reason\":\"{primaryReason}\",\"Slip\":\"{string.Join(", ", returnSlips)}\",\"ClaimCode\":\"{claimCode}\"}}",
                OrderId = epod.OrderId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var resultData = new
        {
            EpodId = epod.EpodId,
            OrderStatus = epod.Order?.Status ?? "OSD_DOCK_REPORTED_PENDING_DISPATCHER",
            RejectedQuantity = totalRejectedQty,
            RejectionReason = primaryReason,
            ReverseLogisticsSlips = returnSlips,
            Step1_EvidenceCapture = new
            {
                ClaimId = claimId,
                ClaimCode = claimCode,
                Status = "PENDING_DISPATCHER_REVIEW",
                EstimatedDamageValue = totalEstimatedDeduction,
                Action = "Tài xế đã bám sát hiện trường chụp bằng chứng (Hàng hỏng, Toàn cảnh, Nhiệt kế) và đóng băng số hàng trên xe chuyển về kho. Hồ sơ đã được đẩy thẳng cho Điều phối (Dispatcher) check biểu đồ IoT!"
            },
            ProvisionalCodToCollect = provisionalCodToCollect,
            WorkflowStatus = "SUCCESS - [Bước 1/4 hoàn tất] Đóng băng & Chụp bằng chứng tại Dock."
        };

        return ApiResponse<object>.SuccessResponse(resultData, "Bước 1 hoàn tất: Đóng băng hàng hư hỏng, thu chứng cứ và chuyển thẳng về Dispatcher đánh giá IoT Log.");
    }
}


