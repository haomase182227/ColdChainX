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

            var colorMap = new Dictionary<string, string>();
            for (int i = 0; i < loadPlan.Count; i++)
                colorMap[loadPlan[i].LpnCode ?? $"LPN-{i}"] = ColorPalette[i % ColorPalette.Length];

            int totalRows = (loadPlan.Count + COLS - 1) / COLS;

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
                int r2 = Convert.ToInt32(hex.Substring(1, 2), 16);
                int g2 = Convert.ToInt32(hex.Substring(3, 2), 16);
                int b2 = Convert.ToInt32(hex.Substring(5, 2), 16);
                return $"#{(int)(r2 * factor):X2}{(int)(g2 * factor):X2}{(int)(b2 * factor):X2}";
            }

            string H(string? s) => WebUtility.HtmlEncode(s ?? "");

            // ══════════════════════════════════════════════════════════════════
            //  TRUCK SIDE-VIEW SVG
            //  Cab on LEFT, Rear door on RIGHT
            //  LoadOrder=1 (deepest, near cab) = leftmost slot
            //  LoadOrder=N (near door) = rightmost slot
            // ══════════════════════════════════════════════════════════════════

            const int TRUCK_W = 860;
            const int TRUCK_H = 252;
            // Inner cargo area inside trailer
            const int CARGO_X = 126;
            const int CARGO_Y = 52;
            const int CARGO_W = 700;
            const int CARGO_H = 132;

            float sliceW = (float)CARGO_W / Math.Max(totalRows, 1);
            float itemH  = (float)CARGO_H / COLS;

            var truckSb = new StringBuilder();
            truckSb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {TRUCK_W} {TRUCK_H}' style='width:100%;display:block;'>");

            // Ground shadow
            truckSb.Append($"<ellipse cx='440' cy='{TRUCK_H - 6}' rx='408' ry='7' fill='rgba(0,0,0,0.09)'/>");

            // ── TRAILER BODY ──────────────────────────────────────────────────
            // Main body
            truckSb.Append("<rect x='118' y='36' width='728' height='164' rx='4' fill='#F0F4F8' stroke='#455A64' stroke-width='2.5'/>");
            // Roof trim
            truckSb.Append("<rect x='118' y='36' width='728' height='15' rx='4' fill='#546E7A'/>");
            // Floor trim
            truckSb.Append("<rect x='118' y='185' width='728' height='15' fill='#546E7A'/>");
            // Rivet line on roof
            for (int rx = 140; rx < 840; rx += 60)
                truckSb.Append($"<circle cx='{rx}' cy='43' r='2.5' fill='#78909C'/>");
            // Rivet line on floor
            for (int rx = 140; rx < 840; rx += 60)
                truckSb.Append($"<circle cx='{rx}' cy='192' r='2.5' fill='#78909C'/>");

            // Rear door (red vertical bar with handle)
            truckSb.Append("<rect x='838' y='36' width='10' height='164' rx='3' fill='#C62828'/>");
            truckSb.Append("<rect x='841' y='104' width='4' height='36' rx='2' fill='#FFCDD2'/>");
            // Door hinges
            truckSb.Append("<rect x='838' y='52' width='10' height='6' rx='2' fill='#B71C1C'/>");
            truckSb.Append("<rect x='838' y='178' width='10' height='6' rx='2' fill='#B71C1C'/>");

            // ── CARGO ITEMS ───────────────────────────────────────────────────
            for (int r = 0; r < totalRows; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    var lpn  = Cell(r, c);
                    float ix = CARGO_X + r * sliceW + 1.5f;
                    float iy = CARGO_Y + c * itemH + 1.5f;
                    float iw = sliceW - 3f;
                    float ih = itemH - 3f;

                    if (lpn == null)
                    {
                        truckSb.Append($"<rect x='{ix:F1}' y='{iy:F1}' width='{iw:F1}' height='{ih:F1}' rx='2' fill='#ECEFF1' stroke='#CFD8DC' stroke-width='1' stroke-dasharray='3,2'/>");
                        continue;
                    }

                    var fill  = Color(lpn);
                    var dark  = DarkenHex(fill, 0.72);
                    var light = DarkenHex(fill, 1.0); // same as fill for top gradient

                    truckSb.Append($"<defs><linearGradient id='g{r}_{c}' x1='0' y1='0' x2='0' y2='1'><stop offset='0%' stop-color='{light}'/><stop offset='100%' stop-color='{dark}'/></linearGradient></defs>");
                    truckSb.Append($"<rect x='{ix:F1}' y='{iy:F1}' width='{iw:F1}' height='{ih:F1}' rx='2' fill='url(#g{r}_{c})' stroke='{dark}' stroke-width='1.5'/>");

                    float tx = ix + iw / 2f;
                    float ty = iy + ih / 2f;

                    float fsSz = iw > 36 ? 11 : (iw > 20 ? 8 : 6);
                    truckSb.Append($"<text x='{tx:F1}' y='{(ty + 4.5f):F1}' text-anchor='middle' font-family='Arial' font-size='{fsSz:F0}' font-weight='bold' fill='white' stroke='rgba(0,0,0,0.45)' stroke-width='2.5' paint-order='stroke'>#{lpn.LoadOrder}</text>");

                    if (iw > 46)
                    {
                        string sc = (lpn.LpnCode ?? "").Length > 7 ? (lpn.LpnCode ?? "")[^7..] : (lpn.LpnCode ?? "");
                        truckSb.Append($"<text x='{tx:F1}' y='{(ty + 15f):F1}' text-anchor='middle' font-family='Arial' font-size='6' fill='rgba(255,255,255,0.88)'>{H(sc)}</text>");
                    }
                }
            }

            // Trailer inner frame (top of cargo area)
            truckSb.Append($"<rect x='{CARGO_X - 2}' y='{CARGO_Y - 2}' width='{CARGO_W + 4}' height='{CARGO_H + 4}' rx='3' fill='none' stroke='#90A4AE' stroke-width='1'/>");

            // ── CAB ───────────────────────────────────────────────────────────
            // Sleeper / roof fairing
            truckSb.Append("<rect x='26' y='24' width='98' height='20' rx='5' fill='#263238'/>");
            // Cab body (trapezoid — cab slopes at front)
            truckSb.Append("<polygon points='4,52 122,38 122,200 4,200' fill='#2E4057'/>");
            // Windshield
            truckSb.Append("<polygon points='11,57 118,44 118,110 11,110' fill='#64B5F6' opacity='0.82'/>");
            truckSb.Append("<polygon points='11,57 118,44 118,110 11,110' fill='none' stroke='#1A252F' stroke-width='1.5'/>");
            // Wiper
            truckSb.Append("<line x1='26' y1='109' x2='90' y2='59' stroke='#1A252F' stroke-width='1.2' opacity='0.5'/>");
            // Cab door panel
            truckSb.Append("<rect x='8' y='115' width='110' height='81' rx='3' fill='#243447'/>");
            // Door window
            truckSb.Append("<rect x='13' y='120' width='100' height='46' rx='3' fill='#90CAF9' opacity='0.68'/>");
            // Door handle
            truckSb.Append("<rect x='97' y='166' width='20' height='5' rx='2.5' fill='#90A4AE'/>");
            // Door step
            truckSb.Append("<rect x='4' y='198' width='74' height='9' rx='3' fill='#1C2B38'/>");
            // Front bumper / grille area
            truckSb.Append("<rect x='2' y='164' width='14' height='36' rx='3' fill='#1C2B38'/>");
            truckSb.Append("<line x1='2' y1='170' x2='16' y2='170' stroke='#546E7A' stroke-width='1.5'/>");
            truckSb.Append("<line x1='2' y1='177' x2='16' y2='177' stroke='#546E7A' stroke-width='1.5'/>");
            truckSb.Append("<line x1='2' y1='184' x2='16' y2='184' stroke='#546E7A' stroke-width='1.5'/>");
            // Cab-to-trailer coupling
            truckSb.Append("<line x1='120' y1='38' x2='120' y2='200' stroke='#37474F' stroke-width='2' stroke-dasharray='5,3'/>");

            // ── WHEELS ────────────────────────────────────────────────────────
            // Front wheel
            truckSb.Append("<circle cx='60' cy='222' r='24' fill='#1A1A1A'/>");
            truckSb.Append("<circle cx='60' cy='222' r='15' fill='#2C2C2C'/>");
            truckSb.Append("<circle cx='60' cy='222' r='6'  fill='#607D8B'/>");
            // Rear axle bar
            truckSb.Append("<rect x='672' y='216' width='76' height='12' rx='4' fill='#455A64'/>");
            // Rear wheel 1
            truckSb.Append("<circle cx='676' cy='222' r='24' fill='#1A1A1A'/>");
            truckSb.Append("<circle cx='676' cy='222' r='15' fill='#2C2C2C'/>");
            truckSb.Append("<circle cx='676' cy='222' r='6'  fill='#607D8B'/>");
            // Rear wheel 2
            truckSb.Append("<circle cx='744' cy='222' r='24' fill='#1A1A1A'/>");
            truckSb.Append("<circle cx='744' cy='222' r='15' fill='#2C2C2C'/>");
            truckSb.Append("<circle cx='744' cy='222' r='6'  fill='#607D8B'/>");

            // ── ANNOTATIONS ──────────────────────────────────────────────────
            // CABIN label
            truckSb.Append("<text x='62' y='250' text-anchor='middle' font-family='Arial' font-size='10' fill='#2E4057' font-weight='bold'>⬆ CABIN</text>");
            // DOOR label
            truckSb.Append("<text x='843' y='250' text-anchor='middle' font-family='Arial' font-size='10' fill='#C62828' font-weight='bold'>🚪 CỬA SAU</text>");
            // Direction hint
            truckSb.Append("<text x='480' y='250' text-anchor='middle' font-family='Arial' font-size='8' fill='#78909C'>← Xếp vào trước (LIFO: vào trước, lấy ra sau)</text>");

            truckSb.Append("</svg>");
            string truckSvg = truckSb.ToString();

            // ══════════════════════════════════════════════════════════════════
            //  TOP VIEW (floor plan)
            // ══════════════════════════════════════════════════════════════════

            var topSb = new StringBuilder();
            topSb.Append("<table class='plan-table'>");
            topSb.Append("<tr><td class='axis-cell'></td><td class='axis-cell' style='text-align:center;font-size:8px;color:#888;' colspan='2'>← Trái &nbsp; Phải →</td></tr>");
            for (int r = 0; r < totalRows; r++)
            {
                topSb.Append("<tr>");
                string rowLabel = r == 0 ? "CAB" : r == totalRows - 1 ? "CỬA" : $"R{r + 1}";
                topSb.Append($"<td class='axis-cell'>{rowLabel}</td>");
                for (int c = 0; c < COLS; c++)
                {
                    var lpn = Cell(r, c);
                    var bg  = Color(lpn);
                    if (lpn == null)
                        topSb.Append("<td class='plan-cell empty-cell'></td>");
                    else
                        topSb.Append($"<td class='plan-cell' style='background:{bg};'><span class='plan-order'>#{lpn.LoadOrder}</span><br/><span class='plan-code'>{H(lpn.LpnCode?.Split('-').Last() ?? "")}</span></td>");
                }
                topSb.Append("</tr>");
            }
            topSb.Append("</table>");

            // ══════════════════════════════════════════════════════════════════
            //  COLOR LEGEND
            // ══════════════════════════════════════════════════════════════════

            var legendSb = new StringBuilder();
            legendSb.Append("<div class='legend-wrap'>");
            foreach (var lpn in loadPlan)
            {
                var bg   = Color(lpn);
                string zoneIcon = (lpn.Zone ?? "") == "REAR" ? "🔵" : (lpn.Zone ?? "") == "MID" ? "🩵" : "🟢";
                legendSb.Append($"<div class='legend-row'>");
                legendSb.Append($"<div class='legend-swatch' style='background:{bg};'></div>");
                legendSb.Append($"<div class='legend-info'>");
                legendSb.Append($"<span class='legend-order'>#{lpn.LoadOrder}</span>");
                legendSb.Append($"<span class='legend-code'>{H(lpn.LpnCode)}</span>");
                legendSb.Append($"<span class='legend-item'>{H(lpn.ItemName)} {zoneIcon}</span>");
                legendSb.Append($"<span class='legend-weight'>{lpn.WeightKg:0.#}kg</span>");
                legendSb.Append("</div></div>");
            }
            legendSb.Append("</div>");

            // ══════════════════════════════════════════════════════════════════
            //  GOONG NAVIGATION — lộ trình chi tiết
            // ══════════════════════════════════════════════════════════════════

            var navSb = new StringBuilder();
            var navInfo = result.Navigation;
            if (navInfo?.Legs?.Any() == true)
            {
                navSb.Append($"<div class='nav-summary'>");
                navSb.Append($"<span class='ns-item'>🛣️ Tổng: <b>{navInfo.TotalDistanceKm:F1} km</b></span>");
                navSb.Append($"<span class='ns-item'>⏱️ ~<b>{navInfo.TotalDurationMinutes} phút</b></span>");
                navSb.Append($"<span class='ns-item'>📍 <b>{navInfo.Legs.Count}</b> chặng</span>");
                navSb.Append("</div>");

                foreach (var leg in navInfo.Legs)
                {
                    navSb.Append("<div class='leg-block'>");
                    navSb.Append($"<div class='leg-header'>");
                    navSb.Append($"<span class='leg-num'>Chặng {leg.LegIndex}</span>");
                    navSb.Append($"<span class='leg-route'>{H(leg.FromAddress)} <span class='leg-arrow'>→</span> {H(leg.ToAddress)}</span>");
                    navSb.Append($"<span class='leg-dist'>{leg.DistanceKm:F1} km / {leg.DurationMinutes} phút</span>");
                    navSb.Append("</div>");

                    if (leg.Steps?.Any() == true)
                    {
                        navSb.Append("<div class='step-list'>");
                        foreach (var step in leg.Steps)
                        {
                            string icon = (step.Maneuver ?? "") switch
                            {
                                "turn-left"  or "turn-sharp-left"  => "↰",
                                "turn-right" or "turn-sharp-right" => "↱",
                                "roundabout" or "roundabout-left"  => "↻",
                                "uturn-left" or "uturn-right"      => "↩",
                                "merge"                            => "⤵",
                                "ramp-left"                        => "↖",
                                "ramp-right"                       => "↗",
                                _                                  => "↑"
                            };
                            navSb.Append($"<div class='step'><span class='step-icon'>{icon}</span><span class='step-text'>{H(step.Instruction)}</span><span class='step-dist'>{step.DistanceKm:F2} km</span></div>");
                        }
                        navSb.Append("</div>");
                    }
                    navSb.Append("</div>");
                }
            }
            else
            {
                // Fallback: timeline from Route stops
                navSb.Append("<div class='nav-fallback'>");
                var originAddr = result.Navigation?.Legs?.FirstOrDefault()?.FromAddress
                                 ?? result.Route?.Stops?.FirstOrDefault()?.Address
                                 ?? "Kho xuất phát";
                navSb.Append($"<div class='tl-node origin'><div class='tl-dot'></div><div class='tl-label'><b>Kho xuất phát</b><br/>{H(originAddr)}</div></div>");
                if (result.Route?.Stops != null)
                {
                    foreach (var stop in result.Route.Stops.OrderBy(s => s.Sequence))
                    {
                        navSb.Append($"<div class='tl-arrow'>▶</div>");
                        navSb.Append($"<div class='tl-node stop'><div class='tl-dot'></div><div class='tl-label'><b>Trạm {stop.Sequence}</b><br/>{H(stop.Address ?? "—")} ({stop.DistanceFromPreviousKm:F1} km)</div></div>");
                    }
                }
                navSb.Append("</div>");
            }

            // ══════════════════════════════════════════════════════════════════
            //  LIFO TABLE
            // ══════════════════════════════════════════════════════════════════

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

            // ══════════════════════════════════════════════════════════════════
            //  FULL HTML
            // ══════════════════════════════════════════════════════════════════

            return $@"<!DOCTYPE html>
