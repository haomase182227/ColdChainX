CREATE TYPE public.attachment_category AS ENUM ('operational', 'compliance', 'quality', 'incident', 'disposal', 'evidence');
CREATE TYPE public.attachment_format AS ENUM ('image', 'pdf', 'document');
CREATE TYPE public.attachment_sub_category AS ENUM ('delivery_note', 'packing_list', 'invoice', 'vat_invoice', 'warehouse_receipt_note', 'warehouse_issue_note', 'handover_report', 'food_safety_certificate', 'quarantine_certificate', 'coa_certificate', 'product_license', 'batch_release_certificate', 'customs_declaration', 'import_permit', 'certificate_of_origin', 'plant_quarantine_certificate', 'vietgap_certificate', 'qc_report', 'damage_report', 'temperature_log', 'temperature_exception_report', 'dispute_report', 'disposal_report', 'destruction_certificate', 'vehicle_photo', 'seal_photo', 'temperature_photo', 'goods_condition_photo', 'damage_photo', 'barcode_photo', 'batch_photo', 'expiry_date_photo', 'handover_photo');
CREATE TYPE public.document_status AS ENUM ('not_required', 'pending', 'verified', 'rejected', 'expired');
CREATE TYPE public.product_category AS ENUM ('food', 'seafood', 'agriculture', 'pharma', 'vaccine', 'import_goods');
CREATE TYPE public.requirement_level AS ENUM ('mandatory', 'conditional', 'optional');


CREATE TABLE public.compliance_zoning_rules (
    rule_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    product_category integer NOT NULL,
    sub_category integer NOT NULL,
    requirement_level integer NOT NULL,
    is_active boolean NOT NULL DEFAULT TRUE,
    created_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    created_by uuid NOT NULL,
    updated_at timestamp without time zone,
    updated_by uuid,
    CONSTRAINT compliance_zoning_rules_pkey PRIMARY KEY (rule_id)
);


CREATE TABLE public.customers (
    customer_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    company_name character varying(150) NOT NULL,
    tax_code character varying(20) NOT NULL,
    address character varying(200),
    email character varying(200),
    payment_term integer DEFAULT 30,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT customers_pkey PRIMARY KEY (customer_id)
);


CREATE TABLE public.messagetype (
    type_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    type_name character varying(50) NOT NULL,
    description character varying(255),
    CONSTRAINT messagetype_pkey PRIMARY KEY (type_id)
);


CREATE TABLE public.permissions (
    perm_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    perm_code character varying(50) NOT NULL,
    module character varying(50) NOT NULL,
    CONSTRAINT permissions_pkey PRIMARY KEY (perm_id)
);


CREATE TABLE public.pricing_matrix (
    price_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    origin_city character varying(50) NOT NULL,
    dest_city character varying(50) NOT NULL,
    pricing_unit character varying(20) NOT NULL,
    unit_price numeric(15,2) NOT NULL,
    min_value numeric(12,4),
    max_value numeric(12,4),
    min_charge numeric(15,2),
    effective_date date NOT NULL,
    CONSTRAINT pricing_matrix_pkey PRIMARY KEY (price_id)
);


CREATE TABLE public.roles (
    role_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    role_name character varying(50) NOT NULL,
    description character varying(255),
    CONSTRAINT roles_pkey PRIMARY KEY (role_id)
);


CREATE TABLE public.route_master (
    route_id uuid NOT NULL,
    route_code character varying(50) NOT NULL,
    origin_city character varying(50) NOT NULL,
    dest_city character varying(50) NOT NULL,
    transit_time character varying(50) NOT NULL,
    cut_off_time interval NOT NULL,
    status character varying(20) NOT NULL,
    created_at timestamp without time zone,
    CONSTRAINT route_master_pkey PRIMARY KEY (route_id)
);


CREATE TABLE public.system_configs (
    id uuid NOT NULL,
    key character varying(100) NOT NULL,
    value character varying(255) NOT NULL,
    description character varying(500),
    CONSTRAINT system_configs_pkey PRIMARY KEY (id)
);


CREATE TABLE public.vehicles (
    vehicle_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    truck_plate character varying(20) NOT NULL,
    brand character varying(50),
    manufacture_year integer,
    chassis_number character varying(50),
    engine_number character varying(50),
    standard_fuel_liters numeric(5,2),
    vehicle_type character varying(50) NOT NULL,
    max_weight numeric(10,2) NOT NULL,
    max_cbm numeric(8,2) NOT NULL,
    min_temp numeric(5,2) NOT NULL,
    max_temp numeric(5,2) NOT NULL,
    current_location character varying(255),
    current_odometer double precision NOT NULL,
    next_maintenance_odometer double precision NOT NULL,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT vehicles_pkey PRIMARY KEY (vehicle_id)
);


CREATE TABLE public.warehouses (
    warehouse_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    warehouse_name character varying(100) NOT NULL,
    address character varying(100),
    warehouse_code character varying(20) NOT NULL,
    warehouse_type character varying(20) NOT NULL,
    default_min_temp numeric(5,2),
    default_max_temp numeric(5,2),
    max_pallets integer NOT NULL,
    current_pallets integer DEFAULT 0,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    deleted_at timestamp without time zone,
    deleted_by uuid,
    CONSTRAINT warehouses_pkey PRIMARY KEY (warehouse_id)
);


CREATE TABLE public.invoices (
    invoice_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    invoice_code character varying(50) NOT NULL,
    customer_id uuid NOT NULL,
    vat_invoice_no character varying(50),
    pdf_url character varying(255),
    sub_total numeric(15,2) NOT NULL,
    tax_rate numeric(5,2) DEFAULT (8.00),
    tax_amount numeric(15,2) NOT NULL,
    deduction_amount numeric(15,2) DEFAULT (0.00),
    grand_total numeric(15,2) NOT NULL,
    paid_amount numeric(15,2) DEFAULT (0.00),
    issued_date date NOT NULL,
    due_date date NOT NULL,
    status character varying(20) DEFAULT ('DRAFT'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT invoices_pkey PRIMARY KEY (invoice_id),
    CONSTRAINT fk_inv_customers FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id)
);


CREATE TABLE public.locations (
    location_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    customer_id uuid,
    address text NOT NULL,
    latitude numeric(10,7) NOT NULL,
    longitude numeric(10,7) NOT NULL,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT locations_pkey PRIMARY KEY (location_id),
    CONSTRAINT fk_loc_customers FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id)
);


CREATE TABLE public.notification_templates (
    template_id character varying(50) NOT NULL,
    type_id uuid NOT NULL,
    title_template character varying(100) NOT NULL,
    body_template text NOT NULL,
    channel character varying(20) NOT NULL,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    CONSTRAINT notification_templates_pkey PRIMARY KEY (template_id),
    CONSTRAINT fk_nt_msgtype FOREIGN KEY (type_id) REFERENCES public.messagetype (type_id)
);


