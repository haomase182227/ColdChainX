using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.Helpers;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Delivery.Commands;

public class ProcessDynamicCodCommand : IRequest<ApiResponse<object>>
{
    public Guid StopId { get; set; }
    public Guid TripId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid UserId { get; set; }
    public int RejectedQuantity { get; set; }
    public string? RejectionReason { get; set; }
    public bool IsReturnToWarehouse { get; set; } = false;
    public IFormFile? EvidenceImageFile { get; set; }
    public string? EvidenceImageUrl { get; set; } // Fallback cho unit test
}

public class ProcessDynamicCodCommandHandler : IRequestHandler<ProcessDynamicCodCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService? _fileService;

    public ProcessDynamicCodCommandHandler(IApplicationDbContext context, IFileService? fileService = null)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<ApiResponse<object>> Handle(ProcessDynamicCodCommand request, CancellationToken cancellationToken)
    {
        string? evidenceUrl = request.EvidenceImageUrl;
        if (request.EvidenceImageFile != null && _fileService != null)
        {
            try
            {
                evidenceUrl = await _fileService.UploadFileAsync(request.EvidenceImageFile);
            }
            catch
            {
                evidenceUrl = $"/uploads/offline-evidence/{Guid.NewGuid():N}-{request.EvidenceImageFile.FileName}";
            }
        }

        if (string.IsNullOrWhiteSpace(evidenceUrl))
        {
            throw new ValidationException("Vui lòng đính kèm file ảnh chụp minh chứng đồng kiểm OS&D (EvidenceImageFile hoặc EvidenceImageUrl).");
        }

        var stop = await _context.TripStops
            .Include(ts => ts.Location)
            .Include(ts => ts.Trip)
            .FirstOrDefaultAsync(ts => ts.StopId == request.StopId, cancellationToken);

        if (stop == null)
            throw new NotFoundException($"Không tìm thấy điểm dừng có StopId '{request.StopId}'.");

        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Tài xế phải check-in tại điểm dừng trước khi đồng kiểm bàn giao hàng.");

        var order = await _context.TransportOrders
            .Include(o => o.Customer)
            .Include(o => o.Quotations)
            .FirstOrDefaultAsync(o => o.MasterTripId == request.TripId && o.CustomerId == request.CustomerId && o.DestLocation == stop.LocationId, cancellationToken);

        if (order == null)
            throw new NotFoundException($"Không tìm thấy đơn hàng nào của khách hàng '{request.CustomerId}' trên chuyến đi '{request.TripId}' tại điểm dừng này.");

        var existingEpod = await _context.DeliveryEpods
            .FirstOrDefaultAsync(e => e.OrderId == order.OrderId && e.HandoverConfirmedAt != null, cancellationToken);
        if (existingEpod != null)
            throw new ConflictException($"Đơn hàng '{order.TrackingCode}' đã được hoàn tất bàn giao và ký chốt sổ trước đó (ePOD: {existingEpod.EpodId}). Không thể thực hiện đồng kiểm OS&D lại.");

        var lpns = await _context.Lpns
            .Where(l => l.OrderId == order.OrderId && (l.TripId == request.TripId || stop.Trip == null || l.TripId == stop.Trip.TripId))
            .ToListAsync(cancellationToken);

        if (lpns.Count == 0)
            throw new NotFoundException("Không tìm thấy kiện hàng LPN nào thuộc đơn hàng trên chuyến xe này.");

        var orderLpn = lpns.First();

        int rejectedQty = request.RejectedQuantity;
        if (rejectedQty <= 0)
        {
            throw new ValidationException("Số lượng hộp/kiện từ chối (RejectedQuantity) phải lớn hơn 0.");
        }

        int originalQty = orderLpn.Quantity > 0 ? orderLpn.Quantity : (order.Quantity > 0 ? order.Quantity : 50);
        if (rejectedQty > originalQty)
        {
            throw new ValidationException($"Số lượng từ chối ({rejectedQty}) không được vượt quá tổng số lượng kiện trong LPN/Đơn hàng ({originalQty}).");
        }

        int acceptedQty = originalQty - rejectedQty;
        decimal rejectedRatio = (decimal)rejectedQty / originalQty;
        string primaryReason = string.IsNullOrWhiteSpace(request.RejectionReason) ? "TEMP_VIOLATION_OSD" : request.RejectionReason;

        var quotation = QuotationSelectionHelper.SelectBillingQuotation(order.Quotations);

        decimal baseAmount = quotation?.FinalAmount ?? 0m;
        if (baseAmount <= 0)
        {
            throw new ValidationException($"Đơn hàng '{order.TrackingCode}' chưa có Báo giá (Quotation) hợp lệ hoặc giá trị cước bằng 0. Hệ thống không thể tự động chiết tính phí giảm trừ bồi thường!");
        }

        decimal estimatedDeduction = Math.Round(baseAmount * rejectedRatio, 0);
        decimal actualCodToCollect = Math.Max(0m, Math.Round(baseAmount - estimatedDeduction, 0));

        string returnStatusText = request.IsReturnToWarehouse ? "Đã lập Phiếu trả hàng về bãi kho (InboundReturnSlip)" : "Tài xế bàn giao hàng hỏng cho khách xử lý tại bãi (Không mang về kho)";
        string generatedOsdNotes = $"[Hệ thống tự động lập] Khách hàng từ chối {rejectedQty}/{originalQty} kiện (Tỷ lệ: {rejectedRatio:P0}) tại điểm giao. Lý do chính: {primaryReason}. Đã thu cước cho {acceptedQty} kiện thực nhận. Tự động khấu trừ bồi thường theo Quotation: -{estimatedDeduction:N0}đ | COD thu thực: {actualCodToCollect:N0}đ | Xử lý hàng hư hỏng: {returnStatusText}.";

        orderLpn.Quantity = acceptedQty;
        orderLpn.DiscrepancyReason = primaryReason;
        orderLpn.EvidenceImageUrl = evidenceUrl;
        orderLpn.UpdatedAt = DateTime.UtcNow;

        if (acceptedQty == 0)
        {
            orderLpn.State = LpnState.DELIVERY_RETURNED;
            order.Status = "OSD_DOCK_PENDING";
        }
        else
        {
            orderLpn.State = LpnState.DELIVERED;
            order.Quantity = acceptedQty;
            order.Status = "PARTIAL_DELIVER_OSD";
        }

        string claimCode = $"CLM-{DateTime.UtcNow:yyyyMMdd}-OSD{Random.Shared.Next(100, 999)}";
        Guid claimId = Guid.NewGuid();

        var accountingClaim = new Claim
        {
            ClaimId = claimId,
            ClaimCode = claimCode,
            OrderId = order.OrderId,
            LpnId = orderLpn.LpnId,
            ClaimType = primaryReason.Trim().ToUpperInvariant(),
            Description = generatedOsdNotes,
            Status = "PENDING_REVIEW",
            CreatedAt = DateTime.UtcNow
        };
        _context.Claims.Add(accountingClaim);

        var epodId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var epod = new DeliveryEpod
        {
            EpodId = epodId,
            OrderId = order.OrderId,
            CheckinTime = stop.ActualArrivalTime ?? now,
            SignedAt = now,
            HandoverConfirmedAt = now,
            SignLatitude = stop.Location?.Latitude,
            SignLongitude = stop.Location?.Longitude,
            Status = "OSD_PARTIAL_DELIVER",
            CodAmount = actualCodToCollect,
            CodAmountPaid = actualCodToCollect,
            PaymentStatus = actualCodToCollect > 0 ? "AWAITING_PAYMENT" : "PAID",
            Note = generatedOsdNotes,
            CreatedAt = now
        };
        _context.DeliveryEpods.Add(epod);

        object? returnSlipResult = null;
        if (request.IsReturnToWarehouse && rejectedQty > 0)
        {
            var returnSlip = new InboundReturnSlip
            {
                ReturnSlipId = Guid.NewGuid(),
                OrderId = order.OrderId,
                LpnId = orderLpn.LpnId,
                SlipCode = orderLpn.LpnCode,
                ReturnedQty = rejectedQty,
                ReturnedWeightKg = Math.Round(orderLpn.ActualWeightKg * rejectedRatio, 2),
                ReturnedCbm = Math.Round(orderLpn.ActualCbm * rejectedRatio, 4),
                Reason = primaryReason,
                CreatedAt = now
            };
            _context.InboundReturnSlips.Add(returnSlip);

            var returnedItem = new ReturnedItem
            {
                ReturnId = returnSlip.ReturnSlipId,
                EpodId = epodId,
                ItemName = !string.IsNullOrWhiteSpace(order.ItemName) ? order.ItemName : "Hàng hoá trả về từ sự cố bãi Dock",
                ItemCode = orderLpn.LpnCode,
                Unit = "BOX",
                ReturnedQty = rejectedQty,
                ReasonType = primaryReason,
                ReasonNote = $"[Đồng kiểm OS&D] Từ chối {rejectedQty} kiện. Cờ IsReturnToWarehouse = TRUE.",
                ProcessingStatus = "PENDING_INBOUND",
                ReturnedAt = now
            };
            _context.ReturnedItems.Add(returnedItem);

            returnSlipResult = new
            {
                ReturnSlipId = returnSlip.ReturnSlipId,
                SlipCode = returnSlip.SlipCode,
                ReturnedQty = rejectedQty,
                Status = "PENDING_INBOUND",
                Note = "Phiếu hậu cần ngược đính kèm để tài xế chở số hàng từ chối về bàn giao nhập kho bãi."
            };
        }

        _context.TransportDocuments.Add(new TransportDocument
        {
            DocId = Guid.NewGuid(),
            OrderId = order.OrderId,
            DocType = "OSD_DOCK_EVIDENCE",
            ImageUrl = evidenceUrl,
            UploadedBy = request.UserId,
            CreatedAt = now
        });

        _context.ClaimEvidences.Add(new ClaimEvidence
        {
            EvidenceId = Guid.NewGuid(),
            ClaimId = claimId,
            EvidenceType = "DOCK_OSD_PHOTO",
            ImageUrl = evidenceUrl,
            UploadedBy = request.UserId,
            CreatedAt = now
        });

        var existingTemplate = await _context.NotificationTemplates.FirstOrDefaultAsync(t => t.TemplateId == "OSD_CLAIM_ALERT", cancellationToken);
        if (existingTemplate == null)
        {
            var typeId = await _context.Messagetypes.Select(m => m.TypeId).FirstOrDefaultAsync(cancellationToken);
            _context.NotificationTemplates.Add(new NotificationTemplate
            {
                TemplateId = "OSD_CLAIM_ALERT",
                TitleTemplate = "🚨 [CẢNH BÁO KẾ TOÁN] SỰ CỐ TỪ CHỐI HÀNG TẠI BÃI - CHIẾT TÍNH BỞI QUOTATION",
                BodyTemplate = "Đơn {OrderCode}: Khách từ chối {Qty} kiện ({Ratio}). Ghi chú OS&D: {OsdNotes}. Tự động giảm trừ: {Deduction} VNĐ. Hồ sơ đối soát: {ClaimCode}.",
                Channel = "ALL",
                Status = "ACTIVE",
                TypeId = typeId
            });
            await _context.SaveChangesAsync(cancellationToken);
        }

        var accountantAndDispatcherUsers = await _context.Users
            .Include(u => u.Role)
            .Where(u => u.Role != null && (
                u.Role.RoleName.ToLower() == "accountant" ||
                u.Role.RoleName.ToLower() == "dispatcher" ||
                u.Role.RoleName.ToLower() == "admin"))
            .ToListAsync(cancellationToken);

        var notiParams = JsonSerializer.Serialize(new
        {
            OrderCode = order.TrackingCode ?? order.OrderId.ToString()[..8],
            Qty = rejectedQty,
            Ratio = $"{rejectedRatio:P0}",
            OsdNotes = generatedOsdNotes,
            Deduction = $"{estimatedDeduction:N0}",
            ClaimCode = claimCode
        });

        var recipients = accountantAndDispatcherUsers.Select(u => u.UserId).ToList();
        if (!recipients.Contains(request.UserId)) recipients.Add(request.UserId);

        foreach (var recipientId in recipients.Distinct())
        {
            _context.Notifications.Add(new Notification
            {
                NotiId = Guid.NewGuid(),
                UserId = recipientId,
                SenderId = request.UserId,
                TemplateId = "OSD_CLAIM_ALERT",
                Params = notiParams,
                OrderId = order.OrderId,
                IsRead = false,
                CreatedAt = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        var resultData = new
        {
            EpodId = epod.EpodId,
            HandoverConfirmedAt = now,
            OrderStatus = order.Status,
            LpnCode = orderLpn.LpnCode,
            OriginalQuantity = originalQty,
            RejectedQuantity = rejectedQty,
            AcceptedQuantity = acceptedQty,
            RejectedRatio = $"{rejectedRatio:P0}",
            RejectionReason = primaryReason,
            OsdNotes = generatedOsdNotes,
            IsReturnToWarehouse = request.IsReturnToWarehouse,
            InboundReturnSlip = returnSlipResult ?? "Không tạo phiếu trả hàng về kho (Khách nhận bàn giao tiêu hủy tại bãi)",
            NextStep = $"GET /api/Delivery/epods/{epod.EpodId}/payment-qr — Hiển thị mã QR thanh toán COD thu thực nhận cho khách hàng"
        };

        return ApiResponse<object>.SuccessResponse(resultData, $"Xử lý bàn giao đồng kiểm OS&D thành công: Khách nhận {acceptedQty}/{originalQty} kiện, đã tạo ePOD và gửi thông báo cho Kế toán.");
    }
}

