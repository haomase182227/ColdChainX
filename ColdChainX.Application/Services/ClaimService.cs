using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ColdChainX.Application.DTOs.Common;
using ColdChainX.Application.DTOs.Claim;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class ClaimService : IClaimService
    {
        private readonly IApplicationDbContext _db;
        private readonly ILogger<ClaimService> _logger;

        public ClaimService(IApplicationDbContext db, ILogger<ClaimService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<ApiResponse<ClaimResponse>> CreateClaimAsync(CreateClaimRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<ClaimResponse>.Failure("Request is null");

            try
            {
                var userExists = await _db.Users.AnyAsync(u => u.UserId == userId);
                if (!userExists)
                    return ApiResponse<ClaimResponse>.Failure("Reporter user not found.");

                if (request.OrderId.HasValue)
                {
                    var orderExists = await _db.TransportOrders.AnyAsync(o => o.OrderId == request.OrderId.Value);
                    if (!orderExists)
                        return ApiResponse<ClaimResponse>.Failure("Order not found.");
                }

                var claimCode = $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

                var claim = new Claim
                {
                    ClaimId = Guid.NewGuid(),
                    ClaimCode = claimCode,
                    OrderId = request.OrderId,
                    ClaimType = request.ClaimType.Trim().ToUpperInvariant(),
                    Description = request.Description.Trim(),
                    Status = "OPEN",
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };

                _db.Claims.Add(claim);

                if (request.EvidenceImages != null)
                {
                    foreach (var imgUrl in request.EvidenceImages)
                    {
                        var evidence = new ClaimEvidence
                        {
                            EvidenceId = Guid.NewGuid(),
                            ClaimId = claim.ClaimId,
                            EvidenceType = "IMAGE",
                            ImageUrl = imgUrl.Trim(),
                            UploadedBy = userId,
                            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                        };
                        _db.ClaimEvidences.Add(evidence);
                    }
                }

                await _db.SaveChangesAsync();

                // Reload claim details
                var savedClaim = await _db.Claims
                    .Include(c => c.Order)
                    .Include(c => c.ClaimEvidences)
                        .ThenInclude(e => e.UploadedByNavigation)
                    .FirstOrDefaultAsync(c => c.ClaimId == claim.ClaimId);

                var response = MapToResponse(savedClaim!);
                return ApiResponse<ClaimResponse>.SuccessResponse(response, "Claim registered successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create claim. OrderId: {OrderId}", request.OrderId);
                return ApiResponse<ClaimResponse>.Failure($"Failed to create claim: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> ResolveClaimAsync(Guid claimId, ResolveClaimRequest request, Guid userId)
        {
            if (request == null)
                return ApiResponse<bool>.Failure("Request is null");

            try
            {
                var claim = await _db.Claims.FindAsync(claimId);
                if (claim == null)
                    return ApiResponse<bool>.Failure("Claim not found.");

                if (claim.Status == "RESOLVED" || claim.Status == "REJECTED")
                    return ApiResponse<bool>.Failure($"Claim is already finalized as '{claim.Status}'.");

                claim.Status = request.Status.Trim().ToUpperInvariant();
                claim.FaultOwner = request.FaultOwner?.Trim().ToUpperInvariant();
                claim.ResolutionNote = request.ResolutionNote?.Trim();
                claim.ResolvedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _db.SaveChangesAsync();
                return ApiResponse<bool>.SuccessResponse(true, "Claim resolved and status updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve claim. ClaimId: {ClaimId}", claimId);
                return ApiResponse<bool>.Failure($"Failed to resolve claim: {ex.Message}");
            }
        }

        public async Task<ApiResponse<ClaimResponse>> GetClaimByIdAsync(Guid claimId)
        {
            try
            {
                var claim = await _db.Claims
                    .Include(c => c.Order)
                    .Include(c => c.ClaimEvidences)
                        .ThenInclude(e => e.UploadedByNavigation)
                    .FirstOrDefaultAsync(c => c.ClaimId == claimId);

                if (claim == null)
                    return ApiResponse<ClaimResponse>.Failure("Claim not found.");

                var response = MapToResponse(claim);
                return ApiResponse<ClaimResponse>.SuccessResponse(response, "Claim details retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve claim details. ClaimId: {ClaimId}", claimId);
                return ApiResponse<ClaimResponse>.Failure($"Failed to retrieve claim details: {ex.Message}");
            }
        }

        public async Task<ApiResponse<PagedResult<ClaimResponse>>> GetPagedClaimsAsync(Guid? orderId, int pageNumber, int pageSize)
        {
            try
            {
                var query = _db.Claims
                    .Include(c => c.Order)
                    .Include(c => c.ClaimEvidences)
                        .ThenInclude(e => e.UploadedByNavigation)
                    .AsQueryable();

                if (orderId.HasValue)
                {
                    query = query.Where(c => c.OrderId == orderId.Value);
                }

                int totalCount = await query.CountAsync();
                var items = await query
                    .OrderByDescending(c => c.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var responseList = items.Select(MapToResponse).ToList();
                var pagedResult = PagedResult<ClaimResponse>.Create(responseList, totalCount, pageNumber, pageSize);

                return ApiResponse<PagedResult<ClaimResponse>>.SuccessResponse(pagedResult, "Paged claims retrieved successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve paged claims.");
                return ApiResponse<PagedResult<ClaimResponse>>.Failure($"Failed to retrieve claims: {ex.Message}");
            }
        }

        private static ClaimResponse MapToResponse(Claim claim)
        {
            return new ClaimResponse
            {
                ClaimId = claim.ClaimId,
                ClaimCode = claim.ClaimCode,
                OrderId = claim.OrderId,
                OrderTrackingCode = claim.Order?.TrackingCode ?? "N/A",
                ClaimType = claim.ClaimType,
                Description = claim.Description,
                FaultOwner = claim.FaultOwner,
                Status = claim.Status ?? "OPEN",
                ResolutionNote = claim.ResolutionNote,
                CreatedAt = claim.CreatedAt,
                ResolvedAt = claim.ResolvedAt,
                Evidences = claim.ClaimEvidences.Select(e => new ClaimEvidenceResponse
                {
                    EvidenceId = e.EvidenceId,
                    EvidenceType = e.EvidenceType,
                    ImageUrl = e.ImageUrl,
                    UploadedBy = e.UploadedBy,
                    UploadedByUsername = e.UploadedByNavigation?.Username ?? "Unknown",
                    CreatedAt = e.CreatedAt
                }).ToList()
            };
        }
    }
}
