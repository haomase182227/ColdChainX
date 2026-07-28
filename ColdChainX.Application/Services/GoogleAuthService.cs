using System.Security.Cryptography;
using System.Text;
using ColdChainX.Application.DTOs.GoogleAuth;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Shared.Responses;

namespace ColdChainX.Application.Services
{
    public class GoogleAuthService : IGoogleAuthService
    {
        private const string ActiveStatus = "ACTIVE";
        private const string InactiveStatus = "INACTIVE";
        private const string GoogleProvider = "GOOGLE";

        private readonly IUserRepository _userRepository;
        private readonly IDriverRepository _driverRepository;
        private readonly IJwtService _jwtService;
        private readonly IGoogleIdTokenValidator _idTokenValidator;

        public GoogleAuthService(
            IUserRepository userRepository,
            IDriverRepository driverRepository,
            IJwtService jwtService,
            IGoogleIdTokenValidator idTokenValidator)
        {
            _userRepository = userRepository;
            _driverRepository = driverRepository;
            _jwtService = jwtService;
            _idTokenValidator = idTokenValidator;
        }

        public async Task<ApiResponse<GoogleLoginResponse>> AuthenticateAsync(
            string? idToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                return Unauthorized("Google ID token is required");

            var googleUser = await _idTokenValidator.ValidateAsync(idToken, cancellationToken);
            if (googleUser == null ||
                !googleUser.EmailVerified ||
                string.IsNullOrWhiteSpace(googleUser.GoogleId) ||
                string.IsNullOrWhiteSpace(googleUser.Email))
            {
                return Unauthorized("Invalid Google ID token");
            }

            var googleId = googleUser.GoogleId.Trim();
            var email = googleUser.Email.Trim().ToLowerInvariant();

            if (googleId.Length > 255 || email.Length > 255)
                return Unauthorized("Invalid Google account information");

            var user = await _userRepository.GetByGoogleIdAsync(googleId);
            var isNewUser = false;
            Guid? customerId = null;

            if (user == null)
            {
                user = await _userRepository.GetByEmailAsync(email);
                if (user != null)
                {
                    if (!string.IsNullOrWhiteSpace(user.GoogleId) &&
                        !string.Equals(user.GoogleId, googleId, StringComparison.Ordinal))
                    {
                        return ApiResponse<GoogleLoginResponse>.Failure(
                            "Email is already linked to another Google account",
                            409);
                    }

                    user.GoogleId = googleId;
                }
                else
                {
                    var role = await _userRepository.GetRoleByNameAsync("Customer");
                    if (role == null)
                    {
                        return ApiResponse<GoogleLoginResponse>.Failure(
                            "Customer role is not configured",
                            500);
                    }

                    var userId = Guid.NewGuid();
                    var fullName = NormalizeFullName(googleUser.Name, email);
                    user = new User
                    {
                        UserId = userId,
                        Username = await GenerateUniqueUsernameAsync(email, googleId),
                        PasswordHash = null,
                        Email = email,
                        FullName = fullName,
                        GoogleId = googleId,
                        AvatarUrl = NormalizeAvatarUrl(googleUser.Picture),
                        AuthProvider = GoogleProvider,
                        RoleId = role.RoleId,
                        Role = role,
                        Status = ActiveStatus,
                        CreatedAt = DbNow()
                    };

                    customerId = await _userRepository.GetCustomerIdByEmailAsync(email);
                    if (!customerId.HasValue)
                    {
                        var customer = new Customer
                        {
                            CustomerId = Guid.NewGuid(),
                            CompanyName = fullName,
                            TaxCode = GenerateTemporaryTaxCode(userId),
                            Email = email,
                            PaymentTerm = 30,
                            Status = ActiveStatus,
                            CreatedAt = DbNow()
                        };

                        customerId = customer.CustomerId;
                        await _userRepository.AddCustomerAsync(customer);
                    }

                    await _userRepository.AddAsync(user);
                    isNewUser = true;
                }
            }

            if (string.Equals(user.Status, InactiveStatus, StringComparison.OrdinalIgnoreCase))
                return Unauthorized("Account has been deactivated");

            user.GoogleId = googleId;
            user.AvatarUrl = NormalizeAvatarUrl(googleUser.Picture);
            user.AuthProvider = GoogleProvider;

            if (!isNewUser)
            {
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepository.UpdateAsync(user);
            }

            customerId ??= await ResolveCustomerIdForTokenAsync(user);
            if (!customerId.HasValue &&
                string.Equals(user.Role?.RoleName, "Customer", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(user.Email))
            {
                var customer = new Customer
                {
                    CustomerId = Guid.NewGuid(),
                    CompanyName = user.FullName,
                    TaxCode = GenerateTemporaryTaxCode(user.UserId),
                    Email = user.Email,
                    PaymentTerm = 30,
                    Status = ActiveStatus,
                    CreatedAt = DbNow()
                };

                customerId = customer.CustomerId;
                await _userRepository.AddCustomerAsync(customer);
            }

            var driverId = await ResolveDriverIdAsync(user);
            var accessExpiresAt = DateTime.UtcNow.AddMinutes(60);
            var accessToken = _jwtService.GenerateAccessToken(
                user,
                accessExpiresAt,
                customerId,
                driverId);
            var refreshToken = _jwtService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

            await _userRepository.SaveChangesAsync();

            var response = new GoogleLoginResponse
            {
                Token = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = accessExpiresAt,
                User = new GoogleLoginUserDto
                {
                    UserId = user.UserId,
                    CustomerId = customerId,
                    DriverId = driverId,
                    Username = user.Username,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role?.RoleName,
                    Status = user.Status,
                    AvatarUrl = user.AvatarUrl,
                    AuthProvider = user.AuthProvider
                }
            };

            return ApiResponse<GoogleLoginResponse>.SuccessResponse(
                response,
                "Google login successful");
        }

        private async Task<string> GenerateUniqueUsernameAsync(string email, string googleId)
        {
            if (email.Length <= 50 && await _userRepository.GetByUsernameAsync(email) == null)
                return email;

            var googleIdHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(googleId)))
                .ToLowerInvariant();
            var candidate = $"google_{googleIdHash[..24]}";

