using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Linq;

namespace ColdChainX.API.Swagger
{
    /// <summary>
    /// Swagger SchemaFilter: chuyển enum thành kiểu string với danh sách giá trị tên chữ
    /// để dropdown trong Swagger hiển thị tên (Truck, Active, ...) thay vì số (0, 1, 2, ...)
    /// </summary>
    public class EnumSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type is null || !context.Type.IsEnum)
                return;

            var enumNames = Enum.GetNames(context.Type);

            schema.Type = "string";
            schema.Format = null;
            schema.Enum = enumNames
                .Select(name => (IOpenApiAny)new OpenApiString(name))
                .ToList();
        }
    }
}
