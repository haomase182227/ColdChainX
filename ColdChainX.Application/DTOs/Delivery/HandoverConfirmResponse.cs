using System;

namespace ColdChainX.Application.DTOs.Delivery;

/// <summary>
/// Kết quả nghiệm thu hàng và xác nhận chữ ký khách.
/// </summary>
public class HandoverConfirmResponse
{
    /// <summary>ID của ePOD vừa được tạo. Dùng để gọi bước thu tiền COD tiếp theo.</summary>
    public Guid EpodId { get; set; }

    /// <summary>Thời điểm khách ký nhận hàng (UTC).</summary>
    public DateTime HandoverConfirmedAt { get; set; }

    /// <summary>Trạng thái tổng quát của đơn hàng: DELIVERED / RETURNED / PARTIALLY_DELIVERED.</summary>
    public string OrderStatus { get; set; } = null!;

    /// <summary>Số tiền COD cần thu từ khách dựa trên các kiện đã nhận.</summary>
    public decimal CodAmountDue { get; set; }

    /// <summary>Link PDF Biên bản giao nhận hàng (có chữ ký + danh sách kiện).</summary>
    public string HandoverPdfUrl { get; set; } = null!;

    /// <summary>
    /// Bước tiếp theo cần thực hiện.
    /// Ví dụ: "POST /api/epods/{epodId}/payments để thu tiền COD"
    /// </summary>
    public string NextStep { get; set; } = null!;
}
