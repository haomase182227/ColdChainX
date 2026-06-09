using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;

namespace ColdChainX.Core.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetByUsernameAsync(string username);
        Task<User?> GetByRefreshTokenAsync(string refreshToken);
        Task<User?> GetByIdAsync(Guid id);
        Task<Role?> GetRoleByNameAsync(string roleName);
        Task<Guid?> GetCustomerIdByEmailAsync(string email);
        Task<Guid?> GetDriverIdByUserIdAsync(Guid userId);
        Task<Role?> GetRoleByIdAsync(int roleId);
        Task<List<Role>> GetAllRolesAsync();
        Task AddRoleAsync(Role role);
        Task AddCustomerAsync(Customer customer);
        Task AddAsync(User user);
        Task UpdateAsync(User user);
        Task SaveChangesAsync();
        Task<List<User>> GetAllAsync();
        Task<(List<User> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            string? search,
            string? role,
            UserStatus? status,
            string? sortBy,
            string? order);
    }
}
