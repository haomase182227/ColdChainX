using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UserService(
            IUserRepository userRepository,
            IPasswordHasher<User> passwordHasher,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessor)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ApiResponse<UserListResponse>> GetUsersAsync(
            int page,
            int pageSize,
            string? search,
            string? role,
            UserStatus? status,
            string? sortBy,
            string? order)
        {
            var (items, totalCount) = await _userRepository.GetPagedAsync(
                page,
                pageSize,
                search,
                role,
                status,
                sortBy,
                order);

            var dtos = _mapper.Map<List<UserProfileDto>>(items);
            
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var response = new UserListResponse
            {
                Items = dtos,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalCount,
                TotalPages = totalPages
            };

            return ApiResponse<UserListResponse>.SuccessResponse(response, "Users retrieved successfully");
        }

        public async Task<ApiResponse<UserProfileDto>> GetUserByIdAsync(Guid id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<UserProfileDto>.Failure("User not found");

            var dto = _mapper.Map<UserProfileDto>(user);
            return ApiResponse<UserProfileDto>.SuccessResponse(dto, "User retrieved successfully");
        }

        public async Task<ApiResponse<UserProfileDto>> CreateUserAsync(CreateUserRequest request)
        {
            var currentUserId = GetCurrentUserId();
            var email = request.Email.Trim().ToLowerInvariant();

            var existingEmail = await _userRepository.GetByEmailAsync(email);
            if (existingEmail != null)
                return ApiResponse<UserProfileDto>.Failure("Email already in use");

            var username = email;
            var existingUsername = await _userRepository.GetByUsernameAsync(username);
            if (existingUsername != null)
                return ApiResponse<UserProfileDto>.Failure("Username already in use");

            var role = await _userRepository.GetRoleByNameAsync(request.Role);
            if (role == null)
                return ApiResponse<UserProfileDto>.Failure($"Role '{request.Role}' not found");

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Username = username,
                Email = email,
                FullName = request.FullName.Trim(),
                Phone = request.PhoneNumber?.Trim(),
                RoleId = role.Id,
                Role = role,
                Status = request.Status == UserStatus.Active ? "ACTIVE" : "INACTIVE",
                CreatedAt = DbNow(),
                CreatedBy = currentUserId
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            if (request.Status == UserStatus.Inactive)
            {
                user.DeletedAt = DbNow();
                user.DeletedBy = currentUserId;
            }

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<UserProfileDto>(user);
            return ApiResponse<UserProfileDto>.SuccessResponse(dto, "User created successfully");
        }

        public async Task<ApiResponse<UserProfileDto>> UpdateUserAsync(Guid id, AdminUpdateUserRequest request)
        {
            var currentUserId = GetCurrentUserId();
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<UserProfileDto>.Failure("User not found");

            var email = request.Email.Trim().ToLowerInvariant();
            var existingEmail = await _userRepository.GetByEmailAsync(email);
            if (existingEmail != null && existingEmail.UserId != id)
                return ApiResponse<UserProfileDto>.Failure("Email already in use");

            user.FullName = request.FullName.Trim();
            user.Email = email;
            user.Phone = request.PhoneNumber?.Trim();
            user.UpdatedAt = DbNow();
            user.UpdatedBy = currentUserId;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<UserProfileDto>(user);
            return ApiResponse<UserProfileDto>.SuccessResponse(dto, "User updated successfully");
        }

        public async Task<ApiResponse<bool>> ChangeRoleAsync(Guid id, ChangeUserRoleRequest request)
        {
            var currentUserId = GetCurrentUserId();
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Failure("User not found");

            var role = await _userRepository.GetRoleByNameAsync(request.Role);
            if (role == null)
                return ApiResponse<bool>.Failure($"Role '{request.Role}' not found");

            user.RoleId = role.Id;
            user.Role = role;
            user.UpdatedAt = DbNow();
            user.UpdatedBy = currentUserId;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "User role updated successfully");
        }

        public async Task<ApiResponse<bool>> ChangeStatusAsync(Guid id, ChangeUserStatusRequest request)
        {
            var currentUserId = GetCurrentUserId();
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Failure("User not found");

            var statusStr = request.Status == UserStatus.Active ? "ACTIVE" : "INACTIVE";

            if (request.Status == UserStatus.Inactive)
            {
                if (user.DeletedAt == null)
                {
                    user.DeletedAt = DbNow();
                    user.DeletedBy = currentUserId;
                }
            }
            else
            {
                user.DeletedAt = null;
                user.DeletedBy = null;
            }

            user.Status = statusStr;
            user.UpdatedAt = DbNow();
            user.UpdatedBy = currentUserId;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "User status updated successfully");
        }

        public async Task<ApiResponse<bool>> ResetPasswordAsync(Guid id, ResetPasswordRequest request)
        {
            var currentUserId = GetCurrentUserId();
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Failure("User not found");

            user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
            user.UpdatedAt = DbNow();
            user.UpdatedBy = currentUserId;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Password reset successfully");
        }

        public async Task<ApiResponse<bool>> SoftDeleteAsync(Guid id)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId.HasValue && id == currentUserId.Value)
            {
                return ApiResponse<bool>.Failure("Admin cannot delete own account");
            }

            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Failure("User not found");

            if (user.DeletedAt != null || user.Status == "INACTIVE")
                return ApiResponse<bool>.Failure("User is already deactivated/deleted");

            user.Status = "INACTIVE";
            user.DeletedAt = DbNow();
            user.DeletedBy = currentUserId;
            user.UpdatedAt = DbNow();
            user.UpdatedBy = currentUserId;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "User soft-deleted successfully");
        }

        public async Task<ApiResponse<bool>> RestoreAsync(Guid id)
        {
            var currentUserId = GetCurrentUserId();
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null)
                return ApiResponse<bool>.Failure("User not found");

            if (user.DeletedAt == null && user.Status == "ACTIVE")
                return ApiResponse<bool>.Failure("User is already active");

            user.Status = "ACTIVE";
            user.DeletedAt = null;
            user.DeletedBy = null;
            user.UpdatedAt = DbNow();
            user.UpdatedBy = currentUserId;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "User restored successfully");
        }

        private Guid? GetCurrentUserId()
        {
            var userIdStr = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value;

            if (Guid.TryParse(userIdStr, out var userId))
            {
                return userId;
            }
            return null;
        }

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}
