using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPackageLineSizeClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "size_class",
                schema: "public",
                table: "order_package_lines",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "M");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "size_class",
                schema: "public",
                table: "order_package_lines");
        }
    }
}
