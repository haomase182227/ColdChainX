using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.WarehouseZone;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class WarehouseZoneService : IWarehouseZoneService
    {
        private readonly IWarehouseZoneRepository _zoneRepository;
        private readonly IWarehouseRepository _warehouseRepository;

        public WarehouseZoneService(
            IWarehouseZoneRepository zoneRepository,
            IWarehouseRepository warehouseRepository)
        {
            _zoneRepository = zoneRepository;
            _warehouseRepository = warehouseRepository;
        }

        public async Task<ApiResponse<WarehouseZoneResponse>> CreateAsync(Guid warehouseId, CreateWarehouseZoneRequest request, Guid currentUserId)
        {
            var warehouse = await _warehouseRepository.GetByIdAsync(warehouseId);
            if (warehouse == null)
            {
                return ApiResponse<WarehouseZoneResponse>.Failure("Warehouse not found.");
            }

            var exists = await _zoneRepository.ExistsByCodeAsync(warehouseId, request.ZoneCode);
            if (exists)
            {
                return ApiResponse<WarehouseZoneResponse>.Failure($"Zone Code '{request.ZoneCode}' is already in use in this warehouse.");
            }

            // Verify temperature boundaries
            if (warehouse.WarehouseType != "COLD" && (request.TemperatureMin.HasValue || request.TemperatureMax.HasValue))
            {
                return ApiResponse<WarehouseZoneResponse>.Failure("Temperature boundaries must be null for zones in non-cold storage warehouses.");
            }

            if (warehouse.WarehouseType == "COLD")
            {
                if (request.TemperatureMin.HasValue && warehouse.DefaultMinTemp.HasValue && request.TemperatureMin < warehouse.DefaultMinTemp.Value)
                {
                    return ApiResponse<WarehouseZoneResponse>.Failure($"Zone TemperatureMin ({request.TemperatureMin}) cannot be lower than parent warehouse default min temperature ({warehouse.DefaultMinTemp}).");
                }
                if (request.TemperatureMax.HasValue && warehouse.DefaultMaxTemp.HasValue && request.TemperatureMax > warehouse.DefaultMaxTemp.Value)
                {
                    return ApiResponse<WarehouseZoneResponse>.Failure($"Zone TemperatureMax ({request.TemperatureMax}) cannot be greater than parent warehouse default max temperature ({warehouse.DefaultMaxTemp}).");
                }
            }

            var zone = new WarehouseZone
            {
                ZoneId = Guid.NewGuid(),
                WarehouseId = warehouseId,
                ZoneCode = request.ZoneCode.Trim().ToUpperInvariant(),
                ZoneName = request.ZoneName.Trim(),
                ZoneType = request.ZoneType.Trim().ToUpperInvariant(),
                StorageType = request.StorageType.Trim().ToUpperInvariant(),
                TemperatureMin = request.TemperatureMin,
                TemperatureMax = request.TemperatureMax,
                MaxCapacityPallets = request.MaxCapacityPallets,
                CurrentPallets = 0,
                Status = "ACTIVE",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                CreatedBy = currentUserId
            };

            await _zoneRepository.AddAsync(zone);
            await _zoneRepository.SaveChangesAsync();

            // Refresh to include warehouse navigation details
            var savedZone = await _zoneRepository.GetByIdAsync(zone.ZoneId);
            var response = MapToResponse(savedZone!);
            return ApiResponse<WarehouseZoneResponse>.SuccessResponse(response, "Warehouse zone created successfully.");
        }

        public async Task<ApiResponse<WarehouseZoneResponse>> UpdateAsync(Guid zoneId, UpdateWarehouseZoneRequest request, Guid currentUserId)
        {
            var zone = await _zoneRepository.GetByIdAsync(zoneId);
            if (zone == null)
            {
                return ApiResponse<WarehouseZoneResponse>.Failure("Warehouse zone not found.");
            }

            var warehouse = zone.Warehouse; // Eagerly loaded
            var exists = await _zoneRepository.ExistsByCodeAsync(zone.WarehouseId, request.ZoneCode, zoneId);
            if (exists)
            {
                return ApiResponse<WarehouseZoneResponse>.Failure($"Zone Code '{request.ZoneCode}' is already in use in this warehouse.");
            }

            // Verify temperature boundaries
            if (warehouse.WarehouseType != "COLD" && (request.TemperatureMin.HasValue || request.TemperatureMax.HasValue))
            {
                return ApiResponse<WarehouseZoneResponse>.Failure("Temperature boundaries must be null for zones in non-cold storage warehouses.");
            }

            if (warehouse.WarehouseType == "COLD")
            {
                if (request.TemperatureMin.HasValue && warehouse.DefaultMinTemp.HasValue && request.TemperatureMin < warehouse.DefaultMinTemp.Value)
                {
                    return ApiResponse<WarehouseZoneResponse>.Failure($"Zone TemperatureMin ({request.TemperatureMin}) cannot be lower than parent warehouse default min temperature ({warehouse.DefaultMinTemp}).");
                }
                if (request.TemperatureMax.HasValue && warehouse.DefaultMaxTemp.HasValue && request.TemperatureMax > warehouse.DefaultMaxTemp.Value)
                {
                    return ApiResponse<WarehouseZoneResponse>.Failure($"Zone TemperatureMax ({request.TemperatureMax}) cannot be greater than parent warehouse default max temperature ({warehouse.DefaultMaxTemp}).");
                }
            }

            zone.ZoneCode = request.ZoneCode.Trim().ToUpperInvariant();
            zone.ZoneName = request.ZoneName.Trim();
            zone.ZoneType = request.ZoneType.Trim().ToUpperInvariant();
            zone.StorageType = request.StorageType.Trim().ToUpperInvariant();
            zone.TemperatureMin = request.TemperatureMin;
            zone.TemperatureMax = request.TemperatureMax;
            zone.MaxCapacityPallets = request.MaxCapacityPallets;
            zone.Status = request.Status.Trim().ToUpperInvariant();
            zone.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            zone.UpdatedBy = currentUserId;

            await _zoneRepository.UpdateAsync(zone);
            await _zoneRepository.SaveChangesAsync();

            var response = MapToResponse(zone);
            return ApiResponse<WarehouseZoneResponse>.SuccessResponse(response, "Warehouse zone updated successfully.");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid zoneId, Guid currentUserId)
        {
            var zone = await _zoneRepository.GetByIdAsync(zoneId);
            if (zone == null)
            {
                return ApiResponse<bool>.Failure("Warehouse zone not found.");
            }

            if (zone.CurrentPallets > 0)
            {
                return ApiResponse<bool>.Failure("Cannot delete a warehouse zone that currently holds pallets.");
            }

            zone.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            zone.DeletedBy = currentUserId;
            zone.Status = "INACTIVE";

            await _zoneRepository.UpdateAsync(zone);
            await _zoneRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Warehouse zone soft-deleted successfully.");
        }

        public async Task<ApiResponse<WarehouseZoneResponse>> GetByIdAsync(Guid zoneId)
        {
            var zone = await _zoneRepository.GetByIdAsync(zoneId);
            if (zone == null)
            {
                return ApiResponse<WarehouseZoneResponse>.Failure("Warehouse zone not found.");
            }

            var response = MapToResponse(zone);
            return ApiResponse<WarehouseZoneResponse>.SuccessResponse(response, "Warehouse zone retrieved successfully.");
        }

        public async Task<ApiResponse<PagedResult<WarehouseZoneResponse>>> GetListAsync(Guid warehouseId, int pageNumber, int pageSize, string? search = null)
        {
            var (data, totalCount) = await _zoneRepository.GetListAsync(warehouseId, pageNumber, pageSize, search);

            var mappedList = data.Select(MapToResponse).ToList();
            var result = PagedResult<WarehouseZoneResponse>.Create(mappedList, totalCount, pageNumber, pageSize);

            return ApiResponse<PagedResult<WarehouseZoneResponse>>.SuccessResponse(result, "Warehouse zones retrieved successfully.");
        }

        private static WarehouseZoneResponse MapToResponse(WarehouseZone zone)
        {
            return new WarehouseZoneResponse
            {
                ZoneId = zone.ZoneId,
                WarehouseId = zone.WarehouseId,
                WarehouseName = zone.Warehouse?.WarehouseName ?? string.Empty,
                ZoneCode = zone.ZoneCode,
                ZoneName = zone.ZoneName,
                ZoneType = zone.ZoneType,
                StorageType = zone.StorageType,
                TemperatureMin = zone.TemperatureMin,
                TemperatureMax = zone.TemperatureMax,
                MaxCapacityPallets = zone.MaxCapacityPallets,
                CurrentPallets = zone.CurrentPallets,
                Status = zone.Status,
                CreatedAt = zone.CreatedAt,
                CreatedBy = zone.CreatedBy,
                UpdatedAt = zone.UpdatedAt,
                UpdatedBy = zone.UpdatedBy
            };
        }
    }
}