CREATE TABLE public.role_permissions (
    role_id uuid NOT NULL,
    perm_id uuid NOT NULL,
    CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, perm_id),
    CONSTRAINT fk_rp_perms FOREIGN KEY (perm_id) REFERENCES public.permissions (perm_id),
    CONSTRAINT fk_rp_roles FOREIGN KEY (role_id) REFERENCES public.roles (role_id)
);


CREATE TABLE public.users (
    user_id uuid NOT NULL,
    username character varying(50) NOT NULL,
    password_hash character varying(255) NOT NULL,
    email character varying(255),
    role_id uuid,
    full_name character varying(100) NOT NULL,
    phone character varying(20),
    warehouse_id uuid,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    refresh_token text,
    refresh_token_expiry_time timestamp with time zone,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    deleted_at timestamp without time zone,
    deleted_by uuid,
    CONSTRAINT users_pkey PRIMARY KEY (user_id),
    CONSTRAINT fk_users_roles FOREIGN KEY (role_id) REFERENCES public.roles (role_id)
);


CREATE TABLE public.weight_tiers (
    id uuid NOT NULL,
    route_id uuid NOT NULL,
    min_weight_kg numeric(10,2) NOT NULL,
    max_weight_kg numeric(10,2),
    price_per_kg numeric(18,2) NOT NULL,
    CONSTRAINT weight_tiers_pkey PRIMARY KEY (id),
    CONSTRAINT fk_weight_tiers_route FOREIGN KEY (route_id) REFERENCES public.route_master (route_id) ON DELETE CASCADE
);


CREATE TABLE public.iot_devices (
    device_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    device_code character varying(100),
    vehicle_id uuid,
    battery_level integer,
    last_ping_time timestamp without time zone,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    "IsOnline" boolean NOT NULL,
    CONSTRAINT iot_devices_pkey PRIMARY KEY (device_id),
    CONSTRAINT fk_iot_vehicles FOREIGN KEY (vehicle_id) REFERENCES public.vehicles (vehicle_id)
);


CREATE TABLE public.vehicle_documents (
    doc_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    vehicle_id uuid,
    document_type character varying(50) NOT NULL,
    document_number character varying(50) NOT NULL,
    issuer character varying(150),
    issue_date date NOT NULL,
    expire_date date,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT vehicle_documents_pkey PRIMARY KEY (doc_id),
    CONSTRAINT fk_vd_vehicles FOREIGN KEY (vehicle_id) REFERENCES public.vehicles (vehicle_id)
);


CREATE TABLE public.warehouse_zones (
    zone_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    warehouse_id uuid NOT NULL,
    zone_code character varying(20) NOT NULL,
    zone_name character varying(100) NOT NULL,
    zone_type character varying(30) NOT NULL,
    storage_type character varying(30) NOT NULL,
    temperature_min numeric(5,2),
    temperature_max numeric(5,2),
    max_capacity_pallets integer NOT NULL,
    current_pallets integer NOT NULL DEFAULT 0,
    status character varying(20) NOT NULL DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    deleted_at timestamp without time zone,
    deleted_by uuid,
    CONSTRAINT warehouse_zones_pkey PRIMARY KEY (zone_id),
    CONSTRAINT fk_warehouse_zones_warehouses FOREIGN KEY (warehouse_id) REFERENCES public.warehouses (warehouse_id) ON DELETE RESTRICT
);


CREATE TABLE public.geo_fences (
    fence_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    location_id uuid,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT geo_fences_pkey PRIMARY KEY (fence_id),
    CONSTRAINT fk_geo_locations FOREIGN KEY (location_id) REFERENCES public.locations (location_id)
);


CREATE TABLE public.master_trips (
    trip_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    vehicle_id uuid,
    origin_location_id uuid NOT NULL,
    destination_location_id uuid NOT NULL,
    seal_number character varying(100),
    total_distance_km numeric(8,2),
    estimated_duration_hours numeric(8,2),
    target_temperature numeric(5,2) NOT NULL,
    planned_start_time timestamp without time zone NOT NULL,
    planned_end_time timestamp without time zone NOT NULL,
    started_at timestamp without time zone,
    completed_at timestamp without time zone,
    status character varying(20) DEFAULT ('PLANNED'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT master_trips_pkey PRIMARY KEY (trip_id),
    CONSTRAINT fk_mtrip_dest FOREIGN KEY (destination_location_id) REFERENCES public.locations (location_id),
    CONSTRAINT fk_mtrip_orig FOREIGN KEY (origin_location_id) REFERENCES public.locations (location_id),
    CONSTRAINT fk_mtrip_vehicles FOREIGN KEY (vehicle_id) REFERENCES public.vehicles (vehicle_id)
);


CREATE TABLE public.drivers (
    driver_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    user_id uuid,
    full_name character varying(150) NOT NULL,
    identity_number character varying(30) NOT NULL,
    phone_number character varying(20) NOT NULL,
    date_of_birth date NOT NULL,
    join_date date NOT NULL,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT drivers_pkey PRIMARY KEY (driver_id),
    CONSTRAINT fk_drivers_users FOREIGN KEY (user_id) REFERENCES public.users (user_id)
);


CREATE TABLE public.maintenance_tickets (
    ticket_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    ticket_code character varying(50) NOT NULL,
    vehicle_id uuid,
    maintenance_type character varying(30) NOT NULL,
    triggered_at_odometer double precision NOT NULL,
    garage_name character varying(150) NOT NULL,
    description text NOT NULL,
    cost numeric(15,2) DEFAULT (0.00),
    issue_date date NOT NULL,
    completion_date date,
    status character varying(20) DEFAULT ('OPEN'::character varying),
    created_by uuid NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT maintenance_tickets_pkey PRIMARY KEY (ticket_id),
    CONSTRAINT fk_mt_users FOREIGN KEY (created_by) REFERENCES public.users (user_id),
    CONSTRAINT fk_mt_vehicles FOREIGN KEY (vehicle_id) REFERENCES public.vehicles (vehicle_id)
);


CREATE TABLE public.outbound_orders (
    outbound_order_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    order_code character varying(50) NOT NULL,
    customer_id uuid NOT NULL,
    receiver_name character varying(100) NOT NULL,
    receiver_phone character varying(20) NOT NULL,
    destination_address character varying(255) NOT NULL,
    status character varying(30) NOT NULL DEFAULT 'DRAFT',
    assigned_picker_id uuid,
    allocated_at timestamp without time zone,
    created_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    created_by uuid NOT NULL,
    updated_at timestamp without time zone,
    updated_by uuid,
    CONSTRAINT outbound_orders_pkey PRIMARY KEY (outbound_order_id),
    CONSTRAINT fk_outbound_customer FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id) ON DELETE RESTRICT,
    CONSTRAINT fk_outbound_picker FOREIGN KEY (assigned_picker_id) REFERENCES public.users (user_id) ON DELETE RESTRICT
);


