using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;

namespace ColdChainX.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ApplicationDbContext _db;

        public UserRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task AddAsync(User user)
        {
            await _db.Users.AddAsync(user);
        }

        public async Task AddCustomerAsync(Customer customer)
        {
            await _db.Customers.AddAsync(customer);
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            var normalizedEmail = email.ToLower();

            return await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            var normalizedUsername = username.ToLower();

            return await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername);
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            return await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == id);
        }

        public async Task<Role?> GetRoleByNameAsync(string roleName)
        {
            var normalizedRoleName = roleName.ToLower();

            return await _db.Roles
                .FirstOrDefaultAsync(r => r.RoleName.ToLower() == normalizedRoleName);
        }

        public async Task<Guid?> GetCustomerIdByEmailAsync(string email)
        {
            var normalizedEmail = email.ToLower();

            return await _db.Customers
                .Where(c => c.Email != null && c.Email.ToLower() == normalizedEmail)
                .Select(c => (Guid?)c.CustomerId)
                .FirstOrDefaultAsync();
        }

        public async Task<Guid?> GetDriverIdByUserIdAsync(Guid userId)
        {
            return await _db.Drivers
                .Where(d => d.UserId == userId)
                .Select(d => (Guid?)d.DriverId)
                .FirstOrDefaultAsync();
        }

        public async Task<Role?> GetRoleByIdAsync(Guid roleId)
        {
            return await _db.Roles
            .FirstOrDefaultAsync(r => r.RoleId == roleId);
        }

        public async Task<List<Role>> GetAllRolesAsync()
        {
            return await _db.Roles.ToListAsync();
        }

        public async Task AddRoleAsync(Role role)
        {
            await _db.Roles.AddAsync(role);
        }

        public async Task UpdateAsync(User user)
        {
            _db.Users.Update(user);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }

        public async Task<User?> GetByRefreshTokenAsync(string refreshToken)
        {
            return await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);
        }
    }
}
