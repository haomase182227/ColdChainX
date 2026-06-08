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
        public string TempCondition { get; set; } = null!;

        [FromForm(Name = "Expected_Weight_KG")]
        public decimal ExpectedWeightKg { get; set; }

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

        [FromForm(Name = "Customer_ID")]
        public Guid CustomerId { get; set; }

        [FromForm(Name = "Customer_User_ID")]
        public Guid? CustomerUserId { get; set; }

        [FromForm(Name = "DocumentImage")]
        public IFormFile DocumentImage { get; set; } = null!;
    }
}
