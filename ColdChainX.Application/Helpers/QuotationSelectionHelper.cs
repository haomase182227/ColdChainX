using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Helpers;

public static class QuotationSelectionHelper
{
    public static Quotation? SelectBillingQuotation(IEnumerable<Quotation> quotations)
    {
        return quotations
            .Where(quotation => quotation.FinalAmount > 0m && IsFinalActual(quotation))
            .OrderByDescending(quotation => quotation.CreatedAt)
            .FirstOrDefault();
    }

    public static bool IsFinalActual(Quotation quotation)
        => string.Equals(quotation.Status, "FINAL", StringComparison.OrdinalIgnoreCase)
           && string.Equals(quotation.PricingSource, "AUTO_ACTUAL", StringComparison.OrdinalIgnoreCase);
}
