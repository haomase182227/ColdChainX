using ColdChainX.Application.DTOs;
using ColdChainX.Application.Services;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Persistence;
using ColdChainX.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.UnitTests
{
    public class AuthServiceChangePasswordTests : IDisposable
    {
        private const string CurrentPassword = "CurrentPassword@123";
        private const string NewPassword = "NewPassword@456";

        private readonly ApplicationDbContext _db;
        private readonly PasswordHasher<User> _passwordHasher = new();
        private readonly AuthService _service;

        public AuthServiceChangePasswordTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _db = new ApplicationDbContext(options);
            _service = new AuthService(
                new UserRepository(_db),
                null!,
                _passwordHasher,
                null!,
                null!,
                null!);
        }

        [Fact]
        public async Task ChangePasswordAsync_WithCorrectCurrentPassword_ChangesPasswordAndRevokesRefreshToken()
        {
            var user = await AddUserAsync();

            var result = await _service.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            {
                CurrentPassword = CurrentPassword,
                NewPassword = NewPassword
            });

            Assert.True(result.Success);
            Assert.True(result.Data);
            Assert.Equal(
                PasswordVerificationResult.Success,
                _passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, NewPassword));
            Assert.Equal(
                PasswordVerificationResult.Failed,
                _passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, CurrentPassword));
            Assert.Null(user.RefreshToken);
            Assert.Null(user.RefreshTokenExpiryTime);
            Assert.NotNull(user.UpdatedAt);
            Assert.Equal(user.UserId, user.UpdatedBy);
        }

        [Fact]
        public async Task ChangePasswordAsync_WithIncorrectCurrentPassword_DoesNotChangePassword()
        {
            var user = await AddUserAsync();
            var originalHash = user.PasswordHash;

            var result = await _service.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            {
                CurrentPassword = "IncorrectPassword@123",
                NewPassword = NewPassword
            });

            Assert.False(result.Success);
            Assert.Equal("Current password is incorrect", result.Message);
            Assert.Equal(originalHash, user.PasswordHash);
            Assert.Equal("refresh-token", user.RefreshToken);
        }

        [Fact]
        public async Task ChangePasswordAsync_WithSamePassword_ReturnsFailure()
        {
            var user = await AddUserAsync();
            var originalHash = user.PasswordHash;

            var result = await _service.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            {
                CurrentPassword = CurrentPassword,
                NewPassword = CurrentPassword
            });

            Assert.False(result.Success);
            Assert.Equal("New password must be different from current password", result.Message);
            Assert.Equal(originalHash, user.PasswordHash);
            Assert.Equal("refresh-token", user.RefreshToken);
        }

        [Fact]
        public async Task ChangePasswordAsync_ForInactiveUser_ReturnsFailure()
        {
            var user = await AddUserAsync(status: "INACTIVE");

            var result = await _service.ChangePasswordAsync(user.UserId, new ChangePasswordRequest
            {
                CurrentPassword = CurrentPassword,
                NewPassword = NewPassword
            });

            Assert.False(result.Success);
            Assert.Equal("Account has been deactivated", result.Message);
        }

        public void Dispose() => _db.Dispose();

        private async Task<User> AddUserAsync(string status = "ACTIVE")
        {
            var user = new User
            {
                UserId = Guid.NewGuid(),
                Username = "test-user",
                Email = "test@example.com",
                FullName = "Test User",
                Status = status,
                RefreshToken = "refresh-token",
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, CurrentPassword);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            return user;
        }
    }
}
