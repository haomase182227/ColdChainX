using System.IO;
using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.Interfaces
{
    public interface IFileService
    {
        Task<string> UploadFileAsync(IFormFile file);
        Task<string> UploadFileAsync(Stream stream, string fileName);
        Task<string> UploadFileAsync(byte[] fileBytes, string fileName);
        string GetSignedUrl(string publicId);
    }
}
