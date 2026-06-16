CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.route_master (
    route_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    route_code varchar(50) NOT NULL UNIQUE,
    origin_city varchar(50) NOT NULL,
    dest_city varchar(50) NOT NULL,
    etd timestamp without time zone NOT NULL,
    transit_time_hours integer NOT NULL,
    cut_off_time timestamp without time zone NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'ACTIVE',
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

ALTER TABLE public.transport_orders
    ADD COLUMN IF NOT EXISTS route_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_to_route'
    ) THEN
        ALTER TABLE public.transport_orders
            ADD CONSTRAINT fk_to_route
            FOREIGN KEY (route_id)
            REFERENCES public.route_master(route_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_transport_orders_route_id
    ON public.transport_orders(route_id);

ALTER TABLE public.pricing_matrix
    ADD COLUMN IF NOT EXISTS min_value numeric(12,4),
    ADD COLUMN IF NOT EXISTS max_value numeric(12,4),
    ADD COLUMN IF NOT EXISTS min_charge numeric(15,2);

ALTER TABLE public.quotations
    ADD COLUMN IF NOT EXISTS system_base_freight numeric(15,2),
    ADD COLUMN IF NOT EXISTS manual_adjustment numeric(15,2) DEFAULT 0,
    ADD COLUMN IF NOT EXISTS override_reason varchar(500),
    ADD COLUMN IF NOT EXISTS pricing_source varchar(30) DEFAULT 'AUTO';

UPDATE public.quotations
SET pricing_source = 'AUTO'
WHERE pricing_source IS NULL;

ALTER TABLE public.quotations
    ALTER COLUMN pricing_source SET NOT NULL;

CREATE TABLE IF NOT EXISTS public.inbound_asn (
    asn_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asn_code varchar(50) NOT NULL UNIQUE,
    order_id uuid NOT NULL,
    requested_dropoff_time timestamp without time zone NOT NULL,
    qr_code_value varchar(500) NOT NULL,
    status varchar(30) NOT NULL DEFAULT 'SCHEDULED',
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_asn_order'
    ) THEN
        ALTER TABLE public.inbound_asn
            ADD CONSTRAINT fk_asn_order
            FOREIGN KEY (order_id)
            REFERENCES public.transport_orders(order_id)
            ON DELETE CASCADE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_inbound_asn_order_id
    ON public.inbound_asn(order_id);

-- Sample route and tier prices for local testing. Adjust date/time before demo if needed.
INSERT INTO public.route_master (route_code, origin_city, dest_city, etd, transit_time_hours, cut_off_time, status)
VALUES
    ('HCM-HN-FRI-2000', 'Ho Chi Minh', 'Ha Noi', '2026-06-19 20:00:00', 65, '2026-06-19 12:00:00', 'ACTIVE')
ON CONFLICT (route_code) DO NOTHING;

INSERT INTO public.pricing_matrix (origin_city, dest_city, pricing_unit, unit_price, min_value, max_value, min_charge, effective_date)
SELECT 'Ho Chi Minh', 'Ha Noi', 'KG', 9500, 0, 500, 300000, CURRENT_DATE
WHERE NOT EXISTS (
    SELECT 1 FROM public.pricing_matrix
    WHERE origin_city = 'Ho Chi Minh'
      AND dest_city = 'Ha Noi'
      AND pricing_unit = 'KG'
      AND min_value = 0
      AND max_value = 500
);

INSERT INTO public.pricing_matrix (origin_city, dest_city, pricing_unit, unit_price, min_value, max_value, min_charge, effective_date)
SELECT 'Ho Chi Minh', 'Ha Noi', 'KG', 8800, 500, NULL, 300000, CURRENT_DATE
WHERE NOT EXISTS (
    SELECT 1 FROM public.pricing_matrix
    WHERE origin_city = 'Ho Chi Minh'
      AND dest_city = 'Ha Noi'
      AND pricing_unit = 'KG'
      AND min_value = 500
);

INSERT INTO public.pricing_matrix (origin_city, dest_city, pricing_unit, unit_price, min_value, max_value, min_charge, effective_date)
SELECT 'Ho Chi Minh', 'Ha Noi', 'CBM', 2800000, 0, 3, 300000, CURRENT_DATE
WHERE NOT EXISTS (
    SELECT 1 FROM public.pricing_matrix
    WHERE origin_city = 'Ho Chi Minh'
      AND dest_city = 'Ha Noi'
      AND pricing_unit = 'CBM'
      AND min_value = 0
      AND max_value = 3
);

INSERT INTO public.pricing_matrix (origin_city, dest_city, pricing_unit, unit_price, min_value, max_value, min_charge, effective_date)
SELECT 'Ho Chi Minh', 'Ha Noi', 'CBM', 2500000, 3, NULL, 300000, CURRENT_DATE
WHERE NOT EXISTS (
    SELECT 1 FROM public.pricing_matrix
    WHERE origin_city = 'Ho Chi Minh'
      AND dest_city = 'Ha Noi'
      AND pricing_unit = 'CBM'
      AND min_value = 3
);
