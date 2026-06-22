using ColdChainX.Application.Interfaces;
using HandlebarsDotNet;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using System.Reflection;

namespace ColdChainX.Infrastructure.Services;

public class PdfGeneratorService : IPdfGeneratorService
{
    private readonly string _templateDirectory;

    public PdfGeneratorService()
    {
        _templateDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Templates");
    }

    public async Task<byte[]> GeneratePdfAsync<T>(string templateName, T data)
    {
        var templatePath = Path.Combine(_templateDirectory, $"{templateName}.html");
        
        if (!File.Exists(templatePath))
            throw new FileNotFoundException($"Template {templateName} not found at {templatePath}");

        var htmlContent = await File.ReadAllTextAsync(templatePath);

        // Compile and render Handlebars template
        var template = Handlebars.Compile(htmlContent);
        var finalHtml = template(data);

        var executablePath = Environment.GetEnvironmentVariable("PDF_CHROME_EXECUTABLE_PATH");
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            var browserFetcher = new BrowserFetcher();
            var installedBrowser = await browserFetcher.DownloadAsync();
            executablePath = installedBrowser.GetExecutablePath();
        }

        // Generate PDF
        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
            Args = new[] { 
                "--no-sandbox", 
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
                "--no-zygote" 
            }
        });
        
        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(finalHtml);

        var pdfBytes = await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions
            {
                Top = "10mm",
                Bottom = "10mm",
                Left = "10mm",
                Right = "10mm"
            }
        });

        return pdfBytes;
    }
}
