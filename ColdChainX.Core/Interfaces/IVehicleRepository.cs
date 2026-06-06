using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ColdChainX.Core.Entities;

namespace ColdChainX.Core.Interfaces
{
    public interface IVehicleRepository
    {
        Task<List<Vehicle>> GetAllAsync();
        Task<Vehicle?> GetByIdAsync(Guid id);
        Task<Vehicle?> GetByTruckPlateAsync(string truckPlate);
        Task<Vehicle?> GetByChassisNumberAsync(string chassisNumber);
        Task<Vehicle?> GetByEngineNumberAsync(string engineNumber);
        Task AddAsync(Vehicle vehicle);
        Task UpdateAsync(Vehicle vehicle);
        Task DeleteAsync(Vehicle vehicle);
        Task SaveChangesAsync();
    }
}