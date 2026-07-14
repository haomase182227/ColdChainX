using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedServiceCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "public",
                table: "service_catalogs",
                columns: new[] { "service_catalog_id", "default_price", "description", "is_active", "service_code", "service_name", "updated_at" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), 50000m, "Dịch vụ bốc xếp hàng hóa lên xuống xe", true, "BOC_XEP", "Bốc xếp hàng hóa", null },
                    { new Guid("22222222-2222-2222-2222-222222222222"), 20000m, "Dịch vụ bọc màng co bảo vệ pallet", true, "BOC_MANG_CO", "Bọc màng co", null },
                    { new Guid("33333333-3333-3333-3333-333333333333"), 15000m, "Kiểm đếm chi tiết số lượng hàng hóa khi giao nhận", true, "KIEM_DEM", "Kiểm đếm số lượng", null }
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "service_catalogs",
                columns: new[] { "service_catalog_id", "default_price", "description", "is_active", "is_mandatory", "service_code", "service_name", "updated_at" },
                values: new object[] { new Guid("44444444-4444-4444-4444-444444444444"), 100000m, "Bảo hiểm rủi ro hư hỏng hàng do biến thiên nhiệt độ", true, true, "PHI_BAO_HIEM", "Phí bảo hiểm hàng lạnh", null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "public",
                table: "service_catalogs",
                keyColumn: "service_catalog_id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "service_catalogs",
                keyColumn: "service_catalog_id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "service_catalogs",
                keyColumn: "service_catalog_id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "service_catalogs",
                keyColumn: "service_catalog_id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"));
        }
    }
}
