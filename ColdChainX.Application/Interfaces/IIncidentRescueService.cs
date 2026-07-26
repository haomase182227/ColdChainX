using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Incident;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    /// <summary>
    /// Luồng 8 — Xử lý sự cố &amp; cập nhật lộ trình (Incident Routing Flow).
    /// Điều xe lạnh thay thế đến hiện trường, sang toàn bộ hàng, chuyển chuyến
    /// sang DELAYED, tính lại ETA và chủ động thông báo cho khách hàng phía trước.
    /// </summary>
    public interface IIncidentRescueService
    {
        /// <summary>
        /// [Bước 2 — Lookup] Danh sách xe ACTIVE đủ điều kiện thay thế cho chuyến
        /// gặp sự cố: giữ được nhiệt độ mục tiêu của chuyến và đủ tải trọng/thể tích
        /// cho toàn bộ LPN đang trên xe.
        /// </summary>
        Task<ApiResponse<List<RescueCandidateResponse>>> GetRescueCandidatesAsync(Guid incidentId);

        Task<ApiResponse<IncidentWorkflowResult>> ContinueTripAsync(
            Guid incidentId,
            ContinueTripAfterIncidentRequest request,
            Guid driverUserId);

        /// <summary>
        /// [Bước 2 + 3] Xuất lệnh điều xe thay thế đến hiện trường (Sang xe):
        /// xe hỏng → MAINTENANCE (kèm phiếu sửa chữa), xe mới gán vào chuyến → OnTrip,
        /// chuyến → DELAYED, tính lại ETA các trạm phía trước và gửi thông báo
        /// xin lỗi/cập nhật cho tất cả khách hàng đang chờ.
        /// </summary>
        Task<ApiResponse<IncidentRescueResult>> DispatchRescueAsync(Guid incidentId, DispatchRescueRequest request, Guid dispatcherId);

        Task<ApiResponse<IncidentWorkflowResult>> ConfirmTransloadAsync(
            Guid incidentId,
            ConfirmTransloadRequest request,
            Guid confirmedBy);
    }
}