CREATE TABLE public.warehouse_locations (
    location_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    zone_id uuid NOT NULL,
    location_code character varying(50) NOT NULL,
    rack_code character varying(20),
    bay_code character varying(20),
    level_code character varying(20),
    max_capacity_pallets integer NOT NULL,
    current_pallets integer NOT NULL DEFAULT 0,
    status character varying(20) NOT NULL DEFAULT ('ACTIVE'::character varying),
    description text,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    deleted_at timestamp without time zone,
    deleted_by uuid,
    CONSTRAINT warehouse_locations_pkey PRIMARY KEY (location_id),
    CONSTRAINT fk_warehouse_locations_zones FOREIGN KEY (zone_id) REFERENCES public.warehouse_zones (zone_id) ON DELETE RESTRICT
);


CREATE TABLE public.alert_logs (
    alert_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    trip_id uuid,
    alert_type character varying(50) NOT NULL,
    value numeric(5,2),
    latitude numeric(10,7) NOT NULL,
    longitude numeric(10,7) NOT NULL,
    status character varying(20) DEFAULT ('NEW'::character varying),
    resolved_by uuid,
    resolved_at timestamp without time zone,
    resolution_note text,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT alert_logs_pkey PRIMARY KEY (alert_id),
    CONSTRAINT fk_al_mtrip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id),
    CONSTRAINT fk_al_users FOREIGN KEY (resolved_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.incident_reports (
    incident_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    trip_id uuid,
    incident_type character varying(50) NOT NULL,
    severity character varying(20) NOT NULL,
    description text NOT NULL,
    current_latitude numeric(10,7),
    current_longitude numeric(10,7),
    status character varying(20) DEFAULT ('REPORTED'::character varying),
    reported_by uuid NOT NULL,
    reported_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    resolved_at timestamp without time zone,
    CONSTRAINT incident_reports_pkey PRIMARY KEY (incident_id),
    CONSTRAINT fk_ir_mtrip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id),
    CONSTRAINT fk_ir_users FOREIGN KEY (reported_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.telemetry_logs (
    log_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    device_id uuid,
    trip_id uuid,
    temperature numeric(5,2) NOT NULL,
    latitude numeric(10,7) NOT NULL,
    longitude numeric(10,7) NOT NULL,
    timestamp timestamp without time zone NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT telemetry_logs_pkey PRIMARY KEY (log_id),
    CONSTRAINT fk_tl_iot FOREIGN KEY (device_id) REFERENCES public.iot_devices (device_id),
    CONSTRAINT fk_tl_mtrip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id)
);


CREATE TABLE public.transport_orders (
    order_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    tracking_code character varying(50) NOT NULL,
    customer_id uuid,
    item_name character varying(150) NOT NULL,
    category character varying(50) NOT NULL,
    quantity integer NOT NULL DEFAULT 1,
    packing_type character varying(50) NOT NULL DEFAULT ('Thùng'::character varying),
    temp_condition character varying(20) NOT NULL,
    expected_weight_kg numeric(10,2) NOT NULL,
    actual_weight_kg numeric(10,2) NOT NULL,
    expected_cbm numeric(8,2) NOT NULL,
    actual_cbm numeric(8,2),
    length_cm numeric(10,2) NOT NULL,
    width_cm numeric(10,2) NOT NULL,
    height_cm numeric(10,2) NOT NULL,
    pickup_location uuid,
    dest_location uuid,
    cargo_value numeric(15,2) NOT NULL,
    status character varying(30) NOT NULL,
    master_trip_id uuid,
    route_id uuid,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT transport_orders_pkey PRIMARY KEY (order_id),
    CONSTRAINT fk_to_customers FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id),
    CONSTRAINT fk_to_dest FOREIGN KEY (dest_location) REFERENCES public.locations (location_id),
    CONSTRAINT fk_to_mtrip FOREIGN KEY (master_trip_id) REFERENCES public.master_trips (trip_id),
    CONSTRAINT fk_to_pickup FOREIGN KEY (pickup_location) REFERENCES public.locations (location_id),
    CONSTRAINT fk_transport_orders_route_master FOREIGN KEY (route_id) REFERENCES public.route_master (route_id)
);


CREATE TABLE public.trip_stops (
    stop_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    trip_id uuid,
    location_id uuid,
    stop_sequence integer NOT NULL,
    stop_type character varying(30) NOT NULL,
    planned_arrival_time timestamp without time zone NOT NULL,
    planned_departure_time timestamp without time zone NOT NULL,
    actual_arrival_time timestamp without time zone,
    actual_departure_time timestamp without time zone,
    status character varying(20) DEFAULT ('PENDING'::character varying),
    note text,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT trip_stops_pkey PRIMARY KEY (stop_id),
    CONSTRAINT fk_ts_loc FOREIGN KEY (location_id) REFERENCES public.locations (location_id),
    CONSTRAINT fk_ts_mtrip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id)
);


CREATE TABLE public.driver_licenses (
    license_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    driver_id uuid,
    license_number character varying(20) NOT NULL,
    license_class character varying(10) NOT NULL,
    issue_date date NOT NULL,
    expiry_date date NOT NULL,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT driver_licenses_pkey PRIMARY KEY (license_id),
    CONSTRAINT fk_dl_drivers FOREIGN KEY (driver_id) REFERENCES public.drivers (driver_id)
);


CREATE TABLE public.driver_work_logs (
    work_log_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    driver_id uuid NOT NULL,
    trip_id uuid,
    work_date date NOT NULL,
    driving_hours numeric(8,2) NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT driver_work_logs_pkey PRIMARY KEY (work_log_id),
    CONSTRAINT fk_driver_work_logs_driver FOREIGN KEY (driver_id) REFERENCES public.drivers (driver_id) ON DELETE CASCADE,
    CONSTRAINT fk_driver_work_logs_trip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id) ON DELETE SET NULL
);


