using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using ColdChainX.Application.Interfaces;

namespace ColdChainX.Infrastructure.Services
{
    public class FileService : IFileService
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;

        private readonly IWebHostEnvironment _environment;

        public FileService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<string> UploadFileAsync(IFormFile file)
        {
            if (file.Length == 0)
                throw new InvalidOperationException("Uploaded file is empty");

            if (file.Length > MaxFileSizeBytes)
                throw new InvalidOperationException("Uploaded file must be smaller than 10MB");

            var uploadsPath = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads");
            Directory.CreateDirectory(uploadsPath);

            var extension = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadsPath, fileName);

            await using var stream = new FileStream(fullPath, FileMode.CreateNew);
            await file.CopyToAsync(stream);

            return $"/uploads/{fileName}";
        }
    }
}
