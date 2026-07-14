using System;
using System.Collections.Generic;

namespace ColdChainX.Shared.Responses
{
    public class PagedList<T>
    {
        public IReadOnlyCollection<T> Items { get; set; } = null!;
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalRecords { get; set; }
        public int TotalPages { get; set; }

        public PagedList(IReadOnlyCollection<T> items, int totalRecords, int pageNumber, int pageSize)
        {
            Items = items;
            TotalRecords = totalRecords;
            PageNumber = pageNumber;
            PageSize = pageSize;
            TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalRecords / (double)pageSize) : 0;
        }
    }
}
