namespace ColdChainX.Application.DTOs.Common
{
    /// <summary>
    /// Generic paginated result wrapper for list-based API responses.
    /// </summary>
    /// <typeparam name="T">The type of items contained in the result page.</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// Total number of records matching the query across all pages.
        /// </summary>
        public int TotalRecords { get; set; }

        /// <summary>
        /// Total number of pages available.
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Index of the currently returned page (1-based).
        /// </summary>
        public int CurrentPage { get; set; }

        /// <summary>
        /// Number of records per page.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Records in the current page.
        /// </summary>
        public IReadOnlyCollection<T> Data { get; set; } = Array.Empty<T>();

        /// <summary>
        /// Creates a new <see cref="PagedResult{T}"/> from data and pagination parameters.
        /// </summary>
        /// <param name="data">The records for this page.</param>
        /// <param name="totalRecords">Total matching records.</param>
        /// <param name="pageNumber">Requested page number.</param>
        /// <param name="pageSize">Requested page size.</param>
        /// <returns>A populated <see cref="PagedResult{T}"/>.</returns>
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
