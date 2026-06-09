ALTER TABLE customer_contracts
    ADD COLUMN IF NOT EXISTS order_id uuid;

ALTER TABLE customer_contracts
    ALTER COLUMN signed_date DROP NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'fk_cc_orders'
    ) THEN
        ALTER TABLE customer_contracts
            ADD CONSTRAINT fk_cc_orders
            FOREIGN KEY (order_id)
            REFERENCES transport_order(order_id);
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_customer_contracts_order_id
    ON customer_contracts(order_id);
