using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ColdChainX.Application.Interfaces;
using ColdChainX.Application.DTOs.Delivery;
using ColdChainX.Shared.Responses;
using ColdChainX.Shared.Exceptions;

namespace ColdChainX.Application.Features.Delivery.Queries;

public class GetTripOrderCustomersQuery : IRequest<ApiResponse<TripOrderCustomersResponse>>
{
    public Guid TripId { get; set; }
}

public class GetTripOrderCustomersQueryHandler : IRequestHandler<GetTripOrderCustomersQuery, ApiResponse<TripOrderCustomersResponse>>
{
    private readonly IApplicationDbContext _context;

    public GetTripOrderCustomersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ApiResponse<TripOrderCustomersResponse>> Handle(GetTripOrderCustomersQuery request, CancellationToken cancellationToken)
    {
        var trip = await _context.MasterTrips
            .Include(t => t.Vehicle)
                .ThenInclude(v => v!.IotDevices)
            .Include(t => t.TripDrivers)
                .ThenInclude(td => td.Driver)
            .FirstOrDefaultAsync(t => t.TripId == request.TripId, cancellationToken)
            ?? throw new NotFoundException($"Chuyến xe với ID '{request.TripId}' không tồn tại trên hệ thống.");

        string tripCodeStr = $"TRIP-{trip.TripId.ToString()[..8].ToUpper()}";
        string vehiclePlate = trip.Vehicle?.TruckPlate ?? "N/A";

        TripVehicleSummaryItem? vehicleSummary = null;
        if (trip.Vehicle != null)
        {
            var iotDevices = trip.Vehicle.IotDevices?
                .Select(d => new TripIotDeviceSummaryItem
                {
                    DeviceId = d.DeviceId,
                    DeviceCode = d.DeviceCode ?? string.Empty,
                    BatteryLevel = d.BatteryLevel,
                    IsOnline = d.IsOnline,
                    LastPingTime = d.LastPingTime,
                    Status = d.Status ?? string.Empty
                }).ToList() ?? new List<TripIotDeviceSummaryItem>();

            vehicleSummary = new TripVehicleSummaryItem
            {
                VehicleId = trip.Vehicle.VehicleId,
                TruckPlate = trip.Vehicle.TruckPlate ?? string.Empty,
                Brand = trip.Vehicle.Brand ?? string.Empty,
                ManufactureYear = trip.Vehicle.ManufactureYear,
                VehicleType = trip.Vehicle.VehicleType ?? string.Empty,
                MaxWeight = trip.Vehicle.MaxWeight,
                MaxCbm = trip.Vehicle.MaxCbm,
                MinTemp = trip.Vehicle.MinTemp,
                MaxTemp = trip.Vehicle.MaxTemp,
                CurrentLocation = trip.Vehicle.CurrentLocation ?? string.Empty,
                Status = trip.Vehicle.Status ?? string.Empty,
                IotDevices = iotDevices
            };
        }

        var drivers = trip.TripDrivers?
            .Where(td => td.Driver != null)
            .OrderBy(td => td.DriverRole != "PRIMARY") // Lái chính (PRIMARY) xếp trước, lái phụ phía sau
            .Select(td => new TripDriverSummaryItem
            {
                DriverId = td.DriverId,
                FullName = td.Driver.FullName ?? string.Empty,
                PhoneNumber = td.Driver.PhoneNumber ?? string.Empty,
                IdentityNumber = td.Driver.IdentityNumber ?? string.Empty,
                DriverRole = td.DriverRole ?? "PRIMARY",
                AssignedDurationHours = td.AssignedDurationHours,
                Status = td.Driver.Status ?? string.Empty,
                CurrentLocation = td.Driver.CurrentLocation ?? string.Empty
            }).ToList() ?? new List<TripDriverSummaryItem>();

        var primaryDriver = drivers.FirstOrDefault();
        string driverName = primaryDriver?.FullName ?? "Chưa chỉ định";
        string driverPhone = primaryDriver?.PhoneNumber ?? "N/A";

        var orders = await _context.TransportOrders
            .Include(o => o.Customer)
            .Include(o => o.PickupLocationNavigation)
            .Include(o => o.DestLocationNavigation)
            .Where(o => o.MasterTripId == trip.TripId)
            .ToListAsync(cancellationToken);

        var items = orders.Select(o => new TripOrderCustomerItem
        {
            OrderId = o.OrderId,
            TrackingCode = o.TrackingCode ?? string.Empty,
            OrderStatus = o.Status ?? string.Empty,
            ItemName = o.ItemName ?? string.Empty,
            PickupAddress = o.PickupLocationNavigation?.Address ?? string.Empty,
            DestAddress = o.DestLocationNavigation?.Address ?? string.Empty,
            ReceiverName = o.ReceiverName ?? string.Empty,
            ReceiverPhone = o.ReceiverPhone ?? string.Empty,

            CustomerId = o.CustomerId,
            CompanyName = o.Customer?.CompanyName ?? "N/A",
            TaxCode = o.Customer?.TaxCode ?? string.Empty,
            Address = o.Customer?.Address ?? string.Empty,
            Email = o.Customer?.Email ?? string.Empty,
            PaymentTerm = o.Customer?.PaymentTerm,
            CustomerStatus = o.Customer?.Status ?? string.Empty
        }).ToList();

        var response = new TripOrderCustomersResponse
        {
            TripId = trip.TripId,
            TripCode = tripCodeStr,
            Status = trip.Status ?? string.Empty,
            VehiclePlate = vehiclePlate,
            DriverName = driverName,
            DriverPhone = driverPhone,
            TotalOrders = items.Count,
            TotalCustomers = items.Where(i => i.CustomerId.HasValue).Select(i => i.CustomerId.Value).Distinct().Count(),
            Vehicle = vehicleSummary,
            Drivers = drivers,
            Orders = items
        };

        return ApiResponse<TripOrderCustomersResponse>.SuccessResponse(response, "Tra cứu thông tin phương tiện, thiết bị IoT, tài xế, khách hàng và đơn hàng trong chuyến xe thành công.");
    }
}
