using System.Text;
using ColdChainX.Application.DTOs.Dispatch;

namespace ColdChainX.Infrastructure.Services
{
    public static class ManifestTemplateBuilder
    {
        public static string BuildHtml(ManualDispatchResult result, string goongApiKey)
        {
            var sb = new StringBuilder();

            // Lấy overview path từ GoongDirectionsResult
            var encPath = result.Navigation?.GoongRouteOverview ?? "";
            
            // Generate Map Image URL using Static Map API
            // Goong Static Map Path style: weight:3|color:blue|enc:xxx
            var mapUrl = string.IsNullOrEmpty(encPath) 
                ? "" 
                : $"https://rsapi.goong.io/staticmap/route?api_key={goongApiKey}&path=weight:5|color:blue|enc:{encPath}&size=800x400";

            sb.Append($@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <title>Dispatch Manifest</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            color: #333;
            margin: 0;
            padding: 20px;
            font-size: 14px;
        }}
        .header {{
            text-align: center;
            border-bottom: 2px solid #0056b3;
            padding-bottom: 10px;
            margin-bottom: 20px;
        }}
        .header h1 {{
            margin: 0;
            color: #0056b3;
        }}
        .trip-info {{
            display: flex;
            justify-content: space-between;
            background-color: #f8f9fa;
            padding: 15px;
            border-radius: 5px;
            margin-bottom: 20px;
        }}
        .trip-info div {{ flex: 1; }}
        h2 {{ border-bottom: 1px solid #ccc; padding-bottom: 5px; color: #444; }}
        
        .map-container {{
            text-align: center;
            margin-bottom: 20px;
        }}
        .map-container img {{
            max-width: 100%;
            height: auto;
            border: 1px solid #ddd;
            border-radius: 5px;
        }}
        
        table {{
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 20px;
        }}
        th, td {{
            border: 1px solid #ddd;
            padding: 8px;
            text-align: left;
        }}
        th {{ background-color: #f2f2f2; }}
        
        /* CSS Grid cho xe tải */
        .truck-diagram {{
            display: flex;
            flex-direction: column;
            align-items: center;
            margin-bottom: 30px;
        }}
        .cabin {{
            width: 150px;
            height: 60px;
            background-color: #555;
            color: white;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 10px 10px 0 0;
            font-weight: bold;
            margin-bottom: 5px;
        }}
        .container {{
            width: 80%;
            border: 4px solid #333;
            border-bottom: 8px solid #cc0000; /* Cửa sau */
            display: grid;
            grid-template-columns: 1fr;
            /* Phân khoang FRONT, MIDDLE, REAR tùy số lượng đơn */
            gap: 10px;
            padding: 10px;
            background-color: #fdfdfd;
            position: relative;
        }}
        .container::after {{
            content: 'CỬA SAU XE (Bốc hàng từ đây)';
            position: absolute;
            bottom: -30px;
            left: 50%;
            transform: translateX(-50%);
            font-weight: bold;
            color: #cc0000;
        }}
        .zone-label {{
            font-size: 12px;
            color: #777;
            text-transform: uppercase;
            text-align: center;
            margin: 5px 0;
            border-bottom: 1px dashed #ccc;
        }}
        .pallet {{
            background-color: #e0f2fe;
            border: 1px solid #38bdf8;
            padding: 10px;
            text-align: center;
            border-radius: 4px;
        }}
        .pallet.frozen {{ background-color: #bfdbfe; border-color: #2563eb; }}
        .pallet.chilled {{ background-color: #dcfce7; border-color: #16a34a; }}
        
    </style>
</head>
<body>
    <div class='header'>
        <h1>LỆNH ĐIỀU ĐỘNG & BỐC XẾP HÀNG HÓA</h1>
        <p>Hệ thống Quản lý Chuỗi cung ứng lạnh - ColdChainX</p>
    </div>

    <div class='trip-info'>
        <div>
            <strong>Mã Chuyến (Trip ID):</strong> {result.TripId}<br/>
            <strong>Ngày điều động:</strong> {DateTime.Now:dd/MM/yyyy HH:mm}<br/>
        </div>
        <div>
            <strong>Biển số xe:</strong> {result.Vehicle?.TruckPlate}<br/>
            <strong>Tài xế:</strong> {result.Driver?.FullName}<br/>
        </div>
        <div>
            <strong>Tổng khối lượng:</strong> {result.Vehicle?.TotalOrderWeightKg:F1} kg<br/>
            <strong>Tổng khoảng cách:</strong> {result.Route?.TotalDistanceKm:F1} km
        </div>
    </div>

    <h2>1. Bản đồ Lộ trình Tuyến đường</h2>
    <div class='map-container'>
        {(string.IsNullOrEmpty(mapUrl) ? "<p><i>Không có dữ liệu bản đồ</i></p>" : $"<img src='{mapUrl}' alt='Route Map' />")}
    </div>

    <h2>2. Bảng Danh Sách Trạm Dừng (Itinerary)</h2>
    <table>
        <thead>
            <tr>
                <th>Thứ tự</th>
                <th>Địa chỉ trạm dừng</th>
                <th>Số đơn hàng dỡ xuống</th>
                <th>Khoảng cách (km)</th>
            </tr>
        </thead>
        <tbody>
");

            if (result.Route?.Stops != null)
            {
                foreach (var stop in result.Route.Stops.OrderBy(s => s.Sequence))
                {
                    sb.Append($@"
            <tr>
                <td>Trạm {stop.Sequence}</td>
                <td>{stop.Address}</td>
                <td>{stop.OrdersToUnload?.Count ?? 0} đơn</td>
                <td>{stop.DistanceFromPreviousKm:F1}</td>
            </tr>");
                }
            }

            sb.Append(@"
        </tbody>
    </table>

    <h2>3. Sơ đồ Xếp hàng LIFO (Load Plan)</h2>
    <p><i>Hướng dẫn: Hàng xuất hiện sát cửa xe (REAR) sẽ được dỡ xuống trước ở các trạm đầu. Càng vào sâu bên trong (FRONT), hàng sẽ được dỡ ở các trạm cuối.</i></p>
    
    <div class='truck-diagram'>
        <div class='cabin'>ĐẦU XE CABIN</div>
        <div class='container'>
");
            // Sắp xếp loadPlan từ TRONG ra NGOÀI -> FRONT trước, REAR sau.
            // Để hiển thị trên CSS (từ trên xuống dưới = từ FRONT ra REAR).
            // Thường LIFO sẽ đánh zone FRONT -> MIDDLE -> REAR.
            var groupedLoadPlan = result.LoadPlan
                .GroupBy(lp => lp.Zone)
                .OrderBy(g => g.Key == "FRONT" ? 1 : g.Key == "MIDDLE" ? 2 : 3)
                .ToList();

            foreach(var group in groupedLoadPlan)
            {
                sb.Append($"<div class='zone-label'>KHOANG {group.Key}</div>");
                
                // Mọi đơn trong khoang này
                foreach(var lp in group.OrderBy(x => x.DeliveryStopSequence).ThenBy(x => x.LoadOrder))
                {
                    var cssClass = lp.TempCondition.Contains("FROZEN", StringComparison.OrdinalIgnoreCase) || lp.TempCondition.Contains("-18") ? "frozen" :
                                   lp.TempCondition.Contains("CHILLED") || lp.TempCondition.Contains("2 to 8") ? "chilled" : "";
                    
                    sb.Append($@"
            <div class='pallet {cssClass}'>
                <strong>{lp.TrackingCode}</strong> - {lp.ItemName} ({lp.WeightKg} kg)<br/>
                <small>Dỡ tại: Trạm {lp.DeliveryStopSequence} | Lệnh bốc: {lp.LoadOrder}</small>
            </div>");
                }
            }

            sb.Append(@"
        </div>
    </div>
</body>
</html>");

            return sb.ToString();
        }
    }
}
