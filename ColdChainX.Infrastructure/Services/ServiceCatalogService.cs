using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs.ServiceCatalogs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services
{
    public class ServiceCatalogService : IServiceCatalogService
    {
        private readonly IApplicationDbContext _db;

        public ServiceCatalogService(IApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<List<ServiceCatalogDto>>> GetAllAsync()
        {
            var services = await _db.ServiceCatalogs
                .OrderBy(s => s.ServiceName)
                .ToListAsync();

            var dtos = services.Select(MapToDto).ToList();
            return ApiResponse<List<ServiceCatalogDto>>.SuccessResponse(dtos);
        }

        public async Task<ApiResponse<List<ServiceCatalogDto>>> GetActiveAsync()
        {
            var services = await _db.ServiceCatalogs
                .Where(s => s.IsActive)
                .OrderBy(s => s.ServiceName)
                .ToListAsync();

            var dtos = services.Select(MapToDto).ToList();
            return ApiResponse<List<ServiceCatalogDto>>.SuccessResponse(dtos);
        }

        public async Task<ApiResponse<ServiceCatalogDto>> GetByIdAsync(Guid id)
        {
            var service = await _db.ServiceCatalogs.FindAsync(id);
            if (service == null)
            {
                return ApiResponse<ServiceCatalogDto>.Failure("Service not found");
            }
            return ApiResponse<ServiceCatalogDto>.SuccessResponse(MapToDto(service));
        }

        public async Task<ApiResponse<ServiceCatalogDto>> CreateAsync(CreateServiceCatalogRequest request)
        {
            var exists = await _db.ServiceCatalogs.AnyAsync(s => s.ServiceCode.ToLower() == request.ServiceCode.ToLower());
            if (exists)
            {
                return ApiResponse<ServiceCatalogDto>.Failure("Service code already exists");
            }

            var service = new ServiceCatalog
            {
                ServiceCatalogId = Guid.NewGuid(),
                ServiceCode = request.ServiceCode.ToUpper(),
                ServiceName = request.ServiceName,
                Description = request.Description,
                DefaultPrice = request.DefaultPrice,
                IsMandatory = request.IsMandatory,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _db.ServiceCatalogs.Add(service);
            await _db.SaveChangesAsync();

            return ApiResponse<ServiceCatalogDto>.SuccessResponse(MapToDto(service), "Service created successfully");
        }

        public async Task<ApiResponse<ServiceCatalogDto>> UpdateAsync(Guid id, UpdateServiceCatalogRequest request)
        {
            var service = await _db.ServiceCatalogs.FindAsync(id);
            if (service == null)
            {
                return ApiResponse<ServiceCatalogDto>.Failure("Service not found");
            }

            service.ServiceName = request.ServiceName;
            service.Description = request.Description;
            service.DefaultPrice = request.DefaultPrice;
            service.IsMandatory = request.IsMandatory;
            service.IsActive = request.IsActive;
            service.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return ApiResponse<ServiceCatalogDto>.SuccessResponse(MapToDto(service), "Service updated successfully");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            var service = await _db.ServiceCatalogs.FindAsync(id);
            if (service == null)
            {
                return ApiResponse<bool>.Failure("Service not found");
            }

            _db.ServiceCatalogs.Remove(service);
            await _db.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Service deleted successfully");
        }

        private static ServiceCatalogDto MapToDto(ServiceCatalog entity)
        {
            return new ServiceCatalogDto
            {
                ServiceCatalogId = entity.ServiceCatalogId,
                ServiceCode = entity.ServiceCode,
                ServiceName = entity.ServiceName,
                Description = entity.Description,
                DefaultPrice = entity.DefaultPrice,
                IsMandatory = entity.IsMandatory,
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }
    }
}
