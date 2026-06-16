using System;
using FluentValidation;
using ColdChainX.Application.DTOs.WarehouseReceipt;

namespace ColdChainX.Application.Validators
{
    public class UpdateMeasurementsRequestValidator : AbstractValidator<UpdateMeasurementsRequest>
    {
        public UpdateMeasurementsRequestValidator()
        {
            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ItemName).NotEmpty().WithMessage("Item name is required");
                item.RuleFor(i => i.ActualQty).GreaterThan(0).WithMessage("Actual quantity must be greater than zero");

                // If batch number is specified, expiry date is required
                item.RuleFor(i => i.ExpiryDate)
                    .NotEmpty()
                    .When(i => !string.IsNullOrWhiteSpace(i.BatchNumber))
                    .WithMessage("Expiry date is required when batch number is specified");

                // Expiry date must be in the future
                item.RuleFor(i => i.ExpiryDate)
                    .Must(date => !date.HasValue || date.Value > DateOnly.FromDateTime(DateTime.Today))
                    .WithMessage("Expiry date must be in the future");

                // Manufactured date must not be in the future
                item.RuleFor(i => i.ManufacturedDate)
                    .Must(date => !date.HasValue || date.Value <= DateOnly.FromDateTime(DateTime.Today))
                    .WithMessage("Manufactured date cannot be in the future");

                // Expiry date must be after manufactured date
                item.RuleFor(i => i.ExpiryDate)
                    .Must((inst, expDate) => !inst.ManufacturedDate.HasValue || !expDate.HasValue || expDate.Value > inst.ManufacturedDate.Value)
                    .WithMessage("Expiry date must be after manufactured date");

                item.RuleFor(i => i.CountryOfOrigin)
                    .NotEmpty().WithMessage("Country of origin is required")
                    .MaximumLength(100).WithMessage("Country of origin cannot exceed 100 characters");

                item.RuleFor(i => i.ProductCategory)
                    .IsInEnum().WithMessage("Invalid product category");
            });
        }
    }
}