CREATE TABLE public.expense_advances (
    advance_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    advance_code character varying(50) NOT NULL,
    trip_id uuid NOT NULL,
    driver_id uuid NOT NULL,
    amount numeric(15,2) NOT NULL,
    payment_method character varying(20) DEFAULT ('CASH'::character varying),
    advanced_date timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    approved_by uuid NOT NULL,
    cleared_amount numeric(15,2) DEFAULT (0.00),
    returned_amount numeric(15,2) DEFAULT (0.00),
    status character varying(20) DEFAULT ('PENDING'::character varying),
    clearance_status character varying(20) DEFAULT ('OPEN'::character varying),
    note text,
    CONSTRAINT expense_advances_pkey PRIMARY KEY (advance_id),
    CONSTRAINT fk_ea_drivers FOREIGN KEY (driver_id) REFERENCES public.drivers (driver_id),
    CONSTRAINT fk_ea_mtrip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id),
    CONSTRAINT fk_ea_users FOREIGN KEY (approved_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.trip_drivers (
    trip_driver_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    trip_id uuid NOT NULL,
    driver_id uuid NOT NULL,
    driver_role character varying(20) NOT NULL DEFAULT ('PRIMARY'::character varying),
    assigned_duration_hours numeric(8,2) NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT trip_drivers_pkey PRIMARY KEY (trip_driver_id),
    CONSTRAINT fk_trip_drivers_driver FOREIGN KEY (driver_id) REFERENCES public.drivers (driver_id) ON DELETE CASCADE,
    CONSTRAINT fk_trip_drivers_trip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id) ON DELETE CASCADE
);


CREATE TABLE public.outbound_order_items (
    outbound_order_item_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    outbound_order_id uuid NOT NULL,
    item_code character varying(50) NOT NULL,
    item_name character varying(255) NOT NULL,
    unit character varying(20) NOT NULL,
    quantity numeric(10,2) NOT NULL,
    CONSTRAINT outbound_order_items_pkey PRIMARY KEY (outbound_order_item_id),
    CONSTRAINT fk_item_outbound_order FOREIGN KEY (outbound_order_id) REFERENCES public.outbound_orders (outbound_order_id) ON DELETE CASCADE
);


CREATE TABLE public.chat_messages (
    id uuid NOT NULL,
    order_id uuid NOT NULL,
    sender_id uuid NOT NULL,
    receiver_id uuid NOT NULL,
    message_content text NOT NULL,
    created_at timestamp without time zone NOT NULL,
    is_read boolean NOT NULL,
    CONSTRAINT chat_messages_pkey PRIMARY KEY (id),
    CONSTRAINT fk_chat_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE,
    CONSTRAINT fk_chat_receiver FOREIGN KEY (receiver_id) REFERENCES public.users (user_id) ON DELETE CASCADE,
    CONSTRAINT fk_chat_sender FOREIGN KEY (sender_id) REFERENCES public.users (user_id) ON DELETE CASCADE
);


CREATE TABLE public.claims (
    claim_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    claim_code character varying(50) NOT NULL,
    order_id uuid,
    claim_type character varying(50) NOT NULL,
    description text NOT NULL,
    fault_owner character varying(50),
    status character varying(20) DEFAULT ('OPEN'::character varying),
    resolution_note text,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    resolved_at timestamp without time zone,
    CONSTRAINT claims_pkey PRIMARY KEY (claim_id),
    CONSTRAINT fk_claims_to FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id)
);


CREATE TABLE public.customer_contracts (
    contract_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    customer_id uuid,
    order_id uuid,
    contract_number character varying(50) NOT NULL,
    signed_date date,
    expired_date date NOT NULL,
    file_url character varying(255) NOT NULL,
    draft_html_content text,
    signed_file_url character varying(255),
    sent_at timestamp without time zone,
    uploaded_signed_at timestamp without time zone,
    verified_at timestamp without time zone,
    verified_by uuid,
    status character varying(20) DEFAULT ('ACTIVE'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT customer_contracts_pkey PRIMARY KEY (contract_id),
    CONSTRAINT fk_cc_customers FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id),
    CONSTRAINT fk_cc_orders FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id)
);


CREATE TABLE public.delivery_epods (
    epod_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    order_id uuid,
    checkin_time timestamp without time zone NOT NULL,
    signed_at timestamp without time zone,
    receiver_name character varying(100),
    receiver_phone character varying(20),
    sign_image_url character varying(255),
    sign_latitude numeric(10,7),
    sign_longitude numeric(10,7),
    delivery_rating integer DEFAULT 5,
    note text,
    pdf_url character varying(255),
    status character varying(30) DEFAULT ('PENDING'::character varying),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    cod_amount numeric(15,2),
    cod_amount_paid numeric(15,2),
    payment_method character varying(20),
    payment_status character varying(20),
    payment_evidence_image_url character varying(255),
    handover_confirmed_at timestamp without time zone,
    handover_pdf_url character varying(500),
    payment_confirmed_at timestamp without time zone,
    CONSTRAINT delivery_epods_pkey PRIMARY KEY (epod_id),
    CONSTRAINT fk_epod_to FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id)
);


CREATE TABLE public.inbound_asn (
    asn_id uuid NOT NULL,
    asn_code character varying(50) NOT NULL,
    order_id uuid NOT NULL,
    requested_dropoff_time timestamp without time zone NOT NULL,
    qr_code_value character varying(500) NOT NULL,
    status character varying(30) NOT NULL,
    phone character varying(50),
    warehouse_id uuid,
    customer_id uuid,
    file_url character varying(500),
    created_at timestamp without time zone,
    CONSTRAINT inbound_asn_pkey PRIMARY KEY (asn_id),
    CONSTRAINT fk_asn_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE
);


CREATE TABLE public.invoice_lines (
    line_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    invoice_id uuid NOT NULL,
    order_id uuid NOT NULL,
    charge_type character varying(50) NOT NULL,
    description character varying(255) NOT NULL,
    quantity numeric(10,2) DEFAULT (1.00),
    unit_price numeric(15,2) NOT NULL,
    amount numeric(15,2) NOT NULL,
    tax_rate numeric(5,2) DEFAULT (8.00),
    CONSTRAINT invoice_lines_pkey PRIMARY KEY (line_id),
    CONSTRAINT fk_il_inv FOREIGN KEY (invoice_id) REFERENCES public.invoices (invoice_id),
    CONSTRAINT fk_il_to FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id)
);


CREATE TABLE public.notifications (
    noti_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    user_id uuid NOT NULL,
    sender_id uuid,
    template_id character varying(50) NOT NULL,
    params json NOT NULL,
    order_id uuid,
    is_read boolean DEFAULT FALSE,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT notifications_pkey PRIMARY KEY (noti_id),
    CONSTRAINT fk_noti_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id),
    CONSTRAINT fk_noti_sender FOREIGN KEY (sender_id) REFERENCES public.users (user_id),
    CONSTRAINT fk_noti_template FOREIGN KEY (template_id) REFERENCES public.notification_templates (template_id),
    CONSTRAINT fk_noti_users FOREIGN KEY (user_id) REFERENCES public.users (user_id)
);


