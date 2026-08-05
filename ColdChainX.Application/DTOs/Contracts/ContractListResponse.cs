namespace ColdChainX.Application.DTOs.Contracts;

public class ContractListResponse
{
    public IReadOnlyCollection<ContractListItemResponse> Items { get; set; } = Array.Empty<ContractListItemResponse>();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class ContractListItemResponse
{
    public Guid ContractId { get; set; }
    public Guid? OrderId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? TrackingCode { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? UploadedSignedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
}
