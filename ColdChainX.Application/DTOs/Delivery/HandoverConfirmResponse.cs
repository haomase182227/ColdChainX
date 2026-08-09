using System;

namespace ColdChainX.Application.DTOs.Delivery;

public class HandoverConfirmResponse
{
    public Guid EpodId { get; set; }

    public DateTime HandoverConfirmedAt { get; set; }

    public string OrderStatus { get; set; } = null!;

    public decimal CodAmountDue { get; set; }

    public string HandoverPdfUrl { get; set; } = null!;

    public string NextStep { get; set; } = null!;
}
