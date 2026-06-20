using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Warehouse
{
    /// <summary>
    /// Response model for warehouse capacity and zone utilization statistics.
    /// </summary>
    public class WarehouseUtilizationResponse
    {
        public Guid WarehouseId { get; set; }
        public string WarehouseCode { get; set; } = null!;
        public string WarehouseName { get; set; } = null!;
        public int MaxPallets { get; set; }
        public int CurrentPallets { get; set; }
        public double WarehouseOccupancyRate { get; set; }
        public List<ZoneOccupancyDetail> ZoneOccupancyRates { get; set; } = new();
    }

    public class ZoneOccupancyDetail
    {
        public Guid ZoneId { get; set; }
        public string ZoneCode { get; set; } = null!;
        public string ZoneName { get; set; } = null!;
        public int MaxCapacityPallets { get; set; }
        public int CurrentPallets { get; set; }
        public double ZoneOccupancyRate { get; set; }
    }
}
