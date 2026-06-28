using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIotDeviceCodeForMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "device_code",
                schema: "public",
                table: "iot_devices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_iot_devices_device_code",
                schema: "public",
                table: "iot_devices",
                column: "device_code",
                unique: true,
                filter: "device_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_iot_devices_device_code",
                schema: "public",
                table: "iot_devices");

            migrationBuilder.DropColumn(
                name: "device_code",
                schema: "public",
                table: "iot_devices");
        }
    }
}
