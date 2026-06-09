ALTER TABLE transport_order
    ADD COLUMN IF NOT EXISTS quantity integer NOT NULL DEFAULT 1;

ALTER TABLE transport_order
    ADD COLUMN IF NOT EXISTS packing_type character varying(50) NOT NULL DEFAULT 'Thùng';

ALTER TABLE quotations
    ADD COLUMN IF NOT EXISTS file_url character varying(255);
