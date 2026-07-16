using System;
using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs.Incident
{
    public class CreateIncidentRequest
    {
        public Guid? TripId { get; set; }
        public IncidentType? IncidentType { get; set; }
        public IncidentSeverity? Severity { get; set; }
        public string Description { get; set; } = null!;
        public decimal? CurrentLatitude { get; set; }
        public decimal? CurrentLongitude { get; set; }
        public decimal DriverPaidAmount { get; set; }
    }
}
