namespace ColdChainX.Application.DTOs.Delivery;

public class CodHandoverResponse
{
    public decimal ExpectedCash { get; set; }
    public decimal ActualCash { get; set; }
    public decimal ExpectedQr { get; set; }
    public decimal ActualQr { get; set; }
    public decimal CashDiscrepancy { get; set; }
    public decimal QrDiscrepancy { get; set; }
    public string Status { get; set; } = null!;
}
