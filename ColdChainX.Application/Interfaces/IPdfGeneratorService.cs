namespace ColdChainX.Application.Interfaces;

public interface IPdfGeneratorService
{
    Task<byte[]> GeneratePdfAsync<T>(string templateName, T data);
}
