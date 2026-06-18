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
        private readonly IFileService _fileService;

        public SimplePdfService(IWebHostEnvironment environment, IHttpContextAccessor httpContextAccessor, IFileService fileService)
        {
            _environment = environment;
            _httpContextAccessor = httpContextAccessor;
            _fileService = fileService;
        }

        public async Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber)
            => await SavePdfAsync(htmlContent, "contracts", contractNumber);

        public async Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber)
            => await SavePdfAsync(htmlContent, "quotations", quoteNumber);

        public async Task<string> SaveWarehouseReceiptPdfAsync(string htmlContent, string receiptCode)
            => await SavePdfAsync(htmlContent, "receipts", receiptCode);

        public async Task<string> SaveWaybillPdfAsync(string htmlContent, string tripId)
            => await SavePdfAsync(htmlContent, "waybills", tripId);

        private async Task<string> SavePdfAsync(string htmlContent, string folderName, string fileCode)
        {
            var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var fileName = $"{SanitizeFileName(fileCode)}.pdf";
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

            // Upload directly to Cloudinary via IFileService
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
