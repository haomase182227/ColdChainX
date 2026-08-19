using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Delivery;

public class HandoverConfirmRequest
{
    public Guid TripId { get; set; }
    public Guid CustomerId { get; set; }

    public bool IsReceiverConfirmed { get; set; }

    [Required]
    public IFormFile SignatureFile { get; set; } = null!;

    [Required]
    public IFormFile HandoverPhotoFile { get; set; } = null!;
}
