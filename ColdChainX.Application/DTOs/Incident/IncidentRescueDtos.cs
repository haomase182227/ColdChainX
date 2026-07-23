using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Incident
{
    /// <summary>
    /// [Luồng 8 — Bước 2] Yêu cầu điều xe lạnh thay thế đến hiện trường sự cố (Sang xe).
    /// </summary>
    public class DispatchRescueRequest
    {
        /// <summary>Xe lạnh thay thế được điều đến hiện trường (bắt buộc, phải ACTIVE).</summary>
        public Guid ReplacementVehicleId { get; set; }

        /// <summary>Thời gian dự kiến bốc chuyển toàn bộ hàng sang xe mới (phút). Mặc định 45 phút.</summary>
        public int? TransloadMinutes { get; set; }

        /// <summary>Ghi chú lệnh điều động (hiển thị cho đội bốc xếp tại hiện trường).</summary>
        public string? Note { get; set; }
    }

    /// <summary>Xe đủ điều kiện thay thế cho chuyến gặp sự cố (đúng dải nhiệt và đủ tải).</summary>
    public class RescueCandidateResponse
    {
        public Guid VehicleId { get; set; }
        public string TruckPlate { get; set; } = null!;
        public string VehicleType { get; set; } = null!;
        public decimal MaxWeight { get; set; }
        public decimal MaxCbm { get; set; }
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public int IotDeviceCount { get; set; }
        public int OnlineIotDeviceCount { get; set; }
        public bool HasOnlineIot { get; set; }
        public string Label { get; set; } = null!;
    }

    public class ContinueTripAfterIncidentRequest
    {
        public string HandlingNote { get; set; } = null!;
    }

    public class ConfirmTransloadRequest
    {
        public string ConfirmationNote { get; set; } = null!;
    }

    public class IncidentWorkflowResult
    {
        public Guid IncidentId { get; set; }
        public string IncidentStatus { get; set; } = null!;
        public Guid TripId { get; set; }
        public string TripStatus { get; set; } = null!;
        public Guid VehicleId { get; set; }
        public string VehiclePlate { get; set; } = null!;
        public DateTime ConfirmedAt { get; set; }
        public string Message { get; set; } = null!;
    }

    /// <summary>ETA mới của một điểm dừng phía trước sau khi hệ thống tính lại.</summary>
    public class StopEtaChange
    {
        public Guid StopId { get; set; }
        public int StopSequence { get; set; }
        public string? Address { get; set; }
        public DateTime OldEta { get; set; }
        public DateTime NewEta { get; set; }
        public int DelayMinutes { get; set; }
        public int NotifiedCustomers { get; set; }
    }

    /// <summary>
    /// [Luồng 8 — Bước 2 + 3] Kết quả xử lý sự cố: sang xe, chuyến DELAYED,
    /// ETA mới cho các trạm phía trước và số khách hàng đã được thông báo.
    /// </summary>
    public class IncidentRescueResult
    {
        public Guid IncidentId { get; set; }
        public string IncidentStatus { get; set; } = null!;
        public Guid TripId { get; set; }
        public string TripStatus { get; set; } = null!;

        public Guid BrokenVehicleId { get; set; }
        public string BrokenVehiclePlate { get; set; } = null!;
        public string BrokenVehicleStatus { get; set; } = null!;
        public Guid? MaintenanceTicketId { get; set; }

        public Guid RescueVehicleId { get; set; }
        public string RescueVehiclePlate { get; set; } = null!;
        public string RescueVehicleStatus { get; set; } = null!;

        /// <summary>Số LPN cần bốc chuyển sang xe mới tại hiện trường.</summary>
        public int TransloadLpnCount { get; set; }

        /// <summary>Cách tính ETA mới: GOONG | HAVERSINE_FALLBACK | SHIFT_FALLBACK.</summary>
        public string EtaMethod { get; set; } = null!;

        public List<StopEtaChange> UpdatedStops { get; set; } = new();

        public int NotifiedCustomerCount { get; set; }

        public string Message { get; set; } = null!;
    }
}
