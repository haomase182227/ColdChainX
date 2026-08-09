using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Delivery;

public class TripOrderCustomersResponse
{
    public Guid TripId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverPhone { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public int TotalCustomers { get; set; }
    public TripVehicleSummaryItem? Vehicle { get; set; }
    public List<TripDriverSummaryItem> Drivers { get; set; } = new();
    public List<TripOrderCustomerItem> Orders { get; set; } = new();
}

public class TripVehicleSummaryItem
{
    public Guid VehicleId { get; set; }
    public string TruckPlate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public int? ManufactureYear { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    public decimal MaxWeight { get; set; } // Tải trọng (kg)
    public decimal MaxCbm { get; set; } // Thể tích khoang (m³)
    public decimal MinTemp { get; set; } // Dải nhiệt độ tối thiểu (°C)
    public decimal MaxTemp { get; set; } // Dải nhiệt độ tối đa (°C)
    public string CurrentLocation { get; set; } = string.Empty; // Vị trí hiện tại của xe
    public string Status { get; set; } = string.Empty;
    public List<TripIotDeviceSummaryItem> IotDevices { get; set; } = new();
}

public class TripIotDeviceSummaryItem
{
    public Guid DeviceId { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
    public int? BatteryLevel { get; set; } // Mức pin (%)
    public bool IsOnline { get; set; } // Trạng thái kết nối trực tiếp
    public DateTime? LastPingTime { get; set; } // Thời điểm gửi tín hiệu mới nhất
    public string Status { get; set; } = string.Empty;
}

public class TripDriverSummaryItem
{
    public Guid DriverId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string DriverRole { get; set; } = string.Empty; // PRIMARY (Lái chính) hoặc SECONDARY (Lái phụ)
    public decimal AssignedDurationHours { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentLocation { get; set; } = string.Empty;
}

public class TripOrderCustomerItem
{
    public Guid OrderId { get; set; }
    public string TrackingCode { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string DestAddress { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? PaymentTerm { get; set; }
    public string CustomerStatus { get; set; } = string.Empty;
}
