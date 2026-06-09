using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Repositories
{
    public class DriverRepository : IDriverRepository
    {
        private readonly ApplicationDbContext _db;

        public DriverRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<Driver>> GetAllAsync()
        {
            return await _db.Drivers
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<Driver?> GetByIdAsync(Guid id)
        {
            return await _db.Drivers.FirstOrDefaultAsync(x => x.DriverId == id);
        }

        public async Task AddAsync(Driver driver)
        {
            await _db.Drivers.AddAsync(driver);
        }

        public async Task AddLicenseAsync(DriverLicense license)
        {
            await _db.DriverLicenses.AddAsync(license);
        }

        public async Task UpdateAsync(Driver driver)
        {
            _db.Drivers.Update(driver);
            await Task.CompletedTask;
        }

        public async Task DeleteAsync(Driver driver)
        {
            _db.Drivers.Update(driver);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}