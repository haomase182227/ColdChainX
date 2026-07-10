using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.SystemConfigs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class SystemConfigService : ISystemConfigService
    {
        private readonly ApplicationDbContext _db;

        public SystemConfigService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<List<SystemConfigDto>>> GetAllConfigsAsync()
        {
            var items = await _db.SystemConfigs
                .AsNoTracking()
                .OrderBy(c => c.Key)
                .Select(c => new SystemConfigDto
                {
                    Id = c.Id,
                    Key = c.Key,
                    Value = c.Value,
                    Description = c.Description
                })
                .ToListAsync();

            return ApiResponse<List<SystemConfigDto>>.SuccessResponse(items, "System configurations retrieved successfully");
        }

        public async Task<ApiResponse<SystemConfigDto>> GetConfigByKeyAsync(string key)
        {
            var item = await _db.SystemConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Key == key);

            if (item == null)
                return ApiResponse<SystemConfigDto>.Failure("Configuration not found");

            return ApiResponse<SystemConfigDto>.SuccessResponse(new SystemConfigDto
            {
                Id = item.Id,
                Key = item.Key,
                Value = item.Value,
                Description = item.Description
            }, "System configuration retrieved successfully");
        }

        public async Task<ApiResponse<SystemConfigDto>> UpdateConfigAsync(string key, UpdateSystemConfigRequest request)
        {
            var item = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
            if (item == null)
                return ApiResponse<SystemConfigDto>.Failure("Configuration not found");

            item.Value = request.Value;
            if (request.Description != null)
            {
                item.Description = request.Description;
            }

            await _db.SaveChangesAsync();

            return ApiResponse<SystemConfigDto>.SuccessResponse(new SystemConfigDto
            {
                Id = item.Id,
                Key = item.Key,
                Value = item.Value,
                Description = item.Description
            }, "System configuration updated successfully");
        }
    }
}
