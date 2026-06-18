using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces;

public interface IDispatchService
{
    /// <summary>
    /// Lập kế hoạch lấy hàng từ kho: tính lộ trình (Goong), xếp hàng LIFO,
    /// tạo MasterTrip + TripStops, cập nhật trạng thái đơn hàng, gửi thông báo.
    /// </summary>
    Task<PlanLoadResult> PlanLoadFromWarehouseAsync(PlanLoadRequest request);

    // ── Legacy methods kept for backward compatibility ──────────────────────
    Task<string> SuggestLoadPlanAsync(List<Guid> orderIds, Guid vehicleId);
    Task CalculateRouteAndLIFOAsync(Guid tripId);
    Task SealTruckAsync(Guid tripId, string sealCode, Guid warehouseKeeperId);
    Task IssueDispatchDocumentsAsync(Guid tripId, Guid? issuerId = null);
    Task<List<LoadInstruction>> GetLoadPlanAsync(Guid tripId);
    Task<string> GenerateLoadPlanPdfAsync(Guid tripId);
    Task<List<TransportDocument>> GetIssuedDocumentsAsync(Guid tripId);
}
