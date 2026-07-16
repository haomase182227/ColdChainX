using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentFinancialsAndResolutionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentEvidences_incident_reports_IncidentId",
                schema: "public",
                table: "IncidentEvidences");

            migrationBuilder.DropPrimaryKey(
                name: "PK_IncidentEvidences",
                schema: "public",
                table: "IncidentEvidences");

            migrationBuilder.RenameTable(
                name: "IncidentEvidences",
                schema: "public",
                newName: "incident_evidences",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "IncidentId",
                schema: "public",
                table: "incident_evidences",
                newName: "incident_id");

            migrationBuilder.RenameColumn(
                name: "FileUrl",
                schema: "public",
                table: "incident_evidences",
                newName: "file_url");

            migrationBuilder.RenameColumn(
                name: "EvidenceType",
                schema: "public",
                table: "incident_evidences",
                newName: "evidence_type");

            migrationBuilder.RenameColumn(
                name: "EvidenceId",
                schema: "public",
                table: "incident_evidences",
                newName: "evidence_id");

            migrationBuilder.RenameIndex(
                name: "IX_IncidentEvidences_IncidentId",
                schema: "public",
                table: "incident_evidences",
                newName: "IX_incident_evidences_incident_id");

            migrationBuilder.AddColumn<decimal>(
                name: "driver_paid_amount",
                schema: "public",
                table: "incident_reports",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "reimbursed_amount",
                schema: "public",
                table: "incident_reports",
                type: "numeric(15,2)",
                precision: 15,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "file_url",
                schema: "public",
                table: "incident_evidences",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "evidence_type",
                schema: "public",
                table: "incident_evidences",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "evidence_id",
                schema: "public",
                table: "incident_evidences",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddPrimaryKey(
                name: "incident_evidences_pkey",
                schema: "public",
                table: "incident_evidences",
                column: "evidence_id");

            migrationBuilder.AddForeignKey(
                name: "fk_incident_evidences_incident",
                schema: "public",
                table: "incident_evidences",
                column: "incident_id",
                principalSchema: "public",
                principalTable: "incident_reports",
                principalColumn: "incident_id",
                onDelete: ReferentialAction.Cascade);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_incident_evidences_incident",
                schema: "public",
                table: "incident_evidences");

            migrationBuilder.DropPrimaryKey(
                name: "incident_evidences_pkey",
                schema: "public",
                table: "incident_evidences");

            migrationBuilder.DropColumn(
                name: "driver_paid_amount",
                schema: "public",
                table: "incident_reports");

            migrationBuilder.DropColumn(
                name: "reimbursed_amount",
                schema: "public",
                table: "incident_reports");

            migrationBuilder.RenameTable(
                name: "incident_evidences",
                schema: "public",
                newName: "IncidentEvidences",
                newSchema: "public");

            migrationBuilder.RenameColumn(
                name: "incident_id",
                schema: "public",
                table: "IncidentEvidences",
                newName: "IncidentId");

            migrationBuilder.RenameColumn(
                name: "file_url",
                schema: "public",
                table: "IncidentEvidences",
                newName: "FileUrl");

            migrationBuilder.RenameColumn(
                name: "evidence_type",
                schema: "public",
                table: "IncidentEvidences",
                newName: "EvidenceType");

            migrationBuilder.RenameColumn(
                name: "evidence_id",
                schema: "public",
                table: "IncidentEvidences",
                newName: "EvidenceId");

            migrationBuilder.RenameIndex(
                name: "IX_incident_evidences_incident_id",
                schema: "public",
                table: "IncidentEvidences",
                newName: "IX_IncidentEvidences_IncidentId");

            migrationBuilder.AlterColumn<string>(
                name: "FileUrl",
                schema: "public",
                table: "IncidentEvidences",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "EvidenceType",
                schema: "public",
                table: "IncidentEvidences",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<Guid>(
                name: "EvidenceId",
                schema: "public",
                table: "IncidentEvidences",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IncidentEvidences",
                schema: "public",
                table: "IncidentEvidences",
                column: "EvidenceId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentEvidences_incident_reports_IncidentId",
                schema: "public",
                table: "IncidentEvidences",
                column: "IncidentId",
                principalSchema: "public",
                principalTable: "incident_reports",
                principalColumn: "incident_id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
