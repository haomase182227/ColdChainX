using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Application.Validators
{
    public class InboundQCRequestValidator : AbstractValidator<InboundQCRequest>
    {
        public InboundQCRequestValidator()
        {
            RuleFor(x => x.OrderId).NotEmpty().WithMessage("OrderId is required");
            RuleFor(x => x.WarehouseId).NotEmpty().WithMessage("WarehouseId is required");
            RuleFor(x => x.DelivererName).NotEmpty().WithMessage("DelivererName is required");
        }
    }
}
