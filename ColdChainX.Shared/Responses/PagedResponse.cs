namespace ColdChainX.Shared.Responses
{
    public class PagedResponse<T> : ApiResponse<T>
    {
        public static PagedResponse<T> SuccessPagedResponse(T? data, int pageIndex, int pageSize, int totalCount, string? message = null)
        {
            var totalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
            
            var metaData = new 
            {
                pageIndex = pageIndex,
                pageSize = pageSize,
                totalCount = totalCount,
                totalPages = totalPages
            };

            return new PagedResponse<T>
            {
                Success = true,
                StatusCode = 200,
                Message = message,
                Data = data,
                Errors = null,
                Meta = metaData
            };
        }
    }
}
