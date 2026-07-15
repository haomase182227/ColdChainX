using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Vehicle
{
    public Guid VehicleId { get; set; }

    public string TruckPlate { get; set; } = null!;

    public string? Brand { get; set; }

    public int? ManufactureYear { get; set; }

    public string? ChassisNumber { get; set; }

    public string? EngineNumber { get; set; }

    public decimal? StandardFuelLiters { get; set; }

    public string VehicleType { get; set; } = null!;

    public decimal MaxWeight { get; set; }

    public decimal MaxCbm { get; set; }

    public decimal? InnerLengthCm { get; set; }

    public decimal? InnerWidthCm { get; set; }

    public decimal? InnerHeightCm { get; set; }

    public decimal MinTemp { get; set; }

    public decimal MaxTemp { get; set; }

    public string? CurrentLocation { get; set; }

    public double CurrentOdometer { get; set; }

    public double NextMaintenanceOdometer { get; set; }

    public DateOnly? NextMaintenanceDate { get; set; }

    public int WarningDaysBeforeDue { get; set; } = 15;

    public double WarningKmBeforeDue { get; set; } = 500.0;

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<IotDevice> IotDevices { get; set; } = new List<IotDevice>();

    public virtual ICollection<MaintenanceTicket> MaintenanceTickets { get; set; } = new List<MaintenanceTicket>();

    public virtual ICollection<MasterTrip> MasterTrips { get; set; } = new List<MasterTrip>();

    public virtual ICollection<VehicleDocument> VehicleDocuments { get; set; } = new List<VehicleDocument>();
}
