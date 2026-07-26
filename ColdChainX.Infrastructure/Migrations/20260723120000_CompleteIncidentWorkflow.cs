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
        migrationBuilder.Sql(
            """
            ALTER TABLE public.incident_reports
                ALTER COLUMN status TYPE character varying(30),
                ALTER COLUMN status SET DEFAULT 'REPORTED';

            -- Some server environments already contain a subset of these columns
            -- from an earlier manual rollout. Reconcile them without duplicating
            -- columns or destroying existing values.
            ALTER TABLE public.incident_reports
                ADD COLUMN IF NOT EXISTS approved_amount numeric(15,2),
                ADD COLUMN IF NOT EXISTS broken_vehicle_id uuid,
                ADD COLUMN IF NOT EXISTS approval_note text,
                ADD COLUMN IF NOT EXISTS approved_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS approved_by uuid,
                ADD COLUMN IF NOT EXISTS expense_status character varying(30) NOT NULL DEFAULT 'NOT_REQUIRED',
                ADD COLUMN IF NOT EXISTS handled_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS handled_by uuid,
                ADD COLUMN IF NOT EXISTS handling_note text,
                ADD COLUMN IF NOT EXISTS maintenance_ticket_id uuid,
                ADD COLUMN IF NOT EXISTS reimbursed_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS reimbursed_by uuid,
                ADD COLUMN IF NOT EXISTS replacement_vehicle_id uuid,
                ADD COLUMN IF NOT EXISTS requires_rescue boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS rescue_dispatched_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS resolved_by uuid,
                ADD COLUMN IF NOT EXISTS resolution_note text,
                ADD COLUMN IF NOT EXISTS transload_confirmed_at timestamp without time zone,
                ADD COLUMN IF NOT EXISTS transload_confirmed_by uuid,
                ADD COLUMN IF NOT EXISTS transload_note text;

            ALTER TABLE public.incident_reports
                ALTER COLUMN expense_status TYPE character varying(30),
                ALTER COLUMN expense_status SET DEFAULT 'NOT_REQUIRED',
                ALTER COLUMN expense_status SET NOT NULL,
                ALTER COLUMN requires_rescue SET DEFAULT false,
                ALTER COLUMN requires_rescue SET NOT NULL;

            UPDATE public.incident_reports
            SET expense_status = CASE
                WHEN reimbursed_amount IS NOT NULL THEN 'REIMBURSED'
                WHEN approved_amount IS NOT NULL THEN 'APPROVED'
                WHEN driver_paid_amount > 0 THEN 'PENDING_APPROVAL'
                ELSE 'NOT_REQUIRED'
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "This reconciliation migration adopts columns that predated EF migration history on the server. " +
            "Automatic rollback could delete pre-existing incident data and is intentionally disabled.");
    }
}
