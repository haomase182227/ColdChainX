using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Routes;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Infrastructure.Services
{
    public class WeightTierService : IWeightTierService
    {
        private readonly ApplicationDbContext _db;

        public WeightTierService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ApiResponse<PagedResult<WeightTierDto>>> GetAllAsync(int pageNumber, int pageSize)
        {
            var query = _db.WeightTiers.AsNoTracking().OrderBy(w => w.RouteId).ThenBy(w => w.MinWeightKg);
            var totalRecords = await query.CountAsync();
            var items = await query
                .Skip(NormalizeSkip(pageNumber, pageSize))
                .Take(NormalizePageSize(pageSize))
                .Select(w => new WeightTierDto
                {
                    Id = w.Id,
                    RouteId = w.RouteId,
                    MinWeightKg = w.MinWeightKg,
                    MaxWeightKg = w.MaxWeightKg,
                    PricePerKg = w.PricePerKg
                })
                .ToListAsync();

            return ApiResponse<PagedResult<WeightTierDto>>.SuccessResponse(
                PagedResult<WeightTierDto>.Create(items, totalRecords, pageNumber, NormalizePageSize(pageSize)),
                "Weight tiers retrieved successfully");
        }

        public async Task<ApiResponse<List<WeightTierDto>>> GetByRouteIdAsync(Guid routeId)
        {
            var exists = await _db.RouteMasters.AnyAsync(r => r.RouteId == routeId);
            if (!exists)
                return ApiResponse<List<WeightTierDto>>.Failure("Route not found");

            var items = await _db.WeightTiers
                .AsNoTracking()
                .Where(w => w.RouteId == routeId)
                .OrderBy(w => w.MinWeightKg)
                .Select(w => new WeightTierDto
                {
                    Id = w.Id,
                    RouteId = w.RouteId,
                    MinWeightKg = w.MinWeightKg,
                    MaxWeightKg = w.MaxWeightKg,
                    PricePerKg = w.PricePerKg
                })
                .ToListAsync();

            return ApiResponse<List<WeightTierDto>>.SuccessResponse(items, "Weight tiers retrieved successfully");
        }

        public async Task<ApiResponse<WeightTierDto>> GetByIdAsync(Guid id)
        {
            var item = await _db.WeightTiers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
            if (item == null)
                return ApiResponse<WeightTierDto>.Failure("Weight tier not found");

            return ApiResponse<WeightTierDto>.SuccessResponse(new WeightTierDto
            {
                Id = item.Id,
                RouteId = item.RouteId,
                MinWeightKg = item.MinWeightKg,
                MaxWeightKg = item.MaxWeightKg,
                PricePerKg = item.PricePerKg
            }, "Weight tier retrieved successfully");
        }

        public async Task<ApiResponse<WeightTierDto>> CreateAsync(CreateUpdateWeightTierRequest request)
        {
            if (request.MaxWeightKg.HasValue && request.MaxWeightKg.Value <= request.MinWeightKg)
                return ApiResponse<WeightTierDto>.Failure("MaxWeightKg must be greater than MinWeightKg");

            var routeExists = await _db.RouteMasters.AnyAsync(r => r.RouteId == request.RouteId);
            if (!routeExists)
                return ApiResponse<WeightTierDto>.Failure("Route not found");

            var hasOverlap = await CheckOverlapAsync(request.RouteId, request.MinWeightKg, request.MaxWeightKg, null);
            if (hasOverlap)
                return ApiResponse<WeightTierDto>.Failure("The specified weight range overlaps with an existing tier for this route.");

            var weightTier = new WeightTier
            {
                Id = Guid.NewGuid(),
                RouteId = request.RouteId,
                MinWeightKg = request.MinWeightKg,
                MaxWeightKg = request.MaxWeightKg,
                PricePerKg = request.PricePerKg
            };

            _db.WeightTiers.Add(weightTier);
            await _db.SaveChangesAsync();

            return ApiResponse<WeightTierDto>.SuccessResponse(new WeightTierDto
            {
                Id = weightTier.Id,
                RouteId = weightTier.RouteId,
                MinWeightKg = weightTier.MinWeightKg,
                MaxWeightKg = weightTier.MaxWeightKg,
                PricePerKg = weightTier.PricePerKg
            }, "Weight tier created successfully");
        }

        public async Task<ApiResponse<WeightTierDto>> UpdateAsync(Guid id, CreateUpdateWeightTierRequest request)
        {
            if (request.MaxWeightKg.HasValue && request.MaxWeightKg.Value <= request.MinWeightKg)
                return ApiResponse<WeightTierDto>.Failure("MaxWeightKg must be greater than MinWeightKg");

            var weightTier = await _db.WeightTiers.FirstOrDefaultAsync(w => w.Id == id);
            if (weightTier == null)
                return ApiResponse<WeightTierDto>.Failure("Weight tier not found");

            var routeExists = await _db.RouteMasters.AnyAsync(r => r.RouteId == request.RouteId);
            if (!routeExists)
                return ApiResponse<WeightTierDto>.Failure("Route not found");

            var hasOverlap = await CheckOverlapAsync(request.RouteId, request.MinWeightKg, request.MaxWeightKg, id);
            if (hasOverlap)
                return ApiResponse<WeightTierDto>.Failure("The specified weight range overlaps with an existing tier for this route.");

            weightTier.RouteId = request.RouteId;
            weightTier.MinWeightKg = request.MinWeightKg;
            weightTier.MaxWeightKg = request.MaxWeightKg;
            weightTier.PricePerKg = request.PricePerKg;

            await _db.SaveChangesAsync();

            return ApiResponse<WeightTierDto>.SuccessResponse(new WeightTierDto
            {
                Id = weightTier.Id,
                RouteId = weightTier.RouteId,
                MinWeightKg = weightTier.MinWeightKg,
                MaxWeightKg = weightTier.MaxWeightKg,
                PricePerKg = weightTier.PricePerKg
            }, "Weight tier updated successfully");
        }

        public async Task<ApiResponse<bool>> DeleteAsync(Guid id)
        {
            var weightTier = await _db.WeightTiers.FirstOrDefaultAsync(w => w.Id == id);
            if (weightTier == null)
                return ApiResponse<bool>.Failure("Weight tier not found");

            _db.WeightTiers.Remove(weightTier);
            await _db.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Weight tier deleted successfully");
        }

        public async Task<ApiResponse<ImportResultDto>> ImportFromCsvAsync(System.IO.Stream fileStream)
        {
            var result = new ImportResultDto();
            var validTiers = new List<WeightTier>();

            using var reader = new System.IO.StreamReader(fileStream);
            
            // Read header
            var header = await reader.ReadLineAsync();
            if (header == null)
            {
                return ApiResponse<ImportResultDto>.Failure("File is empty");
            }

            int rowNumber = 1;

            // Load existing routes for faster validation
            var existingRouteIds = await _db.RouteMasters.Select(r => r.RouteId).ToListAsync();
            var existingRouteIdsSet = new HashSet<Guid>(existingRouteIds);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                rowNumber++;
                
                if (string.IsNullOrWhiteSpace(line)) continue;
                result.TotalRows++;

                var parts = line.Split(',');
                if (parts.Length < 4)
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: Invalid column count.");
                    continue;
                }

                if (!Guid.TryParse(parts[0].Trim('"').Trim(), out var routeId) || !existingRouteIdsSet.Contains(routeId))
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: Invalid or non-existent RouteId.");
                    continue;
                }

                if (!decimal.TryParse(parts[1].Trim(), out var minWeightKg) || minWeightKg < 0)
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: Invalid MinWeightKg.");
                    continue;
                }

                decimal? maxWeightKg = null;
                var maxWeightStr = parts[2].Trim();
                if (!string.IsNullOrEmpty(maxWeightStr))
                {
                    if (!decimal.TryParse(maxWeightStr, out var max) || max <= minWeightKg)
                    {
                        result.FailedRows++;
                        result.Errors.Add($"Row {rowNumber}: MaxWeightKg must be valid and greater than MinWeightKg.");
                        continue;
                    }
                    maxWeightKg = max;
                }

                if (!decimal.TryParse(parts[3].Trim(), out var pricePerKg) || pricePerKg < 0)
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: Invalid PricePerKg.");
                    continue;
                }

                var hasOverlap = await CheckOverlapAsync(routeId, minWeightKg, maxWeightKg, null);
                // Also check overlap with other tiers in the same file
                if (!hasOverlap)
                {
                    foreach(var validTier in validTiers.Where(t => t.RouteId == routeId))
                    {
                        var maxA = maxWeightKg ?? decimal.MaxValue;
                        var maxB = validTier.MaxWeightKg ?? decimal.MaxValue;

                        if (Math.Max(minWeightKg, validTier.MinWeightKg) < Math.Min(maxA, maxB))
                        {
                            hasOverlap = true;
                            break;
                        }
                    }
                }

                if (hasOverlap)
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: The specified weight range overlaps with an existing tier or another row in this file.");
                    continue;
                }

                validTiers.Add(new WeightTier
                {
                    Id = Guid.NewGuid(),
                    RouteId = routeId,
                    MinWeightKg = minWeightKg,
                    MaxWeightKg = maxWeightKg,
                    PricePerKg = pricePerKg
                });
                
                result.SuccessfulRows++;
            }

            if (validTiers.Any())
            {
                _db.WeightTiers.AddRange(validTiers);
                await _db.SaveChangesAsync();
            }

            return ApiResponse<ImportResultDto>.SuccessResponse(result, "Import process completed");
        }

        private async Task<bool> CheckOverlapAsync(Guid routeId, decimal minWeight, decimal? maxWeight, Guid? excludeId)
        {
            var existingTiers = await _db.WeightTiers
                .Where(w => w.RouteId == routeId && (!excludeId.HasValue || w.Id != excludeId.Value))
                .ToListAsync();

            foreach (var tier in existingTiers)
            {
                // Overlap logic:
                // Range A: [minWeight, maxWeight]
                // Range B: [tier.MinWeightKg, tier.MaxWeightKg]
                
                var maxA = maxWeight ?? decimal.MaxValue;
                var maxB = tier.MaxWeightKg ?? decimal.MaxValue;

                // Two ranges overlap if max(minA, minB) < min(maxA, maxB)
                // (or <= depending on if boundary overlap is allowed. Usually it's strictly greater/less)
                if (Math.Max(minWeight, tier.MinWeightKg) < Math.Min(maxA, maxB))
                {
                    return true;
                }
            }

            return false;
        }

        private static int NormalizePageSize(int pageSize)
            => Math.Clamp(pageSize <= 0 ? 10 : pageSize, 1, 100);

        private static int NormalizeSkip(int pageNumber, int pageSize)
        {
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;
            return (safePageNumber - 1) * NormalizePageSize(pageSize);
        }
    }
}
