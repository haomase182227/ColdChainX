namespace ColdChainX.Application.DTOs.Orders
{
    public class ReviewOrderRequest
    {
        public string Action { get; set; } = null!;
        public string? CustomerNote { get; set; }
        public List<ReviewOrderDocumentRequest> DocumentReviews { get; set; } = new();
    }

    public class ReviewOrderDocumentRequest
    {
        public Guid DocId { get; set; }
        public bool IsApproved { get; set; }
        public string? RejectReason { get; set; }
    }
}

