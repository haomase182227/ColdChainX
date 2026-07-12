namespace ColdChainX.Shared.Responses
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public object? Errors { get; set; }
        public object? Meta { get; set; }

        public static ApiResponse<T> SuccessResponse(T? data, string? message = null, int statusCode = 200, object? meta = null)
            => new ApiResponse<T> 
            { 
                Success = true, 
                StatusCode = statusCode,
                Message = message, 
                Data = data,
                Errors = null,
                Meta = meta
            };

        public static ApiResponse<T> Failure(string? message, int statusCode = 400, object? errors = null)
            => new ApiResponse<T> 
            { 
                Success = false, 
                StatusCode = statusCode,
                Message = message, 
                Data = default,
                Errors = errors,
                Meta = null
            };
    }
}
