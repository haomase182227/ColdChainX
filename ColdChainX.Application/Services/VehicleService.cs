using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class VehicleService : IVehicleService
    {
        private const string ActiveStatus = "ACTIVE";
        private const string InactiveStatus = "INACTIVE";

        private readonly IVehicleRepository _vehicleRepository;

        public VehicleService(IVehicleRepository vehicleRepository)
        {
            _vehicleRepository = vehicleRepository;
        }

        public async Task<ApiResponse<List<VehicleDto>>> GetAllAsync()
        {
            var vehicles = await _vehicleRepository.GetAllAsync();
            var data = vehicles.Select(Map).ToList();
            return ApiResponse<List<VehicleDto>>.SuccessResponse(data);
        }

        public async Task<ApiResponse<VehicleDto>> GetByIdAsync(Guid id)
        {
            var vehicle = await _vehicleRepository.GetByIdAsync(id);
            if (vehicle == null)
                return ApiResponse<VehicleDto>.Failure("Vehicle not found");

            return ApiResponse<VehicleDto>.SuccessResponse(Map(vehicle));
        }

        public async Task<ApiResponse<VehicleDto>> CreateAsync(VehicleCreateRequest request)
        {
            var truckPlate = request.TruckPlate.Trim();
            var normalizedTruckPlate = truckPlate.ToLowerInvariant();

            if (await _vehicleRepository.GetByTruckPlateAsync(normalizedTruckPlate) != null)
                return ApiResponse<VehicleDto>.Failure("Truck plate already exists");

            if (!string.IsNullOrWhiteSpace(request.ChassisNumber))
            {
                var chassisNumber = request.ChassisNumber.Trim();
                if (await _vehicleRepository.GetByChassisNumberAsync(chassisNumber) != null)
                    return ApiResponse<VehicleDto>.Failure("Chassis number already exists");
            }

            if (!string.IsNullOrWhiteSpace(request.EngineNumber))
            {
                var engineNumber = request.EngineNumber.Trim();
                if (await _vehicleRepository.GetByEngineNumberAsync(engineNumber) != null)
                    return ApiResponse<VehicleDto>.Failure("Engine number already exists");
            }

            var vehicle = new Vehicle
            {
                VehicleId = Guid.NewGuid(),
                TruckPlate = truckPlate,
                Brand = NormalizeOptional(request.Brand),
                ManufactureYear = request.ManufactureYear,
                ChassisNumber = NormalizeOptional(request.ChassisNumber),
                EngineNumber = NormalizeOptional(request.EngineNumber),
                StandardFuelLiters = request.StandardFuelLiters,
                VehicleType = request.VehicleType.ToString().ToUpperInvariant(),
                MaxWeight = request.MaxWeight,
                MaxCbm = request.MaxCbm,
                MinTemp = request.MinTemp,
                MaxTemp = request.MaxTemp,
                Status = request.Status.ToString().ToUpperInvariant(),
                CreatedAt = DbNow()
            };

            await _vehicleRepository.AddAsync(vehicle);
            await _vehicleRepository.SaveChangesAsync();

            return ApiResponse<VehicleDto>.SuccessResponse(Map(vehicle), "Vehicle created successfully");
        }

        public async Task<ApiResponse<VehicleDto>> UpdateAsync(Guid id, VehicleUpdateRequest request)
        {
            var vehicle = await _vehicleRepository.GetByIdAsync(id);
            if (vehicle == null)
                return ApiResponse<VehicleDto>.Failure("Vehicle not found");

            if (!string.IsNullOrWhiteSpace(request.TruckPlate))
            {
                var truckPlate = request.TruckPlate.Trim();
                var existing = await _vehicleRepository.GetByTruckPlateAsync(truckPlate);
                if (existing != null && existing.VehicleId != id)
                    return ApiResponse<VehicleDto>.Failure("Truck plate already exists");

                vehicle.TruckPlate = truckPlate;
            }

            if (request.ChassisNumber != null)
            {
                var chassisNumber = request.ChassisNumber.Trim();
                if (!string.IsNullOrWhiteSpace(chassisNumber))
                {
                    var existing = await _vehicleRepository.GetByChassisNumberAsync(chassisNumber);
                    if (existing != null && existing.VehicleId != id)
                        return ApiResponse<VehicleDto>.Failure("Chassis number already exists");

                    vehicle.ChassisNumber = chassisNumber;
                }
                else
                {
                    vehicle.ChassisNumber = null;
                }
            }

            if (request.EngineNumber != null)
            {
                var engineNumber = request.EngineNumber.Trim();
                if (!string.IsNullOrWhiteSpace(engineNumber))
                {
                    var existing = await _vehicleRepository.GetByEngineNumberAsync(engineNumber);
                    if (existing != null && existing.VehicleId != id)
                        return ApiResponse<VehicleDto>.Failure("Engine number already exists");

                    vehicle.EngineNumber = engineNumber;
                }
                else
                {
                    vehicle.EngineNumber = null;
                }
            }

            if (request.Brand != null)
                vehicle.Brand = NormalizeOptional(request.Brand);

            if (request.ManufactureYear.HasValue)
                vehicle.ManufactureYear = request.ManufactureYear;

            if (request.StandardFuelLiters.HasValue)
                vehicle.StandardFuelLiters = request.StandardFuelLiters;

            if (!string.IsNullOrWhiteSpace(request.VehicleType))
                vehicle.VehicleType = request.VehicleType.Trim();

            if (request.MaxWeight.HasValue)
                vehicle.MaxWeight = request.MaxWeight.Value;

            if (request.MaxCbm.HasValue)
                vehicle.MaxCbm = request.MaxCbm.Value;

            if (request.MinTemp.HasValue)
                vehicle.MinTemp = request.MinTemp.Value;

            if (request.MaxTemp.HasValue)
                vehicle.MaxTemp = request.MaxTemp.Value;

            if (request.Status != null)
                vehicle.Status = NormalizeStatus(request.Status, vehicle.Status ?? ActiveStatus);

            await _vehicleRepository.UpdateAsync(vehicle);
            await _vehicleRepository.SaveChangesAsync();

            return ApiResponse<VehicleDto>.SuccessResponse(Map(vehicle), "Vehicle updated successfully");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            var vehicle = await _vehicleRepository.GetByIdAsync(id);
            if (vehicle == null)
                return ApiResponse<bool>.Failure("Vehicle not found");

            vehicle.Status = InactiveStatus;
            await _vehicleRepository.DeleteAsync(vehicle);
            await _vehicleRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Vehicle deleted successfully");
        }

        private static VehicleDto Map(Vehicle vehicle)
        {
            return new VehicleDto
            {
                VehicleId = vehicle.VehicleId,
                TruckPlate = vehicle.TruckPlate,
                Brand = vehicle.Brand,
                ManufactureYear = vehicle.ManufactureYear,
                ChassisNumber = vehicle.ChassisNumber,
                EngineNumber = vehicle.EngineNumber,
                StandardFuelLiters = vehicle.StandardFuelLiters,
                VehicleType = vehicle.VehicleType,
                MaxWeight = vehicle.MaxWeight,
                MaxCbm = vehicle.MaxCbm,
                InnerLengthCm = vehicle.InnerLengthCm,
                InnerWidthCm = vehicle.InnerWidthCm,
                InnerHeightCm = vehicle.InnerHeightCm,
                MinTemp = vehicle.MinTemp,
                MaxTemp = vehicle.MaxTemp,
                Status = vehicle.Status,
                CreatedAt = vehicle.CreatedAt
            };
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string NormalizeStatus(string? value, string defaultValue)
            => string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToUpperInvariant();

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}