CREATE TABLE public.quotations (
    quote_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    order_id uuid,
    base_freight numeric(15,2) NOT NULL,
    last_mile_surcharge numeric(15,2) DEFAULT (0),
    vas_amount numeric(15,2) DEFAULT (0),
    vat_percentage numeric(5,2) DEFAULT 8.0,
    vat_amount numeric(15,2) NOT NULL,
    final_amount numeric(15,2) NOT NULL,
    chargeable_weight_kg numeric(10,2),
    volumetric_weight_kg numeric(10,2),
    price_per_kg numeric(15,2),
    distance_km numeric(10,2),
    system_base_freight numeric(15,2),
    manual_adjustment numeric(15,2) DEFAULT (0),
    additional_charges jsonb,
    override_reason character varying(500),
    pricing_source character varying(30) NOT NULL DEFAULT 'AUTO',
    file_url character varying(255),
    status character varying(20) NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT quotations_pkey PRIMARY KEY (quote_id),
    CONSTRAINT fk_quote_to FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id)
);


CREATE TABLE public.transport_documents (
    doc_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    order_id uuid,
    doc_type character varying(50) NOT NULL,
    image_url character varying(255) NOT NULL,
    status character varying(20) DEFAULT ('PENDING'::character varying),
    verified_by uuid,
    verified_at timestamp without time zone,
    reject_reason character varying(255),
    uploaded_by uuid NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT transport_documents_pkey PRIMARY KEY (doc_id),
    CONSTRAINT fk_td_to FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id),
    CONSTRAINT fk_td_uploaded FOREIGN KEY (uploaded_by) REFERENCES public.users (user_id),
    CONSTRAINT fk_td_verified FOREIGN KEY (verified_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.warehouse_receipts (
    receipt_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    receipt_code character varying(50) NOT NULL,
    reference_doc_no character varying(100),
    order_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    receipt_type character varying(20) NOT NULL,
    reason character varying(255),
    total_expected_qty numeric(10,2) DEFAULT (0),
    total_actual_qty numeric(10,2) DEFAULT (0),
    recorded_temperature numeric(5,2),
    deliverer_name character varying(100) NOT NULL,
    receiver_id uuid NOT NULL,
    note text,
    pdf_url character varying(255),
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT warehouse_receipts_pkey PRIMARY KEY (receipt_id),
    CONSTRAINT fk_wr_to FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id),
    CONSTRAINT fk_wr_users FOREIGN KEY (receiver_id) REFERENCES public.users (user_id),
    CONSTRAINT fk_wr_wh FOREIGN KEY (warehouse_id) REFERENCES public.warehouses (warehouse_id)
);


CREATE TABLE public.seals (
    seal_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    trip_id uuid,
    stop_id uuid,
    seal_code character varying(50) NOT NULL,
    applied_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    applied_image_url character varying(255),
    removed_at timestamp without time zone,
    removed_image_url character varying(255),
    status character varying(20) DEFAULT ('APPLIED'::character varying),
    note text,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT seals_pkey PRIMARY KEY (seal_id),
    CONSTRAINT fk_seals_mtrip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id),
    CONSTRAINT fk_seals_ts FOREIGN KEY (stop_id) REFERENCES public.trip_stops (stop_id)
);


