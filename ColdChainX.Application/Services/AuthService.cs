using System;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IJwtService _jwtService;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IMapper _mapper;

        public AuthService(IUserRepository userRepository, IJwtService jwtService, IPasswordHasher<User> passwordHasher, IMapper mapper)
        {
            _userRepository = userRepository;
            _jwtService = jwtService;
            _passwordHasher = passwordHasher;
            _mapper = mapper;
        }

        public async Task<ApiResponse<AuthResponseDto>> RegisterAsync(RegisterRequest request)
        {
            var existing = await _userRepository.GetByEmailAsync(request.Email.ToLowerInvariant());
            if (existing != null)
                return ApiResponse<AuthResponseDto>.Failure("Email already in use");

            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName,
                Email = request.Email.ToLowerInvariant(),
                PhoneNumber = request.PhoneNumber,
                Role = request.Role,
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.AccessToken = accessToken;
            dto.RefreshToken = refreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "Registration successful");
        }

        public async Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequest request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email.ToLowerInvariant());
            if (user == null)
                return ApiResponse<AuthResponseDto>.Failure("Invalid credentials");

            if (user.Status == UserStatus.Inactive)
                return ApiResponse<AuthResponseDto>.Failure("Account has been deactivated");

            var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verify == PasswordVerificationResult.Failed)
                return ApiResponse<AuthResponseDto>.Failure("Invalid credentials");

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.AccessToken = accessToken;
            dto.RefreshToken = refreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "Login successful");
        }

        public async Task<ApiResponse<AuthResponseDto>> RefreshTokensAsync(string refreshToken)
        {
            var user = await _userRepository.GetByRefreshTokenAsync(refreshToken);
            if (user == null) return ApiResponse<AuthResponseDto>.Failure("Invalid refresh token");

            if (!user.RefreshTokenExpiryTime.HasValue || user.RefreshTokenExpiryTime.Value < DateTime.UtcNow)
                return ApiResponse<AuthResponseDto>.Failure("Refresh token expired");

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.AccessToken = accessToken;
            dto.RefreshToken = newRefreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "Token refreshed");
        }

        public async Task<ApiResponse<bool>> LogoutAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return ApiResponse<bool>.Failure("User not found");

            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "Logout successful");
        }

        public async Task<ApiResponse<UserProfileDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return ApiResponse<UserProfileDto>.Failure("User not found");

            if (user.Status == UserStatus.Inactive)
                return ApiResponse<UserProfileDto>.Failure("Account has been deactivated");

            if (!string.IsNullOrWhiteSpace(request.FullName))
                user.FullName = request.FullName.Trim();

            if (request.PhoneNumber != null)
                user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

            if (!string.IsNullOrWhiteSpace(request.NewPassword))
                user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);

            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<UserProfileDto>(user);
            return ApiResponse<UserProfileDto>.SuccessResponse(dto, "Profile updated successfully");
        }

        public async Task<ApiResponse<bool>> SoftDeleteUserAsync(Guid targetUserId)
        {
            var user = await _userRepository.GetByIdAsync(targetUserId);
            if (user == null) return ApiResponse<bool>.Failure("User not found");

            if (user.Status == UserStatus.Inactive)
                return ApiResponse<bool>.Failure("User is already deactivated");

            user.Status = UserStatus.Inactive;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "User deactivated successfully");
        }
    }
}
