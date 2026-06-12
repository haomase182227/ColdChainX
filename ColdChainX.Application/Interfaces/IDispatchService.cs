using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ColdChainX.Application.Interfaces;

public interface IDispatchService
{
    Task<string> SuggestLoadPlanAsync(List<Guid> orderIds, Guid vehicleId);
    Task CalculateRouteAndLIFOAsync(Guid tripId);
    Task SealTruckAsync(Guid tripId, string sealCode, Guid warehouseKeeperId);
    Task IssueDispatchDocumentsAsync(Guid tripId);
}