CREATE TABLE public.expense_receipts (
    receipt_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    advance_id uuid NOT NULL,
    expense_type character varying(30) NOT NULL,
    description character varying(255),
    amount numeric(15,2) NOT NULL,
    expense_date date NOT NULL,
    image_url character varying(255) NOT NULL,
    status character varying(20) DEFAULT ('PENDING'::character varying),
    verified_by uuid,
    verified_at timestamp without time zone,
    reject_reason character varying(255),
    uploaded_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT expense_receipts_pkey PRIMARY KEY (receipt_id),
    CONSTRAINT fk_er_ea FOREIGN KEY (advance_id) REFERENCES public.expense_advances (advance_id),
    CONSTRAINT fk_er_users FOREIGN KEY (verified_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.contract_appendices (
    appendix_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    contract_id uuid,
    order_id uuid NOT NULL,
    appendix_number character varying(50) NOT NULL,
    adjusted_price numeric(18,2) NOT NULL,
    reason text,
    status character varying(30) NOT NULL,
    draft_html_content text,
    pdf_url character varying(255),
    created_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    sent_at timestamp without time zone,
    resolved_at timestamp without time zone,
    CONSTRAINT contract_appendices_pkey PRIMARY KEY (appendix_id),
    CONSTRAINT fk_contract_appendices_contract FOREIGN KEY (contract_id) REFERENCES public.customer_contracts (contract_id) ON DELETE SET NULL,
    CONSTRAINT fk_contract_appendices_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE
);


CREATE TABLE public.returned_items (
    return_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    epod_id uuid,
    item_name character varying(255) NOT NULL,
    item_code character varying(50),
    unit character varying(20) NOT NULL,
    returned_qty numeric(10,2) NOT NULL,
    reason_type character varying(50) NOT NULL,
    reason_note text,
    processing_status character varying(30) DEFAULT ('PENDING_INSPECT'::character varying),
    processed_by uuid,
    processed_at timestamp without time zone,
    returned_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT returned_items_pkey PRIMARY KEY (return_id),
    CONSTRAINT fk_ri_epod FOREIGN KEY (epod_id) REFERENCES public.delivery_epods (epod_id),
    CONSTRAINT fk_ri_users FOREIGN KEY (processed_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.claim_evidences (
    evidence_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    claim_id uuid,
    evidence_type character varying(30) NOT NULL,
    alert_id uuid,
    doc_id uuid,
    image_url character varying(255),
    uploaded_by uuid NOT NULL,
    created_at timestamp without time zone DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT claim_evidences_pkey PRIMARY KEY (evidence_id),
    CONSTRAINT fk_ce_alert FOREIGN KEY (alert_id) REFERENCES public.alert_logs (alert_id),
    CONSTRAINT fk_ce_claims FOREIGN KEY (claim_id) REFERENCES public.claims (claim_id),
    CONSTRAINT fk_ce_doc FOREIGN KEY (doc_id) REFERENCES public.transport_documents (doc_id),
    CONSTRAINT fk_ce_users FOREIGN KEY (uploaded_by) REFERENCES public.users (user_id)
);


CREATE TABLE public.lpns (
    lpn_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    lpn_code character varying(50) NOT NULL,
    order_id uuid NOT NULL,
    customer_id uuid,
    receipt_id uuid NOT NULL,
    warehouse_id uuid,
    route_id uuid,
    trip_id uuid,
    quantity integer NOT NULL,
    actual_weight_kg numeric(18,2) NOT NULL,
    actual_cbm numeric(18,4) NOT NULL,
    length_cm numeric(10,2),
    width_cm numeric(10,2),
    height_cm numeric(10,2),
    required_temperature numeric(8,2),
    recorded_temperature numeric(8,2),
    storage_location character varying(200),
    state character varying(30) NOT NULL,
    discrepancy_reason text,
    evidence_image_url character varying(255),
    inbound_time timestamp without time zone,
    sla_deadline timestamp without time zone,
    created_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    updated_at timestamp without time zone,
    CONSTRAINT lpns_pkey PRIMARY KEY (lpn_id),
    CONSTRAINT "FK_lpns_warehouse_receipts_receipt_id" FOREIGN KEY (receipt_id) REFERENCES public.warehouse_receipts (receipt_id) ON DELETE CASCADE,
    CONSTRAINT fk_lpns_customer FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id) ON DELETE SET NULL,
    CONSTRAINT fk_lpns_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE,
    CONSTRAINT fk_lpns_route FOREIGN KEY (route_id) REFERENCES public.route_master (route_id) ON DELETE SET NULL,
    CONSTRAINT fk_lpns_trip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id) ON DELETE SET NULL,
    CONSTRAINT fk_lpns_warehouse FOREIGN KEY (warehouse_id) REFERENCES public.warehouses (warehouse_id) ON DELETE SET NULL
);


CREATE TABLE public.inbound_return_slips (
    return_slip_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    order_id uuid NOT NULL,
    lpn_id uuid NOT NULL,
    slip_code character varying(50) NOT NULL,
    returned_weight_kg numeric(18,2) NOT NULL,
    returned_cbm numeric(18,4) NOT NULL,
    returned_qty integer NOT NULL,
    reason text,
    pdf_url character varying(255),
    created_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    CONSTRAINT inbound_return_slips_pkey PRIMARY KEY (return_slip_id),
    CONSTRAINT fk_inbound_return_slips_lpn FOREIGN KEY (lpn_id) REFERENCES public.lpns (lpn_id) ON DELETE CASCADE,
    CONSTRAINT fk_inbound_return_slips_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE
);


CREATE TABLE public.lpn_delivery_confirmations (
    confirmation_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    lpn_id uuid NOT NULL,
    trip_id uuid NOT NULL,
    order_id uuid NOT NULL,
    outcome_type character varying(20) NOT NULL,
    receiver_name character varying(200),
    receiver_phone character varying(20),
    reject_reason character varying(50),
    reject_note text,
    evidence_image_url character varying(500) NOT NULL,
    confirmed_by uuid NOT NULL,
    confirmed_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    checkin_at timestamp without time zone,
    signature_image_url character varying(500),
    cod_amount numeric(15,2) NOT NULL,
    cod_payment_method character varying(20),
    cod_receipt_image_url character varying(500),
    new_seal_number character varying(50),
    recorded_temperature numeric(8,2),
    is_cod_verified boolean NOT NULL DEFAULT FALSE,
    cod_verified_at timestamp without time zone,
    cod_verified_by uuid,
    CONSTRAINT lpn_delivery_confirmations_pkey PRIMARY KEY (confirmation_id),
    CONSTRAINT fk_lpn_delivery_confirmations_driver FOREIGN KEY (confirmed_by) REFERENCES public.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_lpn_delivery_confirmations_lpn FOREIGN KEY (lpn_id) REFERENCES public.lpns (lpn_id) ON DELETE CASCADE,
    CONSTRAINT fk_lpn_delivery_confirmations_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE,
    CONSTRAINT fk_lpn_delivery_confirmations_trip FOREIGN KEY (trip_id) REFERENCES public.master_trips (trip_id) ON DELETE CASCADE,
    CONSTRAINT fk_lpn_delivery_confirmations_verified_by FOREIGN KEY (cod_verified_by) REFERENCES public.users (user_id) ON DELETE RESTRICT
);


CREATE TABLE public.penalty_bills (
    penalty_bill_id uuid NOT NULL DEFAULT (gen_random_uuid()),
    bill_code character varying(50) NOT NULL,
    lpn_id uuid NOT NULL,
    order_id uuid NOT NULL,
    customer_id uuid,
    handling_fee numeric(18,2) NOT NULL,
    storage_fee numeric(18,2) NOT NULL,
    total_amount numeric(18,2) NOT NULL,
    reason text NOT NULL,
    is_paid boolean NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    paid_at timestamp without time zone,
    paid_by uuid,
    CONSTRAINT penalty_bills_pkey PRIMARY KEY (penalty_bill_id),
    CONSTRAINT fk_penalty_bills_customer FOREIGN KEY (customer_id) REFERENCES public.customers (customer_id) ON DELETE SET NULL,
    CONSTRAINT fk_penalty_bills_lpn FOREIGN KEY (lpn_id) REFERENCES public.lpns (lpn_id) ON DELETE CASCADE,
    CONSTRAINT fk_penalty_bills_order FOREIGN KEY (order_id) REFERENCES public.transport_orders (order_id) ON DELETE CASCADE,
    CONSTRAINT fk_penalty_bills_paid_by FOREIGN KEY (paid_by) REFERENCES public.users (user_id) ON DELETE SET NULL
);


INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 0, 0, 7, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a2', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 0, 0, 24, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b001a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a3', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 0, 0, 26, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a1', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 1, 0, 7, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a2', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 1, 0, 8, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a3', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 1, 0, 24, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b002a2a2-a2a2-a2a2-a2a2-a2a2a2a2a2a4', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 1, 0, 26, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b003a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a1', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 2, 0, 15, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b003a3a3-a3a3-a3a3-a3a3-a3a3a3a3a3a2', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 2, 0, 24, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a1', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 3, 0, 10, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a2', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 3, 0, 9, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a3', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 3, 0, 24, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b004a4a4-a4a4-a4a4-a4a4-a4a4a4a4a4a4', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 3, 0, 26, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a1', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 4, 0, 10, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a2', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 4, 0, 11, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a3', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 4, 0, 9, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a4', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 4, 0, 24, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b005a5a5-a5a5-a5a5-a5a5-a5a5a5a5a5a5', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 4, 0, 26, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a1', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 5, 0, 12, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a2', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 5, 0, 14, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a3', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 5, 1, 25, NULL, NULL);
INSERT INTO public.compliance_zoning_rules (rule_id, created_at, created_by, is_active, product_category, requirement_level, sub_category, updated_at, updated_by)
VALUES ('b006a6a6-a6a6-a6a6-a6a6-a6a6a6a6a6a4', TIMESTAMP '2026-06-14T00:00:00', '00000000-0000-0000-0000-000000000000', TRUE, 5, 0, 24, NULL, NULL);


CREATE INDEX "IX_alert_logs_resolved_by" ON public.alert_logs (resolved_by);


CREATE INDEX "IX_alert_logs_trip_id" ON public.alert_logs (trip_id);


CREATE INDEX "IX_chat_messages_order_id" ON public.chat_messages (order_id);


CREATE INDEX "IX_chat_messages_receiver_id" ON public.chat_messages (receiver_id);


CREATE INDEX "IX_chat_messages_sender_id" ON public.chat_messages (sender_id);


CREATE INDEX "IX_claim_evidences_alert_id" ON public.claim_evidences (alert_id);


CREATE INDEX "IX_claim_evidences_claim_id" ON public.claim_evidences (claim_id);


CREATE INDEX "IX_claim_evidences_doc_id" ON public.claim_evidences (doc_id);


CREATE INDEX "IX_claim_evidences_uploaded_by" ON public.claim_evidences (uploaded_by);


CREATE UNIQUE INDEX claims_claim_code_key ON public.claims (claim_code);


CREATE INDEX "IX_claims_order_id" ON public.claims (order_id);


CREATE UNIQUE INDEX uq_rule_category_subcategory ON public.compliance_zoning_rules (product_category, sub_category);


CREATE INDEX "IX_contract_appendices_contract_id" ON public.contract_appendices (contract_id);


CREATE INDEX "IX_contract_appendices_order_id" ON public.contract_appendices (order_id);


CREATE UNIQUE INDEX uq_contract_appendices_number ON public.contract_appendices (appendix_number);


CREATE UNIQUE INDEX customer_contracts_contract_number_key ON public.customer_contracts (contract_number);


CREATE INDEX "IX_customer_contracts_customer_id" ON public.customer_contracts (customer_id);


CREATE INDEX "IX_customer_contracts_order_id" ON public.customer_contracts (order_id);


CREATE UNIQUE INDEX customers_tax_code_key ON public.customers (tax_code);


CREATE INDEX "IX_delivery_epods_order_id" ON public.delivery_epods (order_id);


CREATE UNIQUE INDEX driver_licenses_license_number_key ON public.driver_licenses (license_number);


CREATE INDEX "IX_driver_licenses_driver_id" ON public.driver_licenses (driver_id);


CREATE INDEX ix_driver_work_logs_driver_date ON public.driver_work_logs (driver_id, work_date);


CREATE INDEX "IX_driver_work_logs_trip_id" ON public.driver_work_logs (trip_id);


CREATE INDEX "IX_drivers_user_id" ON public.drivers (user_id);


CREATE UNIQUE INDEX expense_advances_advance_code_key ON public.expense_advances (advance_code);


CREATE INDEX "IX_expense_advances_approved_by" ON public.expense_advances (approved_by);


CREATE INDEX "IX_expense_advances_driver_id" ON public.expense_advances (driver_id);


CREATE INDEX "IX_expense_advances_trip_id" ON public.expense_advances (trip_id);


CREATE INDEX "IX_expense_receipts_advance_id" ON public.expense_receipts (advance_id);


CREATE INDEX "IX_expense_receipts_verified_by" ON public.expense_receipts (verified_by);


CREATE INDEX "IX_geo_fences_location_id" ON public.geo_fences (location_id);


CREATE INDEX "IX_inbound_asn_order_id" ON public.inbound_asn (order_id);


CREATE INDEX "IX_inbound_return_slips_lpn_id" ON public.inbound_return_slips (lpn_id);


CREATE INDEX "IX_inbound_return_slips_order_id" ON public.inbound_return_slips (order_id);


CREATE UNIQUE INDEX uq_inbound_return_slips_code ON public.inbound_return_slips (slip_code);


CREATE INDEX "IX_incident_reports_reported_by" ON public.incident_reports (reported_by);


CREATE INDEX "IX_incident_reports_trip_id" ON public.incident_reports (trip_id);


CREATE INDEX "IX_invoice_lines_invoice_id" ON public.invoice_lines (invoice_id);


CREATE INDEX "IX_invoice_lines_order_id" ON public.invoice_lines (order_id);


CREATE UNIQUE INDEX invoices_invoice_code_key ON public.invoices (invoice_code);


CREATE INDEX "IX_invoices_customer_id" ON public.invoices (customer_id);


CREATE INDEX "IX_iot_devices_vehicle_id" ON public.iot_devices (vehicle_id);


CREATE UNIQUE INDEX uq_iot_devices_device_code ON public.iot_devices (device_code) WHERE device_code IS NOT NULL;


CREATE INDEX "IX_locations_customer_id" ON public.locations (customer_id);


CREATE INDEX "IX_lpn_delivery_confirmations_cod_verified_by" ON public.lpn_delivery_confirmations (cod_verified_by);


CREATE INDEX "IX_lpn_delivery_confirmations_confirmed_by" ON public.lpn_delivery_confirmations (confirmed_by);


CREATE INDEX "IX_lpn_delivery_confirmations_order_id" ON public.lpn_delivery_confirmations (order_id);


CREATE INDEX "IX_lpn_delivery_confirmations_trip_id" ON public.lpn_delivery_confirmations (trip_id);


CREATE UNIQUE INDEX uq_lpn_delivery_confirmations_lpn_id ON public.lpn_delivery_confirmations (lpn_id);


CREATE INDEX idx_lpns_order_id ON public.lpns (order_id);


CREATE INDEX idx_lpns_state ON public.lpns (state);


CREATE INDEX idx_lpns_storage_location ON public.lpns (storage_location);


CREATE INDEX idx_lpns_warehouse_id ON public.lpns (warehouse_id);


CREATE INDEX "IX_lpns_customer_id" ON public.lpns (customer_id);


CREATE INDEX "IX_lpns_receipt_id" ON public.lpns (receipt_id);


CREATE INDEX "IX_lpns_route_id" ON public.lpns (route_id);


CREATE INDEX "IX_lpns_trip_id" ON public.lpns (trip_id);


CREATE UNIQUE INDEX uq_lpns_lpn_code ON public.lpns (lpn_code);


CREATE INDEX "IX_maintenance_tickets_created_by" ON public.maintenance_tickets (created_by);


CREATE INDEX "IX_maintenance_tickets_vehicle_id" ON public.maintenance_tickets (vehicle_id);


CREATE UNIQUE INDEX maintenance_tickets_ticket_code_key ON public.maintenance_tickets (ticket_code);


CREATE INDEX "IX_master_trips_destination_location_id" ON public.master_trips (destination_location_id);


CREATE INDEX "IX_master_trips_origin_location_id" ON public.master_trips (origin_location_id);


CREATE INDEX "IX_master_trips_vehicle_id" ON public.master_trips (vehicle_id);


CREATE INDEX "IX_notification_templates_type_id" ON public.notification_templates (type_id);


CREATE INDEX "IX_notifications_order_id" ON public.notifications (order_id);


CREATE INDEX "IX_notifications_sender_id" ON public.notifications (sender_id);


CREATE INDEX "IX_notifications_template_id" ON public.notifications (template_id);


CREATE INDEX "IX_notifications_user_id" ON public.notifications (user_id);


CREATE INDEX "IX_outbound_order_items_outbound_order_id" ON public.outbound_order_items (outbound_order_id);


CREATE INDEX "IX_outbound_orders_assigned_picker_id" ON public.outbound_orders (assigned_picker_id);


CREATE INDEX "IX_outbound_orders_customer_id" ON public.outbound_orders (customer_id);


CREATE UNIQUE INDEX uq_outbound_order_code ON public.outbound_orders (order_code);


CREATE INDEX idx_penalty_bills_is_paid ON public.penalty_bills (is_paid);


CREATE INDEX idx_penalty_bills_lpn_id ON public.penalty_bills (lpn_id);


CREATE INDEX "IX_penalty_bills_customer_id" ON public.penalty_bills (customer_id);


CREATE INDEX "IX_penalty_bills_order_id" ON public.penalty_bills (order_id);


CREATE INDEX "IX_penalty_bills_paid_by" ON public.penalty_bills (paid_by);


CREATE UNIQUE INDEX uq_penalty_bills_bill_code ON public.penalty_bills (bill_code);


CREATE UNIQUE INDEX permissions_perm_code_key ON public.permissions (perm_code);


CREATE INDEX "IX_quotations_order_id" ON public.quotations (order_id);


CREATE INDEX "IX_returned_items_epod_id" ON public.returned_items (epod_id);


CREATE INDEX "IX_returned_items_processed_by" ON public.returned_items (processed_by);


CREATE INDEX "IX_role_permissions_perm_id" ON public.role_permissions (perm_id);


CREATE UNIQUE INDEX roles_role_name_key ON public.roles (role_name);


CREATE INDEX "IX_seals_stop_id" ON public.seals (stop_id);


CREATE INDEX "IX_seals_trip_id" ON public.seals (trip_id);


CREATE INDEX "IX_telemetry_logs_device_id" ON public.telemetry_logs (device_id);


CREATE INDEX "IX_telemetry_logs_trip_id" ON public.telemetry_logs (trip_id);


CREATE INDEX "IX_transport_documents_order_id" ON public.transport_documents (order_id);


CREATE INDEX "IX_transport_documents_uploaded_by" ON public.transport_documents (uploaded_by);


CREATE INDEX "IX_transport_documents_verified_by" ON public.transport_documents (verified_by);


CREATE INDEX "IX_transport_orders_customer_id" ON public.transport_orders (customer_id);


CREATE INDEX "IX_transport_orders_dest_location" ON public.transport_orders (dest_location);


CREATE INDEX "IX_transport_orders_master_trip_id" ON public.transport_orders (master_trip_id);


CREATE INDEX "IX_transport_orders_pickup_location" ON public.transport_orders (pickup_location);


CREATE INDEX "IX_transport_orders_route_id" ON public.transport_orders (route_id);


CREATE UNIQUE INDEX transport_orders_tracking_code_key ON public.transport_orders (tracking_code);


CREATE INDEX "IX_trip_drivers_driver_id" ON public.trip_drivers (driver_id);


CREATE UNIQUE INDEX trip_drivers_trip_driver_key ON public.trip_drivers (trip_id, driver_id);


CREATE INDEX "IX_trip_stops_location_id" ON public.trip_stops (location_id);


CREATE INDEX "IX_trip_stops_trip_id" ON public.trip_stops (trip_id);


CREATE INDEX "IX_users_role_id" ON public.users (role_id);


CREATE UNIQUE INDEX users_username_key ON public.users (username);


CREATE INDEX "IX_vehicle_documents_vehicle_id" ON public.vehicle_documents (vehicle_id);


CREATE UNIQUE INDEX vehicles_chassis_number_key ON public.vehicles (chassis_number);


CREATE UNIQUE INDEX vehicles_engine_number_key ON public.vehicles (engine_number);


CREATE UNIQUE INDEX vehicles_truck_plate_key ON public.vehicles (truck_plate);


CREATE UNIQUE INDEX "IX_warehouse_locations_zone_id_location_code" ON public.warehouse_locations (zone_id, location_code) WHERE "deleted_at" IS NULL;


CREATE INDEX "IX_warehouse_receipts_order_id" ON public.warehouse_receipts (order_id);


CREATE INDEX "IX_warehouse_receipts_receiver_id" ON public.warehouse_receipts (receiver_id);


CREATE INDEX "IX_warehouse_receipts_warehouse_id" ON public.warehouse_receipts (warehouse_id);


CREATE UNIQUE INDEX warehouse_receipts_receipt_code_key ON public.warehouse_receipts (receipt_code);


CREATE UNIQUE INDEX "IX_warehouse_zones_warehouse_id_zone_code" ON public.warehouse_zones (warehouse_id, zone_code) WHERE "deleted_at" IS NULL;


CREATE UNIQUE INDEX warehouses_warehouse_code_key ON public.warehouses (warehouse_code) WHERE "deleted_at" IS NULL;


CREATE INDEX "IX_weight_tiers_route_id" ON public.weight_tiers (route_id);



CREATE TABLE trip_stop_events (
    event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    stop_id UUID NOT NULL REFERENCES trip_stops(stop_id) ON DELETE CASCADE,
    event_type VARCHAR(50) NOT NULL,
    event_time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    meta_data TEXT
);

CREATE TABLE detention_charges (
    charge_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    stop_id UUID NOT NULL REFERENCES trip_stops(stop_id) ON DELETE CASCADE,
    customer_id UUID REFERENCES customers(customer_id) ON DELETE SET NULL,
    free_minutes_allocated INT NOT NULL DEFAULT 30,
    actual_wait_minutes INT NOT NULL,
    amount_charged DECIMAL(15,2) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'UNPAID'
);

CREATE TABLE incident_evidences (
    evidence_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    incident_id UUID NOT NULL REFERENCES incident_reports(incident_id) ON DELETE CASCADE,
    evidence_type VARCHAR(50) NOT NULL,
    file_url VARCHAR(500) NOT NULL
);

ALTER TABLE master_trips ADD COLUMN requires_inspection BOOLEAN DEFAULT FALSE;
ALTER TABLE expense_receipts ALTER COLUMN advance_id DROP NOT NULL;
ALTER TABLE expense_receipts ADD COLUMN trip_id UUID REFERENCES master_trips(trip_id) ON DELETE CASCADE;
ALTER TABLE alert_logs ADD COLUMN lpn_id UUID REFERENCES lpns(lpn_id) ON DELETE SET NULL;
ALTER TABLE claims ADD COLUMN lpn_id UUID REFERENCES lpns(lpn_id) ON DELETE SET NULL;

