using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Driver
{
    public Guid DriverId { get; set; }

    public DateOnly DateOfBirth { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<DriverLicense> DriverLicenses { get; set; } = new List<DriverLicense>();

    public virtual ICollection<ExpenseAdvance> ExpenseAdvances { get; set; } = new List<ExpenseAdvance>();

    public virtual ICollection<MasterTrip> MasterTrips { get; set; } = new List<MasterTrip>();
}
