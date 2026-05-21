using System;
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

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            return await _db.Users.FindAsync(id);
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
            return await _db.Users.FirstOrDefaultAsync(u => u.RefreshToken == refreshToken);
        }
    }
}
