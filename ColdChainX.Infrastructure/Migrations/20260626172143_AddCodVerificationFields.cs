using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodVerificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "cod_verified_at",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cod_verified_by",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_cod_verified",
                schema: "public",
                table: "lpn_delivery_confirmations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_lpn_delivery_confirmations_cod_verified_by",
                schema: "public",
                table: "lpn_delivery_confirmations",
                column: "cod_verified_by");

            migrationBuilder.AddForeignKey(
                name: "fk_lpn_delivery_confirmations_verified_by",
                schema: "public",
                table: "lpn_delivery_confirmations",
                column: "cod_verified_by",
                principalSchema: "public",
                principalTable: "users",
                principalColumn: "user_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lpn_delivery_confirmations_verified_by",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropIndex(
                name: "IX_lpn_delivery_confirmations_cod_verified_by",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "cod_verified_at",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "cod_verified_by",
                schema: "public",
                table: "lpn_delivery_confirmations");

            migrationBuilder.DropColumn(
                name: "is_cod_verified",
                schema: "public",
                table: "lpn_delivery_confirmations");
        }
    }
}
