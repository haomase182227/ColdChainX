using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IDriverRepository
    {
        Task<List<Driver>> GetAllAsync();
        Task<Driver?> GetByIdAsync(Guid id);
        Task<Driver?> GetByUserIdAsync(Guid userId);

        /// <summary>Drivers that can be assigned to a trip (not RELAX / Offline / Inactive / DELETED).</summary>
        Task<List<Driver>> GetAvailableAsync();

        /// <summary>Work logs for a driver between two calendar dates (inclusive).</summary>
        Task<List<DriverWorkLog>> GetWorkLogsAsync(Guid driverId, DateOnly fromDate, DateOnly toDate);
        Task AddAsync(Driver driver);
        Task AddLicenseAsync(DriverLicense license);
        Task UpdateAsync(Driver driver);
        Task DeleteAsync(Driver driver);
        Task SaveChangesAsync();
    }
}