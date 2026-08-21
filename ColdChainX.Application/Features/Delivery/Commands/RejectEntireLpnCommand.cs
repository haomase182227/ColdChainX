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

public class RejectEntireLpnCommand : IRequest<ApiResponse<object>>
{
    public Guid StopId { get; set; }
    public Guid TripId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid UserId { get; set; }
    public string? RejectionReason { get; set; }
    public bool IsReturnToWarehouse { get; set; } = true;
    public IFormFile? EvidenceImageFile { get; set; }
    public string? EvidenceImageUrl { get; set; } // Fallback cho unit test
}

public class RejectEntireLpnCommandHandler : IRequestHandler<RejectEntireLpnCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileService? _fileService;

    public RejectEntireLpnCommandHandler(IApplicationDbContext context, IFileService? fileService = null)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<ApiResponse<object>> Handle(RejectEntireLpnCommand request, CancellationToken cancellationToken)
    {
        var stop = await _context.TripStops
            .Include(ts => ts.Location)
            .Include(ts => ts.Trip)
            .FirstOrDefaultAsync(ts => ts.StopId == request.StopId, cancellationToken);

        if (stop == null)
            throw new NotFoundException($"Không tìm thấy điểm dừng có StopId '{request.StopId}'.");

        if (stop.TripId != request.TripId)
            throw new ValidationException($"Điểm dừng (StopId: {request.StopId}) không thuộc chuyến đi (TripId: {request.TripId}).");

        if (stop.ActualArrivalTime == null)
            throw new ValidationException("Tài xế phải check-in tại điểm dừng trước khi xử lý từ chối bàn giao hàng.");

        var order = await _context.TransportOrders
            .Include(o => o.Customer)
            .Include(o => o.Quotations)
            .FirstOrDefaultAsync(o => o.MasterTripId == request.TripId && o.CustomerId == request.CustomerId && o.DestLocation == stop.LocationId, cancellationToken);

        if (order == null)
            throw new NotFoundException($"Không tìm thấy Đơn hàng nào thuộc khách hàng (CustomerId: {request.CustomerId}) trong chuyến xe (TripId: {request.TripId}) tại điểm dừng này.");

        var existingEpod = await _context.DeliveryEpods
            .FirstOrDefaultAsync(e => e.OrderId == order.OrderId && e.HandoverConfirmedAt != null, cancellationToken);
        if (existingEpod != null)
            throw new ConflictException($"Đơn hàng '{order.TrackingCode}' đã được hoàn tất bàn giao và ký chốt sổ trước đó (ePOD: {existingEpod.EpodId}). Không thể thao tác từ chối LPN lại.");

        string evidenceUrl = request.EvidenceImageUrl ?? "";
        if (request.EvidenceImageFile != null && _fileService != null)
        {
            try
            {
                evidenceUrl = await _fileService.UploadFileAsync(request.EvidenceImageFile);
            }
            catch (Exception ex)
            {
                throw new ValidationException($"Lỗi khi tải lên ảnh minh chứng từ chối LPN: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(evidenceUrl))
        {
            throw new ValidationException("Vui lòng đính kèm file ảnh chụp minh chứng sự cố từ chối toàn bộ LPN.");
        }

        var lpns = await _context.Lpns
            .Where(l => l.OrderId == order.OrderId && (l.TripId == request.TripId || stop.Trip == null || l.TripId == stop.Trip.TripId))
            .ToListAsync(cancellationToken);

        if (lpns.Count == 0)
            throw new NotFoundException($"Đơn hàng (OrderId: {order.OrderId}) hiện chưa có kiện LPN nào trên chuyến xe để xử lý sự cố từ chối.");

        var orderLpn = lpns.First();
        int originalQty = orderLpn.Quantity > 0 ? orderLpn.Quantity : (order.Quantity);
        string primaryReason = string.IsNullOrWhiteSpace(request.RejectionReason) ? "TEMP_VIOLATION_FULL" : request.RejectionReason;

        var quotation = QuotationSelectionHelper.SelectBillingQuotation(order.Quotations);

        decimal baseAmount = quotation?.FinalAmount ?? 0m;
        if (baseAmount <= 0)
        {
            throw new ValidationException($"Đơn hàng '{order.TrackingCode}' chưa có Báo giá (Quotation) hợp lệ hoặc giá trị cước bằng 0. Hệ thống không thể chiết tính phí bồi thường!");
        }

        decimal estimatedDeduction = baseAmount; // Khách từ chối 100%, khấu trừ/bồi thường toàn bộ giá trị báo giá

        string returnStatusText = request.IsReturnToWarehouse ? "Đã lập Phiếu thu hồi về kho bãi (InboundReturnSlip) cho toàn bộ LPN" : "Tài xế bàn giao tang vật cho khách tự thỏa thuận tiêu hủy/xử lý tại chỗ (Không mang về bãi kho)";
        string generatedOsdNotes = $"[Hệ thống tự động lập - TỪ CHỐI 100% LPN] Khách hàng từ chối toàn bộ {originalQty}/{originalQty} kiện tại bãi Dock. Lý do: {primaryReason}. Tự động chiết tính bồi thường 100% theo Quotation: -{estimatedDeduction:N0}đ | COD thực thu: 0đ | Xử lý tang vật: {returnStatusText}.";

        var now = DateTime.UtcNow;

        orderLpn.State = LpnState.DELIVERY_RETURNED;
        orderLpn.DiscrepancyReason = primaryReason;
        orderLpn.EvidenceImageUrl = evidenceUrl;
        orderLpn.UpdatedAt = now;

        order.Status = "OSD_REJECT_PENDING";
        order.Quantity = 0; // Số lượng thực nhận = 0

        string claimCode = $"CLM-{now:yyyyMMdd}-FULL{Random.Shared.Next(100, 999)}";
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
            CreatedAt = now
        };
        _context.Claims.Add(accountingClaim);

        var epodId = Guid.NewGuid();
        var epod = new DeliveryEpod
        {
            EpodId = epodId,
            OrderId = order.OrderId,
            CheckinTime = stop.ActualArrivalTime ?? now,
            SignedAt = now,
            HandoverConfirmedAt = now,
            SignLatitude = stop.Location?.Latitude,
            SignLongitude = stop.Location?.Longitude,
            Status = "OSD_FULL_REJECTED",
            CodAmount = 0m,
            CodAmountPaid = 0m,
            PaymentStatus = "SKIPPED_FULL_REJECT",
            Note = generatedOsdNotes,
            CreatedAt = now
        };
        _context.DeliveryEpods.Add(epod);

        object? returnSlipResult = null;
        if (request.IsReturnToWarehouse)
        {
            var returnSlip = new InboundReturnSlip
            {
                ReturnSlipId = Guid.NewGuid(),
                OrderId = order.OrderId,
                LpnId = orderLpn.LpnId,
                SlipCode = orderLpn.LpnCode,
                ReturnedQty = originalQty,
                ReturnedWeightKg = orderLpn.ActualWeightKg,
                ReturnedCbm = orderLpn.ActualCbm,
                Reason = primaryReason,
                CreatedAt = now
            };
            _context.InboundReturnSlips.Add(returnSlip);

            var returnedItem = new ReturnedItem
            {
                ReturnId = returnSlip.ReturnSlipId,
                EpodId = epodId,
                ItemName = !string.IsNullOrWhiteSpace(order.ItemName) ? order.ItemName : "Toàn bộ LPN bị từ chối tại Dock",
                ItemCode = orderLpn.LpnCode,
                Unit = "BOX",
                ReturnedQty = originalQty,
                ReasonType = primaryReason,
                ReasonNote = $"[Từ chối trọn gói LPN] Khách từ chối 100% lô hàng. Cờ IsReturnToWarehouse = TRUE.",
                ProcessingStatus = "PENDING_INBOUND",
                ReturnedAt = now
            };
            _context.ReturnedItems.Add(returnedItem);

            returnSlipResult = new
            {
                ReturnSlipId = returnSlip.ReturnSlipId,
                SlipCode = returnSlip.SlipCode,
                ReturnedQty = originalQty,
                Status = "PENDING_INBOUND",
                Note = "Phiếu hậu cần ngược được tự động lập để tài xế chở toàn bộ LPN về nhập bãi kho bảo lưu."
            };
        }

        _context.TransportDocuments.Add(new TransportDocument
        {
            DocId = Guid.NewGuid(),
            OrderId = order.OrderId,
            DocType = "OSD_FULL_REJECT_EVIDENCE",
            ImageUrl = evidenceUrl,
            UploadedBy = request.UserId,
            CreatedAt = now
        });

        _context.ClaimEvidences.Add(new ClaimEvidence
        {
            EvidenceId = Guid.NewGuid(),
            ClaimId = claimId,
            EvidenceType = "DOCK_OSD_FULL_PHOTO",
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
            Qty = originalQty,
            Ratio = "100%",
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
            HandoverProcessedAt = now,
            OrderStatus = order.Status,
            LpnCode = orderLpn.LpnCode,
            OriginalQuantity = originalQty,
            RejectedQuantity = originalQty,
            AcceptedQuantity = 0,
            RejectedRatio = "100%",
            RejectionReason = primaryReason,
            OsdNotes = generatedOsdNotes,
            IsReturnToWarehouse = request.IsReturnToWarehouse,
            InboundReturnSlip = returnSlipResult ?? "Không tạo phiếu trả hàng về kho (Khách và tài xế tự thỏa thuận xử lý/tiêu hủy tại hiện trường)",
            AutomatedClaimCalculation = new
            {
                QuotationBaseAmount = baseAmount,
                EstimatedClaimDeduction = estimatedDeduction,
                ActualCodDue = 0m,
                Note = "Khách hàng từ chối 100% lô hàng LPN. Hệ thống tự động trích lập hồ sơ bồi thường toàn bối gửi Kế toán."
            },
            Step1_EvidenceCapture = new
            {
                ClaimId = claimId,
                ClaimCode = claimCode,
                Status = "PENDING_DISPATCHER_REVIEW",
                EvidenceImageUrl = evidenceUrl,
                Action = "Đã chụp ảnh minh chứng sự cố từ chối toàn bối và thông báo tức thời tới bộ phận Kế toán & Điều phối."
            },
            PaymentSkipped = true,
            NextStep = $"POST /api/Delivery/trips/{request.TripId}/seals/apply — Do khách không nhận hàng và COD thu thực = 0, hệ thống tự động lược qua khúc thanh toán QR (payment-qr & verify-qr-payment). Tài xế chuyển tiếp sang thực hiện đóng kẹp chì (Seal) mới để xuất phát tới điểm dừng tiếp theo!"
        };

        return ApiResponse<object>.SuccessResponse(resultData, "Xử lý từ chối toàn bộ LPN thành công. Đã thông báo Kế toán tính bồi thường, lược bỏ quy trình thu COD và chuẩn bị đóng seal mới.");
    }
}
