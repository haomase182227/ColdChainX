using System;
using System.Threading.Tasks;
using ColdChainX.Application.DTOs;
using ColdChainX.Core.Enums;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Interfaces
{
    public interface IUserService
    {
        Task<ApiResponse<UserListResponse>> GetUsersAsync(
            int page,
            int pageSize,
            string? search,
            string? role,
            UserStatus? status,
            string? sortBy,
            string? order);

        Task<ApiResponse<UserProfileDto>> GetUserByIdAsync(Guid id);

        Task<ApiResponse<UserProfileDto>> CreateUserAsync(CreateUserRequest request);

        Task<ApiResponse<UserProfileDto>> UpdateUserAsync(Guid id, AdminUpdateUserRequest request);

        Task<ApiResponse<bool>> ChangeRoleAsync(
            Guid id,
            ChangeUserRoleRequest request);

        Task<ApiResponse<bool>> ChangeStatusAsync(
            Guid id,
            ChangeUserStatusRequest request);

        Task<ApiResponse<bool>> ResetPasswordAsync(
            Guid id,
            ResetPasswordRequest request);

        Task<ApiResponse<bool>> SoftDeleteAsync(Guid id);

        Task<ApiResponse<bool>> RestoreAsync(Guid id);
    }
}
