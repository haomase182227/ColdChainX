using System.Collections.Generic;

namespace ColdChainX.Application.DTOs
{
    public class UserListResponse
    {
        public List<UserProfileDto> Items { get; set; } = new List<UserProfileDto>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
    }
}
