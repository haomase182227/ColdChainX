using System;
using System.Text;
using ColdChainX.Core.Entities;

namespace ColdChainX.Application.Templates
{
    public static class InvoiceHtmlTemplate
    {
        public static string GenerateHtml(Invoice invoice)
        {
            var sb = new StringBuilder();

            string statusColor = invoice.Status == "PAID" ? "#28a745" : "#dc3545";
            string statusText = invoice.Status == "PAID" ? "ĐÃ THANH TOÁN" : "CHƯA THANH TOÁN";
            string vatHtml = string.IsNullOrEmpty(invoice.VatInvoiceNo) ? "" : $"<p><strong>Số HĐ GTGT:</strong> {invoice.VatInvoiceNo}</p>";
            string customerName = invoice.Customer?.CompanyName ?? "Khách lẻ";
            string taxCode = invoice.Customer?.TaxCode ?? "N/A";
            string address = invoice.Customer?.Address ?? "N/A";

            sb.Append($@"
<!DOCTYPE html>
<html lang=""vi"">
<head>
    <meta charset=""UTF-8"">
    <title>Hóa đơn - {invoice.InvoiceCode}</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            color: #333;
            background-color: #f9f9f9;
            margin: 0;
            padding: 40px;
        }}
        .invoice-box {{
            max-width: 800px;
            margin: auto;
            padding: 30px;
            border: 1px solid #eee;
            box-shadow: 0 0 10px rgba(0, 0, 0, 0.15);
            background-color: #fff;
            border-radius: 8px;
        }}
        .header {{
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            border-bottom: 2px solid #0056b3;
            padding-bottom: 20px;
            margin-bottom: 20px;
        }}
        .header .company-details h1 {{
            color: #0056b3;
            margin: 0 0 5px 0;
            font-size: 28px;
        }}
        .header .company-details p {{
            margin: 2px 0;
            color: #666;
            font-size: 14px;
        }}
        .header .invoice-details {{
            text-align: right;
        }}
        .header .invoice-details h2 {{
            margin: 0 0 10px 0;
            font-size: 24px;
            color: #333;
        }}
        .status-badge {{
            display: inline-block;
            padding: 5px 15px;
            border-radius: 20px;
            color: white;
            font-weight: bold;
            font-size: 14px;
            background-color: {statusColor};
            margin-bottom: 10px;
        }}
        .info-section {{
            display: flex;
            justify-content: space-between;
            margin-bottom: 30px;
        }}
        .bill-to h3 {{
            margin-top: 0;
            color: #444;
            border-bottom: 1px solid #eee;
            padding-bottom: 5px;
        }}
        .bill-to p {{
            margin: 5px 0;
            line-height: 1.5;
        }}
        table.items-table {{
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 30px;
        }}
        table.items-table th, table.items-table td {{
            padding: 12px;
            text-align: left;
            border-bottom: 1px solid #ddd;
        }}
        table.items-table th {{
            background-color: #f1f8ff;
            color: #0056b3;
            font-weight: bold;
        }}
        table.items-table th.text-right, table.items-table td.text-right {{
            text-align: right;
        }}
        .totals-section {{
            width: 100%;
            display: flex;
            justify-content: flex-end;
        }}
        .totals-table {{
            width: 350px;
            border-collapse: collapse;
        }}
        .totals-table th, .totals-table td {{
            padding: 8px 12px;
            text-align: right;
        }}
        .totals-table th {{
            color: #555;
            font-weight: normal;
        }}
        .totals-table tr.grand-total {{
            font-size: 18px;
            font-weight: bold;
            color: #0056b3;
            border-top: 2px solid #eee;
        }}
        .footer {{
            margin-top: 50px;
            text-align: center;
            color: #888;
            font-size: 12px;
            border-top: 1px solid #eee;
            padding-top: 20px;
        }}
    </style>
</head>
<body>
    <div class=""invoice-box"">
        <div class=""header"">
            <div class=""company-details"">
                <h1>COLDCHAINX LOGISTICS</h1>
                <p>123 Đường Vận Tải, Quận 1, TP. HCM</p>
                <p>MST: 0123456789</p>
                <p>Email: contact@coldchainx.vn | Hotline: 1900 6868</p>
            </div>
            <div class=""invoice-details"">
                <h2>HÓA ĐƠN ĐIỆN TỬ</h2>
                <div class=""status-badge"">{statusText}</div>
                <p><strong>Mã hóa đơn:</strong> {invoice.InvoiceCode}</p>
                <p><strong>Ngày lập:</strong> {invoice.IssuedDate.ToString("dd/MM/yyyy")}</p>
                <p><strong>Hạn thanh toán:</strong> {invoice.DueDate.ToString("dd/MM/yyyy")}</p>
                {vatHtml}
            </div>
        </div>

