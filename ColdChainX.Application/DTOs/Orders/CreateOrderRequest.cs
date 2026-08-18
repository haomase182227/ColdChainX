using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.Application.DTOs.Orders
{
    public class CreateOrderRequest
    {
        /// <summary>
        /// Target customer for an internal user creating an order on behalf of a customer.
        /// Customer tokens always use their own CustomerId claim and ignore this value.
        /// </summary>
        [FromForm(Name = "Customer_ID")]
        public Guid? CustomerId { get; set; }

        [FromForm(Name = "Item_Name")]
        public string ItemName { get; set; } = null!;

        [FromForm(Name = "Category")]
        public string Category { get; set; } = null!;

        [FromForm(Name = "Temp_Condition")]
        public decimal TempCondition { get; set; }

        [FromForm(Name = "Expected_Weight_KG")]
        public decimal ExpectedWeightKg { get; set; }

        [FromForm(Name = "Quantity")]
        public int Quantity { get; set; } = 1;

        [FromForm(Name = "Packaging_Type")]
        public string PackagingType { get; set; } = null!;

        [FromForm(Name = "Length_CM")]
        public decimal LengthCm { get; set; }

        [FromForm(Name = "Width_CM")]
        public decimal WidthCm { get; set; }

        [FromForm(Name = "Height_CM")]
        public decimal HeightCm { get; set; }

        [FromForm(Name = "Dest_Address_Text")]
        public string DestAddressText { get; set; } = null!;

        [FromForm(Name = "Schedule_ID")]
        public Guid ScheduleId { get; set; }

        [FromForm(Name = "Dropoff_Stop_ID")]
        public Guid DropoffStopId { get; set; }

        [FromForm(Name = "Has_Strong_Odor")]
        public bool HasStrongOdor { get; set; } = false;

        [FromForm(Name = "Is_Stackable")]
        public bool IsStackable { get; set; } = true;

        /// <summary>
        /// Package sizes for the same item. Indexed multipart keys use the
        /// PackageVariants[0].PropertyName convention.
        /// </summary>
        public List<CreateOrderPackageVariantRequest> PackageVariants { get; set; } = new();

        // Legacy order-level files remain supported for existing clients.
        [FromForm(Name = "Legal_Documents")]
        public List<IFormFile> LegalDocuments { get; set; } = new();

        [FromForm(Name = "Cargo_Photos")]
        public List<IFormFile> CargoPhotos { get; set; } = new();
    }
}