<html lang='vi'>
<head>
<meta charset='UTF-8'>
<title>Lệnh Điều Động LIFO — ColdChainX</title>
<style>
  @page {{ size: A3 landscape; margin: 10mm 12mm; }}
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{ font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; background: #ECEFF1; color: #263238; }}

  /* ── Header ── */
  .page-header {{
    display: flex; align-items: center; justify-content: space-between;
    background: linear-gradient(135deg, #1A237E 0%, #283593 100%);
    color: #fff; padding: 10px 20px; border-radius: 6px; margin-bottom: 8px;
    box-shadow: 0 2px 6px rgba(0,0,0,0.2);
  }}
  .brand {{ font-size: 22px; font-weight: 900; letter-spacing: 2px; }}
  .brand span {{ color: #FF6F00; }}
  .doc-title {{ font-size: 13px; font-weight: 700; text-align: center; flex: 1; letter-spacing: 0.5px; }}
  .doc-meta {{ text-align: right; font-size: 10px; opacity: 0.85; line-height: 1.7; }}

  /* ── Info Bar ── */
  .info-bar {{ display: grid; grid-template-columns: repeat(5, 1fr); gap: 6px; margin-bottom: 8px; }}
  .ic {{ background: #fff; border-radius: 6px; padding: 8px 12px; border-left: 4px solid #FF6F00; box-shadow: 0 1px 4px rgba(0,0,0,0.08); }}
  .ic .lbl {{ font-size: 8px; text-transform: uppercase; color: #90A4AE; letter-spacing: .6px; }}
  .ic .val {{ font-size: 13px; font-weight: 800; margin-top: 2px; }}

  /* ── Section ── */
  .section {{ background: #fff; border-radius: 6px; padding: 10px 14px; margin-bottom: 8px; box-shadow: 0 1px 4px rgba(0,0,0,0.07); }}
  .sec-title {{ font-size: 9px; font-weight: 800; text-transform: uppercase; letter-spacing: 1px; color: #78909C; margin-bottom: 8px; padding-bottom: 6px; border-bottom: 1px solid #ECEFF1; display: flex; align-items: center; gap: 6px; }}
  .sec-title::before {{ content: ''; width: 14px; height: 3px; background: #1A237E; border-radius: 2px; flex-shrink: 0; }}

  /* ── Two-column layout ── */
  .two-col {{ display: grid; grid-template-columns: 1fr 1.6fr; gap: 8px; margin-bottom: 8px; }}

  /* ── Floor plan table ── */
  .plan-table {{ border-collapse: collapse; }}
  .plan-table td {{ padding: 0; }}
  .axis-cell {{ font-size: 8px; font-weight: 700; color: #78909C; text-align: center; padding: 2px 5px; min-width: 28px; }}
  .plan-cell {{ width: 54px; height: 44px; border: 2.5px solid #fff; text-align: center; vertical-align: middle; font-size: 8px; font-weight: 700; color: #fff; }}
  .empty-cell {{ background: #ECEFF1 !important; border: 2px dashed #CFD8DC !important; }}
  .plan-order {{ font-size: 12px; font-weight: 800; display: block; line-height: 1.2; text-shadow: 0 1px 3px rgba(0,0,0,.45); }}
  .plan-code  {{ font-size: 6.5px; opacity: .85; }}

  /* ── Legend ── */
  .legend-wrap {{ display: flex; flex-direction: column; gap: 4px; }}
  .legend-row {{ display: flex; align-items: center; gap: 6px; padding: 3px 0; border-bottom: 1px solid #F5F5F5; }}
  .legend-swatch {{ width: 20px; height: 20px; border-radius: 4px; flex-shrink: 0; border: 1px solid rgba(0,0,0,.15); }}
  .legend-info {{ display: flex; flex-wrap: wrap; gap: 4px; align-items: center; }}
  .legend-order {{ font-size: 11px; font-weight: 800; min-width: 22px; }}
  .legend-code  {{ font-size: 8px; color: #546E7A; font-family: monospace; }}
  .legend-item  {{ font-size: 10px; font-weight: 600; flex: 1; }}
  .legend-weight{{ font-size: 9px; color: #90A4AE; }}

  /* ── Goong Navigation ── */
  .nav-summary {{ display: flex; gap: 14px; padding: 6px 10px; background: #E8F5E9; border-radius: 5px; margin-bottom: 8px; border-left: 4px solid #43A047; }}
  .ns-item {{ font-size: 10px; color: #2E7D32; }}
  .leg-block {{ margin-bottom: 8px; border: 1px solid #E8EAF6; border-radius: 5px; overflow: hidden; }}
  .leg-header {{ display: flex; align-items: baseline; gap: 6px; flex-wrap: wrap; padding: 6px 10px; background: #E8EAF6; }}
  .leg-num {{ font-size: 9px; font-weight: 800; color: #1A237E; background: #C5CAE9; padding: 2px 7px; border-radius: 10px; flex-shrink: 0; }}
  .leg-route {{ font-size: 10px; color: #263238; font-weight: 600; flex: 1; }}
  .leg-arrow {{ color: #3F51B5; font-weight: 800; margin: 0 2px; }}
  .leg-dist {{ font-size: 9px; color: #546E7A; flex-shrink: 0; }}
  .step-list {{ padding: 4px 8px; display: flex; flex-direction: column; gap: 2px; }}
  .step {{ display: flex; align-items: baseline; gap: 5px; padding: 2px 0; border-bottom: 1px solid #F5F5F5; font-size: 9px; }}
  .step-icon {{ font-size: 11px; flex-shrink: 0; width: 14px; text-align: center; color: #5C6BC0; }}
  .step-text {{ flex: 1; color: #37474F; line-height: 1.4; }}
  .step-dist {{ color: #90A4AE; font-size: 8px; flex-shrink: 0; }}
  .nav-fallback {{ display: flex; align-items: flex-start; flex-wrap: wrap; gap: 4px; padding: 4px 0; }}
  .tl-node {{ display: flex; align-items: flex-start; gap: 5px; }}
  .tl-dot  {{ width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; margin-top: 3px; }}
  .tl-node.origin .tl-dot {{ background: #E53935; }}
  .tl-node.stop   .tl-dot {{ background: #1A237E; }}
  .tl-label {{ font-size: 9px; color: #546E7A; line-height: 1.4; max-width: 140px; }}
  .tl-arrow {{ color: #90A4AE; font-size: 11px; margin-top: 2px; }}

  /* ── Data Table ── */
  .data-table {{ width: 100%; border-collapse: collapse; font-size: 10px; }}
  .data-table thead th {{ background: #1A237E; color: #fff; padding: 7px 9px; text-align: left; font-size: 9px; letter-spacing: .3px; }}
  .data-table tbody td {{ padding: 6px 9px; border-bottom: 1px solid #F5F5F5; vertical-align: middle; }}
  .data-table tbody tr:nth-child(even) td {{ background: #FAFAFA; }}
  .data-table code {{ font-size: 9px; background: #ECEFF1; padding: 1px 4px; border-radius: 3px; }}
  .order-badge {{ display: inline-flex; align-items: center; justify-content: center; width: 26px; height: 26px; border-radius: 50%; font-size: 10px; font-weight: 800; color: #fff; }}
  .badge {{ display: inline-block; padding: 2px 7px; border-radius: 10px; font-size: 8px; font-weight: 700; }}

  /* ── Footer ── */
  .page-footer {{ text-align: center; font-size: 9px; color: #90A4AE; padding: 5px; margin-top: 4px; }}
</style>
</head>
<body>

<div class='page-header'>
  <div class='brand'>Cold<span>Chain</span>X</div>
  <div class='doc-title'>LỆNH ĐIỀU ĐỘNG &amp; SƠ ĐỒ XẾP HÀNG LIFO</div>
  <div class='doc-meta'>
    Trip: {result.TripId}<br/>
    Ngày: {DateTime.Now:dd/MM/yyyy HH:mm}
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
  <div class='sec-title'>🚛 Sơ đồ xếp hàng — Nhìn từ bên hông xe (LIFO)</div>
  {truckSvg}
</div>

<div class='two-col'>
  <div>
    <div class='section'>
      <div class='sec-title'>🗺️ Mặt bằng container (nhìn từ trên)</div>
      {topSb}
    </div>
    <div class='section'>
      <div class='sec-title'>🎨 Chú thích màu lô hàng</div>
      {legendSb}
    </div>
  </div>
  <div class='section'>
    <div class='sec-title'>📍 Lộ trình chi tiết (Goong Maps)</div>
    {navSb}
  </div>
</div>

<div class='section'>
  <div class='sec-title'>📋 Bảng lệnh bốc xếp — LIFO Load Plan</div>
  {tableSb}
</div>

<div class='page-footer'>
  ColdChainX — Tài liệu nội bộ — In lúc {DateTime.Now:dd/MM/yyyy HH:mm} (GMT+7) — Trip: {result.TripId}
</div>

</body>
</html>";
        }
    }
}
