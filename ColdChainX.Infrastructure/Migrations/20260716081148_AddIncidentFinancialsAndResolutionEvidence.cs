using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    public partial class AddIncidentFinancialsAndResolutionEvidence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "driver_paid_amount",
                schema: "public",
                table: "incident_reports");

            migrationBuilder.DropColumn(
                name: "reimbursed_amount",
                schema: "public",
                table: "incident_reports");
        }
    }
}
