namespace ColdChainX.Application.DTOs
{
    /// <summary>
    /// Request tạo tài khoản Loader (Người vận chuyển hàng lên container).
    /// Chỉ cần thông tin user cơ bản, không cần bảng phụ.
    /// </summary>
    public class CreateLoaderRequest
    {
        public string? Username { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string? Phone { get; set; }
    }
}
