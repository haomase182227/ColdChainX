using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFirebaseNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_noti_template",
                schema: "public",
                table: "notifications");

            // Some deployed databases no longer have the legacy EF-generated index.
            // PostgreSQL requires IF EXISTS here so the migration works for both
            // the original schema and those reconciled environments.
            migrationBuilder.Sql(
                @"DROP INDEX IF EXISTS public.""IX_notifications_user_id"";");

            migrationBuilder.AlterColumn<string>(
                name: "template_id",
                schema: "public",
                table: "notifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "body",
                schema: "public",
                table: "notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "data_json",
                schema: "public",
                table: "notifications",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_status",
                schema: "public",
                table: "notifications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PENDING");

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "public",
                table: "notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "read_at",
                schema: "public",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_id",
                schema: "public",
                table: "notifications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "sent_at",
                schema: "public",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                schema: "public",
                table: "notifications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "public",
                table: "notifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "device_tokens",
                schema: "public",
                columns: table => new
                {
                    device_token_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("device_tokens_pkey", x => x.device_token_id);
                    table.ForeignKey(
                        name: "fk_device_tokens_users",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_created_at",
                schema: "public",
                table: "notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_read_created_at",
                schema: "public",
                table: "notifications",
                columns: new[] { "user_id", "is_read", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_type",
                schema: "public",
                table: "notifications",
                columns: new[] { "user_id", "type" });

            migrationBuilder.CreateIndex(
                name: "device_tokens_token_key",
                schema: "public",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_active",
                schema: "public",
                table: "device_tokens",
                columns: new[] { "user_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_device",
                schema: "public",
                table: "device_tokens",
                columns: new[] { "user_id", "device_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_noti_template",
                schema: "public",
                table: "notifications",
                column: "template_id",
                principalSchema: "public",
                principalTable: "notification_templates",
                principalColumn: "template_id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_noti_template",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "device_tokens",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_created_at",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_read_created_at",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_type",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "body",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "data_json",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "delivery_status",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "read_at",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "reference_id",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "sent_at",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "title",
                schema: "public",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "public",
                table: "notifications");

            migrationBuilder.AlterColumn<string>(
                name: "template_id",
                schema: "public",
                table: "notifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id",
                schema: "public",
                table: "notifications",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_noti_template",
                schema: "public",
                table: "notifications",
                column: "template_id",
                principalSchema: "public",
                principalTable: "notification_templates",
                principalColumn: "template_id");
        }
    }
}
