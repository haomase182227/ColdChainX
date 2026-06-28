using AutoMapper;
using ColdChainX.Application.DTOs;
using ColdChainX.Application.DTOs.Dispatch;
using ColdChainX.Application.Features.Inventory.DTOs;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Mappings
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<User, AuthResponseDto>()
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId))
                .ForMember(d => d.Role, o => o.MapFrom(s => s.Role != null ? s.Role.RoleName : null));

            CreateMap<User, UserProfileDto>()
                .ForMember(d => d.UserId, o => o.MapFrom(s => s.UserId))
                .ForMember(d => d.Role, o => o.MapFrom(s => s.Role != null ? s.Role.RoleName : null));

            // Dispatch — Vehicle + TripDriver architecture.
            // (DispatchService uses manual projections; these maps document the shape and
            //  are available for any consumer that prefers AutoMapper.)
            CreateMap<Driver, DriverInfo>()
                .ForMember(d => d.LicenseClass, o => o.Ignore())
                .ForMember(d => d.LicenseExpiry, o => o.Ignore())
                .ForMember(d => d.LicenseStatus, o => o.Ignore())
                .ForMember(d => d.DriverRole, o => o.Ignore())
                .ForMember(d => d.AssignedDurationHours, o => o.Ignore());

            CreateMap<TripDriver, DriverInfo>()
                .ForMember(d => d.DriverId, o => o.MapFrom(s => s.DriverId))
                .ForMember(d => d.FullName, o => o.MapFrom(s => s.Driver != null ? s.Driver.FullName : null))
                .ForMember(d => d.PhoneNumber, o => o.MapFrom(s => s.Driver != null ? s.Driver.PhoneNumber : null))
                .ForMember(d => d.IdentityNumber, o => o.MapFrom(s => s.Driver != null ? s.Driver.IdentityNumber : null))
                .ForMember(d => d.LicenseClass, o => o.Ignore())
                .ForMember(d => d.LicenseExpiry, o => o.Ignore())
                .ForMember(d => d.LicenseStatus, o => o.Ignore());

            CreateMap<Vehicle, VehicleInfo>()
                .ForMember(d => d.MaxWeightKg, o => o.MapFrom(s => s.MaxWeight))
                .ForMember(d => d.TotalOrderWeightKg, o => o.Ignore())
                .ForMember(d => d.TotalOrderCbm, o => o.Ignore())
                .ForMember(d => d.WeightUtilizationPct, o => o.Ignore())
                .ForMember(d => d.CbmUtilizationPct, o => o.Ignore());

            // LPN inventory projection — includes the warehouse the LPN was put away into.
            // (Queries use manual projections; this map documents the shape and is available
            //  for any consumer that prefers AutoMapper.)
            CreateMap<Lpn, LpnDto>()
                .ForMember(d => d.ItemName, o => o.MapFrom(s => s.Order != null ? s.Order.ItemName : null))
                .ForMember(d => d.ExpectedWeightKg, o => o.MapFrom(s => s.Order != null ? s.Order.ExpectedWeightKg : 0))
                .ForMember(d => d.WarehouseName, o => o.MapFrom(s => s.Warehouse != null ? s.Warehouse.WarehouseName : null))
                .ForMember(d => d.Condition, o => o.MapFrom(s => s.DiscrepancyReason))
                .ForMember(d => d.State, o => o.MapFrom(s => s.State.ToString()))
                .ForMember(d => d.BatchNumber, o => o.Ignore());
        }
    }
}
