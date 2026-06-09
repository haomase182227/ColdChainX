using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.Interfaces
{
    public interface IFileService
    {
        Task<string> UploadFileAsync(IFormFile file);
    }
}
