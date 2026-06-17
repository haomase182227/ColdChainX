using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ColdChainX.API.Swagger
{
    public class UpdateContractDraftOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var relativePath = context.ApiDescription.RelativePath?.TrimEnd('/');
            var httpMethod = context.ApiDescription.HttpMethod;

            if (!string.Equals(httpMethod, "PUT", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(relativePath, "api/contracts/{contractId}", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            operation.Description =
                "Cập nhật nội dung HTML của hợp đồng nháp.\n\n" +
                "### Cách 1 — Dễ nhất: `text/plain` (chọn ở dropdown \"Media type\")\n" +
                "Paste thẳng raw HTML vào body textarea, **không cần bọc JSON**.\n\n" +
                "### Cách 2: `application/json`\n" +
                "Gửi `{ \"editedHtmlContent\": \"<html>...</html>\" }`. " +
                "Lưu ý: nếu paste HTML trực tiếp vào value của JSON mà không escape, API vẫn cố tự trích xuất. " +
                "Để chắc chắn, hãy dùng Cách 1.";

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content =
                {
                    ["text/plain"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "string",
                            Description = "Paste toàn bộ raw HTML vào đây. Không cần bọc JSON."
                        },
                        Example = new OpenApiString("<!DOCTYPE html><html lang=\"vi\"><head></head><body><h1>Hợp đồng</h1></body></html>")
                    },
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Required = new HashSet<string> { "editedHtmlContent" },
                            Properties =
                            {
                                ["editedHtmlContent"] = new OpenApiSchema
                                {
                                    Type = "string",
                                    Description = "Toàn bộ nội dung HTML đã chỉnh sửa của hợp đồng."
                                }
                            }
                        },
                        Example = new OpenApiObject
                        {
                            ["editedHtmlContent"] = new OpenApiString("<!DOCTYPE html><html><body><h1>Hop dong da sua</h1></body></html>")
                        }
                    }
                }
            };
        }
    }
}
