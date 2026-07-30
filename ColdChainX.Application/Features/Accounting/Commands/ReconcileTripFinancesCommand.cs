using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Exceptions;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Features.Accounting.Commands;

public class ReconcileTripFinancesCommand : IRequest<ApiResponse<object>>
{
    public Guid TripId { get; set; }
    public Guid AccountantUserId { get; set; }
    public decimal ActualCashReceived { get; set; }
    public decimal ActualQrReceived { get; set; }
    public string? Note { get; set; }
}

public class ReconcileTripFinancesCommandHandler : IRequestHandler<ReconcileTripFinancesCommand, ApiResponse<object>>
{
    private readonly IApplicationDbContext _context;
    private readonly IErpIntegrationService _erpService;

    public ReconcileTripFinancesCommandHandler(IApplicationDbContext context, IErpIntegrationService erpService)
    {
        _context = context;
        _erpService = erpService;
    }

    public async Task<ApiResponse<object>> Handle(ReconcileTripFinancesCommand request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TripStops)
            .Include(t => t.TransportOrders)
                .ThenInclude(o => o.Customer)
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken);

        if (trip == null)
            throw new NotFoundException($"Không tìm thấy chuyến xe '{request.TripId}'.");

        var stopIds = trip.TripStops.Select(s => s.StopId).ToList();

        // 1. Tổng thu Dynamic COD từ ePODs
        var epods = await _context.DeliveryEpods
            .Where(e => e.Order != null && e.Order.MasterTripId == request.TripId)
            .ToListAsync(cancellationToken);

        decimal expectedCodCash = epods.Where(e => string.Equals(e.PaymentMethod, "CASH", StringComparison.OrdinalIgnoreCase)).Sum(e => e.CodAmountPaid ?? 0m);
        decimal expectedCodQr = epods.Where(e => string.Equals(e.PaymentMethod, "QR", StringComparison.OrdinalIgnoreCase)).Sum(e => e.CodAmountPaid ?? 0m);
        decimal totalExpectedCod = expectedCodCash + expectedCodQr;

        // 2. Tổng thu Phí neo xe (Detention Charges) của các điểm dừng trong chuyến
        var detentionCharges = await _context.DetentionCharges
            .Where(c => stopIds.Contains(c.StopId))
            .ToListAsync(cancellationToken);
        decimal totalDetentionFee = detentionCharges.Sum(c => c.AmountCharged);

        decimal totalExpectedRevenue = totalExpectedCod + totalDetentionFee;
        decimal totalActualReceived = request.ActualCashReceived + request.ActualQrReceived;
        decimal totalDiscrepancy = totalActualReceived - totalExpectedRevenue;

        string reconciliationStatus = "RECONCILED_SUCCESS";
        string? penaltyBillCode = null;

        // 3. Trường hợp nộp hụt tiền -> Sinh PenaltyBill trừ vào lương / công nợ tài xế
        if (totalDiscrepancy < -0.01m)
        {
            reconciliationStatus = "RECONCILED_WITH_DEFICIT";
            decimal deficitAmount = Math.Abs(totalDiscrepancy);

            var tripDriver = await _context.TripDrivers
                .Where(td => td.TripId == request.TripId)
                .Include(td => td.Driver)
                .FirstOrDefaultAsync(cancellationToken);

            var driverName = tripDriver?.Driver?.FullName ?? "Tài xế chuyến";

            penaltyBillCode = $"PB-TRIP-DEFICIT-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var penalty = new PenaltyBill
            {
                PenaltyBillId = Guid.NewGuid(),
                BillCode = penaltyBillCode,
                OrderId = trip.TransportOrders.FirstOrDefault()?.OrderId,
                TotalAmount = deficitAmount,
                HandlingFee = 0,
                StorageFee = 0,
                Reason = $"Đối soát chuyến {trip.TripId}: Nộp hụt {deficitAmount:N0} VND so với tổng phải thu (Dynamic COD: {totalExpectedCod:N0} + Detention Fee: {totalDetentionFee:N0}). Hạch toán trừ công nợ Tài xế {driverName}.",
                IsPaid = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.PenaltyBills.Add(penalty);
        }

        // 4. Tự động hóa MISA/SAP ERP: Đồng bộ giảm tồn kho & phát hành VAT Electronic Invoice
        var erpSyncResults = new List<object>();
        var vatInvoices = new List<object>();

        foreach (var order in trip.TransportOrders.Where(o => o.Status == "DELIVERED" || o.Status == "PARTIALLY_DELIVERED" || o.Status == "PARTIALLY_DELIVERED_OSD"))
        {
            // Giảm tồn MISA/SAP
            var syncRes = await _erpService.DeductInventoryAsync(order.OrderId, order.ItemName ?? "CARGO-ITEM", order.Quantity, cancellationToken);
            erpSyncResults.Add(syncRes);

            // Xuất VAT Electronic Invoice
            decimal orderAmount = epods.Where(e => e.OrderId == order.OrderId).Sum(e => e.CodAmountPaid ?? (e.CodAmount ?? 2000000m));
            if (orderAmount == 0) orderAmount = 2000000m; // Giá trị định mức mặc định nếu đơn không thu COD
            var invoiceRes = await _erpService.GenerateVatInvoiceAsync(order.OrderId, orderAmount, order.Customer?.CompanyName ?? "Khách Hàng", cancellationToken);
            vatInvoices.Add(invoiceRes);
        }

        // 5. Cập nhật trạng thái chuyến và ePODs
        foreach (var epod in epods)
        {
            epod.PaymentStatus = "FINISH_RECONCILED";
            epod.Note = $"{epod.Note} [Accountant Reconciled by {request.AccountantUserId} at {DateTime.UtcNow:dd/MM/yyyy HH:mm}: {request.Note}]".Trim();
        }

        await _context.SaveChangesAsync(cancellationToken);

        var responseData = new
        {
            TripId = request.TripId,
            TripCode = $"TRIP-{trip.TripId.ToString().Substring(0, 8).ToUpper()}",
            ExpectedCod = totalExpectedCod,
            ExpectedDetentionFee = totalDetentionFee,
            TotalExpectedRevenue = totalExpectedRevenue,
            ActualCashReceived = request.ActualCashReceived,
            ActualQrReceived = request.ActualQrReceived,
            TotalActualReceived = totalActualReceived,
            Discrepancy = totalDiscrepancy,
            Status = reconciliationStatus,
            GeneratedPenaltyBillCode = penaltyBillCode,
            ErpInventorySync = erpSyncResults,
            ElectronicVatInvoices = vatInvoices,
            Message = "Đối soát kế toán chuyến (Trip Reconciliation) & tích hợp tự động hóa MISA/SAP ERP thành công."
        };

        return ApiResponse<object>.SuccessResponse(responseData, responseData.Message);
    }
}
