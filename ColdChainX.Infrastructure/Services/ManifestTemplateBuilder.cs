using System.Linq;
using System.Net;
using System.Text;
using ColdChainX.Application.DTOs.Dispatch;

namespace ColdChainX.Infrastructure.Services
{
    public static class ManifestTemplateBuilder
    {
        private const int COLS = 2;

        public static string BuildHtml(ManualDispatchResult result, string goongApiKey)
        {
            var loadPlan = result.LoadPlan?.OrderBy(x => x.LoadOrder).ToList()
                           ?? new List<LoadInstruction>();

            int totalRows = (loadPlan.Count + COLS - 1) / COLS;

            LoadInstruction? Cell(int r, int c)
            {
                int idx = r * COLS + c;
                return idx < loadPlan.Count ? loadPlan[idx] : null;
            }

            string H(string? s) => WebUtility.HtmlEncode(s ?? "");

            var issuedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            var tripShortId = result.TripId.ToString()[..8].ToUpper();

            // ══════════════════════════════════════════════════════════════════
            //  TOP VIEW (Minimalist Table for Load Plan)
            // ══════════════════════════════════════════════════════════════════
            var topSb = new StringBuilder();
            topSb.Append("<table class='layout-table'>");
            topSb.Append("<tr><td class='axis' style='border:none;'></td><td colspan='2' style='text-align:center;font-size:10px;border:none;'>← Trái &nbsp;|&nbsp; Phải →</td></tr>");
            
            for (int r = 0; r < totalRows; r++)
            {
                topSb.Append("<tr>");
                string rowLabel = r == 0 ? "CABIN (TRONG)" : r == totalRows - 1 ? "CỬA (NGOÀI)" : $"HÀNG {r + 1}";
                topSb.Append($"<td class='axis'>{rowLabel}</td>");
                
                for (int c = 0; c < COLS; c++)
                {
                    var lpn = Cell(r, c);
                    if (lpn == null)
                    {
                        topSb.Append("<td class='cell empty'>—</td>");
                    }
                    else
                    {
                        topSb.Append($"<td class='cell'><b>#{lpn.LoadOrder}</b><br/><span class='code'>{H(lpn.LpnCode?.Split('-').Last() ?? "")}</span></td>");
                    }
                }
                topSb.Append("</tr>");
            }
            topSb.Append("</table>");

            // ══════════════════════════════════════════════════════════════════
            //  DATA TABLE (Formal Document Style)
            // ══════════════════════════════════════════════════════════════════
            var tableSb = new StringBuilder();
            tableSb.Append(@"<table class='data-table'>
<thead><tr>
  <th style='width:5%'>STT Bốc</th>
  <th style='width:15%'>Mã Kiện (LPN)</th>
  <th style='width:25%'>Tên Hàng Hóa</th>
  <th style='width:10%'>Phân Khu</th>
  <th style='width:10%'>Nhiệt Độ</th>
  <th style='width:10%'>TL (kg)</th>
  <th style='width:10%'>Thể Tích</th>
  <th style='width:15%'>Trạm Giao</th>
</tr></thead><tbody>");
            
            decimal totalKg = 0;
            decimal totalCbm = 0;

            foreach (var lp in loadPlan)
            {
                totalKg += lp.WeightKg;
                totalCbm += lp.Cbm;
                var zone = lp.Zone ?? "—";
                
                tableSb.Append($@"<tr>
  <td style='text-align:center'><b>{lp.LoadOrder}</b></td>
  <td>{H(lp.LpnCode)}</td>
  <td>{H(lp.ItemName)}</td>
  <td style='text-align:center'>{H(zone)}</td>
  <td style='text-align:center'>{H(lp.TempCondition)}</td>
  <td style='text-align:right'>{lp.WeightKg:0.#}</td>
  <td style='text-align:right'>{lp.Cbm:0.##}</td>
  <td style='text-align:center'>Trạm {lp.DeliveryStopSequence}</td>
</tr>");
            }

            tableSb.Append($@"<tr>
  <td colspan='5' style='text-align:right'><b>TỔNG CỘNG:</b></td>
  <td style='text-align:right'><b>{totalKg:0.#}</b></td>
  <td style='text-align:right'><b>{totalCbm:0.##}</b></td>
  <td></td>
</tr>");
            tableSb.Append("</tbody></table>");

            // ══════════════════════════════════════════════════════════════════
            //  ROUTE INFORMATION
            // ══════════════════════════════════════════════════════════════════
            var routeSb = new StringBuilder();
            if (result.Navigation?.Legs?.Any() == true)
            {
                var navInfo = result.Navigation;
                routeSb.Append($"<p><b>Tổng quãng đường dự kiến:</b> {navInfo.TotalDistanceKm:F1} km &nbsp;|&nbsp; <b>Thời gian di chuyển:</b> {navInfo.TotalDurationMinutes} phút</p>");
                routeSb.Append("<ul class='route-list'>");
                foreach (var leg in navInfo.Legs)
                {
                    routeSb.Append($"<li><b>Chặng {leg.LegIndex}:</b> {H(leg.FromAddress)} &rarr; {H(leg.ToAddress)} <i>({leg.DistanceKm:F1} km)</i></li>");
                }
                routeSb.Append("</ul>");
            }
            else
            {
                var originAddr = result.Route?.Stops?.FirstOrDefault()?.Address ?? "Kho trung tâm";
                routeSb.Append($"<p><b>Điểm xuất phát:</b> {H(originAddr)}</p>");
                if (result.Route?.Stops != null)
                {
                    routeSb.Append("<ul class='route-list'>");
                    foreach (var stop in result.Route.Stops.OrderBy(s => s.Sequence))
                    {
                        routeSb.Append($"<li><b>Trạm {stop.Sequence}:</b> {H(stop.Address ?? "—")} <i>(Cách {stop.DistanceFromPreviousKm:F1} km)</i></li>");
                    }
                    routeSb.Append("</ul>");
                }
            }

            // ══════════════════════════════════════════════════════════════════
            //  FULL HTML DOCUMENT (A4 Portrait, Formal)
            // ══════════════════════════════════════════════════════════════════
            return $@"<!DOCTYPE html>
<html lang='vi'>
<head>
<meta charset='UTF-8'>
<title>Lệnh Điều Động LIFO</title>
<style>
  @page {{ size: A4 portrait; margin: 15mm; }}
  body {{ 
    font-family: 'Times New Roman', Times, serif; 
    font-size: 13pt; 
    color: #000; 
    line-height: 1.4; 
    background: #fff;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }}
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  
  .header-table {{ width: 100%; border-bottom: 2px solid #000; padding-bottom: 10px; margin-bottom: 20px; }}
  .header-left {{ width: 50%; vertical-align: top; }}
  .header-right {{ width: 50%; vertical-align: top; text-align: right; }}
  .company-name {{ font-size: 14pt; font-weight: bold; text-transform: uppercase; }}
  .document-type {{ font-size: 11pt; font-style: italic; }}
  
  .doc-title {{ text-align: center; font-size: 18pt; font-weight: bold; margin: 20px 0 10px 0; text-transform: uppercase; }}
  .doc-number {{ text-align: center; font-size: 12pt; margin-bottom: 20px; font-style: italic; }}

  .info-table {{ width: 100%; margin-bottom: 20px; font-size: 13pt; }}
  .info-table td {{ padding: 4px 0; vertical-align: top; }}
  .info-label {{ width: 25%; font-weight: bold; }}

  .section-title {{ font-size: 14pt; font-weight: bold; margin: 25px 0 10px 0; text-transform: uppercase; border-bottom: 1px solid #ccc; padding-bottom: 5px; }}

  .data-table {{ width: 100%; border-collapse: collapse; margin-bottom: 20px; font-size: 11pt; }}
  .data-table th, .data-table td {{ border: 1px solid #000; padding: 6px; }}
  .data-table th {{ background: #f0f0f0; text-align: center; font-weight: bold; }}

  .layout-table {{ border-collapse: collapse; margin: 0 auto 20px auto; font-size: 11pt; width: 60%; }}
  .layout-table td {{ border: 1px solid #000; text-align: center; padding: 10px; }}
  .layout-table .axis {{ font-weight: bold; background: #f9f9f9; width: 30%; }}
  .layout-table .cell {{ width: 35%; height: 60px; }}
  .layout-table .empty {{ color: #999; }}
  .layout-table .code {{ font-size: 9pt; font-family: monospace; display: block; margin-top: 5px; }}

  .route-list {{ list-style-type: disc; margin-left: 20px; margin-top: 5px; }}
  .route-list li {{ margin-bottom: 4px; }}

  .signature-table {{ width: 100%; margin-top: 40px; text-align: center; page-break-inside: avoid; }}
  .signature-table td {{ width: 33%; vertical-align: top; }}
  .sig-title {{ font-weight: bold; margin-bottom: 60px; }}

  .footer {{ margin-top: 30px; font-size: 10pt; text-align: center; font-style: italic; border-top: 1px solid #ccc; padding-top: 10px; }}
</style>
</head>
<body>

<table class='header-table'>
  <tr>
    <td class='header-left'>
      <div class='company-name'>HỆ THỐNG COLDCHAINX</div>
      <div class='document-type'>Phòng Điều Phối Vận Tải</div>
    </td>
    <td class='header-right'>
      <div style='margin-top:10px;'>Ngày in: {issuedAt}</div>
    </td>
  </tr>
</table>

<div class='doc-title'>LỆNH ĐIỀU ĐỘNG VÀ BỐC XẾP HÀNG HÓA</div>
<div class='doc-number'>Mã chuyến: LIFO-{tripShortId}</div>

<table class='info-table'>
  <tr>
    <td class='info-label'>Biển số xe:</td>
    <td>{H(result.Vehicle?.TruckPlate ?? "N/A")}</td>
    <td class='info-label'>Tài xế:</td>
    <td>{H(result.Drivers != null && result.Drivers.Count > 0 ? string.Join(", ", result.Drivers.Select(d => d.FullName)) : "N/A")}</td>
  </tr>
  <tr>
    <td class='info-label'>Tổng tải trọng (kg):</td>
    <td>{result.Vehicle?.TotalOrderWeightKg:F1}</td>
    <td class='info-label'>Tổng thể tích (m³):</td>
    <td>{result.Vehicle?.TotalOrderCbm:F2}</td>
  </tr>
  <tr>
    <td class='info-label'>Tổng số kiện (LPN):</td>
    <td>{loadPlan.Count} kiện</td>
    <td class='info-label'>Trạng thái:</td>
    <td>Điều động bốc xếp (LIFO)</td>
  </tr>
</table>

<div class='section-title'>I. SƠ ĐỒ MẶT BẰNG BỐC XẾP (Nhìn từ trên)</div>
<p style='margin-bottom: 10px; font-style: italic; text-align: center;'>Quy tắc LIFO: Hàng bốc vào trước nằm ở phía trong (CABIN), hàng bốc vào sau nằm ở phía ngoài (CỬA).</p>
{topSb}

<div class='section-title'>II. DANH SÁCH KIỆN HÀNG VÀ THỨ TỰ BỐC</div>
{tableSb}

<div class='section-title'>III. THÔNG TIN LỘ TRÌNH DỰ KIẾN</div>
{routeSb}

<table class='signature-table'>
  <tr>
    <td>
      <div class='sig-title'>Người lập phiếu</div>
      <div>(Ký, ghi rõ họ tên)</div>
    </td>
    <td>
      <div class='sig-title'>Tài xế nhận lệnh</div>
      <div>(Ký, ghi rõ họ tên)</div>
    </td>
    <td>
      <div class='sig-title'>Thủ kho xuất</div>
      <div>(Ký, ghi rõ họ tên)</div>
    </td>
  </tr>
</table>

<div class='footer'>
  Tài liệu được xuất tự động từ hệ thống quản lý vận tải ColdChainX. Lệnh này thay thế cho các chỉ thị bốc xếp trước đó.
</div>

</body>
</html>";
        }
    }
}
