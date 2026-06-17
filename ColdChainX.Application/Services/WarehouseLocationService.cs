using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.WarehouseLocation;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class WarehouseLocationService : IWarehouseLocationService
    {
        private readonly IWarehouseLocationRepository _locationRepository;
        private readonly IWarehouseZoneRepository _zoneRepository;

        public WarehouseLocationService(
            IWarehouseLocationRepository locationRepository,
            IWarehouseZoneRepository zoneRepository)
        {
            _locationRepository = locationRepository;
            _zoneRepository = zoneRepository;
        }

        public async Task<ApiResponse<WarehouseLocationResponse>> CreateAsync(
            Guid zoneId, CreateWarehouseLocationRequest request, Guid currentUserId)
        {
            var zone = await _zoneRepository.GetByIdAsync(zoneId);
            if (zone == null)
            {
                return ApiResponse<WarehouseLocationResponse>.Failure("Warehouse zone not found.");
            }

            var normalizedCode = request.LocationCode.Trim().ToUpperInvariant();
            var exists = await _locationRepository.ExistsByCodeAsync(zoneId, normalizedCode);
            if (exists)
            {
                return ApiResponse<WarehouseLocationResponse>.Failure(
                    $"Location code '{normalizedCode}' already exists in this zone.");
            }

            var location = new WarehouseLocation
            {
                LocationId = Guid.NewGuid(),
                ZoneId = zoneId,
                LocationCode = normalizedCode,
                RackCode = request.RackCode?.Trim().ToUpperInvariant(),
                BayCode = request.BayCode?.Trim().ToUpperInvariant(),
                LevelCode = request.LevelCode?.Trim().ToUpperInvariant(),
                MaxCapacityPallets = request.MaxCapacityPallets,
                CurrentPallets = 0,
                Status = "ACTIVE",
                Description = request.Description?.Trim(),
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                CreatedBy = currentUserId
            };

            await _locationRepository.AddAsync(location);
            await _locationRepository.SaveChangesAsync();

            // Refresh to include zone + warehouse navigation
            var saved = await _locationRepository.GetByIdAsync(location.LocationId);
            return ApiResponse<WarehouseLocationResponse>.SuccessResponse(
                MapToResponse(saved!), "Warehouse location created successfully.");
        }

        public async Task<ApiResponse<WarehouseLocationResponse>> UpdateAsync(
            Guid locationId, UpdateWarehouseLocationRequest request, Guid currentUserId)
        {
            var location = await _locationRepository.GetByIdAsync(locationId);
            if (location == null)
            {
                return ApiResponse<WarehouseLocationResponse>.Failure("Warehouse location not found.");
            }

            var normalizedCode = request.LocationCode.Trim().ToUpperInvariant();
            var exists = await _locationRepository.ExistsByCodeAsync(location.ZoneId, normalizedCode, locationId);
            if (exists)
            {
                return ApiResponse<WarehouseLocationResponse>.Failure(
                    $"Location code '{normalizedCode}' already exists in this zone.");
            }

            location.LocationCode = normalizedCode;
            location.RackCode = request.RackCode?.Trim().ToUpperInvariant();
            location.BayCode = request.BayCode?.Trim().ToUpperInvariant();
            location.LevelCode = request.LevelCode?.Trim().ToUpperInvariant();
            location.MaxCapacityPallets = request.MaxCapacityPallets;
            location.Status = request.Status.Trim().ToUpperInvariant();
            location.Description = request.Description?.Trim();
            location.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            location.UpdatedBy = currentUserId;

            await _locationRepository.UpdateAsync(location);
            await _locationRepository.SaveChangesAsync();

            return ApiResponse<WarehouseLocationResponse>.SuccessResponse(
                MapToResponse(location), "Warehouse location updated successfully.");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid locationId, Guid currentUserId)
        {
            var location = await _locationRepository.GetByIdAsync(locationId);
            if (location == null)
            {
                return ApiResponse<bool>.Failure("Warehouse location not found.");
            }

            if (location.CurrentPallets > 0)
            {
                return ApiResponse<bool>.Failure(
                    "Cannot delete a warehouse location that currently holds pallets.");
            }

            location.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            location.DeletedBy = currentUserId;
            location.Status = "INACTIVE";

            await _locationRepository.UpdateAsync(location);
            await _locationRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Warehouse location soft-deleted successfully.");
        }

        public async Task<ApiResponse<WarehouseLocationResponse>> GetByIdAsync(Guid locationId)
        {
            var location = await _locationRepository.GetByIdAsync(locationId);
            if (location == null)
            {
                return ApiResponse<WarehouseLocationResponse>.Failure("Warehouse location not found.");
            }

            return ApiResponse<WarehouseLocationResponse>.SuccessResponse(
                MapToResponse(location), "Warehouse location retrieved successfully.");
        }

        public async Task<ApiResponse<PagedResult<WarehouseLocationResponse>>> GetListAsync(
            Guid zoneId, int pageNumber, int pageSize, string? search = null)
        {
            var (data, totalCount) = await _locationRepository.GetListAsync(zoneId, pageNumber, pageSize, search);
            var mappedList = data.Select(MapToResponse).ToList();
            var result = PagedResult<WarehouseLocationResponse>.Create(mappedList, totalCount, pageNumber, pageSize);
            return ApiResponse<PagedResult<WarehouseLocationResponse>>.SuccessResponse(
                result, "Warehouse locations retrieved successfully.");
        }

        private static WarehouseLocationResponse MapToResponse(WarehouseLocation location)
        {
            return new WarehouseLocationResponse
            {
                LocationId = location.LocationId,
                ZoneId = location.ZoneId,
                ZoneName = location.Zone?.ZoneName ?? string.Empty,
                WarehouseName = location.Zone?.Warehouse?.WarehouseName ?? string.Empty,
                LocationCode = location.LocationCode,
                RackCode = location.RackCode,
                BayCode = location.BayCode,
                LevelCode = location.LevelCode,
                MaxCapacityPallets = location.MaxCapacityPallets,
                CurrentPallets = location.CurrentPallets,
                Status = location.Status,
                Description = location.Description,
                CreatedAt = location.CreatedAt,
                CreatedBy = location.CreatedBy,
                UpdatedAt = location.UpdatedAt,
                UpdatedBy = location.UpdatedBy
            };
        }
    }
}
