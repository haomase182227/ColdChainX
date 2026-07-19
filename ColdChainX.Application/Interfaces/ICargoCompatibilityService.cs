using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Interfaces;

public interface ICargoCompatibilityService
{
    CargoCompatibilityValidationResult ValidateSelectedSet(
        IReadOnlyCollection<Lpn> lpns,
        Guid scheduleId,
        IReadOnlyCollection<Guid>? requestedLpnIds = null);

    List<CargoCompatibilityConflictDto> ValidateCandidate(
        Lpn candidate,
        IReadOnlyCollection<Lpn> selectedLpns,
        Guid scheduleId,
        Guid? warehouseId);

    List<CargoCompatibilityConflictDto> ValidateVehicleTemperature(
        Vehicle vehicle,
        IReadOnlyCollection<Lpn> lpns);

    decimal? ResolveRequiredTemperature(Lpn lpn);
}
