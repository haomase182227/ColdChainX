using System;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Identity;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class AuthService : IAuthService
    {
        private const string ActiveStatus = "ACTIVE";
        private const string InactiveStatus = "INACTIVE";

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
            var email = request.Email.Trim().ToLowerInvariant();
            var username = string.IsNullOrWhiteSpace(request.Username)
                ? email
                : request.Username.Trim().ToLowerInvariant();

            if (username.Length > 50)
                return ApiResponse<AuthResponseDto>.Failure("Username must not exceed 50 characters");

            var existing = await _userRepository.GetByEmailAsync(email);
            if (existing != null)
                return ApiResponse<AuthResponseDto>.Failure("Email already in use");

            var existingUsername = await _userRepository.GetByUsernameAsync(username);
            if (existingUsername != null)
                return ApiResponse<AuthResponseDto>.Failure("Username already in use");

            var role = await _userRepository.GetRoleByNameAsync(request.Role);
            if (role == null)
                return ApiResponse<AuthResponseDto>.Failure($"Role '{request.Role}' was not found in database");

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Username = username,
                FullName = request.FullName.Trim(),
                Email = email,
                RoleId = role.RoleId,
                Role = role,
                Status = ActiveStatus,
                CreatedAt = DbNow()
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DbNow().AddDays(7);

            Customer? customer = null;
            if (string.Equals(request.Role, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                customer = new Customer
                {
                    CustomerId = Guid.NewGuid(),
                    CompanyName = string.Empty,
                    TaxCode = GenerateTemporaryTaxCode(user.UserId),
                    Email = email,
                    PaymentTerm = 30,
                    Status = ActiveStatus,
                    CreatedAt = DbNow()
                };

                await _userRepository.AddCustomerAsync(customer);
            }

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.CustomerId = customer?.CustomerId;
            dto.AccessToken = accessToken;
            dto.RefreshToken = refreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "Registration successful");
        }

        public async Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequest request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email.Trim().ToLowerInvariant());
            if (user == null)
                return ApiResponse<AuthResponseDto>.Failure("Invalid credentials");

            if (IsInactive(user))
                return ApiResponse<AuthResponseDto>.Failure("Account has been deactivated");

            var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verify == PasswordVerificationResult.Failed)
                return ApiResponse<AuthResponseDto>.Failure("Invalid credentials");

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DbNow().AddDays(7);

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

            if (!user.RefreshTokenExpiryTime.HasValue || user.RefreshTokenExpiryTime.Value < DbNow())
                return ApiResponse<AuthResponseDto>.Failure("Refresh token expired");

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DbNow().AddDays(7);

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

            if (IsInactive(user))
                return ApiResponse<UserProfileDto>.Failure("Account has been deactivated");

            if (!string.IsNullOrWhiteSpace(request.FullName))
                user.FullName = request.FullName.Trim();

            if (!string.IsNullOrWhiteSpace(request.NewPassword))
                user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);

            user.UpdatedAt = DbNow();

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<UserProfileDto>(user);
            return ApiResponse<UserProfileDto>.SuccessResponse(dto, "Profile updated successfully");
        }

        public async Task<ApiResponse<bool>> SoftDeleteUserAsync(Guid targetUserId)
        {
            var user = await _userRepository.GetByIdAsync(targetUserId);
            if (user == null) return ApiResponse<bool>.Failure("User not found");

            if (IsInactive(user))
                return ApiResponse<bool>.Failure("User is already deactivated");

            user.Status = InactiveStatus;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            user.UpdatedAt = DbNow();

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return ApiResponse<bool>.SuccessResponse(true, "User deactivated successfully");
        }

        private static bool IsInactive(User user)
            => string.Equals(user.Status, InactiveStatus, StringComparison.OrdinalIgnoreCase);

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        private static string GenerateTemporaryTaxCode(Guid userId)
            => $"TEMP{userId:N}"[..20];
    }
}
