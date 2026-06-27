using System;
using System.ComponentModel.DataAnnotations;

namespace ColdChainX.Application.DTOs.Delivery;

public class DepartRequest
{
    [Required]
    public Guid StopId { get; set; }

    public string? NewSealCode { get; set; }
}
