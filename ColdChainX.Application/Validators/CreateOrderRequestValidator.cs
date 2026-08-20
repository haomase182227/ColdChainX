using ColdChainX.Application.DTOs.Orders;
using FluentValidation;
using System;
using System.Linq;

namespace ColdChainX.Application.Validators
{
    public class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
    {
        public static readonly string[] AllowedCategories =
        [
            "MEAT_SEAFOOD",
            "FRUITS_VEGGIES",
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
                .NotNull().WithMessage("Temp_Condition is required")
                .InclusiveBetween(-18m, 5m)
                .WithMessage("Temp_Condition must be between -18 and 5 Celsius");

            RuleFor(x => x.ExpectedWeightKg)
                .NotNull()
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson))
                .WithMessage("Expected_Weight_KG is required when Package_Lines is not provided")
                .GreaterThan(0)
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson))
                .WithMessage("Expected_Weight_KG must be greater than 0 when Package_Lines is not provided");

            RuleFor(x => x.Quantity)
                .NotNull()
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson))
                .WithMessage("Quantity is required when Package_Lines is not provided")
                .GreaterThan(0)
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson))
                .WithMessage("Quantity must be greater than 0 when Package_Lines is not provided");

            RuleFor(x => x.PackagingType)
                .NotEmpty().WithMessage("Packaging_Type is required")
                .Must(ContainsOnlyAllowedPackagingTypes)
                .WithMessage(request => BuildPackagingTypeErrorMessage(request.PackagingType));

            RuleFor(x => x.LengthCm)
                .NotNull()
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson) && !x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Length_CM is required when Package_Lines and Customer_Provided_Total_CBM are not provided")
                .GreaterThan(0)
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson) && !x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Length_CM must be greater than 0 when Package_Lines and Customer_Provided_Total_CBM are not provided");

            RuleFor(x => x.WidthCm)
                .NotNull()
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson) && !x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Width_CM is required when Package_Lines and Customer_Provided_Total_CBM are not provided")
                .GreaterThan(0)
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson) && !x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Width_CM must be greater than 0 when Package_Lines and Customer_Provided_Total_CBM are not provided");

            RuleFor(x => x.HeightCm)
                .NotNull()
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson) && !x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Height_CM is required when Package_Lines and Customer_Provided_Total_CBM are not provided")
                .GreaterThan(0)
                .When(x => string.IsNullOrWhiteSpace(x.PackageLinesJson) && !x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Height_CM must be greater than 0 when Package_Lines and Customer_Provided_Total_CBM are not provided");

            RuleFor(x => x.CustomerProvidedTotalCbm)
                .GreaterThan(0)
                .When(x => x.CustomerProvidedTotalCbm.HasValue)
                .WithMessage("Customer_Provided_Total_CBM must be greater than 0");

            RuleFor(x => x.DestAddressText)
                .NotEmpty().WithMessage("Dest_Address_Text is required")
                .MaximumLength(500).WithMessage("Dest_Address_Text must not exceed 500 characters");

            RuleFor(x => x.ScheduleId)
                .Must(id => id.HasValue && id.Value != Guid.Empty)
                .WithMessage("Schedule_ID is required");

            RuleFor(x => x.DropoffStopId)
                .Must(id => id.HasValue && id.Value != Guid.Empty)
                .WithMessage("Dropoff_Stop_ID is required");

            RuleFor(x => x.ReceiverName)
                .NotEmpty().WithMessage("Receiver_Name is required")
                .MaximumLength(100).WithMessage("Receiver_Name must not exceed 100 characters");

            RuleFor(x => x.ReceiverPhone)
                .NotEmpty().WithMessage("Receiver_Phone is required")
                .MaximumLength(20).WithMessage("Receiver_Phone must not exceed 20 characters")
                .Must(BeAValidPhoneNumber).WithMessage("Receiver_Phone must contain between 8 and 15 digits");

            RuleFor(x => x.HasStrongOdor)
                .NotNull().WithMessage("Has_Strong_Odor is required");

            RuleFor(x => x.IsStackable)
                .NotNull().WithMessage("Is_Stackable is required");

            RuleFor(x => x.LegalDocuments)
                .NotEmpty().WithMessage("At least one Legal_Documents file is required");

            RuleForEach(x => x.LegalDocuments)
                .Must(file => file != null && file.Length > 0)
                .WithMessage("Legal_Documents files must not be empty")
                .Must(file => file == null || file.Length <= MaxDocumentSizeBytes)
                .WithMessage("Legal_Documents files must not exceed 10 MB");

            RuleFor(x => x.CargoPhotos)
                .NotEmpty().WithMessage("At least one Cargo_Photos file is required");

            RuleForEach(x => x.CargoPhotos)
                .Must(file => file != null && file.Length > 0)
                .WithMessage("Cargo_Photos files must not be empty")
                .Must(file => file == null || file.Length <= MaxDocumentSizeBytes)
                .WithMessage("Cargo_Photos files must not exceed 10 MB");

        }

        private static bool BeAValidPhoneNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var digitCount = value.Count(char.IsDigit);
            return digitCount is >= 8 and <= 15
                && value.All(character => char.IsDigit(character)
                    || character is '+' or ' ' or '-' or '(' or ')');
        }

        private static bool ContainsOnlyAllowedPackagingTypes(string? value)
        {
            var packagingTypes = SplitPackagingTypes(value);

            return packagingTypes.Length > 0 && packagingTypes.All(packagingType => AllowedPackagingTypes.Contains(packagingType));
        }

        private static string BuildPackagingTypeErrorMessage(string? value)
        {
            var invalidValue = SplitPackagingTypes(value)
                .FirstOrDefault(packagingType => !AllowedPackagingTypes.Contains(packagingType));

            if (!string.IsNullOrWhiteSpace(invalidValue))
                return $"Packaging_Type contains invalid value: {invalidValue}. Allowed values: {string.Join(", ", AllowedPackagingTypes)}";

            return $"Packaging_Type must be one of: {string.Join(", ", AllowedPackagingTypes)}";
        }

        private static string[] SplitPackagingTypes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return [];

            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(packagingType => !string.IsNullOrWhiteSpace(packagingType))
                .ToArray();
        }
    }
}

