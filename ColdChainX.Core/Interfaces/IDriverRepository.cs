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
        Task AddAsync(Driver driver);
        Task UpdateAsync(Driver driver);
        Task DeleteAsync(Driver driver);
        Task SaveChangesAsync();
    }
}