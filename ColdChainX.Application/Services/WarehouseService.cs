using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.Warehouse;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.WarehouseFlow;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class WarehouseService : IWarehouseService
    {
        private readonly IWarehouseRepository _repository;
        public WarehouseService(IWarehouseRepository repository)
        {
            _repository = repository;
        }

        public async Task<ApiResponse<WarehouseResponse>> CreateAsync(CreateWarehouseRequest request, Guid currentUserId)
        {
            var exists = await _repository.ExistsByCodeAsync(request.WarehouseCode);
            if (exists)
            {
                return ApiResponse<WarehouseResponse>.Failure($"Warehouse Code '{request.WarehouseCode}' is already in use.");
            }

            var warehouse = new Warehouse
            {
                WarehouseId = Guid.NewGuid(),
                WarehouseCode = request.WarehouseCode.Trim().ToUpperInvariant(),
                WarehouseName = request.WarehouseName.Trim(),
                WarehouseType = request.WarehouseType.Trim().ToUpperInvariant(),
                Address = request.Address?.Trim(),
                MaxPallets = request.MaxPallets,
                CurrentPallets = 0,
                DefaultMinTemp = request.DefaultMinTemp,
                DefaultMaxTemp = request.DefaultMaxTemp,
                Status = request.Status.Trim().ToUpperInvariant(),
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                CreatedBy = currentUserId
            };

            await _repository.AddAsync(warehouse);
            await _repository.SaveChangesAsync();

            var response = MapToResponse(warehouse);
            return ApiResponse<WarehouseResponse>.SuccessResponse(response, "Warehouse created successfully.");
        }

        public async Task<ApiResponse<WarehouseResponse>> UpdateAsync(Guid warehouseId, UpdateWarehouseRequest request, Guid currentUserId)
        {
            var warehouse = await _repository.GetByIdAsync(warehouseId);
            if (warehouse == null)
            {
                return ApiResponse<WarehouseResponse>.Failure("Warehouse not found.");
            }

            var exists = await _repository.ExistsByCodeAsync(request.WarehouseCode, warehouseId);
            if (exists)
            {
                return ApiResponse<WarehouseResponse>.Failure($"Warehouse Code '{request.WarehouseCode}' is already in use by another warehouse.");
            }

            warehouse.WarehouseCode = request.WarehouseCode.Trim().ToUpperInvariant();
            warehouse.WarehouseName = request.WarehouseName.Trim();
            warehouse.WarehouseType = request.WarehouseType.Trim().ToUpperInvariant();
            warehouse.Address = request.Address?.Trim();
            warehouse.MaxPallets = request.MaxPallets;
            warehouse.DefaultMinTemp = request.DefaultMinTemp;
            warehouse.DefaultMaxTemp = request.DefaultMaxTemp;
            warehouse.Status = request.Status.Trim().ToUpperInvariant();
            warehouse.UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
            warehouse.UpdatedBy = currentUserId;

            await _repository.UpdateAsync(warehouse);
            await _repository.SaveChangesAsync();

            var response = MapToResponse(warehouse);
            return ApiResponse<WarehouseResponse>.SuccessResponse(response, "Warehouse updated successfully.");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid warehouseId, Guid currentUserId)
        {
            var warehouse = await _repository.GetByIdAsync(warehouseId);
            if (warehouse == null)
            {
                return ApiResponse<bool>.Failure("Warehouse not found.");
            }

            warehouse.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            warehouse.DeletedBy = currentUserId;
            warehouse.Status = "INACTIVE"; // Aligning status on soft delete

            await _repository.UpdateAsync(warehouse);
            await _repository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Warehouse soft-deleted successfully.");
        }

        public async Task<ApiResponse<WarehouseResponse>> GetByIdAsync(Guid warehouseId)
        {
            var warehouse = await _repository.GetByIdAsync(warehouseId);
            if (warehouse == null)
            {
                return ApiResponse<WarehouseResponse>.Failure("Warehouse not found.");
            }

            var response = MapToResponse(warehouse);
            return ApiResponse<WarehouseResponse>.SuccessResponse(response, "Warehouse retrieved successfully.");
        }

        public async Task<ApiResponse<PagedResult<WarehouseResponse>>> GetListAsync(int pageNumber, int pageSize, string? search = null)
        {
            var (data, totalCount) = await _repository.GetListAsync(pageNumber, pageSize, search);
            
            var mappedList = data.Select(MapToResponse).ToList();
            var result = PagedResult<WarehouseResponse>.Create(mappedList, totalCount, pageNumber, pageSize);

            return ApiResponse<PagedResult<WarehouseResponse>>.SuccessResponse(result, "Warehouses retrieved successfully.");
        }

        private static WarehouseResponse MapToResponse(Warehouse warehouse)
        {
            return new WarehouseResponse
            {
                WarehouseId = warehouse.WarehouseId,
                WarehouseCode = warehouse.WarehouseCode,
                WarehouseName = warehouse.WarehouseName,
                WarehouseType = warehouse.WarehouseType,
                Address = warehouse.Address,
                MaxPallets = warehouse.MaxPallets,
                CurrentPallets = warehouse.CurrentPallets,
                DefaultMinTemp = warehouse.DefaultMinTemp,
                DefaultMaxTemp = warehouse.DefaultMaxTemp,
                Status = warehouse.Status ?? "ACTIVE",
                CreatedAt = warehouse.CreatedAt,
                CreatedBy = warehouse.CreatedBy,
                UpdatedAt = warehouse.UpdatedAt,
                UpdatedBy = warehouse.UpdatedBy
            };
        }

        public async Task<ApiResponse<PagedResult<LpnResponse>>> GetLpnsInWarehouseAsync(Guid warehouseId, int page, int pageSize)
        {
            var (lpns, totalCount) = await _repository.GetLpnsInWarehouseAsync(warehouseId, page, pageSize);

            var mappedList = lpns.Select(l => new LpnResponse
            {
                LpnId = l.LpnId,
                LpnCode = l.LpnCode,
                OrderId = l.OrderId,
                WarehouseId = l.WarehouseId,
                StorageLocation = l.StorageLocation ?? string.Empty,
                Quantity = l.Quantity,
                ActualWeightKg = l.ActualWeightKg,
                ActualCbm = l.ActualCbm,
                State = l.State,
                InboundTime = l.CreatedAt
            }).ToList();

            var result = PagedResult<LpnResponse>.Create(mappedList, totalCount, page, pageSize);
            return ApiResponse<PagedResult<LpnResponse>>.SuccessResponse(result, "Lấy danh sách LPN trong kho thành công.");
        }
    }
}
