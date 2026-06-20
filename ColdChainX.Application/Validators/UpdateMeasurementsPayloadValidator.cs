using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Application.Validators
{
    public class UpdateMeasurementsPayloadValidator : AbstractValidator<UpdateMeasurementsPayload>
    {
        public UpdateMeasurementsPayloadValidator()
        {
            RuleFor(x => x.WarehouseReceipt).NotNull().WithMessage("warehouse_receipt block is required");
            RuleFor(x => x.WarehouseReceipt)
                .SetValidator(new UpdateMeasurementsBlockValidator())
                .When(x => x.WarehouseReceipt != null);
        }
    }

    public class UpdateMeasurementsBlockValidator : AbstractValidator<UpdateMeasurementsBlock>
    {
        public UpdateMeasurementsBlockValidator()
        {
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ItemName).NotEmpty().WithMessage("Item name is required");
                item.RuleFor(i => i.ActualQty).GreaterThan(0).WithMessage("Actual quantity must be greater than zero");
                item.RuleFor(i => i.CountryOfOrigin).NotEmpty().WithMessage("Country of origin is required");
                item.RuleFor(i => i.ProductCategory).IsInEnum().WithMessage("Invalid product category");
            });
        }
    }
}