        <div class=""info-section"">
            <div class=""bill-to"">
                <h3>Khách Hàng</h3>
                <p><strong>{customerName}</strong></p>
                <p>MST: {taxCode}</p>
                <p>Địa chỉ: {address}</p>
            </div>
        </div>

        <table class=""items-table"">
            <thead>
                <tr>
                    <th>STT</th>
                    <th>Nội dung / Chi tiết</th>
                    <th class=""text-right"">Số lượng</th>
                    <th class=""text-right"">Đơn giá</th>
                    <th class=""text-right"">Thành tiền (VND)</th>
                </tr>
            </thead>
            <tbody>
");
            int stt = 1;
            foreach (var line in invoice.InvoiceLines)
            {
                string trackingCodeHtml = string.IsNullOrEmpty(line.Order?.TrackingCode) ? "" : $"<br/><small>Mã vận đơn: {line.Order.TrackingCode}</small>";
                string qty = line.Quantity.HasValue ? line.Quantity.Value.ToString("N2") : "1";
                string unitPrice = line.UnitPrice.ToString("N0");
                string amount = line.Amount.ToString("N0");

                sb.Append($@"
                <tr>
                    <td>{stt++}</td>
                    <td>
                        <strong>{line.ChargeType}</strong><br/>
                        <span style=""font-size: 13px; color: #666;"">{line.Description}</span>
                        {trackingCodeHtml}
                    </td>
                    <td class=""text-right"">{qty}</td>
                    <td class=""text-right"">{unitPrice}</td>
                    <td class=""text-right"">{amount}</td>
                </tr>
");
            }

            string deductionHtml = invoice.DeductionAmount > 0 ? $"<tr><th>Giảm trừ:</th><td>-{invoice.DeductionAmount.Value.ToString("N0")}</td></tr>" : "";
            string subTotal = invoice.SubTotal.ToString("N0");
            string taxRate = (invoice.TaxRate ?? 0).ToString("0");
            string taxAmount = invoice.TaxAmount.ToString("N0");
            string grandTotal = invoice.GrandTotal.ToString("N0");
            string paidAmount = (invoice.PaidAmount ?? 0).ToString("N0");

            sb.Append($@"
            </tbody>
        </table>

        <div class=""totals-section"">
            <table class=""totals-table"">
                <tr>
                    <th>Tổng cộng tiền hàng:</th>
                    <td>{subTotal}</td>
                </tr>
                <tr>
                    <th>Thuế GTGT ({taxRate}%):</th>
                    <td>{taxAmount}</td>
                </tr>
                {deductionHtml}
                <tr class=""grand-total"">
                    <th>TỔNG THANH TOÁN:</th>
                    <td>{grandTotal} VND</td>
                </tr>
                <tr>
                    <th>Đã thanh toán:</th>
                    <td style=""color: #28a745;"">{paidAmount} VND</td>
                </tr>
            </table>
        </div>

        <div class=""footer"">
            <p>Hóa đơn được lập tự động từ hệ thống quản lý ColdChainX. Cảm ơn quý khách đã sử dụng dịch vụ của chúng tôi!</p>
        </div>
    </div>
</body>
</html>
");

            return sb.ToString();
        }
    }
}
