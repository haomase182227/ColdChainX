ALTER TABLE drivers
    ADD COLUMN IF NOT EXISTS user_id uuid;

ALTER TABLE locations
    DROP COLUMN IF EXISTS location_name;
