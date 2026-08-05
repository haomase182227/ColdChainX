using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardQueryIndexesAndCorrectQuotationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing quotations predate event tracking. Values copied from created_at by
            // an earlier revision were inferred, not real send/accept event timestamps.
            migrationBuilder.Sql("""
                UPDATE public.quotations
                SET sent_at = NULL
                WHERE sent_at = created_at
                  AND created_at < TIMESTAMP '2026-08-05 15:56:14';

                UPDATE public.quotations
                SET accepted_at = NULL
                WHERE accepted_at = created_at
                  AND created_at < TIMESTAMP '2026-08-05 15:56:14';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_documents_expire_date",
                schema: "public",
                table: "vehicle_documents",
                column: "expire_date");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_accepted_at",
                schema: "public",
                table: "quotations",
                column: "accepted_at");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_created_at",
                schema: "public",
                table: "quotations",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_sent_at",
                schema: "public",
                table: "quotations",
                column: "sent_at");

            migrationBuilder.CreateIndex(
                name: "ix_quotations_status_created_at",
                schema: "public",
                table: "quotations",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_created_at",
                schema: "public",
                table: "payment_transactions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_dashboard_filters",
                schema: "public",
                table: "payment_transactions",
                columns: new[] { "status", "transaction_type", "payment_method", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_master_trips_planned_start_status",
                schema: "public",
                table: "master_trips",
                columns: new[] { "planned_start_time", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_issued_status",
                schema: "public",
                table: "invoices",
                columns: new[] { "issued_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_reports_reimbursed_expense_status",
                schema: "public",
                table: "incident_reports",
                columns: new[] { "reimbursed_at", "expense_status" });

            migrationBuilder.CreateIndex(
                name: "ix_incident_reports_reported_status",
                schema: "public",
                table: "incident_reports",
                columns: new[] { "reported_at", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_contracts_created_at",
                schema: "public",
                table: "customer_contracts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_customer_contracts_customer_created_at",
                schema: "public",
                table: "customer_contracts",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_contracts_status_created_at",
                schema: "public",
                table: "customer_contracts",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_claims_status_created_at",
                schema: "public",
                table: "claims",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_receiver_read_created_at",
                schema: "public",
                table: "chat_messages",
                columns: new[] { "receiver_id", "is_read", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_alert_logs_trip_created_at",
                schema: "public",
                table: "alert_logs",
                columns: new[] { "trip_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vehicle_documents_expire_date",
                schema: "public",
                table: "vehicle_documents");

            migrationBuilder.DropIndex(
                name: "ix_quotations_accepted_at",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_quotations_created_at",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_quotations_sent_at",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_quotations_status_created_at",
                schema: "public",
                table: "quotations");

            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_created_at",
                schema: "public",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_dashboard_filters",
                schema: "public",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "ix_master_trips_planned_start_status",
                schema: "public",
                table: "master_trips");

            migrationBuilder.DropIndex(
                name: "ix_invoices_issued_status",
                schema: "public",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_incident_reports_reimbursed_expense_status",
                schema: "public",
                table: "incident_reports");

            migrationBuilder.DropIndex(
                name: "ix_incident_reports_reported_status",
                schema: "public",
                table: "incident_reports");

            migrationBuilder.DropIndex(
                name: "ix_customer_contracts_created_at",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropIndex(
                name: "ix_customer_contracts_customer_created_at",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropIndex(
                name: "ix_customer_contracts_status_created_at",
                schema: "public",
                table: "customer_contracts");

            migrationBuilder.DropIndex(
                name: "ix_claims_status_created_at",
                schema: "public",
                table: "claims");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_receiver_read_created_at",
                schema: "public",
                table: "chat_messages");

            migrationBuilder.DropIndex(
                name: "ix_alert_logs_trip_created_at",
                schema: "public",
                table: "alert_logs");

        }
    }
}
