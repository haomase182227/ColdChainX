using System;
using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.WarehouseReceipt
{
    public class WarehouseReceiptResponse
    {
        public Guid ReceiptId { get; set; }
        public string ReceiptCode { get; set; } = null!;
        public string? ReferenceDocNo { get; set; }
        public Guid OrderId { get; set; }
        public string OrderTrackingCode { get; set; } = null!;
        public Guid WarehouseId { get; set; }
        public string WarehouseName { get; set; } = null!;
        public string ReceiptType { get; set; } = null!;
        public string? Reason { get; set; }
        public decimal? TotalExpectedQty { get; set; }
        public decimal? TotalActualQty { get; set; }
        public decimal? RecordedTemperature { get; set; }
        public string DelivererName { get; set; } = null!;
        public Guid ReceiverId { get; set; }
        public string? Note { get; set; }
        public string? PdfUrl { get; set; }
        public string? Status { get; set; }
        public string? WarningMessage { get; set; }
        public DateTime? CreatedAt { get; set; }
        public List<WarehouseReceiptItemDto> Items { get; set; } = new List<WarehouseReceiptItemDto>();
    }

    public class WarehouseReceiptItemDto
    {
        public Guid ItemId { get; set; }
        public string ItemName { get; set; } = null!;
        public string? ItemCode { get; set; }
        public string Unit { get; set; } = null!;
        public decimal ExpectedQty { get; set; }
        public decimal ActualQty { get; set; }
        public decimal? ActualWeightKg { get; set; }
        public decimal? LengthCm { get; set; }
        public decimal? WidthCm { get; set; }
        public decimal? HeightCm { get; set; }
        public string? Barcode { get; set; }
        public string? QrCode { get; set; }
        public string? ConditionStatus { get; set; }
        public string? Note { get; set; }
        public string? BatchNumber { get; set; }
        public DateOnly? ManufacturedDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }
}
