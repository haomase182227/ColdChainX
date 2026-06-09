using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260609000200_SecurityPaginationAndLocationCleanup")]
    public partial class SecurityPaginationAndLocationCleanup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "drivers",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "location_name",
                table: "locations");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_name",
                table: "locations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "drivers");
        }
    }
}
