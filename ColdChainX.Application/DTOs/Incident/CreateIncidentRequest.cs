using System;

namespace ColdChainX.Application.DTOs.Incident
{
    public class CreateIncidentRequest
    {
        public Guid? TripId { get; set; }
        public string IncidentType { get; set; } = null!;
        public string Severity { get; set; } = null!;
        public string Description { get; set; } = null!;
        public decimal? CurrentLatitude { get; set; }
        public decimal? CurrentLongitude { get; set; }
    }
}
