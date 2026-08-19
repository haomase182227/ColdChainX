using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819000000_AddRiskAwareIncidentWorkflow")]
public partial class AddRiskAwareIncidentWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE public.incident_reports
                ADD COLUMN IF NOT EXISTS risk_level character varying(10),
                ADD COLUMN IF NOT EXISTS temperature_source character varying(30),
                ADD COLUMN IF NOT EXISTS latest_temperature numeric(8,2),
                ADD COLUMN IF NOT EXISTS temperature_measured_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS temperature_tolerance numeric(8,2) NOT NULL DEFAULT 2,
                ADD COLUMN IF NOT EXISTS temperature_threshold_breached boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS containment_confirmed_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS remaining_safe_time_minutes integer,
                ADD COLUMN IF NOT EXISTS safe_time_calculation character varying(50),
                ADD COLUMN IF NOT EXISTS direct_delivery_locked boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS previous_incident_id uuid,
                ADD COLUMN IF NOT EXISTS sla_due_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS last_sla_escalated_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS rescue_plan_type character varying(40),
                ADD COLUMN IF NOT EXISTS rescue_plan_details jsonb,
                ADD COLUMN IF NOT EXISTS redispatch_plan text,
                ADD COLUMN IF NOT EXISTS transload_details_json jsonb;

            UPDATE public.incident_reports
            SET risk_level = CASE severity
                WHEN 'LOW' THEN 'LOW'
                WHEN 'MEDIUM' THEN 'WARNING'
                ELSE 'CRITICAL'
            END
            WHERE risk_level IS NULL;

            UPDATE public.incident_reports
            SET sla_due_at = reported_at + interval '15 minutes'
            WHERE sla_due_at IS NULL AND reported_at IS NOT NULL;

            CREATE INDEX IF NOT EXISTS ix_incident_reports_status_sla_due_at
                ON public.incident_reports (status, sla_due_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS public.ix_incident_reports_status_sla_due_at;
            ALTER TABLE public.incident_reports
                DROP COLUMN IF EXISTS transload_details_json,
                DROP COLUMN IF EXISTS redispatch_plan,
                DROP COLUMN IF EXISTS rescue_plan_details,
                DROP COLUMN IF EXISTS rescue_plan_type,
                DROP COLUMN IF EXISTS last_sla_escalated_at,
                DROP COLUMN IF EXISTS sla_due_at,
                DROP COLUMN IF EXISTS previous_incident_id,
                DROP COLUMN IF EXISTS direct_delivery_locked,
                DROP COLUMN IF EXISTS safe_time_calculation,
                DROP COLUMN IF EXISTS remaining_safe_time_minutes,
                DROP COLUMN IF EXISTS containment_confirmed_at,
                DROP COLUMN IF EXISTS temperature_threshold_breached,
                DROP COLUMN IF EXISTS temperature_tolerance,
                DROP COLUMN IF EXISTS temperature_measured_at,
                DROP COLUMN IF EXISTS latest_temperature,
                DROP COLUMN IF EXISTS temperature_source,
                DROP COLUMN IF EXISTS risk_level;
            """);
    }
}
