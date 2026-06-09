using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ColdChainX.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace ColdChainX.Infrastructure.Services
{
    public class SimplePdfService : IPdfService
    {
        private readonly IWebHostEnvironment _environment;

        public SimplePdfService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> SaveContractPdfAsync(string htmlContent, string contractNumber)
            => await SavePdfAsync(htmlContent, "contracts", contractNumber);

        public async Task<string> SaveQuotationPdfAsync(string htmlContent, string quoteNumber)
            => await SavePdfAsync(htmlContent, "quotations", quoteNumber);

        private async Task<string> SavePdfAsync(string htmlContent, string folderName, string fileCode)
        {
            var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
            var folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);

            var fileName = $"{SanitizeFileName(fileCode)}.pdf";
            var fullPath = Path.Combine(folder, fileName);
            var text = ExtractText(htmlContent);
            var pdfBytes = CreatePdf(text);

            await File.WriteAllBytesAsync(fullPath, pdfBytes);
            return $"/{folderName}/{fileName}";
        }

        private static string ExtractText(string html)
        {
            var normalized = Regex.Replace(html, @"<(br|/p|/div|/tr|/h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, "<[^>]+>", " ");
            normalized = WebUtility.HtmlDecode(normalized);
            normalized = Regex.Replace(normalized, @"[ \t]+", " ");
            normalized = Regex.Replace(normalized, @"\n\s+", "\n");
            return normalized.Trim();
        }

        private static byte[] CreatePdf(string text)
        {
            var lines = WrapLines(RemoveDiacritics(text), 95).Take(60).ToList();
            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine("BT");
            contentBuilder.AppendLine("/F1 11 Tf");
            contentBuilder.AppendLine("50 790 Td");

            foreach (var line in lines)
            {
                contentBuilder.Append('(').Append(EscapePdfText(line)).AppendLine(") Tj");
                contentBuilder.AppendLine("0 -16 Td");
            }

            contentBuilder.AppendLine("ET");
            var stream = contentBuilder.ToString();

            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}endstream"
            };

            using var ms = new MemoryStream();
            WriteAscii(ms, "%PDF-1.4\n");
            var offsets = new List<long> { 0 };

            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                WriteAscii(ms, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            var xrefOffset = ms.Position;
            WriteAscii(ms, $"xref\n0 {objects.Count + 1}\n");
            WriteAscii(ms, "0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
                WriteAscii(ms, $"{offset:0000000000} 00000 n \n");

            WriteAscii(ms, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
            return ms.ToArray();
        }

        private static IEnumerable<string> WrapLines(string text, int maxLength)
        {
            foreach (var paragraph in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var line = new StringBuilder();

                foreach (var word in words)
                {
                    if (line.Length + word.Length + 1 > maxLength)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }

                    if (line.Length > 0)
                        line.Append(' ');
                    line.Append(word);
                }

                if (line.Length > 0)
                    yield return line.ToString();
            }
        }

        private static string EscapePdfText(string text)
            => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        private static void WriteAscii(Stream stream, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string SanitizeFileName(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
