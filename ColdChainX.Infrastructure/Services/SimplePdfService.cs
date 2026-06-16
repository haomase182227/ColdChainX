using System.Text.RegularExpressions;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace ColdChainX.Infrastructure.Services
{
    public class SimplePdfService : IPdfService
    {
        private const string ChromeExecutablePathEnv = "PDF_CHROME_EXECUTABLE_PATH";

        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SimplePdfService(IWebHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
        {
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber)
            => await SavePdfAsync(htmlContent, "contracts", contractNumber);

        public async Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber)
            => await SavePdfAsync(htmlContent, "quotations", quoteNumber);

        public async Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode)
            => await SavePdfAsync(htmlContent, "receipts", receiptCode);

        private async Task<string> SavePdfAsync(string htmlContent, string folderName, string fileCode)
        {
            var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);

            var fileName = $"{SanitizeFileName(fileCode)}.pdf";
            var fullPath = Path.Combine(folder, fileName);
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

            await File.WriteAllBytesAsync(fullPath, pdfBytes);

            // Build absolute URL so the link works from any client/browser
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request != null)
            {
                var baseUrl = $"{request.Scheme}://{request.Host}";
                return $"{baseUrl}/{folderName}/{fileName}";
            }

            // Fallback to relative path if no HTTP context (e.g., background jobs)
            return $"/{folderName}/{fileName}";
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
