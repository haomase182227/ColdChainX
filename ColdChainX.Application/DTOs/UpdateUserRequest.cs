using ColdChainX.Core.Enums;

namespace ColdChainX.Application.DTOs
{
    public class UpdateUserRequest
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        /// <summary>
        /// Để trống nếu không muốn đổi mật khẩu
        /// </summary>
        public string? NewPassword { get; set; }
    }
}