            if (await _userRepository.GetByUsernameAsync(candidate) == null)
                return candidate;

            return $"google_{Guid.NewGuid():N}"[..39];
        }

        private async Task<Guid?> ResolveCustomerIdForTokenAsync(User user)
        {
            if (!string.Equals(user.Role?.RoleName, "Customer", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(user.Email))
            {
                return null;
            }

            return await _userRepository.GetCustomerIdByEmailAsync(user.Email);
        }

        private async Task<Guid?> ResolveDriverIdAsync(User user)
        {
            if (!string.Equals(user.Role?.RoleName, "Driver", StringComparison.OrdinalIgnoreCase))
                return null;

            var driver = await _driverRepository.GetByUserIdAsync(user.UserId);
            return driver?.DriverId;
        }

        private static string NormalizeFullName(string? name, string email)
        {
            var value = string.IsNullOrWhiteSpace(name)
                ? email.Split('@', 2)[0]
                : name.Trim();

            return value.Length <= 100 ? value : value[..100];
        }

        private static string? NormalizeAvatarUrl(string? picture)
        {
            if (string.IsNullOrWhiteSpace(picture))
                return null;

            var value = picture.Trim();
            return value.Length <= 1024 ? value : null;
        }

        private static DateTime DbNow()
            => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        private static string GenerateTemporaryTaxCode(Guid userId)
            => $"TEMP{userId:N}"[..20];

        private static ApiResponse<GoogleLoginResponse> Unauthorized(string message)
            => ApiResponse<GoogleLoginResponse>.Failure(message, 401);
    }
}
