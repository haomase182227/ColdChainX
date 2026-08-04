INSERT INTO vehicles (
    vehicle_id, truck_plate, brand, manufacture_year, chassis_number, engine_number, 
    standard_fuel_liters, vehicle_type, max_weight, max_cbm, min_temp, max_temp, 
    status, created_at, current_location, current_odometer, next_maintenance_odometer, 
    next_maintenance_date, warning_days_before_due, warning_km_before_due
) VALUES 
('b8c4c37f-5d1f-4b08-8e6c-0e782d8db1a1', '51C-101.11', 'Kia', 2023, 'CHASSIS-K1-001', 'ENG-K1-001', 10.50, 'TRUCK_1T', 1000.00, 6.00, -18.00, 5.00, 'ACTIVE', '2026-07-15 09:07:10', 'Kho trung chuyển HCM', 0, 10000, '2026-12-30', 15, 500),
('e0f3d9d3-6e4a-42c2-8430-8a4a4f89d3c1', '29C-102.22', 'Hyundai', 2024, 'CHASSIS-H1-002', 'ENG-H1-002', 10.50, 'TRUCK_1T', 1000.00, 6.00, -18.00, 5.00, 'ACTIVE', '2026-07-15 09:07:10', 'Kho trung chuyển HN', 0, 10000, '2026-12-30', 15, 500),
('a3f5b741-2b0e-43dc-8e54-5a9e623194a3', '51D-201.33', 'Isuzu', 2022, 'CHASSIS-I2-003', 'ENG-I2-003', 12.00, 'TRUCK_2T', 2000.00, 11.00, -20.00, 10.00, 'ACTIVE', '2026-07-15 09:07:10', 'Kho trung chuyển HCM', 5000, 15000, '2026-10-15', 15, 500),
('4f7e1b52-9c3f-48d5-b6d8-1c4b8e2a3c57', '29D-202.44', 'Thaco', 2023, 'CHASSIS-T2-004', 'ENG-T2-004', 12.00, 'TRUCK_2T', 2000.00, 11.00, -20.00, 10.00, 'ACTIVE', '2026-07-15 09:07:10', 'Kho trung chuyển HN', 12000, 20000, '2026-11-20', 15, 500),
('8b1e4c9f-3d6a-4b12-9c78-5e4f2a1b0c3d', '51D-050.55', 'Suzuki', 2024, 'CHASSIS-S05-005', 'ENG-S05-005', 7.50, 'MINI_0.5T', 500.00, 4.00, -15.00, 10.00, 'ACTIVE', '2026-07-15 09:07:10', 'Kho trung chuyển HCM', 0, 5000, '2026-12-01', 15, 500);

INSERT INTO vehicle_documents (doc_id, vehicle_id, document_type, document_number, issuer, issue_date, expire_date, status, created_at)
VALUES 
(gen_random_uuid(), 'b8c4c37f-5d1f-4b08-8e6c-0e782d8db1a1', 'REGISTRATION', 'REG-10111', 'Bộ GTVT', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'b8c4c37f-5d1f-4b08-8e6c-0e782d8db1a1', 'INSURANCE', 'INS-10111', 'Bảo Việt', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),

(gen_random_uuid(), 'e0f3d9d3-6e4a-42c2-8430-8a4a4f89d3c1', 'REGISTRATION', 'REG-10222', 'Bộ GTVT', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'e0f3d9d3-6e4a-42c2-8430-8a4a4f89d3c1', 'INSURANCE', 'INS-10222', 'Bảo Việt', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),

(gen_random_uuid(), 'a3f5b741-2b0e-43dc-8e54-5a9e623194a3', 'REGISTRATION', 'REG-20133', 'Bộ GTVT', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'a3f5b741-2b0e-43dc-8e54-5a9e623194a3', 'INSURANCE', 'INS-20133', 'Bảo Việt', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),

(gen_random_uuid(), '4f7e1b52-9c3f-48d5-b6d8-1c4b8e2a3c57', 'REGISTRATION', 'REG-20244', 'Bộ GTVT', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), '4f7e1b52-9c3f-48d5-b6d8-1c4b8e2a3c57', 'INSURANCE', 'INS-20244', 'Bảo Việt', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),

(gen_random_uuid(), '8b1e4c9f-3d6a-4b12-9c78-5e4f2a1b0c3d', 'REGISTRATION', 'REG-05055', 'Bộ GTVT', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), '8b1e4c9f-3d6a-4b12-9c78-5e4f2a1b0c3d', 'INSURANCE', 'INS-05055', 'Bảo Việt', '2024-01-01', '2027-12-31', 'ACTIVE', '2026-07-15 09:07:10');

INSERT INTO iot_devices (device_id, device_code, vehicle_id, battery_level, last_ping_time, status, created_at)
VALUES
(gen_random_uuid(), 'ESP32-V1', 'b8c4c37f-5d1f-4b08-8e6c-0e782d8db1a1', 100, CURRENT_TIMESTAMP, 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'ESP32-V2', 'e0f3d9d3-6e4a-42c2-8430-8a4a4f89d3c1', 100, CURRENT_TIMESTAMP, 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'ESP32-V3', 'a3f5b741-2b0e-43dc-8e54-5a9e623194a3', 100, CURRENT_TIMESTAMP, 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'ESP32-V4', '4f7e1b52-9c3f-48d5-b6d8-1c4b8e2a3c57', 100, CURRENT_TIMESTAMP, 'ACTIVE', '2026-07-15 09:07:10'),
(gen_random_uuid(), 'ESP32-V5', '8b1e4c9f-3d6a-4b12-9c78-5e4f2a1b0c3d', 100, CURRENT_TIMESTAMP, 'ACTIVE', '2026-07-15 09:07:10');
