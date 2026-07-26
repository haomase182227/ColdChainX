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
        private readonly IDriverRepository _driverRepository;

        public AuthService(IUserRepository userRepository, IJwtService jwtService, IPasswordHasher<User> passwordHasher, IMapper mapper, IDriverRepository driverRepository)
        {
            _userRepository = userRepository;
            _jwtService = jwtService;
            _passwordHasher = passwordHasher;
            _mapper = mapper;
            _driverRepository = driverRepository;
        }

        public async Task<ApiResponse<AuthResponseDto>> RegisterAsync(RegisterRequest request)
        {
            if (!string.Equals(request.Role, "Admin", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Role, "Dispatcher", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Role, "Sales", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Role, "Loader", StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<AuthResponseDto>.Failure("Only Admin, Dispatcher, Sales, or Loader roles can be created through this endpoint");
            }

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
            {
                return ApiResponse<AuthResponseDto>.Failure($"Role '{request.Role}' not found in the system");
            }

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
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.AccessToken = accessToken;
            dto.RefreshToken = refreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, $"{request.Role} account created successfully");
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
            var customerId = await ResolveCustomerIdForTokenAsync(user);
            var driverId = await ResolveDriverIdAsync(user);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt, customerId, driverId);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.CustomerId = customerId;
            dto.DriverId = driverId;
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
            var customerId = await ResolveCustomerIdForTokenAsync(user);
            var driverId = await ResolveDriverIdAsync(user);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt, customerId, driverId);
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.CustomerId = customerId;
            dto.DriverId = driverId;
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

            if (IsInactive(user))
                return ApiResponse<bool>.Failure("User is already deactivated");

            user.Status = InactiveStatus;
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            user.UpdatedAt = DateTime.UtcNow;

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

        public async Task<ApiResponse<AuthResponseDto>> CreateCustomerAsync(CreateCustomerRequest request)
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

            // Get Customer role (ID should be 2 in the database)
            var role = await _userRepository.GetRoleByNameAsync("Customer");
            if (role == null)
                return ApiResponse<AuthResponseDto>.Failure("Customer role not found in the system");

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


            // Create Customer entity with provided information
            var customer = new Customer
            {
                CustomerId = Guid.NewGuid(),
                CompanyName = request.CompanyName.Trim(),
                TaxCode = request.TaxCode.Trim(),
                Address = request.Address?.Trim(),
                Email = email,
                PaymentTerm = request.PaymentTerm ?? 30,
                Status = ActiveStatus,
                CreatedAt = DbNow()
            };

            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(user, accessExpiresAt, customer.CustomerId);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.AddCustomerAsync(customer);
            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.CustomerId = customer.CustomerId;
            dto.AccessToken = accessToken;
            dto.RefreshToken = refreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "Customer account created successfully");
        }

        public async Task<ApiResponse<AuthResponseDto>> CreateDriverAsync(CreateDriverRequest request)
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

            // Get Driver role
            var role = await _userRepository.GetRoleByNameAsync("Driver");
            if (role == null)
                return ApiResponse<AuthResponseDto>.Failure("Driver role not found in the system");

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
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            // 1. Save user account first to satisfy foreign key constraints on the drivers table
            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            // 2. Create Driver entity linked to the saved user
            var driver = new Driver
            {
                DriverId = Guid.NewGuid(),
                UserId = user.UserId,
                DateOfBirth = request.DateOfBirth,
                Status = "AVAILABLE",
                CreatedAt = DbNow()
            };

            await _driverRepository.AddAsync(driver);

            // 3. Create Driver License if provided
            if (!string.IsNullOrWhiteSpace(request.LicenseNumber) &&
                !string.IsNullOrWhiteSpace(request.LicenseClass) &&
                request.IssueDate.HasValue &&
                request.ExpiryDate.HasValue)
            {
                var license = new DriverLicense
                {
                    LicenseId = Guid.NewGuid(),
                    DriverId = driver.DriverId,
                    LicenseNumber = request.LicenseNumber.Trim(),
                    LicenseClass = request.LicenseClass.Trim(),
                    IssueDate = request.IssueDate.Value,
                    ExpiryDate = request.ExpiryDate.Value,
                    DocumentUrl = request.DocumentUrl ?? string.Empty,
                    Status = ActiveStatus,
                    CreatedAt = DbNow()
                };

                await _driverRepository.AddLicenseAsync(license);
            }

            // 4. Save driver and driver license records
            await _userRepository.SaveChangesAsync();

            var dto = _mapper.Map<AuthResponseDto>(user);
            dto.AccessToken = accessToken;
            dto.RefreshToken = refreshToken;
            dto.AccessTokenExpiresAt = accessExpiresAt;

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "Driver account created successfully");
        }


        public async Task<ApiResponse<AuthResponseDto>> CreateWarehouseWorkerAsync(CreateWarehouseWorkerRequest request)
        {
            var email = request.Email?.Trim().ToLowerInvariant();
            var username = string.IsNullOrWhiteSpace(request.Username)
                ? (email ?? Guid.NewGuid().ToString("N")[..10])
                : request.Username.Trim().ToLowerInvariant();

            if (username.Length > 50)
                return ApiResponse<AuthResponseDto>.Failure("Username must not exceed 50 characters");

            if (!string.IsNullOrWhiteSpace(email))
            {
                var existing = await _userRepository.GetByEmailAsync(email);
                if (existing != null)
                    return ApiResponse<AuthResponseDto>.Failure("Email already in use");
            }

            var existingUsername = await _userRepository.GetByUsernameAsync(username);
            if (existingUsername != null)
                return ApiResponse<AuthResponseDto>.Failure("Username already in use");

            var role = await _userRepository.GetRoleByNameAsync("WarehouseOperator");
            if (role == null)
                return ApiResponse<AuthResponseDto>.Failure("WarehouseOperator role not found in the system");

            var user = new User
            {
                UserId = Guid.NewGuid(),
                Username = username,
                FullName = request.FullName.Trim(),
                Email = email,
                Phone = request.Phone?.Trim(),
                RoleId = role.RoleId,
                Role = role,
                WarehouseId = request.WarehouseId,
                Status = ActiveStatus,
                CreatedAt = DbNow()
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

            return ApiResponse<AuthResponseDto>.SuccessResponse(dto, "WarehouseOperator account created successfully");
        }

        public async Task<ApiResponse<DriverDto>> UpdateDriverAsync(Guid driverId, UpdateDriverInfoRequest request)
        {
            var driver = await _driverRepository.GetByIdAsync(driverId);
            if (driver == null)
                return ApiResponse<DriverDto>.Failure("Driver not found");

            // Update User fields if the driver has a linked user account
            if (driver.UserId.HasValue && driver.User != null)
            {
                var user = driver.User;

                if (IsInactive(user))
                    return ApiResponse<DriverDto>.Failure("Driver account has been deactivated");

                if (!string.IsNullOrWhiteSpace(request.FullName))
                    user.FullName = request.FullName.Trim();

                if (!string.IsNullOrWhiteSpace(request.NewPassword))
                    user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);

                user.UpdatedAt = DbNow();
                await _userRepository.UpdateAsync(user);
            }

            // Update Driver-specific fields
            if (request.DateOfBirth.HasValue)
                driver.DateOfBirth = request.DateOfBirth.Value;

            if (!string.IsNullOrWhiteSpace(request.Status))
            {
                var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "AVAILABLE", "ON_TRIP", "OFFLINE", "INACTIVE" };

                if (!validStatuses.Contains(request.Status.Trim()))
                    return ApiResponse<DriverDto>.Failure(
                        $"Invalid driver status '{request.Status}'. Allowed values: {string.Join(", ", validStatuses)}");

                driver.Status = request.Status.Trim().ToUpperInvariant();
            }

            // Update or create Driver License
            if (!string.IsNullOrWhiteSpace(request.LicenseNumber) &&
                !string.IsNullOrWhiteSpace(request.LicenseClass) &&
                request.IssueDate.HasValue &&
                request.ExpiryDate.HasValue)
            {
                var existingLicense = driver.DriverLicenses?.FirstOrDefault();
                if (existingLicense != null)
                {
                    existingLicense.LicenseNumber = request.LicenseNumber.Trim();
                    existingLicense.LicenseClass = request.LicenseClass.Trim();
                    existingLicense.IssueDate = request.IssueDate.Value;
                    existingLicense.ExpiryDate = request.ExpiryDate.Value;
                    if (request.DocumentUrl != null)
                        existingLicense.DocumentUrl = request.DocumentUrl;
                }
                else
                {
                    var newLicense = new DriverLicense
                    {
                        LicenseId = Guid.NewGuid(),
                        DriverId = driver.DriverId,
                        LicenseNumber = request.LicenseNumber.Trim(),
                        LicenseClass = request.LicenseClass.Trim(),
                        IssueDate = request.IssueDate.Value,
                        ExpiryDate = request.ExpiryDate.Value,
                        DocumentUrl = request.DocumentUrl ?? string.Empty,
                        Status = ActiveStatus,
                        CreatedAt = DbNow()
                    };
                    await _driverRepository.AddLicenseAsync(newLicense);
                }
            }

            await _driverRepository.UpdateAsync(driver);
            await _driverRepository.SaveChangesAsync();

            var dto = MapDriverDto(driver);
            return ApiResponse<DriverDto>.SuccessResponse(dto, "Driver information updated successfully");
        }

        private static DriverDto MapDriverDto(Driver driver)
        {
            return new DriverDto
            {
                DriverId = driver.DriverId,
                UserId = driver.UserId,
                Username = driver.User?.Username,
                Email = driver.User?.Email,
                FullName = driver.User?.FullName,
                DateOfBirth = driver.DateOfBirth,
                Status = driver.Status,
                CreatedAt = driver.CreatedAt,
                DriverLicenses = driver.DriverLicenses?.Select(l => new DriverLicenseDto
                {
                    LicenseId = l.LicenseId,
                    DriverId = l.DriverId,
                    LicenseNumber = l.LicenseNumber,
                    LicenseClass = l.LicenseClass,
                    IssueDate = l.IssueDate,
                    ExpiryDate = l.ExpiryDate,
                    DocumentUrl = l.DocumentUrl,
                    Status = l.Status,
                    CreatedAt = l.CreatedAt
                }).ToList() ?? new List<DriverLicenseDto>()
            };
        }

        private async Task<Guid?> ResolveCustomerIdForTokenAsync(User user)
        {
            if (!string.Equals(user.Role?.RoleName, "Customer", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return null;
            }

            return await _userRepository.GetCustomerIdByEmailAsync(user.Email);
        }

        private async Task<Guid?> ResolveDriverIdAsync(User user)
        {
            if (!string.Equals(user.Role?.RoleName, "Driver", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var driver = await _driverRepository.GetByUserIdAsync(user.UserId);
            return driver?.DriverId;
        }

        public async Task<ApiResponse<List<RoleDto>>> GetAllRolesAsync()
        {
            var roles = await _userRepository.GetAllRolesAsync();
            var roleDtos = roles.Select(r => new RoleDto
            {
                Id = r.RoleId,
                RoleName = r.RoleName,
                Description = r.Description
            }).ToList();

            return ApiResponse<List<RoleDto>>.SuccessResponse(roleDtos, "Roles retrieved successfully");
        }
    }
}
