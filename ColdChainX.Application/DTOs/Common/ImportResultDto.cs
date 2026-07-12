using System.Collections.Generic;

namespace ColdChainX.Application.DTOs.Common
{
    public class ImportResultDto
    {
        public int TotalRows { get; set; }
        public int SuccessfulRows { get; set; }
        public int FailedRows { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
