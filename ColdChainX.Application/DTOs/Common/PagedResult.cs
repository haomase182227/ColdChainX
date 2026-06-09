namespace ColdChainX.Application.DTOs.Common
{
    public class PagedResult<T>
    {
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public IReadOnlyCollection<T> Data { get; set; } = Array.Empty<T>();

        public static PagedResult<T> Create(IReadOnlyCollection<T> data, int totalRecords, int pageNumber, int pageSize)
        {
            var safePageSize = pageSize <= 0 ? 10 : pageSize;
            var safePageNumber = pageNumber <= 0 ? 1 : pageNumber;

            return new PagedResult<T>
            {
                TotalRecords = totalRecords,
                TotalPages = (int)Math.Ceiling(totalRecords / (double)safePageSize),
                CurrentPage = safePageNumber,
                PageSize = safePageSize,
                Data = data
            };
        }
    }
}
