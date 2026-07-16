using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ColdChainX.Application.Interfaces;
using ColdChainX.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace ColdChainX.Infrastructure.Services
{
    public class SimplePdfService : IPdfService
    {
        private const string ChromeExecutablePathEnv = "PDF_CHROME_EXECUTABLE_PATH";

        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFileService _fileService;
        private readonly ApplicationDbContext _context;

        public SimplePdfService(
            IWebHostEnvironment environment,
            IHttpContextAccessor httpContextAccessor,
            IFileService fileService,
            ApplicationDbContext context)
        {
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
            _fileService = fileService;
            _context = context;
        }

        public async Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber)
            => await SavePdfAsync(htmlContent, contractNumber);

        public async Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber)
            => await SavePdfAsync(htmlContent, quoteNumber);

        public async Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode)
            => await SavePdfAsync(htmlContent, receiptCode, "receipt");

        public async Task<string> SaveWaybillPdfAsync(string htmlContent, string fileCode)
            => await SavePdfAsync(htmlContent, fileCode, "waybill");

        public async Task<string> SaveLifoMapPdfAsync(string htmlContent, string tripId)
            => await SavePdfAsync(htmlContent, tripId, "lifo");

        public async Task<string> SaveLoadPlanPdfAsync(string htmlContent, string tripId)
            => await SavePdfAsync(htmlContent, tripId, "loadplan");

        public async Task<string> SaveInvoicePdfAsync(string htmlContent, string invoiceCode)
            => await SavePdfAsync(htmlContent, "invoices", invoiceCode, "invoice");

        public async Task<string> SaveContractAppendixPdfAsync(string htmlContent, string appendixNumber)
            => await SavePdfAsync(htmlContent, "appendices", appendixNumber, "appendix");

        public async Task<string> SaveInboundReturnSlipPdfAsync(string htmlContent, string slipCode)
            => await SavePdfAsync(htmlContent, "returnslips", slipCode, "returnslip");

        public async Task<string> GenerateManifestPdfAsync(Guid tripId)
        {
            var trip = await _context.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.TripDrivers).ThenInclude(td => td.Driver)
                .Include(t => t.OriginLocation)
                .Include(t => t.DestinationLocation)
                .Include(t => t.TripStops).ThenInclude(ts => ts.Location)
                .FirstOrDefaultAsync(t => t.TripId == tripId)
                ?? throw new KeyNotFoundException($"KhÃ´ng tÃ¬m tháº¥y chuyáº¿n hÃ ng {tripId}.");

            var lpns = await _context.Lpns
                .Include(l => l.Order)
                .Where(l => l.TripId == tripId)
                .OrderBy(l => l.LpnCode)
                .ToListAsync();

            var issuedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
            sb.AppendLine("<style>body{font-family:Arial,sans-serif;font-size:12px;padding:20px}");
            sb.AppendLine("h1{font-size:16px;text-align:center;text-transform:uppercase}");
            sb.AppendLine(".info{margin-bottom:12px}.info td{padding:3px 8px}");
            sb.AppendLine("table.lpn{width:100%;border-collapse:collapse;margin-top:12px}");
            sb.AppendLine("table.lpn th,table.lpn td{border:1px solid #333;padding:5px 8px;text-align:left}");
            sb.AppendLine("table.lpn th{background:#eee;font-weight:bold}");
            sb.AppendLine(".footer{margin-top:30px;display:flex;justify-content:space-between}");
            sb.AppendLine(".sign-box{text-align:center;width:200px}");
            sb.AppendLine(".sign-box p{border-top:1px solid #333;margin-top:50px;padding-top:4px}</style></head><body>");

            sb.AppendLine("<h1>BiÃªn Báº£n HÃ ng GhÃ©p Chuyáº¿n (Manifest)</h1>");
            sb.AppendLine("<table class='info'>");
            sb.AppendLine($"<tr><td><b>Sá»‘ chuyáº¿n:</b></td><td>{tripId.ToString()[..8].ToUpper()}</td>");
            sb.AppendLine($"<td><b>NgÃ y láº­p:</b></td><td>{issuedAt}</td></tr>");
            sb.AppendLine($"<tr><td><b>Xe:</b></td><td>{trip.Vehicle?.TruckPlate ?? "N/A"} ({trip.Vehicle?.VehicleType ?? ""})</td>");
            var manifestPrimary = trip.TripDrivers.OrderBy(td => td.DriverRole == "PRIMARY" ? 0 : 1).Select(td => td.Driver).FirstOrDefault(d => d != null);
            var manifestDriverNames = trip.TripDrivers.Count > 0 ? string.Join(", ", trip.TripDrivers.Select(td => td.Driver != null ? td.Driver.FullName : null).Where(n => !string.IsNullOrEmpty(n))) : "N/A";
            sb.AppendLine($"<td><b>TÃ i xáº¿:</b></td><td>{manifestDriverNames} â€” {manifestPrimary?.PhoneNumber ?? ""}</td></tr>");
            sb.AppendLine($"<tr><td><b>Kho xuáº¥t:</b></td><td>{trip.OriginLocation?.Address ?? "N/A"}</td>");
            sb.AppendLine($"<td><b>Äiá»ƒm Ä‘áº¿n:</b></td><td>{trip.DestinationLocation?.Address ?? "N/A"}</td></tr>");
            sb.AppendLine($"<tr><td><b>Sá»‘ seal:</b></td><td>{trip.SealNumber ?? "â€”"}</td>");
            sb.AppendLine($"<td><b>Tráº¡ng thÃ¡i:</b></td><td>{trip.Status}</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<table class='lpn'>");
            sb.AppendLine("<thead><tr><th>STT</th><th>MÃ£ LPN</th><th>ÄÆ¡n hÃ ng</th><th>HÃ ng hÃ³a</th><th>SL</th><th>Trá»ng lÆ°á»£ng (kg)</th><th>CBM (mÂ³)</th><th>Nhiá»‡t Ä‘á»™</th></tr></thead><tbody>");
            var totalWeight = 0m; var totalCbm = 0m;
            for (int i = 0; i < lpns.Count; i++)
            {
                var l = lpns[i];
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{l.LpnCode}</td><td>{l.Order?.TrackingCode ?? "N/A"}</td>");
                sb.AppendLine($"<td>{l.Order?.ItemName ?? "N/A"}</td><td>{l.Quantity}</td>");
                sb.AppendLine($"<td>{l.ActualWeightKg:F2}</td><td>{l.ActualCbm:F3}</td><td>{l.Order?.TempCondition ?? "AMBIENT"}</td></tr>");
                totalWeight += l.ActualWeightKg; totalCbm += l.ActualCbm;
            }
            sb.AppendLine($"<tr><td colspan='5'><b>Tá»•ng cá»™ng</b></td><td><b>{totalWeight:F2}</b></td><td><b>{totalCbm:F3}</b></td><td></td></tr>");
            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<div class='footer'>");
            sb.AppendLine("<div class='sign-box'><p>NgÆ°á»i láº­p biÃªn báº£n</p></div>");
            sb.AppendLine("<div class='sign-box'><p>Thá»§ kho</p></div>");
            sb.AppendLine("<div class='sign-box'><p>TÃ i xáº¿</p></div>");
            sb.AppendLine("</div></body></html>");

            return await SavePdfAsync(sb.ToString(), tripId.ToString(), "manifest");
        }

        public async Task<string> GenerateOutboundTicketPdfAsync(Guid tripId)
        {
            var trip = await _context.MasterTrips
                .Include(t => t.Vehicle)
                .Include(t => t.TripDrivers).ThenInclude(td => td.Driver)
                .Include(t => t.OriginLocation)
                .FirstOrDefaultAsync(t => t.TripId == tripId)
                ?? throw new KeyNotFoundException($"KhÃ´ng tÃ¬m tháº¥y chuyáº¿n hÃ ng {tripId}.");

            var lpns = await _context.Lpns
                .Include(l => l.Order)
                .Where(l => l.TripId == tripId)
                .OrderBy(l => l.LpnCode)
                .ToListAsync();

            var issuedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
            sb.AppendLine("<style>body{font-family:Arial,sans-serif;font-size:12px;padding:20px}");
            sb.AppendLine("h1{font-size:16px;text-align:center;text-transform:uppercase}");
            sb.AppendLine("h2{font-size:13px;text-align:center;margin-top:0}");
            sb.AppendLine(".info{margin-bottom:12px}.info td{padding:3px 8px}");
            sb.AppendLine("table.lpn{width:100%;border-collapse:collapse;margin-top:12px}");
            sb.AppendLine("table.lpn th,table.lpn td{border:1px solid #333;padding:5px 8px}");
            sb.AppendLine("table.lpn th{background:#eee;font-weight:bold;text-align:center}");
            sb.AppendLine(".footer{margin-top:30px;display:flex;justify-content:space-between}");
            sb.AppendLine(".sign-box{text-align:center;width:200px}");
            sb.AppendLine(".sign-box p{border-top:1px solid #333;margin-top:50px;padding-top:4px}</style></head><body>");

            sb.AppendLine("<h1>Phiáº¿u Xuáº¥t Kho</h1>");
            sb.AppendLine($"<h2>Sá»‘: XK-{tripId.ToString()[..8].ToUpper()} &nbsp;|&nbsp; NgÃ y: {issuedAt}</h2>");

            sb.AppendLine("<table class='info'>");
            sb.AppendLine($"<tr><td><b>Kho xuáº¥t:</b></td><td>{trip.OriginLocation?.Address ?? "N/A"}</td>");
            sb.AppendLine($"<td><b>Biá»ƒn sá»‘ xe:</b></td><td>{trip.Vehicle?.TruckPlate ?? "N/A"}</td></tr>");
            sb.AppendLine($"<tr><td><b>TÃ i xáº¿:</b></td><td>{(trip.TripDrivers.Count > 0 ? string.Join(", ", trip.TripDrivers.Select(td => td.Driver != null ? td.Driver.FullName : null).Where(n => !string.IsNullOrEmpty(n))) : "N/A")}</td>");
            sb.AppendLine($"<td><b>Sá»‘ seal:</b></td><td>{trip.SealNumber ?? "â€”"}</td></tr>");
            sb.AppendLine($"<tr><td><b>Giá» xuáº¥t:</b></td><td>{issuedAt}</td>");
            sb.AppendLine($"<td><b>Tá»•ng LPN:</b></td><td>{lpns.Count}</td></tr>");
            sb.AppendLine("</table>");

            sb.AppendLine("<table class='lpn'>");
            sb.AppendLine("<thead><tr><th>STT</th><th>MÃ£ LPN</th><th>MÃ£ váº­n Ä‘Æ¡n</th><th>HÃ ng hÃ³a</th><th>Sá»‘ lÆ°á»£ng</th><th>KG</th><th>CBM</th></tr></thead><tbody>");
            var tw = 0m; var tc = 0m;
            for (int i = 0; i < lpns.Count; i++)
            {
                var l = lpns[i];
                sb.AppendLine($"<tr><td style='text-align:center'>{i + 1}</td><td>{l.LpnCode}</td>");
                sb.AppendLine($"<td>{l.Order?.TrackingCode ?? "N/A"}</td><td>{l.Order?.ItemName ?? "N/A"}</td>");
                sb.AppendLine($"<td style='text-align:center'>{l.Quantity}</td><td style='text-align:right'>{l.ActualWeightKg:F2}</td><td style='text-align:right'>{l.ActualCbm:F3}</td></tr>");
                tw += l.ActualWeightKg; tc += l.ActualCbm;
            }
            sb.AppendLine($"<tr><td colspan='5' style='text-align:right'><b>Tá»•ng</b></td><td style='text-align:right'><b>{tw:F2}</b></td><td style='text-align:right'><b>{tc:F3}</b></td></tr>");
            sb.AppendLine("</tbody></table>");

            sb.AppendLine("<div class='footer'>");
            sb.AppendLine("<div class='sign-box'><p>Thá»§ kho (Xuáº¥t)</p></div>");
            sb.AppendLine("<div class='sign-box'><p>NgÆ°á»i nháº­n hÃ ng / TÃ i xáº¿</p></div>");
            sb.AppendLine("<div class='sign-box'><p>Äiá»u phá»‘i viÃªn</p></div>");
            sb.AppendLine("</div></body></html>");

            return await SavePdfAsync(sb.ToString(), tripId.ToString(), "phieu-xuat-kho");
        }

        private async Task<string> SavePdfAsync(string htmlContent, string fileCode, string prefix = "waybill")
        {
            return await SavePdfAsync(htmlContent, string.Empty, fileCode, prefix);
        }

        private async Task<string> SavePdfAsync(string htmlContent, string folderName, string fileCode, string prefix = "waybill")
        {
            var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var fileName = $"{prefix}_{SanitizeFileName(fileCode)}.pdf";
            var normalizedHtml = NormalizeHtmlForLocalAssets(htmlContent, root);

            await using var browser = await LaunchBrowserAsync();
            await using var page = await browser.NewPageAsync();
            await page.SetContentAsync(normalizedHtml);
            await page.EvaluateExpressionHandleAsync("document.fonts.ready");

            var pdfBytes = await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true,
                MarginOptions = new MarginOptions
                {
                    Top = "12mm",
                    Bottom = "12mm",
                    Left = "10mm",
                    Right = "10mm"
                }
            });

            if (!string.IsNullOrWhiteSpace(folderName))
            {
                var targetFolder = Path.Combine(root, folderName);
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }
                var localPath = Path.Combine(targetFolder, fileName);
                await File.WriteAllBytesAsync(localPath, pdfBytes);
            }

            return await _fileService.UploadFileAsync(pdfBytes, fileName);
        }

                public async Task<string> SavePdfFromUrlAsync(string url, string fileId, string prefix)
        {
            var fileName = $"{prefix}_{SanitizeFileName(fileId)}.pdf";

            await using var browser = await LaunchBrowserAsync();
            await using var page = await browser.NewPageAsync();
            
            // Navigate to the URL and wait until network is mostly idle
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
            });

            // Wait an additional 2 seconds for 3D scripts/charts to fully render
            await Task.Delay(2000);

            var pdfBytes = await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true,
                MarginOptions = new MarginOptions
                {
                    Top = "12mm",
                    Bottom = "12mm",
                    Left = "10mm",
                    Right = "10mm"
                }
            });

            return await _fileService.UploadFileAsync(pdfBytes, fileName);
        }

        private static async Task<IBrowser> LaunchBrowserAsync()
        {
            var executablePath = Environment.GetEnvironmentVariable(ChromeExecutablePathEnv);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                var browserFetcher = new BrowserFetcher();
                var installedBrowser = await browserFetcher.DownloadAsync();
                executablePath = installedBrowser.GetExecutablePath();
            }

            return await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = executablePath,
                Args =
                [
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--no-zygote"
                ]
            });
        }

        private static string NormalizeHtmlForLocalAssets(string html, string webRootPath)
        {
            var webRootUri = new Uri(Path.GetFullPath(webRootPath) + Path.DirectorySeparatorChar);
            var normalized = Regex.Replace(
                html,
                "(?<attribute>src|href)=[\"']/(?<path>[^\"']+)[\"']",
                match =>
                {
                    var attribute = match.Groups["attribute"].Value;
                    var relativePath = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
                    var fileUri = new Uri(Path.Combine(webRootPath, relativePath)).AbsoluteUri;
                    return $"{attribute}=\"{fileUri}\"";
                },
                RegexOptions.IgnoreCase);

            if (normalized.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                return Regex.Replace(
                    normalized,
                    "<head>",
                    $"<head><base href=\"{webRootUri.AbsoluteUri}\"><meta charset=\"UTF-8\">",
                    RegexOptions.IgnoreCase);

            return $"<base href=\"{webRootUri.AbsoluteUri}\"><meta charset=\"UTF-8\">{normalized}";
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        }
    }
}
