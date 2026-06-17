using ColdChainX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260615142000_RepairLocalSchemaAndSeedReferenceData")]
    public partial class RepairLocalSchemaAndSeedReferenceData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
CREATE OR REPLACE FUNCTION public._coldchainx_repair_order_fk(
    p_table_name text,
    p_constraint_name text,
    p_on_delete text
) RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    table_reg regclass;
    fk record;
    is_nullable boolean;
BEGIN
    table_reg := to_regclass('public.' || quote_ident(p_table_name));

    IF table_reg IS NULL OR to_regclass('public.transport_orders') IS NULL THEN
        RETURN;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = p_table_name
          AND column_name = 'order_id'
    ) THEN
        RETURN;
    END IF;

    FOR fk IN
        SELECT c.conname
        FROM pg_constraint c
        JOIN pg_attribute a
          ON a.attrelid = c.conrelid
         AND a.attnum = ANY(c.conkey)
        WHERE c.conrelid = table_reg
          AND c.contype = 'f'
          AND a.attname = 'order_id'
    LOOP
        EXECUTE format('ALTER TABLE public.%I DROP CONSTRAINT IF EXISTS %I', p_table_name, fk.conname);
    END LOOP;

    SELECT c.is_nullable = 'YES'
    INTO is_nullable
    FROM information_schema.columns c
    WHERE c.table_schema = 'public'
      AND c.table_name = p_table_name
      AND c.column_name = 'order_id';

    IF is_nullable THEN
        EXECUTE format(
            'UPDATE public.%I t
             SET order_id = NULL
             WHERE t.order_id IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM public.transport_orders o WHERE o.order_id = t.order_id
               )',
            p_table_name);
    ELSE
        EXECUTE format(
            'DELETE FROM public.%I t
             WHERE t.order_id IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM public.transport_orders o WHERE o.order_id = t.order_id
               )',
            p_table_name);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.table_constraints
        WHERE constraint_schema = 'public'
          AND table_name = p_table_name
          AND constraint_name = p_constraint_name
    ) THEN
        EXECUTE format(
            'ALTER TABLE public.%I
             ADD CONSTRAINT %I
             FOREIGN KEY (order_id)
             REFERENCES public.transport_orders(order_id)
             ON DELETE %s',
            p_table_name,
            p_constraint_name,
            p_on_delete);
    END IF;
END $$;

SELECT public._coldchainx_repair_order_fk('claims', 'fk_claims_to', 'SET NULL');
SELECT public._coldchainx_repair_order_fk('chat_messages', 'fk_chat_order', 'CASCADE');
SELECT public._coldchainx_repair_order_fk('customer_contracts', 'fk_cc_orders', 'SET NULL');
SELECT public._coldchainx_repair_order_fk('delivery_epods', 'fk_epod_to', 'SET NULL');
SELECT public._coldchainx_repair_order_fk('inbound_asn', 'fk_asn_order', 'CASCADE');
SELECT public._coldchainx_repair_order_fk('invoice_lines', 'fk_il_to', 'CASCADE');
SELECT public._coldchainx_repair_order_fk('notifications', 'fk_noti_order', 'SET NULL');
SELECT public._coldchainx_repair_order_fk('quotations', 'fk_quote_to', 'SET NULL');
SELECT public._coldchainx_repair_order_fk('transport_documents', 'fk_td_to', 'SET NULL');
SELECT public._coldchainx_repair_order_fk('warehouse_receipts', 'fk_wr_to', 'CASCADE');

DROP FUNCTION IF EXISTS public._coldchainx_repair_order_fk(text, text, text);

DROP TABLE IF EXISTS public.transport_order CASCADE;

ALTER TABLE public.route_master
    ADD COLUMN IF NOT EXISTS transit_time varchar(50);

ALTER TABLE public.route_master
    ADD COLUMN IF NOT EXISTS cut_off_time time;

INSERT INTO public.route_master (route_id, route_code, origin_city, dest_city, transit_time, cut_off_time, status)
VALUES
    ('10000000-0000-0000-0000-000000000001', 'HCM-DAKLAK', 'HCM', 'Dak Lak', '1 - 1.5 ngay', '17:00:00', 'ACTIVE'),
    ('10000000-0000-0000-0000-000000000002', 'HCM-CANTHO', 'HCM', 'Can Tho', '1 ngay', '18:00:00', 'ACTIVE'),
    ('10000000-0000-0000-0000-000000000003', 'HCM-DANANG', 'HCM', 'Da Nang', '2 - 3 ngay', '16:00:00', 'ACTIVE'),
    ('10000000-0000-0000-0000-000000000004', 'HCM-HANOI', 'HCM', 'Ha Noi', '3 - 4 ngay', '15:00:00', 'ACTIVE')
