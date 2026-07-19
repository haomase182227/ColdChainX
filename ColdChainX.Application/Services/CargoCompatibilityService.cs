using System.Globalization;
using System.Text.RegularExpressions;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.Services;

public sealed class CargoCompatibilityService : ICargoCompatibilityService
{
    private static readonly Regex NumberRegex = new(@"-?\d+(?:[\.,]\d+)?", RegexOptions.Compiled);

    public CargoCompatibilityValidationResult ValidateSelectedSet(
        IReadOnlyCollection<Lpn> lpns,
        Guid scheduleId,
        IReadOnlyCollection<Guid>? requestedLpnIds = null)
    {
        var result = new CargoCompatibilityValidationResult();
        var requestedIds = (requestedLpnIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var loadedIds = lpns.Select(l => l.LpnId).ToHashSet();
        foreach (var missingId in requestedIds.Where(id => !loadedIds.Contains(id)))
        {
            result.Conflicts.Add(new CargoCompatibilityConflictDto
            {
                ReasonCode = CargoCompatibilityReasonCodes.InvalidLpnState,
                Message = $"LPN {missingId} does not exist or cannot be loaded.",
                LpnId = missingId
            });
        }

        foreach (var lpn in lpns)
        {
            result.Conflicts.AddRange(ValidateLpnForSchedule(lpn, scheduleId, null));
        }

        var warehouseIds = lpns.Select(l => l.WarehouseId).Distinct().ToList();
        if (warehouseIds.Count > 1)
        {
            var firstWarehouseId = warehouseIds.FirstOrDefault(id => id.HasValue);
            foreach (var lpn in lpns.Where(l => l.WarehouseId != firstWarehouseId))
            {
                result.Conflicts.Add(new CargoCompatibilityConflictDto
                {
                    ReasonCode = CargoCompatibilityReasonCodes.DifferentWarehouse,
                    Message = $"LPN {lpn.LpnCode} is not in the same warehouse as the selected set.",
                    LpnId = lpn.LpnId,
                    LpnCode = lpn.LpnCode
                });
            }
        }

        var selected = lpns.ToList();
        for (var i = 0; i < selected.Count; i++)
        {
            for (var j = i + 1; j < selected.Count; j++)
            {
                result.Conflicts.AddRange(ValidatePair(selected[i], selected[j]));
            }
        }

        return result;
    }

    public List<CargoCompatibilityConflictDto> ValidateCandidate(
        Lpn candidate,
        IReadOnlyCollection<Lpn> selectedLpns,
        Guid scheduleId,
        Guid? warehouseId)
    {
        var conflicts = ValidateLpnForSchedule(candidate, scheduleId, warehouseId);

        foreach (var selected in selectedLpns)
        {
            conflicts.AddRange(ValidatePair(selected, candidate));
        }

        return conflicts;
    }

    public List<CargoCompatibilityConflictDto> ValidateVehicleTemperature(
        Vehicle vehicle,
        IReadOnlyCollection<Lpn> lpns)
    {
        var conflicts = new List<CargoCompatibilityConflictDto>();

        foreach (var lpn in lpns)
        {
            var requiredTemp = ResolveRequiredTemperature(lpn);
            if (!requiredTemp.HasValue)
            {
                conflicts.Add(new CargoCompatibilityConflictDto
                {
                    ReasonCode = CargoCompatibilityReasonCodes.MissingTemperature,
                    Message = $"LPN {lpn.LpnCode} is missing required temperature.",
                    LpnId = lpn.LpnId,
                    LpnCode = lpn.LpnCode
                });
                continue;
            }

            if (requiredTemp.Value < vehicle.MinTemp || requiredTemp.Value > vehicle.MaxTemp)
            {
                conflicts.Add(new CargoCompatibilityConflictDto
                {
                    ReasonCode = CargoCompatibilityReasonCodes.TemperatureMismatch,
                    Message = $"LPN {lpn.LpnCode} requires {requiredTemp.Value:0.##}C, outside vehicle range {vehicle.MinTemp:0.##}C to {vehicle.MaxTemp:0.##}C.",
                    LpnId = lpn.LpnId,
                    LpnCode = lpn.LpnCode
                });
            }
        }

        return conflicts;
    }

    public decimal? ResolveRequiredTemperature(Lpn lpn)
    {
        if (lpn.RequiredTemperature.HasValue)
        {
            return lpn.RequiredTemperature.Value;
        }

        return ParseTemperature(lpn.Order?.TempCondition);
    }

    private List<CargoCompatibilityConflictDto> ValidateLpnForSchedule(
        Lpn lpn,
        Guid scheduleId,
        Guid? warehouseId)
    {
        var conflicts = new List<CargoCompatibilityConflictDto>();

        if (lpn.Order == null || lpn.Order.ScheduleId != scheduleId)
        {
            conflicts.Add(new CargoCompatibilityConflictDto
            {
                ReasonCode = CargoCompatibilityReasonCodes.DifferentSchedule,
                Message = $"LPN {lpn.LpnCode} does not belong to the selected schedule.",
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode
            });
        }

        if (!lpn.WarehouseId.HasValue || (warehouseId.HasValue && lpn.WarehouseId != warehouseId))
        {
            conflicts.Add(new CargoCompatibilityConflictDto
            {
                ReasonCode = CargoCompatibilityReasonCodes.DifferentWarehouse,
                Message = $"LPN {lpn.LpnCode} does not belong to the selected warehouse.",
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode
            });
        }

        if (lpn.State != LpnState.IN_STOCK || lpn.TripId.HasValue)
        {
            conflicts.Add(new CargoCompatibilityConflictDto
            {
                ReasonCode = CargoCompatibilityReasonCodes.InvalidLpnState,
                Message = $"LPN {lpn.LpnCode} must be IN_STOCK and not assigned to a trip.",
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode
            });
        }

        if (!ResolveRequiredTemperature(lpn).HasValue)
        {
            conflicts.Add(new CargoCompatibilityConflictDto
            {
                ReasonCode = CargoCompatibilityReasonCodes.MissingTemperature,
                Message = $"LPN {lpn.LpnCode} is missing required temperature.",
                LpnId = lpn.LpnId,
                LpnCode = lpn.LpnCode
            });
        }

        return conflicts;
    }

    private List<CargoCompatibilityConflictDto> ValidatePair(Lpn left, Lpn right)
    {
        var conflicts = new List<CargoCompatibilityConflictDto>();
        var leftCategory = NormalizeCategory(left.Order?.Category);
        var rightCategory = NormalizeCategory(right.Order?.Category);

        if (!string.Equals(leftCategory, rightCategory, StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add(BuildPairConflict(
                CargoCompatibilityReasonCodes.CategoryMismatch,
                $"LPN {left.LpnCode} category {leftCategory} is not compatible with LPN {right.LpnCode} category {rightCategory}.",
                left,
                right));
        }

        var leftTemp = ResolveRequiredTemperature(left);
        var rightTemp = ResolveRequiredTemperature(right);
        if (leftTemp.HasValue && rightTemp.HasValue)
        {
            var tolerance = Math.Min(GetTemperatureTolerance(leftCategory), GetTemperatureTolerance(rightCategory));
            if (Math.Abs(leftTemp.Value - rightTemp.Value) > tolerance)
            {
                conflicts.Add(BuildPairConflict(
                    CargoCompatibilityReasonCodes.TemperatureMismatch,
                    $"LPN {left.LpnCode} temperature {leftTemp.Value:0.##}C differs from LPN {right.LpnCode} temperature {rightTemp.Value:0.##}C beyond tolerance {tolerance:0.##}C.",
                    left,
                    right));
            }
        }

        if ((left.Order?.HasStrongOdor == true && rightCategory == "ICE_CREAM_BEVERAGES")
            || (right.Order?.HasStrongOdor == true && leftCategory == "ICE_CREAM_BEVERAGES"))
        {
            conflicts.Add(BuildPairConflict(
                CargoCompatibilityReasonCodes.OdorConflict,
                $"LPN {left.LpnCode} and LPN {right.LpnCode} have an odor compatibility conflict.",
                left,
                right));
        }

        return conflicts;
    }

    private static CargoCompatibilityConflictDto BuildPairConflict(
        string reasonCode,
        string message,
        Lpn left,
        Lpn right)
        => new()
        {
            ReasonCode = reasonCode,
            Message = message,
            LpnId = left.LpnId,
            LpnCode = left.LpnCode,
            OtherLpnId = right.LpnId,
            OtherLpnCode = right.LpnCode
        };

    private static string NormalizeCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? "UNKNOWN" : category.Trim().ToUpperInvariant();

    private static decimal GetTemperatureTolerance(string category)
        => category switch
        {
            "PHARMACEUTICALS" => 0m,
            "ICE_CREAM_BEVERAGES" => 1m,
            _ => 2m
        };

    private static decimal? ParseTemperature(string? tempCondition)
    {
        if (string.IsNullOrWhiteSpace(tempCondition))
        {
            return null;
        }

        var value = tempCondition.Trim().ToUpperInvariant();
        if (value.Contains("FROZEN") || value.Contains("-18")) return -18m;
        if (value.Contains("CHILLED") || value.Contains("2-8")) return 2m;
        if (value.Contains("0-4")) return 0m;
        if (value.Contains("AMBIENT")) return 15m;

        var match = NumberRegex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return decimal.TryParse(
            match.Value.Replace(',', '.'),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }
}
