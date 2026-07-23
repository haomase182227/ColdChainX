using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameDriverCurrentLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CurrentLocation",
                schema: "public",
                table: "drivers",
                newName: "current_location");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "current_location",
                schema: "public",
                table: "drivers",
                newName: "CurrentLocation");
        }
    }
}
