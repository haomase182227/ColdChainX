using ColdChainX.Application.DTOs.Orders;
using FluentValidation;

namespace ColdChainX.Application.Validators
{
    public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
    {
        public static readonly string[] AllowedCategories =
        [
            "MEAT_SEAFOOD",
            "FROZEN_FRUITS_VEGGIES",
            "ICE_CREAM_BEVERAGES",
            "PHARMACEUTICALS",
            "RAW_MATERIALS_OTHERS"
        ];

        public static readonly string[] AllowedPackagingTypes =
        [
            "Pallet",
            "Thùng",
            "Bao",
            "Plastic Box",
            "Foam Box",
            "Carton Box"
        ];

        private const long MaxDocumentSizeBytes = 10 * 1024 * 1024;

        public CreateOrderRequestValidator()
        {
            RuleFor(x => x.ItemName)
                .NotEmpty().WithMessage("Item_Name is required")
                .MaximumLength(255).WithMessage("Item_Name must not exceed 255 characters");

            RuleFor(x => x.Category)
                .NotEmpty().WithMessage("Category is required")
                .Must(value => AllowedCategories.Contains(value))
                .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}");

            RuleFor(x => x.TempCondition)
                .InclusiveBetween(-18m, -5m)
                .WithMessage("Temp_Condition must be between -18 and -5 Celsius");

            RuleFor(x => x.ExpectedWeightKg)
                .GreaterThan(0).WithMessage("Expected_Weight_KG must be greater than 0");

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than 0");

            RuleFor(x => x.PackagingType)
                .NotEmpty().WithMessage("Packaging_Type is required")
                .Must(value => AllowedPackagingTypes.Contains(value))
                .WithMessage($"Packaging_Type must be one of: {string.Join(", ", AllowedPackagingTypes)}");

            RuleFor(x => x.LengthCm)
                .GreaterThan(0).WithMessage("Length_CM must be greater than 0");

            RuleFor(x => x.WidthCm)
                .GreaterThan(0).WithMessage("Width_CM must be greater than 0");

            RuleFor(x => x.HeightCm)
                .GreaterThan(0).WithMessage("Height_CM must be greater than 0");

            RuleFor(x => x.DestAddressText)
                .NotEmpty().WithMessage("Dest_Address_Text is required")
                .MaximumLength(500).WithMessage("Dest_Address_Text must not exceed 500 characters");

            RuleFor(x => x.CustomerId)
                .NotEmpty().WithMessage("Customer_ID is required");

            RuleFor(x => x.DocumentImage)
                .NotNull().WithMessage("DocumentImage is required")
                .Must(file => file != null && file.Length > 0)
                .WithMessage("DocumentImage must not be empty")
                .Must(file => file == null || file.Length <= MaxDocumentSizeBytes)
                .WithMessage("DocumentImage must be smaller than 10MB");
        }
    }
}