ON CONFLICT (route_id) DO UPDATE
SET route_code = EXCLUDED.route_code,
    origin_city = EXCLUDED.origin_city,
    dest_city = EXCLUDED.dest_city,
    transit_time = EXCLUDED.transit_time,
    cut_off_time = EXCLUDED.cut_off_time,
    status = EXCLUDED.status;

CREATE TABLE IF NOT EXISTS public.weight_tiers (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    route_id uuid NOT NULL,
    min_weight_kg numeric(10,2) NOT NULL,
    max_weight_kg numeric(10,2),
    price_per_kg numeric(15,2) NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_weight_tiers_route_min
    ON public.weight_tiers(route_id, min_weight_kg);

ALTER TABLE public.weight_tiers DROP CONSTRAINT IF EXISTS fk_weight_tiers_route;
ALTER TABLE public.weight_tiers
    ADD CONSTRAINT fk_weight_tiers_route
    FOREIGN KEY (route_id)
    REFERENCES public.route_master(route_id)
    ON DELETE CASCADE;

ALTER TABLE public.customer_contracts
    ALTER COLUMN signed_date DROP NOT NULL;

ALTER TABLE public.customer_contracts
    ADD COLUMN IF NOT EXISTS order_id uuid;

CREATE INDEX IF NOT EXISTS ix_customer_contracts_order_id
    ON public.customer_contracts(order_id);

ALTER TABLE public.customer_contracts DROP CONSTRAINT IF EXISTS fk_cc_orders;
ALTER TABLE public.customer_contracts
    ADD CONSTRAINT fk_cc_orders
    FOREIGN KEY (order_id)
    REFERENCES public.transport_orders(order_id)
    ON DELETE SET NULL;

ALTER TABLE public.transport_orders
    ADD COLUMN IF NOT EXISTS quantity integer NOT NULL DEFAULT 1;

ALTER TABLE public.transport_orders
    ADD COLUMN IF NOT EXISTS packing_type varchar(50) NOT NULL DEFAULT 'Thung';

ALTER TABLE public.quotations
    ADD COLUMN IF NOT EXISTS file_url varchar(255);

ALTER TABLE public.drivers
    ADD COLUMN IF NOT EXISTS user_id uuid;

ALTER TABLE public.locations
    DROP COLUMN IF EXISTS location_name;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'roles'
          AND column_name = 'id'
    ) THEN
        ALTER TABLE public.users DROP CONSTRAINT IF EXISTS fk_users_roles;
        DROP TABLE IF EXISTS public.role_permissions CASCADE;
        ALTER TABLE public.roles DROP CONSTRAINT IF EXISTS roles_pkey;
        ALTER TABLE public.roles ADD COLUMN IF NOT EXISTS role_id uuid DEFAULT gen_random_uuid();
        UPDATE public.roles SET role_id = gen_random_uuid() WHERE role_id IS NULL;
        ALTER TABLE public.roles ALTER COLUMN role_id SET NOT NULL;
        ALTER TABLE public.roles DROP COLUMN IF EXISTS id;
        ALTER TABLE public.roles ADD CONSTRAINT roles_pkey PRIMARY KEY (role_id);
        ALTER TABLE public.users DROP COLUMN IF EXISTS role_id;
        ALTER TABLE public.users ADD COLUMN role_id uuid;
        ALTER TABLE public.users
            ADD CONSTRAINT fk_users_roles
            FOREIGN KEY (role_id)
            REFERENCES public.roles(role_id);

        CREATE TABLE public.role_permissions (
            role_id uuid NOT NULL,
            perm_id uuid NOT NULL,
            CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, perm_id),
            CONSTRAINT fk_rp_roles FOREIGN KEY (role_id) REFERENCES public.roles(role_id),
            CONSTRAINT fk_rp_perms FOREIGN KEY (perm_id) REFERENCES public.permissions(perm_id)
        );
    END IF;
