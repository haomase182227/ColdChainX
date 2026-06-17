using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ColdChainX.Application.DTOs.Orders
{
    public class CreateOrderRequest
    {
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

        [FromForm(Name = "Route_ID")]
        public Guid RouteId { get; set; }

        [FromForm(Name = "DocumentImage")]
        public IFormFile DocumentImage { get; set; } = null!;
    }
}
