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
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60); // will use jwt settings in jwt service
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
    }
}
