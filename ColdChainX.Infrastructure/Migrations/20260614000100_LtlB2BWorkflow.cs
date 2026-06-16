using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260614000100_LtlB2BWorkflow")]
    public partial class LtlB2BWorkflow : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
ALTER TABLE public.route_master
    ADD COLUMN IF NOT EXISTS transit_time varchar(50);

UPDATE public.route_master
SET transit_time = COALESCE(transit_time, transit_time_hours::text || ' hours')
WHERE transit_time IS NULL;

ALTER TABLE public.route_master
    ALTER COLUMN transit_time SET NOT NULL;

ALTER TABLE public.route_master
    ALTER COLUMN cut_off_time TYPE time without time zone
    USING cut_off_time::time;

ALTER TABLE public.route_master
    DROP COLUMN IF EXISTS etd,
    DROP COLUMN IF EXISTS transit_time_hours;

CREATE TABLE IF NOT EXISTS public.system_configs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    key varchar(100) NOT NULL UNIQUE,
    value varchar(255) NOT NULL,
    description varchar(500)
);

INSERT INTO public.system_configs (id, key, value, description)
VALUES
    ('20000000-0000-0000-0000-000000000001', 'PricePerKm', '15000', 'Last-mile surcharge price per kilometer'),
    ('20000000-0000-0000-0000-000000000002', 'VolumetricConversionRate', '250', 'CBM to volumetric kilogram conversion rate')
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    description = EXCLUDED.description;

INSERT INTO public.route_master (route_id, route_code, origin_city, dest_city, transit_time, cut_off_time, status)
VALUES
    ('10000000-0000-0000-0000-000000000001', 'HCM-DAKLAK', 'HCM', 'Dak Lak', '1 - 1.5 ngay', '17:00:00', 'ACTIVE'),
    ('10000000-0000-0000-0000-000000000002', 'HCM-CANTHO', 'HCM', 'Can Tho', '1 ngay', '18:00:00', 'ACTIVE'),
    ('10000000-0000-0000-0000-000000000003', 'HCM-DANANG', 'HCM', 'Da Nang', '2 - 3 ngay', '16:00:00', 'ACTIVE'),
    ('10000000-0000-0000-0000-000000000004', 'HCM-HANOI', 'HCM', 'Ha Noi', '3 - 4 ngay', '15:00:00', 'ACTIVE')
ON CONFLICT (route_code) DO UPDATE
SET origin_city = EXCLUDED.origin_city,
    dest_city = EXCLUDED.dest_city,
    transit_time = EXCLUDED.transit_time,
    cut_off_time = EXCLUDED.cut_off_time,
    status = EXCLUDED.status;

CREATE TABLE IF NOT EXISTS public.weight_tiers (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    route_id uuid NOT NULL REFERENCES public.route_master(route_id) ON DELETE CASCADE,
    min_weight_kg numeric(10,2) NOT NULL,
    max_weight_kg numeric(10,2),
    price_per_kg numeric(15,2) NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_weight_tiers_route_min
    ON public.weight_tiers(route_id, min_weight_kg);

INSERT INTO public.weight_tiers (id, route_id, min_weight_kg, max_weight_kg, price_per_kg)
VALUES
    ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000004', 30, 100, 9000),
    ('30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000004', 100, 500, 7500),
    ('30000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000004', 500, 1000, 6000),
    ('30000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000004', 1000, 1500, 5000),
    ('30000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000003', 30, 100, 7000),
    ('30000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000003', 100, 500, 5500),
    ('30000000-0000-0000-0000-000000000007', '10000000-0000-0000-0000-000000000003', 500, 1000, 4000),
    ('30000000-0000-0000-0000-000000000008', '10000000-0000-0000-0000-000000000003', 1000, 1500, 3500),
    ('30000000-0000-0000-0000-000000000009', '10000000-0000-0000-0000-000000000002', 30, 100, 4500),
    ('30000000-0000-0000-0000-000000000010', '10000000-0000-0000-0000-000000000002', 100, 500, 3500),
    ('30000000-0000-0000-0000-000000000011', '10000000-0000-0000-0000-000000000002', 500, 1000, 2500),
    ('30000000-0000-0000-0000-000000000012', '10000000-0000-0000-0000-000000000002', 1000, 1500, 2000)
ON CONFLICT (route_id, min_weight_kg) DO UPDATE
SET max_weight_kg = EXCLUDED.max_weight_kg,
    price_per_kg = EXCLUDED.price_per_kg;

ALTER TABLE public.quotations
    ADD COLUMN IF NOT EXISTS vat_percentage numeric(5,2) DEFAULT 8,
    ADD COLUMN IF NOT EXISTS chargeable_weight_kg numeric(10,2),
    ADD COLUMN IF NOT EXISTS volumetric_weight_kg numeric(10,2),
    ADD COLUMN IF NOT EXISTS price_per_kg numeric(15,2),
    ADD COLUMN IF NOT EXISTS distance_km numeric(10,2);

ALTER TABLE public.customer_contracts
    ADD COLUMN IF NOT EXISTS draft_html_content text,
    ADD COLUMN IF NOT EXISTS signed_file_url varchar(255),
    ADD COLUMN IF NOT EXISTS sent_at timestamp without time zone,
    ADD COLUMN IF NOT EXISTS uploaded_signed_at timestamp without time zone,
    ADD COLUMN IF NOT EXISTS verified_at timestamp without time zone,
    ADD COLUMN IF NOT EXISTS verified_by uuid;

CREATE TABLE IF NOT EXISTS public.chat_messages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    order_id uuid NOT NULL REFERENCES public.transport_orders(order_id) ON DELETE CASCADE,
    sender_id uuid NOT NULL REFERENCES public.users(user_id),
    receiver_id uuid NOT NULL REFERENCES public.users(user_id),
    message_content text NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_read boolean NOT NULL DEFAULT false
);

CREATE INDEX IF NOT EXISTS ix_chat_messages_order_created_at
    ON public.chat_messages(order_id, created_at DESC);
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
