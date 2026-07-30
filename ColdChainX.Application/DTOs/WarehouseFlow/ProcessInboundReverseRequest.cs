using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.WarehouseFlow;

public class ProcessInboundReverseRequest
{
    public Guid WarehouseId { get; set; }
    public List<string> LpnCodes { get; set; } = new List<string>();
    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
}
