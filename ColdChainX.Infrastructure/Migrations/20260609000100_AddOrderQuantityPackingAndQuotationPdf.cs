using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260609000100_AddOrderQuantityPackingAndQuotationPdf")]
    public partial class AddOrderQuantityPackingAndQuotationPdf : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quantity",
                table: "transport_order",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "packing_type",
                table: "transport_order",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Thùng");

            migrationBuilder.AddColumn<string>(
                name: "file_url",
                table: "quotations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "quantity",
                table: "transport_order");

            migrationBuilder.DropColumn(
                name: "packing_type",
                table: "transport_order");

            migrationBuilder.DropColumn(
                name: "file_url",
                table: "quotations");
        }
    }
}
