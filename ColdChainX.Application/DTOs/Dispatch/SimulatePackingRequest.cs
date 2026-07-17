using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Dispatch
{
    public class SimulatePackingRequest
    {
        public Guid VehicleId { get; set; }
        public List<Guid> LpnIds { get; set; } = new();
    }

    public class PreviewPlacedItem
    {
        public Guid LpnId { get; set; }
        public string LpnCode { get; set; } = null!;
        public decimal X { get; set; }
        public decimal Y { get; set; }
        public decimal Z { get; set; }
        public decimal W { get; set; }
        public decimal H { get; set; }
        public decimal D { get; set; }
        public string Color { get; set; } = "#cccccc"; // Default color for 3D render
        public string? ItemName { get; set; }
        public int Quantity { get; set; }
        public string? Location { get; set; }
    }

    public class SimulatePackingResponse
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<PreviewPlacedItem>? PlacedItems { get; set; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<Guid>? UnplacedLpnIds { get; set; }

        public decimal Utilisation { get; set; }
        public string VehicleType { get; set; } = null!;
        public decimal ContainerLength { get; set; }
        public decimal ContainerWidth { get; set; }
        public decimal ContainerHeight { get; set; }
        public string ShareableLink { get; set; } = null!;
    }
}