END $$;

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
    ('30000000-0000-0000-0000-000000000012', '10000000-0000-0000-0000-000000000002', 1000, 1500, 2000),
    ('30000000-0000-0000-0000-000000000013', '10000000-0000-0000-0000-000000000001', 30, 100, 6000),
    ('30000000-0000-0000-0000-000000000014', '10000000-0000-0000-0000-000000000001', 100, 500, 4500),
    ('30000000-0000-0000-0000-000000000015', '10000000-0000-0000-0000-000000000001', 500, 1000, 3500),
    ('30000000-0000-0000-0000-000000000016', '10000000-0000-0000-0000-000000000001', 1000, 1500, 3000)
ON CONFLICT (route_id, min_weight_kg) DO UPDATE
SET max_weight_kg = EXCLUDED.max_weight_kg,
    price_per_kg = EXCLUDED.price_per_kg;

INSERT INTO public.roles (role_id, role_name, description)
SELECT '40000000-0000-0000-0000-000000000001', 'Admin', 'System administrator'
WHERE NOT EXISTS (SELECT 1 FROM public.roles WHERE role_name = 'Admin');

INSERT INTO public.roles (role_id, role_name, description)
SELECT '40000000-0000-0000-0000-000000000002', 'Customer', 'Customer account'
WHERE NOT EXISTS (SELECT 1 FROM public.roles WHERE role_name = 'Customer');

INSERT INTO public.roles (role_id, role_name, description)
SELECT '40000000-0000-0000-0000-000000000003', 'Sales', 'Sales staff'
WHERE NOT EXISTS (SELECT 1 FROM public.roles WHERE role_name = 'Sales');

INSERT INTO public.roles (role_id, role_name, description)
SELECT '40000000-0000-0000-0000-000000000004', 'Driver', 'Driver account'
WHERE NOT EXISTS (SELECT 1 FROM public.roles WHERE role_name = 'Driver');

INSERT INTO public.roles (role_id, role_name, description)
SELECT '40000000-0000-0000-0000-000000000005', 'Dispatcher', 'Container dispatcher'
WHERE NOT EXISTS (SELECT 1 FROM public.roles WHERE role_name = 'Dispatcher');

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'warehouses'
          AND column_name = 'warehouse_code'
    ) THEN
        INSERT INTO public.warehouses (warehouse_id, warehouse_code, warehouse_name, address, max_pallets, current_pallets, status, created_at)
        VALUES
            ('50000000-0000-0000-0000-000000000001', 'WH-HCM', 'Cold Hub HCM', 'Khu Cong Nghe Cao, TP. Thu Duc, TP. HCM', 1000, 0, 'ACTIVE', CURRENT_TIMESTAMP),
            ('50000000-0000-0000-0000-000000000002', 'WH-CT', 'Cold Hub Can Tho', 'Ninh Kieu, Can Tho', 500, 0, 'ACTIVE', CURRENT_TIMESTAMP),
            ('50000000-0000-0000-0000-000000000003', 'WH-DN', 'Cold Hub Da Nang', 'Hai Chau, Da Nang', 700, 0, 'ACTIVE', CURRENT_TIMESTAMP),
            ('50000000-0000-0000-0000-000000000004', 'WH-HN', 'Cold Hub Ha Noi', 'Long Bien, Ha Noi', 900, 0, 'ACTIVE', CURRENT_TIMESTAMP)
        ON CONFLICT (warehouse_id) DO UPDATE
        SET warehouse_name = EXCLUDED.warehouse_name,
            address = EXCLUDED.address,
            max_pallets = EXCLUDED.max_pallets,
            status = EXCLUDED.status;
    ELSE
        INSERT INTO public.warehouses (warehouse_id, warehouse_name, address, max_pallets, current_pallets, status, created_at)
        VALUES
            ('50000000-0000-0000-0000-000000000001', 'Cold Hub HCM', 'Khu Cong Nghe Cao, TP. Thu Duc, TP. HCM', 1000, 0, 'ACTIVE', CURRENT_TIMESTAMP),
            ('50000000-0000-0000-0000-000000000002', 'Cold Hub Can Tho', 'Ninh Kieu, Can Tho', 500, 0, 'ACTIVE', CURRENT_TIMESTAMP),
            ('50000000-0000-0000-0000-000000000003', 'Cold Hub Da Nang', 'Hai Chau, Da Nang', 700, 0, 'ACTIVE', CURRENT_TIMESTAMP),
            ('50000000-0000-0000-0000-000000000004', 'Cold Hub Ha Noi', 'Long Bien, Ha Noi', 900, 0, 'ACTIVE', CURRENT_TIMESTAMP)
        ON CONFLICT (warehouse_id) DO UPDATE
        SET warehouse_name = EXCLUDED.warehouse_name,
            address = EXCLUDED.address,
            max_pallets = EXCLUDED.max_pallets,
            status = EXCLUDED.status;
    END IF;
