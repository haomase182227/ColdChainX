using FluentValidation;
using ColdChainX.Application.DTOs.Outbound;

namespace ColdChainX.Application.Validators
{
    public class UpdateOutboundOrderRequestValidator : AbstractValidator<UpdateOutboundOrderRequest>
    {
        public UpdateOutboundOrderRequestValidator()
        {
            RuleFor(x => x.ReceiverName).NotEmpty().WithMessage("Receiver name is required.");
            RuleFor(x => x.ReceiverPhone).NotEmpty().WithMessage("Receiver phone is required.");
            RuleFor(x => x.DestinationAddress).NotEmpty().WithMessage("Destination address is required.");
            RuleFor(x => x.Items).NotEmpty().WithMessage("Outbound order items list cannot be empty.");
            
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ItemCode).NotEmpty().WithMessage("Item code is required.");
                item.RuleFor(i => i.ItemName).NotEmpty().WithMessage("Item name is required.");
                item.RuleFor(i => i.Unit).NotEmpty().WithMessage("Unit is required.");
                item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
            });
        }
    }
}
