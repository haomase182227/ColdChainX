using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723120000_CompleteIncidentWorkflow")]
public partial class CompleteIncidentWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "status",
            schema: "public",
            table: "incident_reports",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true,
            defaultValueSql: "'REPORTED'::character varying",
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true,
            oldDefaultValueSql: "'REPORTED'::character varying");

        migrationBuilder.AddColumn<decimal>(
            name: "approved_amount",
            schema: "public",
            table: "incident_reports",
            type: "numeric(15,2)",
            precision: 15,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "broken_vehicle_id",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "expense_approval_note",
            schema: "public",
            table: "incident_reports",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "expense_approved_at",
            schema: "public",
            table: "incident_reports",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "expense_approved_by",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "expense_status",
            schema: "public",
            table: "incident_reports",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true,
            defaultValueSql: "'NOT_REQUIRED'::character varying");

        migrationBuilder.AddColumn<DateTime>(
            name: "handled_at",
            schema: "public",
            table: "incident_reports",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "handled_by",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "handling_note",
            schema: "public",
            table: "incident_reports",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "maintenance_ticket_id",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "reimbursed_at",
            schema: "public",
            table: "incident_reports",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "reimbursed_by",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "replacement_vehicle_id",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "requires_rescue",
            schema: "public",
            table: "incident_reports",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "rescue_dispatched_at",
            schema: "public",
            table: "incident_reports",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "resolved_by",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "resolution_note",
            schema: "public",
            table: "incident_reports",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "transload_confirmed_at",
            schema: "public",
            table: "incident_reports",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "transload_confirmed_by",
            schema: "public",
            table: "incident_reports",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "transload_note",
            schema: "public",
            table: "incident_reports",
            type: "text",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE public.incident_reports
            SET expense_status = CASE
                WHEN reimbursed_amount IS NOT NULL THEN 'REIMBURSED'
                WHEN driver_paid_amount > 0 THEN 'PENDING_APPROVAL'
                ELSE 'NOT_REQUIRED'
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "approved_amount", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "broken_vehicle_id", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "expense_approval_note", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "expense_approved_at", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "expense_approved_by", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "expense_status", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "handled_at", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "handled_by", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "handling_note", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "maintenance_ticket_id", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "reimbursed_at", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "reimbursed_by", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "replacement_vehicle_id", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "requires_rescue", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "rescue_dispatched_at", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "resolved_by", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "resolution_note", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "transload_confirmed_at", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "transload_confirmed_by", schema: "public", table: "incident_reports");
        migrationBuilder.DropColumn(name: "transload_note", schema: "public", table: "incident_reports");

        migrationBuilder.AlterColumn<string>(
            name: "status",
            schema: "public",
            table: "incident_reports",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true,
            defaultValueSql: "'REPORTED'::character varying",
            oldClrType: typeof(string),
            oldType: "character varying(30)",
            oldMaxLength: 30,
            oldNullable: true,
            oldDefaultValueSql: "'REPORTED'::character varying");
    }
}
