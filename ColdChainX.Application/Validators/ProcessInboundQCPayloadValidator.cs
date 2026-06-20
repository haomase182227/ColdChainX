using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Application.Validators
{
    public class ProcessInboundQCPayloadValidator : AbstractValidator<ProcessInboundQCPayload>
    {
        public ProcessInboundQCPayloadValidator()
        {
            RuleFor(x => x.WarehouseReceipt).NotNull().WithMessage("warehouse_receipt block is required");
            RuleFor(x => x.WarehouseReceipt)
                .SetValidator(new InboundQCBlockValidator())
                .When(x => x.WarehouseReceipt != null);
        }
    }

    public class InboundQCBlockValidator : AbstractValidator<InboundQCBlock>
    {
        public InboundQCBlockValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required");
            RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("WarehouseId is required");
            RuleFor(x => x.DelivererName).NotEmpty().WithMessage("DelivererName is required");
        }
    }
}