END $$;

INSERT INTO public.system_configs (key, value, description)
VALUES
    ('VolumetricConversionRate', '250', 'CBM to chargeable kilogram conversion rate'),
    ('PricePerKm', '15000', 'Last-mile surcharge per kilometer')
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    description = EXCLUDED.description;

INSERT INTO public.messagetype (type_id, type_name, description)
VALUES
    ('60000000-0000-0000-0000-000000000001', 'ORDER_STATUS', 'Cap nhat trang thai don hang, bao gia, hop dong'),
    ('60000000-0000-0000-0000-000000000002', 'DELIVERY_ALERT', 'Canh bao su co tren duong, nhiet do, thay doi ETA'),
    ('60000000-0000-0000-0000-000000000003', 'SYSTEM_INFO', 'Thong bao he thong va nhac nho giay to')
ON CONFLICT (type_id) DO UPDATE
SET type_name = EXCLUDED.type_name,
    description = EXCLUDED.description;

INSERT INTO public.notification_templates (template_id, type_id, title_template, body_template, channel, status)
VALUES
    ('NOTI_ORDER_NEW', '60000000-0000-0000-0000-000000000001', 'Don hang moi: {{Tracking_Code}}', 'He thong vua tiep nhan yeu cau van chuyen moi (Ma don: {{Tracking_Code}}). Vui long kiem tra chung tu va tien hanh lam bao gia.', 'IN_APP', 'ACTIVE'),
    ('NOTI_ORDER_REJECTED', '60000000-0000-0000-0000-000000000001', 'Don hang {{Tracking_Code}} chua hop le', 'Yeu cau van chuyen {{Tracking_Code}} cua ban da bi tu choi. Ly do: {{Reject_Reason}}.', 'IN_APP', 'ACTIVE'),
    ('NOTI_QUOTATION_SENT', '60000000-0000-0000-0000-000000000001', 'Co bao gia moi: Don hang {{Tracking_Code}}', 'Don hang {{Tracking_Code}} cua ban da duoc duyet va co bao gia moi. Tong chi phi du kien: {{Final_Amount}} VND.', 'IN_APP', 'ACTIVE'),
    ('NOTI_QUOTATION_ACCEPTED', '60000000-0000-0000-0000-000000000001', 'Khach hang da chot gia: {{Tracking_Code}}', 'Khach hang da xac nhan dong y bao gia cho don hang {{Tracking_Code}}. Vui long chuyen sang buoc lam hop dong.', 'IN_APP', 'ACTIVE'),
    ('NOTI_CONTRACT_PENDING_SIGNATURE', '60000000-0000-0000-0000-000000000001', 'Hop dong cho ky: {{Contract_Number}}', 'Hop dong {{Contract_Number}} cua don {{Tracking_Code}} da san sang. Vui long xem va ky duyet.', 'IN_APP', 'ACTIVE'),
    ('NOTI_CONTRACT_APPROVED_SALES', '60000000-0000-0000-0000-000000000001', 'Khach da ky hop dong: {{Tracking_Code}}', 'Khach hang da ky hop dong cho don {{Tracking_Code}}. Vui long tiep tuc xu ly van hanh.', 'IN_APP', 'ACTIVE'),
    ('NOTI_CONTRACT_APPROVED_CUSTOMER', '60000000-0000-0000-0000-000000000001', 'Hop dong da kich hoat: {{Tracking_Code}}', 'Hop dong cua don {{Tracking_Code}} da duoc kich hoat thanh cong.', 'IN_APP', 'ACTIVE'),
    ('NOTI_REQUIRED_DOCUMENTS_UPLOAD', '60000000-0000-0000-0000-000000000003', 'Can bo sung chung tu: {{Tracking_Code}}', 'Vui long upload hoa don GTGT, phieu xuat kho va phieu luan chuyen noi bo cho don {{Tracking_Code}}.', 'IN_APP', 'ACTIVE')
ON CONFLICT (template_id) DO UPDATE
SET type_id = EXCLUDED.type_id,
    title_template = EXCLUDED.title_template,
    body_template = EXCLUDED.body_template,
    channel = EXCLUDED.channel,
    status = EXCLUDED.status;
""");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
