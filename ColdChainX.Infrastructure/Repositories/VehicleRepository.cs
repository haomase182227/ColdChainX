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
    public class VehicleRepository : IVehicleRepository
    {
        private readonly ApplicationDbContext _db;

        public VehicleRepository(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<Vehicle>> GetAllAsync()
        {
            return await _db.Vehicles
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();
        }

        public async Task<Vehicle?> GetByIdAsync(Guid id)
        {
            return await _db.Vehicles.FirstOrDefaultAsync(x => x.VehicleId == id);
        }

        public async Task<Vehicle?> GetByTruckPlateAsync(string truckPlate)
        {
            var normalized = truckPlate.Trim().ToLowerInvariant();
            return await _db.Vehicles.FirstOrDefaultAsync(x => x.TruckPlate.ToLower() == normalized);
        }

        public async Task<Vehicle?> GetByChassisNumberAsync(string chassisNumber)
        {
            var normalized = chassisNumber.Trim().ToLowerInvariant();
            return await _db.Vehicles.FirstOrDefaultAsync(x => x.ChassisNumber != null && x.ChassisNumber.ToLower() == normalized);
        }

        public async Task<Vehicle?> GetByEngineNumberAsync(string engineNumber)
        {
            var normalized = engineNumber.Trim().ToLowerInvariant();
            return await _db.Vehicles.FirstOrDefaultAsync(x => x.EngineNumber != null && x.EngineNumber.ToLower() == normalized);
        }

        public async Task AddAsync(Vehicle vehicle)
        {
            await _db.Vehicles.AddAsync(vehicle);
        }

        public async Task UpdateAsync(Vehicle vehicle)
        {
            _db.Vehicles.Update(vehicle);
            await Task.CompletedTask;
        }

        public async Task DeleteAsync(Vehicle vehicle)
        {
            _db.Vehicles.Update(vehicle);
            await Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _db.SaveChangesAsync();
        }
    }
}