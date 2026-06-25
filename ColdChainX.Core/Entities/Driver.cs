using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Driver
{
    public Guid DriverId { get; set; }

    public Guid? UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string IdentityNumber { get; set; } = null!;

    public string PhoneNumber { get; set; } = null!;

    public DateOnly DateOfBirth { get; set; }

    public DateOnly JoinDate { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual ICollection<DriverLicense> DriverLicenses { get; set; } = new List<DriverLicense>();

    public virtual ICollection<ExpenseAdvance> ExpenseAdvances { get; set; } = new List<ExpenseAdvance>();

    public virtual ICollection<TripDriver> TripDrivers { get; set; } = new List<TripDriver>();

    public virtual ICollection<DriverWorkLog> WorkLogs { get; set; } = new List<DriverWorkLog>();

    public virtual User? User { get; set; }
}
