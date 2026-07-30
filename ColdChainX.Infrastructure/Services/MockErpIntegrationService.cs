using System;
using System.Threading;
using System.Threading.Tasks;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services;

public class MockErpIntegrationService : IErpIntegrationService
{
    public Task<ErpInventorySyncResponse> DeductInventoryAsync(Guid orderId, string itemCode, int quantity, CancellationToken cancellationToken = default)
    {
        // Giả lập tích hợp với MISA / SAP ERP: Tự động trừ tồn kho khi giao nhận/trả hàng hoàn tất
        var response = new ErpInventorySyncResponse
        {
            IsSuccess = true,
            TransactionId = $"ERP-DEDUCT-{orderId.ToString().Substring(0, 8).ToUpper()}-{DateTime.UtcNow:HHmmss}",
            ErpSystem = "MISA/SAP ERP (Mock Integration)",
            Message = $"Đã xác thực đồng bộ và trừ {quantity} đơn vị ({itemCode}) khỏi kho chính trong hệ thống ERP.",
            SyncedAt = DateTime.UtcNow
        };

        return Task.FromResult(response);
    }

    public Task<ErpInvoiceResponse> GenerateVatInvoiceAsync(Guid orderId, decimal totalAmount, string customerName, CancellationToken cancellationToken = default)
    {
        // Giả lập phát hành hóa đơn giá trị gia tăng điện tử (VAT Electronic Invoice)
        var randomCode = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();
        var invoiceNo = $"VAT-{DateTime.UtcNow:yyyy}-{randomCode.Substring(0, 6)}";

        var response = new ErpInvoiceResponse
        {
            IsSuccess = true,
            InvoiceNo = invoiceNo,
            LookupCode = $"LOOKUP-{randomCode}",
            ElectronicInvoiceUrl = $"https://einvoice.coldchainx.vn/lookup?code={randomCode}&order={orderId}",
            IssuedAt = DateTime.UtcNow
        };

        return Task.FromResult(response);
    }
}
