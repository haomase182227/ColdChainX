using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetManagementModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_url",
                schema: "public",
                table: "vehicle_documents");

            migrationBuilder.DropColumn(
                name: "document_url",
                schema: "public",
                table: "driver_licenses");

            migrationBuilder.AddColumn<string>(
                name: "current_location",
                schema: "public",
                table: "vehicles",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "current_odometer",
                schema: "public",
                table: "vehicles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<Guid>(
                name: "driver_id",
                schema: "public",
                table: "vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "next_maintenance_odometer",
                schema: "public",
                table: "vehicles",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "expire_date",
                schema: "public",
                table: "vehicle_documents",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<string>(
                name: "document_type",
                schema: "public",
                table: "vehicle_documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "triggered_at_odometer",
                schema: "public",
                table: "maintenance_tickets",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "public",
                table: "drivers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValueSql: "'ACTIVE'::character varying",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldDefaultValueSql: "'AVAILABLE'::character varying");

            migrationBuilder.AddColumn<string>(
                name: "full_name",
                schema: "public",
                table: "drivers",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "identity_number",
                schema: "public",
                table: "drivers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "join_date",
                schema: "public",
                table: "drivers",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                schema: "public",
                table: "drivers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_driver_id",
                schema: "public",
                table: "vehicles",
                column: "driver_id");

            migrationBuilder.AddForeignKey(
                name: "fk_vehicles_drivers",
                schema: "public",
                table: "vehicles",
                column: "driver_id",
                principalSchema: "public",
                principalTable: "drivers",
                principalColumn: "driver_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vehicles_drivers",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_driver_id",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "current_location",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "current_odometer",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "driver_id",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "next_maintenance_odometer",
                schema: "public",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "document_type",
                schema: "public",
                table: "vehicle_documents");

            migrationBuilder.DropColumn(
                name: "triggered_at_odometer",
                schema: "public",
                table: "maintenance_tickets");

            migrationBuilder.DropColumn(
                name: "full_name",
                schema: "public",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "identity_number",
                schema: "public",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "join_date",
                schema: "public",
                table: "drivers");

            migrationBuilder.DropColumn(
                name: "phone_number",
                schema: "public",
                table: "drivers");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "expire_date",
                schema: "public",
                table: "vehicle_documents",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                schema: "public",
                table: "vehicle_documents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "public",
                table: "drivers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValueSql: "'AVAILABLE'::character varying",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldDefaultValueSql: "'ACTIVE'::character varying");

            migrationBuilder.AddColumn<string>(
                name: "document_url",
                schema: "public",
                table: "driver_licenses",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }
    }
}
