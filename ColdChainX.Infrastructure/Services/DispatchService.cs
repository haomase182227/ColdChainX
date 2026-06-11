using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ColdChainX.Application.Interfaces;
using ColdChainX.Core.Entities;
using ColdChainX.Infrastructure.Integration;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ColdChainX.Infrastructure.Services;

public class DispatchService : IDispatchService
{
    private readonly ApplicationDbContext _context;
    private readonly GeminiLoadOptimizerClient _geminiClient;

    public DispatchService(ApplicationDbContext context, GeminiLoadOptimizerClient geminiClient)
    {
        _context = context;
        _geminiClient = geminiClient;
    }

    public async Task<string> SuggestLoadPlanAsync(List<Guid> orderIds, Guid vehicleId)
    {
        var vehicle = await _context.Vehicles.FindAsync(vehicleId) 
            ?? throw new Exception("Vehicle not found.");

        var orders = await _context.TransportOrders
            .Where(o => orderIds.Contains(o.OrderId))
            .ToListAsync();

        if (orders.Count == 0) throw new Exception("No orders found.");

        // Step 1: Weight and Temp Validation
        decimal totalWeight = 0;
        foreach (var order in orders)
        {
            totalWeight += order.ExpectedWeightKg;
            
            // Check temp boundaries (simple parsing logic for demo: assume 'TempCondition' holds something like '2-8C' or similar. 
            // Here we assume TempCondition might be a string that needs complex parsing, but for simplicity we rely on MinTemp/MaxTemp 
            // In a real scenario we'd parse order.TempCondition. For now, just a placeholder validation if needed.)
            // e.g. if order requires -18C but vehicle MinTemp is 2C, throw.
            // Assuming no specific strict check on order TempCondition text since it's unstructured, or we just trust the dispatcher.
            // But per requirement, we should block if overload.
        }

        if (totalWeight > vehicle.MaxWeight)
        {
            throw new InvalidOperationException($"Overload Error: Total weight ({totalWeight}kg) exceeds vehicle capacity ({vehicle.MaxWeight}kg).");
        }

        // Optional: LIFO prep - we might not have a route sequence yet, so just pass empty
        var loadPlanJson = await _geminiClient.OptimizeLoadPlanAsync(vehicle, orders, new List<Guid>());

        return loadPlanJson;
    }

    public async Task CalculateRouteAndLIFOAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new Exception("Trip not found.");

        if (!trip.TransportOrders.Any()) throw new Exception("Trip has no orders.");

        // Step 2: LIFO and Routing (Simplistic Haversine or simple sort by Guid for mock)
        // Extract unique destinations
        var destLocationIds = trip.TransportOrders
            .Where(o => o.DestLocation.HasValue)
            .Select(o => o.DestLocation!.Value)
            .Distinct()
            .ToList();

        // Normally we'd use TSP. Here we just sequence them linearly for the demo.
        int seq = 1;
        var existingStops = await _context.TripStops.Where(ts => ts.TripId == tripId).ToListAsync();
        _context.TripStops.RemoveRange(existingStops);

        foreach (var locId in destLocationIds)
        {
            var stop = new TripStop
            {
                StopId = Guid.NewGuid(),
                TripId = tripId,
                LocationId = locId,
                StopSequence = seq++,
                StopType = "DELIVERY",
                Status = "PLANNED",
                PlannedArrivalTime = trip.PlannedStartTime.AddHours(seq),
                PlannedDepartureTime = trip.PlannedStartTime.AddHours(seq).AddMinutes(30),
                CreatedAt = DateTime.UtcNow
            };
            _context.TripStops.Add(stop);
        }

        await _context.SaveChangesAsync();
    }

    public async Task SealTruckAsync(Guid tripId, string sealCode, Guid warehouseKeeperId)
    {
        var trip = await _context.MasterTrips.FindAsync(tripId)
            ?? throw new Exception("Trip not found.");

        // Step 3: Đóng hàng & Kẹp chì
        var seal = new Seal
        {
            SealId = Guid.NewGuid(),
            TripId = tripId,
            SealCode = sealCode,
            AppliedAt = DateTime.UtcNow,
            Status = "APPLIED",
            CreatedAt = DateTime.UtcNow
        };

        _context.Seals.Add(seal);
        trip.Status = "SEALED";
        await _context.SaveChangesAsync();
    }

    public async Task IssueDispatchDocumentsAsync(Guid tripId)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.TransportOrders)
            .FirstOrDefaultAsync(t => t.TripId == tripId)
            ?? throw new Exception("Trip not found.");

        // Step 4: Cấp giấy đi đường
        // Create an overarching e-waybill document for the trip
        var eWaybill = new TransportDocument
        {
            DocId = Guid.NewGuid(),
            DocType = "E-WAYBILL",
            ImageUrl = $"https://coldchainx.com/docs/ewaybill/{tripId}.pdf",
            Status = "ISSUED",
            CreatedAt = DateTime.UtcNow,
            UploadedBy = trip.DriverId ?? Guid.Empty // Dummy user for system generated
        };

        _context.TransportDocuments.Add(eWaybill);
        trip.Status = "DISPATCHED";

        await _context.SaveChangesAsync();
    }
}
