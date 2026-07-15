using System;
using System.Collections.Generic;

namespace ColdChainX.Core.Entities;

public partial class Quotation
{
    public Guid QuoteId { get; set; }

    public Guid? OrderId { get; set; }

    public decimal BaseFreight { get; set; }

    public decimal? LastMileSurcharge { get; set; }

    public decimal? VasAmount { get; set; }

    public decimal? VatPercentage { get; set; }

    public decimal VatAmount { get; set; }

    public decimal FinalAmount { get; set; }

    public decimal? ChargeableWeightKg { get; set; }

    public decimal? VolumetricWeightKg { get; set; }

    public decimal? PricePerKg { get; set; }

    public decimal? DistanceKm { get; set; }

    public decimal? SystemBaseFreight { get; set; }

    public decimal? ManualAdjustment { get; set; }

    public string? AdditionalCharges { get; set; }

    public string? MandatoryCharges { get; set; }

    public string? OptionalServicesMenu { get; set; }

    public string? OverrideReason { get; set; }

    public string PricingSource { get; set; } = null!;

    public string? FileUrl { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual TransportOrder? Order { get; set; }
}
