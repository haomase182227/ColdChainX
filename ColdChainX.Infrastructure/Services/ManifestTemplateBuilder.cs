using System.Net;
using System.Text;
using ColdChainX.Application.DTOs.Dispatch;

namespace ColdChainX.Infrastructure.Services
{
    public static class ManifestTemplateBuilder
    {
        private static readonly string[] ColorPalette =
        {
            "#E74C3C","#3498DB","#2ECC71","#F39C12","#8E44AD",
            "#1ABC9C","#E67E22","#2980B9","#27AE60","#D35400",
            "#16A085","#C0392B","#2471A3","#6C3483","#117A65",
            "#B9770E","#922B21","#1A5276","#145A32","#784212"
        };

        private const int COLS = 2;

        public static string BuildHtml(ManualDispatchResult result, string goongApiKey)
        {
            var loadPlan = result.LoadPlan?.OrderBy(x => x.LoadOrder).ToList()
                           ?? new List<LoadInstruction>();

            // Assign a unique color to each LPN by index
            var colorMap = new Dictionary<string, string>();
            for (int i = 0; i < loadPlan.Count; i++)
                colorMap[loadPlan[i].LpnCode ?? $"LPN-{i}"] = ColorPalette[i % ColorPalette.Length];

            int totalRows = (loadPlan.Count + COLS - 1) / COLS;

            // Helper: get LPN at grid position
            LoadInstruction? Cell(int r, int c)
            {
                int idx = r * COLS + c;
                return idx < loadPlan.Count ? loadPlan[idx] : null;
            }

            string Color(LoadInstruction? lpn) =>
                lpn != null && colorMap.TryGetValue(lpn.LpnCode ?? "", out var col) ? col : "#CCCCCC";

            string DarkenHex(string hex, double factor = 0.65)
            {
                if (hex.Length < 7) return hex;
                int r = Convert.ToInt32(hex.Substring(1, 2), 16);
                int g = Convert.ToInt32(hex.Substring(3, 2), 16);
                int b = Convert.ToInt32(hex.Substring(5, 2), 16);
                return $"#{(int)(r * factor):X2}{(int)(g * factor):X2}{(int)(b * factor):X2}";
            }

            string H(string? s) => WebUtility.HtmlEncode(s ?? "");

            var sb = new StringBuilder();

            // ── ISO-3D SVG ──────────────────────────────────────────────────
            // Isometric math: origin at top-center, each cell CELL_W×CELL_H
            const int CELL_W = 70;
            const int CELL_H = 35;
            const int BOX_H  = 38;
            int svgW = (COLS + totalRows + 2) * CELL_W / 2 + 60;
            int svgH = (COLS + totalRows + 1) * CELL_H / 2 + BOX_H + 70;
            int originX = (totalRows + 1) * CELL_W / 2 + 10;
            int originY = BOX_H + 20;

            // Convert truck (a=col, b=row) to screen (iso)
            (int sx, int sy) IsoXY(int a, int b, int z = 0) => (
                originX + (a - b) * CELL_W / 2,
                originY + (a + b) * CELL_H / 2 - z * BOX_H
            );

            string Pt(int x, int y) => $"{x},{y}";

            var svgSb = new StringBuilder();
            svgSb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{svgW}' height='{svgH}' style='max-width:100%;'>");

            // Draw container floor shadow
            var (fx0, fy0) = IsoXY(0, 0, 0);
            var (fx1, fy1) = IsoXY(COLS, 0, 0);
            var (fx2, fy2) = IsoXY(COLS, totalRows, 0);
            var (fx3, fy3) = IsoXY(0, totalRows, 0);
            svgSb.Append($"<polygon points='{Pt(fx0,fy0+4)} {Pt(fx1,fy1+4)} {Pt(fx2,fy2+4)} {Pt(fx3,fy3+4)}' fill='rgba(0,0,0,0.12)'/>");
            svgSb.Append($"<polygon points='{Pt(fx0,fy0)} {Pt(fx1,fy1)} {Pt(fx2,fy2)} {Pt(fx3,fy3)}' fill='#ECEFF1' stroke='#90A4AE' stroke-width='1.5'/>");

            // Draw boxes painter's order: high row+col first → low last
            var drawOrder = new List<(int row, int col)>();
            for (int r = totalRows - 1; r >= 0; r--)
                for (int c = COLS - 1; c >= 0; c--)
                    drawOrder.Add((r, c));

            foreach (var (row, col) in drawOrder)
            {
                var lpn  = Cell(row, col);
                var fill = Color(lpn);
                var dark = DarkenHex(fill, 0.60);
                var mid  = DarkenHex(fill, 0.78);

                // 8 corners of the box
                var (t0x, t0y) = IsoXY(col,   row,   1); // top back-left
                var (t1x, t1y) = IsoXY(col+1, row,   1); // top back-right
                var (t2x, t2y) = IsoXY(col+1, row+1, 1); // top front-right
                var (t3x, t3y) = IsoXY(col,   row+1, 1); // top front-left
                var (b0x, b0y) = IsoXY(col,   row,   0); // bot back-left
                var (b1x, b1y) = IsoXY(col+1, row,   0); // bot back-right
                var (b2x, b2y) = IsoXY(col+1, row+1, 0); // bot front-right
                var (b3x, b3y) = IsoXY(col,   row+1, 0); // bot front-left

                if (lpn == null)
                {
                    // Empty slot - draw faint outline only
                    svgSb.Append($"<polygon points='{Pt(t0x,t0y)} {Pt(t1x,t1y)} {Pt(t2x,t2y)} {Pt(t3x,t3y)}' fill='#ECEFF1' stroke='#CFD8DC' stroke-width='1'/>");
                    svgSb.Append($"<polygon points='{Pt(t3x,t3y)} {Pt(b3x,b3y)} {Pt(b0x,b0y)} {Pt(t0x,t0y)}' fill='#E0E0E0' stroke='#CFD8DC' stroke-width='0.5'/>");
                    svgSb.Append($"<polygon points='{Pt(t1x,t1y)} {Pt(t2x,t2y)} {Pt(b2x,b2y)} {Pt(b1x,b1y)}' fill='#DADADA' stroke='#CFD8DC' stroke-width='0.5'/>");
                    continue;
                }

                // Top face
                svgSb.Append($"<polygon points='{Pt(t0x,t0y)} {Pt(t1x,t1y)} {Pt(t2x,t2y)} {Pt(t3x,t3y)}' fill='{fill}' stroke='white' stroke-width='1.5'/>");
                // Left face (front-left)
                svgSb.Append($"<polygon points='{Pt(t3x,t3y)} {Pt(b3x,b3y)} {Pt(b0x,b0y)} {Pt(t0x,t0y)}' fill='{mid}' stroke='white' stroke-width='1'/>");
                // Right face (front-right)
                svgSb.Append($"<polygon points='{Pt(t1x,t1y)} {Pt(t2x,t2y)} {Pt(b2x,b2y)} {Pt(b1x,b1y)}' fill='{dark}' stroke='white' stroke-width='1'/>");

                // Text on top face: load order
                int tx = (t0x + t1x + t2x + t3x) / 4;
                int ty = (t0y + t1y + t2y + t3y) / 4;
                svgSb.Append($"<text x='{tx}' y='{ty-4}' text-anchor='middle' font-family='Arial' font-size='10' font-weight='bold' fill='white' stroke='rgba(0,0,0,0.3)' stroke-width='2' paint-order='stroke'>{lpn.LoadOrder}</text>");
                string shortCode = (lpn.LpnCode ?? "").Length > 8 ? (lpn.LpnCode ?? "")[^8..] : (lpn.LpnCode ?? "");
                svgSb.Append($"<text x='{tx}' y='{ty+8}' text-anchor='middle' font-family='Arial' font-size='7' fill='white' opacity='0.9'>{H(shortCode)}</text>");
            }

            // Container outline on top
            svgSb.Append($"<polygon points='{Pt(fx0,fy0)} {Pt(fx1,fy1)} {Pt(fx2,fy2)} {Pt(fx3,fy3)}' fill='none' stroke='#455A64' stroke-width='2'/>");

            // Door arrow at front (row = totalRows side)
            var (dax, day) = IsoXY(COLS / 2, totalRows, 0);
            svgSb.Append($"<text x='{dax}' y='{day + 18}' text-anchor='middle' font-family='Arial' font-size='9' font-weight='bold' fill='#E74C3C'>🚪 CỬA XE</text>");

            // Cab label at back (row = 0 side)
            var (cax, cay) = IsoXY(COLS / 2, 0, 1);
            svgSb.Append($"<text x='{cax}' y='{cay - 8}' text-anchor='middle' font-family='Arial' font-size='9' fill='#546E7A'>⬆ CABIN</text>");

            svgSb.Append("</svg>");
            string isoSvg = svgSb.ToString();

            // ── TOP VIEW (Floor Plan) ────────────────────────────────────────
            var topSb = new StringBuilder();
            topSb.Append("<table class='plan-table'>");
            topSb.Append("<tr><td class='axis-cell' colspan='1'></td><td class='axis-cell' style='text-align:center;font-size:8px;color:#888;' colspan='2'>← Trái &nbsp; Phải →</td></tr>");
            for (int r = 0; r < totalRows; r++)
            {
                topSb.Append("<tr>");
                string rowLabel = r == 0 ? "CAB" : r == totalRows - 1 ? "CỬA" : $"R{r+1}";
                topSb.Append($"<td class='axis-cell'>{rowLabel}</td>");
                for (int c = 0; c < COLS; c++)
                {
                    var lpn = Cell(r, c);
                    var bg  = Color(lpn);
                    if (lpn == null)
                        topSb.Append("<td class='plan-cell empty-cell'></td>");
                    else
                        topSb.Append($"<td class='plan-cell' style='background:{bg};' title='{H(lpn.LpnCode)}'><span class='plan-order'>#{lpn.LoadOrder}</span><br/><span class='plan-code'>{H(lpn.LpnCode?.Split('-').Last() ?? "")}</span></td>");
                }
                topSb.Append("</tr>");
            }
            topSb.Append("</table>");

            // ── FRONT CROSS-SECTION (looking from door inward) ─────────────
            var frontSb = new StringBuilder();
            frontSb.Append("<div class='front-section'>");
            frontSb.Append("<div class='front-container-outline'>");
            // Show first visible row from door = last row in grid
            for (int r = totalRows - 1; r >= 0; r--)
            {
                frontSb.Append($"<div class='front-row-depth' style='opacity:{1.0 - r * 0.12:F2};'>");
                for (int c = 0; c < COLS; c++)
                {
                    var lpn = Cell(r, c);
                    var bg  = Color(lpn);
                    if (lpn == null)
                        frontSb.Append("<div class='front-cell front-empty'></div>");
                    else
                    {
                        var border = DarkenHex(bg, 0.7);
                        frontSb.Append($"<div class='front-cell' style='background:{bg};border-color:{border};'>");
                        frontSb.Append($"<span class='fc-order'>#{lpn.LoadOrder}</span>");
                        string itemShort = (lpn.ItemName ?? "").Length > 8 ? (lpn.ItemName ?? "")[..8] + "…" : (lpn.ItemName ?? "");
                        frontSb.Append($"<span class='fc-name'>{H(itemShort)}</span>");
                        frontSb.Append("</div>");
                    }
                }
                frontSb.Append("</div>");
            }
            frontSb.Append("</div>");
            frontSb.Append("<div class='front-door-bar'>🚪 NHÌN TỪ CỬA SAU VÀO</div>");
            frontSb.Append("</div>");

            // ── SIDE VIEW (elevation from the right) ────────────────────────
            var sideSb = new StringBuilder();
            sideSb.Append("<table class='plan-table'>");
            sideSb.Append("<tr><td class='axis-cell'></td><td class='axis-cell' style='text-align:center;font-size:8px;color:#888;' colspan='1'>Độ sâu hàng</td></tr>");
            for (int r = 0; r < totalRows; r++)
            {
                sideSb.Append("<tr>");
                string rowLabel = r == 0 ? "CAB" : r == totalRows - 1 ? "CỬA" : $"R{r+1}";
                sideSb.Append($"<td class='axis-cell'>{rowLabel}</td>");
                // Side view: show 1 cell wide representing the row (both cols merged as one side slice)
                var lpn0 = Cell(r, 0);
                var lpn1 = Cell(r, 1);
                // Use gradient to represent two pallets side by side
                string bg0 = Color(lpn0);
                string bg1 = Color(lpn1);
                if (lpn0 == null && lpn1 == null)
                {
                    sideSb.Append("<td class='plan-cell empty-cell' colspan='1' style='width:90px;'></td>");
                }
                else
                {
                    string gradBg = lpn1 != null
                        ? $"linear-gradient(to right, {bg0} 50%, {bg1} 50%)"
                        : bg0;
                    sideSb.Append($"<td class='plan-cell' style='background:{gradBg};width:90px;'>");
                    if (lpn0 != null) sideSb.Append($"<span class='plan-order' style='float:left'>#{lpn0.LoadOrder}</span>");
                    if (lpn1 != null) sideSb.Append($"<span class='plan-order' style='float:right'>#{lpn1.LoadOrder}</span>");
                    sideSb.Append("</td>");
                }
                sideSb.Append("</tr>");
            }
            sideSb.Append("</table>");

            // ── COLOR LEGEND ─────────────────────────────────────────────────
            var legendSb = new StringBuilder();
            legendSb.Append("<div class='legend-wrap'>");
            foreach (var lpn in loadPlan)
            {
                var bg      = Color(lpn);
                string zone = lpn.Zone ?? "—";
                string zoneIcon = zone == "REAR" ? "🔵" : zone == "MID" ? "🩵" : "🟢";
                legendSb.Append($"<div class='legend-row'>");
                legendSb.Append($"<div class='legend-swatch' style='background:{bg};'></div>");
                legendSb.Append($"<div class='legend-info'>");
                legendSb.Append($"<span class='legend-order'>#{lpn.LoadOrder}</span>");
                legendSb.Append($"<span class='legend-code'>{H(lpn.LpnCode)}</span>");
                legendSb.Append($"<span class='legend-item'>{H(lpn.ItemName)} {zoneIcon}</span>");
                legendSb.Append($"<span class='legend-weight'>{lpn.WeightKg:0.#}kg / {lpn.Cbm:0.##}m³</span>");
                legendSb.Append($"</div>");
                legendSb.Append("</div>");
            }
            legendSb.Append("</div>");

            // ── TIMELINE ─────────────────────────────────────────────────────
            var tlSb = new StringBuilder();
            tlSb.Append("<div class='timeline-bar'>");
            var originAddr = result.Navigation?.Legs?.FirstOrDefault()?.FromAddress ?? "Kho xuất phát";
            tlSb.Append($"<div class='tl-node origin'><div class='tl-dot'></div><div class='tl-label'><b>Kho</b><br/>{H(originAddr)}</div></div>");
            if (result.Route?.Stops != null)
            {
                foreach (var stop in result.Route.Stops.OrderBy(s => s.Sequence))
                {
                    tlSb.Append($"<div class='tl-arrow'>▶</div>");
                    tlSb.Append($"<div class='tl-node stop'><div class='tl-dot'></div><div class='tl-label'><b>Trạm {stop.Sequence}</b><br/>{H(stop.Address ?? "—")} ({stop.DistanceFromPreviousKm:F1}km)</div></div>");
                }
            }
            tlSb.Append("</div>");

            // ── DISPATCH TABLE ───────────────────────────────────────────────
            var tableSb = new StringBuilder();
            tableSb.Append(@"<table class='data-table'>
<thead><tr>
  <th>Thứ tự</th><th>Mã LPN</th><th>Tên hàng</th><th>Ngăn</th>
  <th>Nhiệt độ</th><th>Trọng lượng</th><th>CBM</th><th>Trạm giao</th><th>Lý do xếp</th>
</tr></thead><tbody>");
            foreach (var lp in loadPlan)
            {
                var bg   = Color(lp);
                var zone = lp.Zone ?? "—";
                var zoneBadge = zone == "REAR" ? "#1565C0" : zone == "MID" ? "#00838F" : "#2E7D32";
                tableSb.Append($@"<tr>
  <td style='text-align:center;'><div class='order-badge' style='background:{bg};'>#{lp.LoadOrder}</div></td>
  <td><code>{H(lp.LpnCode)}</code><br/><small style='color:#888;'>{H(lp.TrackingCode)}</small></td>
  <td><b>{H(lp.ItemName)}</b></td>
  <td><span class='badge' style='background:{zoneBadge};color:#fff;'>{H(zone)}</span></td>
  <td style='font-size:11px;'>{H(lp.TempCondition)}</td>
  <td style='text-align:right;'>{lp.WeightKg:0.#} kg</td>
  <td style='text-align:right;'>{lp.Cbm:0.##} m³</td>
  <td style='text-align:center;'>#{lp.DeliveryStopSequence}</td>
  <td style='font-size:10px;color:#555;'>{H(lp.Reason)}</td>
</tr>");
            }
            tableSb.Append("</tbody></table>");

            // ── FULL HTML ────────────────────────────────────────────────────
            sb.Append($@"<!DOCTYPE html>
<html lang='vi'>
<head>
<meta charset='UTF-8'>
<title>Lệnh Điều Động LIFO — ColdChainX</title>
<style>
  @page {{ size: A3 landscape; margin: 12mm 14mm; }}
  *  {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{ font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; background: #ECEFF1; color: #263238; }}

  /* ── Header ── */
  .page-header {{
    display: flex; align-items: center; justify-content: space-between;
    background: #1A237E; color: #fff; padding: 10px 20px; border-radius: 6px 6px 0 0; margin-bottom: 8px;
  }}
  .brand {{ font-size: 20px; font-weight: 800; letter-spacing: 1.5px; }}
  .brand span {{ color: #FF6F00; }}
  .doc-meta {{ text-align: right; font-size: 10px; opacity: 0.8; line-height: 1.6; }}
  .doc-title {{ font-size: 13px; font-weight: 600; text-align: center; flex: 1; }}

  /* ── Info Bar ── */
  .info-bar {{ display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; margin-bottom: 8px; }}
  .ic {{ background: #fff; border-radius: 5px; padding: 8px 12px; border-left: 3px solid #FF6F00; box-shadow: 0 1px 3px rgba(0,0,0,.1); }}
  .ic .lbl {{ font-size: 8px; text-transform: uppercase; color: #90A4AE; letter-spacing: .5px; }}
  .ic .val {{ font-size: 12px; font-weight: 700; margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }}

  /* ── Section ── */
  .section {{ background: #fff; border-radius: 6px; padding: 12px 14px; margin-bottom: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.08); }}
  .sec-title {{ font-size: 9px; font-weight: 800; text-transform: uppercase; letter-spacing: 1px; color: #78909C; margin-bottom: 10px; padding-bottom: 6px; border-bottom: 1px solid #ECEFF1; display: flex; align-items: center; gap: 6px; }}
  .sec-title::before {{ content: ''; width: 12px; height: 3px; background: #1A237E; border-radius: 2px; }}

  /* ── 4-view grid ── */
  .diagram-grid {{
    display: grid;
    grid-template-columns: 1fr 1fr;
    grid-template-rows: auto auto;
    gap: 8px;
  }}
  .view-box {{
    background: #F5F7FA; border: 1px solid #CFD8DC; border-radius: 6px;
    padding: 8px; display: flex; flex-direction: column;
  }}
  .view-title {{
    font-size: 8px; font-weight: 800; text-transform: uppercase; letter-spacing: .8px;
    color: #546E7A; margin-bottom: 6px; padding-bottom: 5px; border-bottom: 1px dashed #CFD8DC;
    display: flex; align-items: center; gap: 4px;
  }}
  .view-body {{ flex: 1; display: flex; align-items: center; justify-content: center; overflow: hidden; }}

  /* ── Plan table (top view + side view) ── */
  .plan-table {{ border-collapse: collapse; }}
  .plan-table td {{ padding: 0; }}
  .axis-cell {{
    font-size: 8px; font-weight: 700; color: #78909C; text-align: center;
    padding: 2px 4px; min-width: 26px;
  }}
  .plan-cell {{
    width: 52px; height: 42px; border: 2px solid #fff; text-align: center; vertical-align: middle;
    font-size: 8px; font-weight: 700; color: #fff; position: relative; cursor: default;
  }}
  .empty-cell {{ background: #ECEFF1 !important; border: 2px dashed #CFD8DC !important; }}
  .plan-order {{ font-size: 11px; font-weight: 800; display: block; line-height: 1.2; text-shadow: 0 1px 2px rgba(0,0,0,.4); }}
  .plan-code  {{ font-size: 6.5px; opacity: .85; }}

  /* ── Front cross-section ── */
  .front-section {{ display: flex; flex-direction: column; align-items: center; gap: 3px; width: 100%; }}
  .front-container-outline {{ border: 2.5px solid #455A64; background: #ECEFF1; padding: 6px; border-radius: 3px; display: flex; flex-direction: column; gap: 3px; width: 100%; }}
  .front-row-depth {{ display: flex; gap: 4px; justify-content: center; }}
  .front-cell {{
    flex: 1; min-width: 55px; height: 48px; border: 2px solid; border-radius: 3px;
    display: flex; flex-direction: column; align-items: center; justify-content: center;
    font-size: 8px; color: #fff; font-weight: 700;
  }}
  .front-empty {{ background: #E0E0E0 !important; border: 2px dashed #BDBDBD !important; color: #aaa; }}
  .fc-order {{ font-size: 12px; font-weight: 800; text-shadow: 0 1px 2px rgba(0,0,0,.5); line-height: 1.1; }}
  .fc-name  {{ font-size: 7px; opacity: .9; max-width: 54px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }}
  .front-door-bar {{ background: #B71C1C; color: #fff; padding: 3px 12px; border-radius: 3px; font-size: 8px; font-weight: 700; letter-spacing: .5px; }}

  /* ── Legend ── */
  .legend-wrap {{ display: flex; flex-direction: column; gap: 4px; }}
  .legend-row {{ display: flex; align-items: center; gap: 6px; padding: 3px 0; border-bottom: 1px solid #F5F5F5; }}
  .legend-swatch {{ width: 22px; height: 22px; border-radius: 4px; flex-shrink: 0; border: 1px solid rgba(0,0,0,.15); }}
  .legend-info {{ display: flex; flex-wrap: wrap; gap: 4px; align-items: center; }}
  .legend-order {{ font-size: 11px; font-weight: 800; min-width: 22px; }}
  .legend-code  {{ font-size: 9px; color: #546E7A; font-family: monospace; }}
  .legend-item  {{ font-size: 10px; font-weight: 600; flex: 1; }}
  .legend-weight{{ font-size: 9px; color: #78909C; }}

  /* ── Timeline ── */
  .timeline-bar {{ display: flex; align-items: flex-start; flex-wrap: wrap; gap: 4px; padding: 4px 0; }}
  .tl-node {{ display: flex; align-items: flex-start; gap: 5px; }}
  .tl-dot  {{ width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; margin-top: 3px; }}
  .tl-node.origin .tl-dot {{ background: #E53935; }}
  .tl-node.stop   .tl-dot {{ background: #1A237E; }}
  .tl-label {{ font-size: 9px; color: #546E7A; line-height: 1.4; max-width: 130px; }}
  .tl-arrow {{ color: #90A4AE; font-size: 11px; margin-top: 1px; }}

  /* ── Data Table ── */
  .data-table {{ width: 100%; border-collapse: collapse; font-size: 10px; }}
  .data-table thead th {{ background: #1A237E; color: #fff; padding: 7px 9px; text-align: left; font-size: 9px; letter-spacing: .3px; }}
  .data-table tbody td {{ padding: 6px 9px; border-bottom: 1px solid #F5F5F5; vertical-align: middle; }}
  .data-table tbody tr:nth-child(even) td {{ background: #FAFAFA; }}
  .data-table code {{ font-size: 9px; background: #ECEFF1; padding: 1px 4px; border-radius: 3px; }}
  .order-badge {{ display: inline-flex; align-items: center; justify-content: center; width: 26px; height: 26px; border-radius: 50%; font-size: 10px; font-weight: 800; color: #fff; }}
  .badge {{ display: inline-block; padding: 2px 7px; border-radius: 10px; font-size: 8px; font-weight: 700; }}

  /* ── Footer ── */
  .page-footer {{ text-align: center; font-size: 9px; color: #90A4AE; margin-top: 8px; padding: 6px; }}
</style>
</head>
<body>

<div class='page-header'>
  <div class='brand'>Cold<span>Chain</span>X</div>
  <div class='doc-title'>LỆNH ĐIỀU ĐỘNG &amp; SƠ ĐỒ XẾP HÀNG LIFO</div>
  <div class='doc-meta'>
    Trip ID: {result.TripId}<br/>
    Ngày lập: {DateTime.Now:dd/MM/yyyy HH:mm}
  </div>
</div>

<div class='info-bar'>
  <div class='ic'><div class='lbl'>🚛 Biển số xe</div><div class='val'>{H(result.Vehicle?.TruckPlate)}</div></div>
  <div class='ic'><div class='lbl'>👤 Tài xế</div><div class='val'>{H(result.Driver?.FullName)}</div></div>
  <div class='ic'><div class='lbl'>⚖️ Tổng tải</div><div class='val'>{result.Vehicle?.TotalOrderWeightKg:F1} kg</div></div>
  <div class='ic'><div class='lbl'>📐 Tổng CBM</div><div class='val'>{result.Vehicle?.TotalOrderCbm:F2} m³</div></div>
  <div class='ic'><div class='lbl'>📦 Số LPN</div><div class='val'>{loadPlan.Count} lô hàng</div></div>
</div>

<div class='section'>
  <div class='sec-title'>Sơ đồ Container Nhiều Góc Nhìn</div>
  <div class='diagram-grid'>

    <!-- View 1: Isometric 3D -->
    <div class='view-box' style='grid-row: 1 / 3;'>
      <div class='view-title'>📦 Mặt xéo 3D — Isometric (nhìn từ góc phải trên)</div>
      <div class='view-body'>
        {isoSvg}
      </div>
    </div>

    <!-- View 2: Front Cross-Section -->
    <div class='view-box'>
      <div class='view-title'>🔲 Mặt cắt trước — Nhìn từ cửa sau vào</div>
      <div class='view-body'>
        {frontSb}
      </div>
    </div>

    <!-- View 3: Top view + Side view side by side -->
    <div class='view-box' style='display:grid; grid-template-columns: 1fr 1fr; gap: 8px; padding:8px;'>
      <div>
        <div class='view-title'>🗺️ Mặt trên — Sơ đồ mặt bằng</div>
        <div class='view-body' style='justify-content:flex-start;'>
          {topSb}
        </div>
      </div>
      <div>
        <div class='view-title'>📐 Mặt bên — Nhìn từ bên phải</div>
        <div class='view-body' style='justify-content:flex-start;'>
          {sideSb}
        </div>
      </div>
    </div>

  </div>
</div>

<div class='section' style='display:grid; grid-template-columns: 2fr 1fr; gap: 12px;'>
  <div>
    <div class='sec-title'>Lộ trình chuyến</div>
    {tlSb}
  </div>
  <div>
    <div class='sec-title'>Chú thích màu lô hàng</div>
    {legendSb}
  </div>
</div>

<div class='section'>
  <div class='sec-title'>Bảng lệnh bốc xếp — LIFO Load Plan</div>
  {tableSb}
</div>

<div class='page-footer'>
  ColdChainX — Tài liệu nội bộ — In lúc {DateTime.Now:dd/MM/yyyy HH:mm} (GMT+7) — Trip: {result.TripId}
</div>

</body>
</html>");

            return sb.ToString();
        }
    }
}
