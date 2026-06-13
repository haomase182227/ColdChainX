using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Application.Validators
{
    public class StockRelocationRequestValidator : AbstractValidator<StockRelocationRequest>
    {
        public StockRelocationRequestValidator()
        {
            RuleFor(x => x.SourceLocationId)
                .NotEmpty().WithMessage("Source location is required");

            RuleFor(x => x.DestinationLocationId)
                .NotEmpty().WithMessage("Destination location is required")
                .NotEqual(x => x.SourceLocationId).WithMessage("Source and destination locations cannot be identical");

            RuleFor(x => x.ItemCode)
                .NotEmpty().WithMessage("Item code is required");

            RuleFor(x => x.BatchId)
                .NotEmpty().WithMessage("Batch ID is required");

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("Relocation quantity must be greater than zero");

            RuleFor(x => x.Pallets)
                .GreaterThanOrEqualTo(0).WithMessage("Pallet count cannot be negative");
        }
    }
}
