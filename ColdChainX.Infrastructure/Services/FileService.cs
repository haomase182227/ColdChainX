using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ColdChainX.Application.Interfaces;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace ColdChainX.Infrastructure.Services
{
    public class FileService : IFileService
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;
        private readonly Cloudinary _cloudinary;

        public FileService(IConfiguration configuration)
        {
            // Ưu tiên đọc từ CLOUDINARY_URL env var (format: cloudinary://API_KEY:API_SECRET@CLOUD_NAME)
            var cloudinaryUrl = Environment.GetEnvironmentVariable("CLOUDINARY_URL");
            if (!string.IsNullOrWhiteSpace(cloudinaryUrl))
            {
                _cloudinary = new Cloudinary(cloudinaryUrl);
                return;
            }

            // Fallback: đọc từ appsettings / environment variables (Cloudinary__CloudName, ...)
            var cloudName = configuration["Cloudinary:CloudName"] 
                ?? throw new InvalidOperationException("Cloudinary:CloudName is not configured.");
            var apiKey = configuration["Cloudinary:ApiKey"] 
                ?? throw new InvalidOperationException("Cloudinary:ApiKey is not configured.");
            var apiSecret = configuration["Cloudinary:ApiSecret"] 
                ?? throw new InvalidOperationException("Cloudinary:ApiSecret is not configured.");

            var account = new Account(cloudName, apiKey, apiSecret);
            _cloudinary = new Cloudinary(account);
        }

        public async Task<string> UploadFileAsync(IFormFile file)
        {
            if (file.Length == 0)
                throw new InvalidOperationException("Uploaded file is empty");

            if (file.Length > MaxFileSizeBytes)
                throw new InvalidOperationException("Uploaded file must be smaller than 10MB");

            using var stream = file.OpenReadStream();
            return await UploadStreamToCloudinaryAsync(stream, file.FileName);
        }

        public async Task<string> UploadFileAsync(Stream stream, string fileName)
        {
            return await UploadStreamToCloudinaryAsync(stream, fileName);
        }

        public async Task<string> UploadFileAsync(byte[] fileBytes, string fileName)
        {
            using var stream = new MemoryStream(fileBytes);
            return await UploadStreamToCloudinaryAsync(stream, fileName);
        }

        private async Task<string> UploadStreamToCloudinaryAsync(Stream stream, string fileName)
        {
            var isPdf = fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
            var folder = "coldchainx";
            
            var cleanFileName = isPdf ? fileName : Path.GetFileNameWithoutExtension(fileName);
            var sanitizedName = string.Concat(cleanFileName.Split(Path.GetInvalidFileNameChars()));
            
            var isLifo = fileName.StartsWith("lifo_", StringComparison.OrdinalIgnoreCase) || 
                            (fileName.EndsWith(".pdf") && fileName.Contains("-"));
            
            var publicId = isLifo ? sanitizedName : (isPdf 
                ? $"{Path.GetFileNameWithoutExtension(sanitizedName)}_{Guid.NewGuid():N}.pdf" 
                : $"{sanitizedName}_{Guid.NewGuid():N}");

            if (isPdf)
            {
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(sanitizedName, stream),
                    Folder = folder,
                    PublicId = publicId
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                if (uploadResult.Error != null)
                    throw new InvalidOperationException($"Cloudinary upload failed: {uploadResult.Error.Message}");
                return uploadResult.SecureUrl.ToString();
            }
            else
            {
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(sanitizedName, stream),
                    Folder = folder,
                    PublicId = publicId
                };
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                if (uploadResult.Error != null)
                    throw new InvalidOperationException($"Cloudinary upload failed: {uploadResult.Error.Message}");
                return uploadResult.SecureUrl.ToString();
            }
        }
    }
}
