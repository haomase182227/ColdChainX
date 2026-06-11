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
                .IgnoreQueryFilters() // Also check soft-deleted users to prevent email duplication
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            var normalizedUsername = username.ToLower();

            return await _db.Users
                .IgnoreQueryFilters() // Also check soft-deleted users to prevent username duplication
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername);
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            return await _db.Users
                .IgnoreQueryFilters() // Allow fetching soft-deleted users by ID (required for Restore and Admin detail)
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

        public async Task<List<User>> GetAllAsync()
        {
            return await _db.Users
                .Include(u => u.Role)
                .ToListAsync();
        }

        public async Task<(List<User> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? role,
            ColdChainX.Core.Enums.UserStatus? status,
            string? sortBy,
            string? order)
        {
            IQueryable<User> query = _db.Users.Include(u => u.Role);

            // If we explicitly search for Inactive/Deleted status, we must ignore the query filter
            if (status.HasValue && status.Value == ColdChainX.Core.Enums.UserStatus.Inactive)
            {
                query = _db.Users.IgnoreQueryFilters()
                    .Include(u => u.Role)
                    .Where(u => u.DeletedAt != null || u.Status == "INACTIVE");
            }
            else if (status.HasValue && status.Value == ColdChainX.Core.Enums.UserStatus.Active)
            {
                query = query.Where(u => u.Status == "ACTIVE");
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(u => u.Username.ToLower().Contains(s)
                                      || (u.Email != null && u.Email.ToLower().Contains(s))
                                      || u.FullName.ToLower().Contains(s));
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                var roleName = role.Trim().ToLower();
                query = query.Where(u => u.Role != null && u.Role.RoleName.ToLower() == roleName);
            }

            var totalCount = await query.CountAsync();

            var isDesc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
            sortBy = sortBy?.Trim().ToLowerInvariant();

            query = sortBy switch
            {
                "username" => isDesc ? query.OrderByDescending(u => u.Username) : query.OrderBy(u => u.Username),
                "email" => isDesc ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
                "fullname" => isDesc ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName),
                "status" => isDesc ? query.OrderByDescending(u => u.Status) : query.OrderBy(u => u.Status),
                "role" => isDesc ? query.OrderByDescending(u => u.Role != null ? u.Role.RoleName : "") : query.OrderBy(u => u.Role != null ? u.Role.RoleName : ""),
                "createdat" or _ => isDesc ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt)
            };

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
    }
}
