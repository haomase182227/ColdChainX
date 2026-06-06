using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Location
{
    public Guid LocationId { get; set; }

    public Guid? CustomerId { get; set; }

    public string LocationName { get; set; } = null!;

    public string Address { get; set; } = null!;

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<GeoFence> GeoFences { get; set; } = new List<GeoFence>();

    public virtual ICollection<MasterTrip> MasterTripDestinationLocations { get; set; } = new List<MasterTrip>();

    public virtual ICollection<MasterTrip> MasterTripOriginLocations { get; set; } = new List<MasterTrip>();

    public virtual ICollection<TransportOrder> TransportOrderDestLocationNavigations { get; set; } = new List<TransportOrder>();

    public virtual ICollection<TransportOrder> TransportOrderPickupLocationNavigations { get; set; } = new List<TransportOrder>();

    public virtual ICollection<TripStop> TripStops { get; set; } = new List<TripStop>();
}
