using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces;

public interface IDispatchService
{

    Task<ManualDispatchResult> ManualDispatchAsync(ManualDispatchRequest request);

    Task<ManualDispatchResult> CreateTripFromWarehouseAsync(WarehouseRedispatchRequest request);


    Task<StartPickingResult> StartPickingAsync(Guid tripId);

    Task<CancelTripResult> CancelTripAsync(Guid tripId);


    Task<VehicleIoTStatus> CheckVehicleIoTAsync(Guid vehicleId, Guid tripId);


    Task<SealAndDispatchResult> SealAndDispatchAsync(Guid tripId, string sealCode, Guid sealedBy);

}
