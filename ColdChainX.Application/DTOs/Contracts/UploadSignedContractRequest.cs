using Microsoft.AspNetCore.Http;

namespace ColdChainX.Application.DTOs.Contracts
{
    public class UploadSignedContractRequest
    {
        public IFormFile SignedFile { get; set; } = null!;
    }
}
