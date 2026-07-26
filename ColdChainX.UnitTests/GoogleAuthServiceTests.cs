using ColdChainX.Application.DTOs.GoogleAuth;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.UnitTests
{
    public class GoogleAuthServiceTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly FakeGoogleIdTokenValidator _tokenValidator = new();
        private readonly GoogleAuthService _service;
        private readonly Role _customerRole = new()
        {
            RoleId = Guid.NewGuid(),
            RoleName = "Customer"
        };

        public GoogleAuthServiceTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(options);
            _db.Roles.Add(_customerRole);
            _db.SaveChanges();

            _service = new GoogleAuthService(
                new UserRepository(_db),
                new FakeDriverRepository(),
                new FakeJwtService(),
                _tokenValidator);
        }

        [Fact]
        public async Task AuthenticateAsync_ForNewGoogleUser_CreatesCustomerAndApplicationTokens()
        {
            _tokenValidator.User = VerifiedUser();

            var result = await _service.AuthenticateAsync("valid-id-token");

            Assert.True(result.Success);
            Assert.Equal("application-jwt", result.Data?.Token);
            Assert.Equal("application-refresh-token", result.Data?.RefreshToken);

            var user = Assert.Single(await _db.Users.ToListAsync());
            Assert.Equal("google-subject", user.GoogleId);
            Assert.Equal("GOOGLE", user.AuthProvider);
            Assert.Equal("https://example.com/avatar.png", user.AvatarUrl);
            Assert.Null(user.PasswordHash);
            Assert.Equal(_customerRole.RoleId, user.RoleId);

            var customer = Assert.Single(await _db.Customers.ToListAsync());
            Assert.Equal(user.Email, customer.Email);
            Assert.Equal(customer.CustomerId, result.Data?.User.CustomerId);
        }

        [Fact]
        public async Task AuthenticateAsync_WhenEmailExists_LinksGoogleAccountWithoutChangingPassword()
        {
            var existingUser = await AddExistingCustomerAsync();
            var originalPasswordHash = existingUser.PasswordHash;
            _tokenValidator.User = VerifiedUser();

            var result = await _service.AuthenticateAsync("valid-id-token");

            Assert.True(result.Success);
            Assert.Equal(existingUser.UserId, result.Data?.User.UserId);
            Assert.Equal("google-subject", existingUser.GoogleId);
            Assert.Equal("GOOGLE", existingUser.AuthProvider);
            Assert.Equal(originalPasswordHash, existingUser.PasswordHash);
            Assert.Single(await _db.Users.ToListAsync());
            Assert.Single(await _db.Customers.ToListAsync());
        }

        [Fact]
        public async Task AuthenticateAsync_WhenEmailIsLinkedToAnotherGoogleId_ReturnsConflict()
        {
            var existingUser = await AddExistingCustomerAsync();
            existingUser.GoogleId = "different-google-subject";
            await _db.SaveChangesAsync();
            _tokenValidator.User = VerifiedUser();

            var result = await _service.AuthenticateAsync("valid-id-token");

            Assert.False(result.Success);
            Assert.Equal(409, result.StatusCode);
            Assert.Equal("different-google-subject", existingUser.GoogleId);
        }

        [Fact]
        public async Task AuthenticateAsync_WhenEmailIsNotVerified_ReturnsUnauthorized()
        {
            _tokenValidator.User = VerifiedUser();
            _tokenValidator.User.EmailVerified = false;

            var result = await _service.AuthenticateAsync("valid-id-token");

            Assert.False(result.Success);
            Assert.Equal(401, result.StatusCode);
            Assert.Empty(await _db.Users.ToListAsync());
        }

        [Fact]
        public async Task AuthenticateAsync_WhenTokenIsInvalid_ReturnsUnauthorized()
        {
            _tokenValidator.User = null;

            var result = await _service.AuthenticateAsync("invalid-id-token");

            Assert.False(result.Success);
            Assert.Equal(401, result.StatusCode);
            Assert.Empty(await _db.Users.ToListAsync());
        }

        public void Dispose() => _db.Dispose();

        private async Task<User> AddExistingCustomerAsync()
        {
            var user = new User
            {
                UserId = Guid.NewGuid(),
                Username = "google.user@example.com",
                PasswordHash = "existing-password-hash",
                Email = "google.user@example.com",
                FullName = "Existing User",
                RoleId = _customerRole.RoleId,
                Role = _customerRole,
                Status = "ACTIVE"
            };
            var customer = new Customer
            {
                CustomerId = Guid.NewGuid(),
                CompanyName = "Existing Customer",
                TaxCode = $"TEST{Guid.NewGuid():N}"[..20],
                Email = user.Email,
                Status = "ACTIVE"
            };

            _db.Users.Add(user);
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
            return user;
        }

        private static VerifiedGoogleUserDto VerifiedUser()
        {
            return new VerifiedGoogleUserDto
            {
                GoogleId = "google-subject",
                Email = "google.user@example.com",
                Name = "Google User",
                Picture = "https://example.com/avatar.png",
                EmailVerified = true
            };
        }

        private sealed class FakeGoogleIdTokenValidator : IGoogleIdTokenValidator
        {
            public VerifiedGoogleUserDto? User { get; set; }

            public Task<VerifiedGoogleUserDto?> ValidateAsync(
                string idToken,
                CancellationToken cancellationToken = default)
                => Task.FromResult(User);
        }

        private sealed class FakeJwtService : IJwtService
        {
            public string GenerateAccessToken(
                User user,
                DateTime expiresAt,
                Guid? customerId = null,
                Guid? driverId = null)
                => "application-jwt";

            public string GenerateRefreshToken() => "application-refresh-token";
        }

        private sealed class FakeDriverRepository : IDriverRepository
        {
            public Task<List<Driver>> GetAllAsync() => Task.FromResult(new List<Driver>());
            public Task<Driver?> GetByIdAsync(Guid id) => Task.FromResult<Driver?>(null);
            public Task<Driver?> GetByUserIdAsync(Guid userId) => Task.FromResult<Driver?>(null);
            public Task<List<Driver>> GetAvailableAsync() => Task.FromResult(new List<Driver>());
            public Task<List<DriverWorkLog>> GetWorkLogsAsync(Guid driverId, DateOnly fromDate, DateOnly toDate)
                => Task.FromResult(new List<DriverWorkLog>());
            public Task AddAsync(Driver driver) => Task.CompletedTask;
            public Task AddLicenseAsync(DriverLicense license) => Task.CompletedTask;
            public Task UpdateAsync(Driver driver) => Task.CompletedTask;
            public Task DeleteAsync(Driver driver) => Task.CompletedTask;
            public Task SaveChangesAsync() => Task.CompletedTask;
        }
    }
}
