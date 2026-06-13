using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ColdChainX.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "public",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    company_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    payment_term = table.Column<int>(type: "integer", nullable: true, defaultValue: 30),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("customers_pkey", x => x.customer_id);
                });

            migrationBuilder.CreateTable(
                name: "drivers",
                schema: "public",
                columns: table => new
                {
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'AVAILABLE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("drivers_pkey", x => x.driver_id);
                });

            migrationBuilder.CreateTable(
                name: "messagetype",
                schema: "public",
                columns: table => new
                {
                    type_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("messagetype_pkey", x => x.type_id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "public",
                columns: table => new
                {
                    perm_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    perm_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("permissions_pkey", x => x.perm_id);
                });

            migrationBuilder.CreateTable(
                name: "pricing_matrix",
                schema: "public",
                columns: table => new
                {
                    price_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    origin_city = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    dest_city = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    pricing_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pricing_matrix_pkey", x => x.price_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "public",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    role_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("roles_pkey", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                schema: "public",
                columns: table => new
                {
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    truck_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    brand = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    manufacture_year = table.Column<int>(type: "integer", nullable: true),
                    chassis_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    engine_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    standard_fuel_liters = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    vehicle_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    max_weight = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    max_cbm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    min_temp = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    max_temp = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("vehicles_pkey", x => x.vehicle_id);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "public",
                columns: table => new
                {
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    warehouse_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    max_pallets = table.Column<int>(type: "integer", nullable: false),
                    current_pallets = table.Column<int>(type: "integer", nullable: true, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouses_pkey", x => x.warehouse_id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                schema: "public",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vat_invoice_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pdf_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sub_total = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true, defaultValueSql: "8.00"),
                    tax_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    deduction_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    grand_total = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    issued_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'DRAFT'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("invoices_pkey", x => x.invoice_id);
                    table.ForeignKey(
                        name: "fk_inv_customers",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id");
                });

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "public",
                columns: table => new
                {
                    location_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    address = table.Column<string>(type: "text", nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("locations_pkey", x => x.location_id);
                    table.ForeignKey(
                        name: "fk_loc_customers",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id");
                });

            migrationBuilder.CreateTable(
                name: "driver_licenses",
                schema: "public",
                columns: table => new
                {
                    license_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    license_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    license_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    document_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("driver_licenses_pkey", x => x.license_id);
                    table.ForeignKey(
                        name: "fk_dl_drivers",
                        column: x => x.driver_id,
                        principalSchema: "public",
                        principalTable: "drivers",
                        principalColumn: "driver_id");
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                schema: "public",
                columns: table => new
                {
                    template_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_template = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    body_template = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying")
                },
                constraints: table =>
                {
                    table.PrimaryKey("notification_templates_pkey", x => x.template_id);
                    table.ForeignKey(
                        name: "fk_nt_msgtype",
                        column: x => x.type_id,
                        principalSchema: "public",
                        principalTable: "messagetype",
                        principalColumn: "type_id");
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "public",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    perm_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("role_permissions_pkey", x => new { x.role_id, x.perm_id });
                    table.ForeignKey(
                        name: "fk_rp_perms",
                        column: x => x.perm_id,
                        principalSchema: "public",
                        principalTable: "permissions",
                        principalColumn: "perm_id");
                    table.ForeignKey(
                        name: "fk_rp_roles",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "roles",
                        principalColumn: "role_id");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "public",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", maxLength: 36, nullable: false),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    refresh_token = table.Column<string>(type: "text", nullable: true),
                    refresh_token_expiry_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("users_pkey", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_users_roles",
                        column: x => x.role_id,
                        principalSchema: "public",
                        principalTable: "roles",
                        principalColumn: "role_id");
                });

            migrationBuilder.CreateTable(
                name: "iot_devices",
                schema: "public",
                columns: table => new
                {
                    device_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    battery_level = table.Column<int>(type: "integer", nullable: true),
                    last_ping_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("iot_devices_pkey", x => x.device_id);
                    table.ForeignKey(
                        name: "fk_iot_vehicles",
                        column: x => x.vehicle_id,
                        principalSchema: "public",
                        principalTable: "vehicles",
                        principalColumn: "vehicle_id");
                });

            migrationBuilder.CreateTable(
                name: "vehicle_documents",
                schema: "public",
                columns: table => new
                {
                    doc_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    issuer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("vehicle_documents_pkey", x => x.doc_id);
                    table.ForeignKey(
                        name: "fk_vd_vehicles",
                        column: x => x.vehicle_id,
                        principalSchema: "public",
                        principalTable: "vehicles",
                        principalColumn: "vehicle_id");
                });

            migrationBuilder.CreateTable(
                name: "geo_fences",
                schema: "public",
                columns: table => new
                {
                    fence_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("geo_fences_pkey", x => x.fence_id);
                    table.ForeignKey(
                        name: "fk_geo_locations",
                        column: x => x.location_id,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                });

            migrationBuilder.CreateTable(
                name: "master_trips",
                schema: "public",
                columns: table => new
                {
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origin_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_distance_km = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    target_temperature = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    planned_start_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    planned_end_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'PLANNED'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("master_trips_pkey", x => x.trip_id);
                    table.ForeignKey(
                        name: "fk_mtrip_dest",
                        column: x => x.destination_location_id,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                    table.ForeignKey(
                        name: "fk_mtrip_drivers",
                        column: x => x.driver_id,
                        principalSchema: "public",
                        principalTable: "drivers",
                        principalColumn: "driver_id");
                    table.ForeignKey(
                        name: "fk_mtrip_orig",
                        column: x => x.origin_location_id,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                    table.ForeignKey(
                        name: "fk_mtrip_vehicles",
                        column: x => x.vehicle_id,
                        principalSchema: "public",
                        principalTable: "vehicles",
                        principalColumn: "vehicle_id");
                });

            migrationBuilder.CreateTable(
                name: "maintenance_tickets",
                schema: "public",
                columns: table => new
                {
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ticket_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: true),
                    maintenance_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    garage_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    cost = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    completion_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'OPEN'::character varying"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("maintenance_tickets_pkey", x => x.ticket_id);
                    table.ForeignKey(
                        name: "fk_mt_users",
                        column: x => x.created_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "fk_mt_vehicles",
                        column: x => x.vehicle_id,
                        principalSchema: "public",
                        principalTable: "vehicles",
                        principalColumn: "vehicle_id");
                });

            migrationBuilder.CreateTable(
                name: "alert_logs",
                schema: "public",
                columns: table => new
                {
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alert_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'NEW'::character varying"),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("alert_logs_pkey", x => x.alert_id);
                    table.ForeignKey(
                        name: "fk_al_mtrip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                    table.ForeignKey(
                        name: "fk_al_users",
                        column: x => x.resolved_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "expense_advances",
                schema: "public",
                columns: table => new
                {
                    advance_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    advance_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'CASH'::character varying"),
                    advanced_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    cleared_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    returned_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0.00"),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'PENDING'::character varying"),
                    clearance_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'OPEN'::character varying"),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("expense_advances_pkey", x => x.advance_id);
                    table.ForeignKey(
                        name: "fk_ea_drivers",
                        column: x => x.driver_id,
                        principalSchema: "public",
                        principalTable: "drivers",
                        principalColumn: "driver_id");
                    table.ForeignKey(
                        name: "fk_ea_mtrip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                    table.ForeignKey(
                        name: "fk_ea_users",
                        column: x => x.approved_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "incident_reports",
                schema: "public",
                columns: table => new
                {
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incident_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    current_latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    current_longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'REPORTED'::character varying"),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("incident_reports_pkey", x => x.incident_id);
                    table.ForeignKey(
                        name: "fk_ir_mtrip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                    table.ForeignKey(
                        name: "fk_ir_users",
                        column: x => x.reported_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "telemetry_logs",
                schema: "public",
                columns: table => new
                {
                    log_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("telemetry_logs_pkey", x => x.log_id);
                    table.ForeignKey(
                        name: "fk_tl_iot",
                        column: x => x.device_id,
                        principalSchema: "public",
                        principalTable: "iot_devices",
                        principalColumn: "device_id");
                    table.ForeignKey(
                        name: "fk_tl_mtrip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                });

            migrationBuilder.CreateTable(
                name: "transport_orders",
                schema: "public",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tracking_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    packing_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Thùng'::character varying"),
                    temp_condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    expected_cbm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    actual_cbm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    pickup_location = table.Column<Guid>(type: "uuid", nullable: true),
                    dest_location = table.Column<Guid>(type: "uuid", nullable: true),
                    cargo_value = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    master_trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("transport_orders_pkey", x => x.order_id);
                    table.ForeignKey(
                        name: "fk_to_customers",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "fk_to_dest",
                        column: x => x.dest_location,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                    table.ForeignKey(
                        name: "fk_to_mtrip",
                        column: x => x.master_trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                    table.ForeignKey(
                        name: "fk_to_pickup",
                        column: x => x.pickup_location,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                });

            migrationBuilder.CreateTable(
                name: "trip_stops",
                schema: "public",
                columns: table => new
                {
                    stop_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    stop_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    planned_arrival_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    planned_departure_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    actual_arrival_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    actual_departure_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'PENDING'::character varying"),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("trip_stops_pkey", x => x.stop_id);
                    table.ForeignKey(
                        name: "fk_ts_loc",
                        column: x => x.location_id,
                        principalSchema: "public",
                        principalTable: "locations",
                        principalColumn: "location_id");
                    table.ForeignKey(
                        name: "fk_ts_mtrip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                });

            migrationBuilder.CreateTable(
                name: "expense_receipts",
                schema: "public",
                columns: table => new
                {
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    advance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expense_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'PENDING'::character varying"),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("expense_receipts_pkey", x => x.receipt_id);
                    table.ForeignKey(
                        name: "fk_er_ea",
                        column: x => x.advance_id,
                        principalSchema: "public",
                        principalTable: "expense_advances",
                        principalColumn: "advance_id");
                    table.ForeignKey(
                        name: "fk_er_users",
                        column: x => x.verified_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "claims",
                schema: "public",
                columns: table => new
                {
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    claim_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claim_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    fault_owner = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'OPEN'::character varying"),
                    resolution_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("claims_pkey", x => x.claim_id);
                    table.ForeignKey(
                        name: "fk_claims_to",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateTable(
                name: "customer_contracts",
                schema: "public",
                columns: table => new
                {
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    signed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expired_date = table.Column<DateOnly>(type: "date", nullable: false),
                    file_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'ACTIVE'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("customer_contracts_pkey", x => x.contract_id);
                    table.ForeignKey(
                        name: "fk_cc_customers",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "customer_id");
                    table.ForeignKey(
                        name: "fk_cc_orders",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateTable(
                name: "delivery_epods",
                schema: "public",
                columns: table => new
                {
                    epod_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checkin_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    signed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    receiver_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    receiver_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sign_image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sign_latitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    sign_longitude = table.Column<decimal>(type: "numeric(10,7)", precision: 10, scale: 7, nullable: true),
                    delivery_rating = table.Column<int>(type: "integer", nullable: true, defaultValue: 5),
                    note = table.Column<string>(type: "text", nullable: true),
                    pdf_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true, defaultValueSql: "'PENDING'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("delivery_epods_pkey", x => x.epod_id);
                    table.ForeignKey(
                        name: "fk_epod_to",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "public",
                columns: table => new
                {
                    line_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true, defaultValueSql: "1.00"),
                    unit_price = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true, defaultValueSql: "8.00")
                },
                constraints: table =>
                {
                    table.PrimaryKey("invoice_lines_pkey", x => x.line_id);
                    table.ForeignKey(
                        name: "fk_il_inv",
                        column: x => x.invoice_id,
                        principalSchema: "public",
                        principalTable: "invoices",
                        principalColumn: "invoice_id");
                    table.ForeignKey(
                        name: "fk_il_to",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "public",
                columns: table => new
                {
                    noti_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    @params = table.Column<string>(name: "params", type: "json", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("notifications_pkey", x => x.noti_id);
                    table.ForeignKey(
                        name: "fk_noti_order",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                    table.ForeignKey(
                        name: "fk_noti_sender",
                        column: x => x.sender_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "fk_noti_template",
                        column: x => x.template_id,
                        principalSchema: "public",
                        principalTable: "notification_templates",
                        principalColumn: "template_id");
                    table.ForeignKey(
                        name: "fk_noti_users",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "quotations",
                schema: "public",
                columns: table => new
                {
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    base_freight = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    last_mile_surcharge = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0"),
                    vas_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true, defaultValueSql: "0"),
                    vat_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    final_amount = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: false),
                    file_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("quotations_pkey", x => x.quote_id);
                    table.ForeignKey(
                        name: "fk_quote_to",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                });

            migrationBuilder.CreateTable(
                name: "transport_documents",
                schema: "public",
                columns: table => new
                {
                    doc_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doc_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'PENDING'::character varying"),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("transport_documents_pkey", x => x.doc_id);
                    table.ForeignKey(
                        name: "fk_td_to",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                    table.ForeignKey(
                        name: "fk_td_uploaded",
                        column: x => x.uploaded_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "fk_td_verified",
                        column: x => x.verified_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "warehouse_receipts",
                schema: "public",
                columns: table => new
                {
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    receipt_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_doc_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    total_expected_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true, defaultValueSql: "0"),
                    total_actual_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true, defaultValueSql: "0"),
                    recorded_temperature = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    deliverer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    receiver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    pdf_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_receipts_pkey", x => x.receipt_id);
                    table.ForeignKey(
                        name: "fk_wr_to",
                        column: x => x.order_id,
                        principalSchema: "public",
                        principalTable: "transport_orders",
                        principalColumn: "order_id");
                    table.ForeignKey(
                        name: "fk_wr_users",
                        column: x => x.receiver_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "fk_wr_wh",
                        column: x => x.warehouse_id,
                        principalSchema: "public",
                        principalTable: "warehouses",
                        principalColumn: "warehouse_id");
                });

            migrationBuilder.CreateTable(
                name: "seals",
                schema: "public",
                columns: table => new
                {
                    seal_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seal_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP"),
                    applied_image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    removed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    removed_image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true, defaultValueSql: "'APPLIED'::character varying"),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("seals_pkey", x => x.seal_id);
                    table.ForeignKey(
                        name: "fk_seals_mtrip",
                        column: x => x.trip_id,
                        principalSchema: "public",
                        principalTable: "master_trips",
                        principalColumn: "trip_id");
                    table.ForeignKey(
                        name: "fk_seals_ts",
                        column: x => x.stop_id,
                        principalSchema: "public",
                        principalTable: "trip_stops",
                        principalColumn: "stop_id");
                });

            migrationBuilder.CreateTable(
                name: "returned_items",
                schema: "public",
                columns: table => new
                {
                    return_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    epod_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    returned_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reason_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reason_note = table.Column<string>(type: "text", nullable: true),
                    processing_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true, defaultValueSql: "'PENDING_INSPECT'::character varying"),
                    processed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    returned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("returned_items_pkey", x => x.return_id);
                    table.ForeignKey(
                        name: "fk_ri_epod",
                        column: x => x.epod_id,
                        principalSchema: "public",
                        principalTable: "delivery_epods",
                        principalColumn: "epod_id");
                    table.ForeignKey(
                        name: "fk_ri_users",
                        column: x => x.processed_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "claim_evidences",
                schema: "public",
                columns: table => new
                {
                    evidence_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evidence_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doc_id = table.Column<Guid>(type: "uuid", nullable: true),
                    image_url = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("claim_evidences_pkey", x => x.evidence_id);
                    table.ForeignKey(
                        name: "fk_ce_alert",
                        column: x => x.alert_id,
                        principalSchema: "public",
                        principalTable: "alert_logs",
                        principalColumn: "alert_id");
                    table.ForeignKey(
                        name: "fk_ce_claims",
                        column: x => x.claim_id,
                        principalSchema: "public",
                        principalTable: "claims",
                        principalColumn: "claim_id");
                    table.ForeignKey(
                        name: "fk_ce_doc",
                        column: x => x.doc_id,
                        principalSchema: "public",
                        principalTable: "transport_documents",
                        principalColumn: "doc_id");
                    table.ForeignKey(
                        name: "fk_ce_users",
                        column: x => x.uploaded_by,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "user_id");
                });

            migrationBuilder.CreateTable(
                name: "warehouse_receipt_items",
                schema: "public",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    item_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    actual_qty = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    condition_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, defaultValueSql: "'GOOD'::character varying"),
                    note = table.Column<string>(type: "text", nullable: true),
                    actual_weight_kg = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    length_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    width_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    qr_code = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("warehouse_receipt_items_pkey", x => x.item_id);
                    table.ForeignKey(
                        name: "fk_wri_wr",
                        column: x => x.receipt_id,
                        principalSchema: "public",
                        principalTable: "warehouse_receipts",
                        principalColumn: "receipt_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_logs_resolved_by",
                schema: "public",
                table: "alert_logs",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "IX_alert_logs_trip_id",
                schema: "public",
                table: "alert_logs",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_claim_evidences_alert_id",
                schema: "public",
                table: "claim_evidences",
                column: "alert_id");

            migrationBuilder.CreateIndex(
                name: "IX_claim_evidences_claim_id",
                schema: "public",
                table: "claim_evidences",
                column: "claim_id");

            migrationBuilder.CreateIndex(
                name: "IX_claim_evidences_doc_id",
                schema: "public",
                table: "claim_evidences",
                column: "doc_id");

            migrationBuilder.CreateIndex(
                name: "IX_claim_evidences_uploaded_by",
                schema: "public",
                table: "claim_evidences",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "claims_claim_code_key",
                schema: "public",
                table: "claims",
                column: "claim_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_claims_order_id",
                schema: "public",
                table: "claims",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "customer_contracts_contract_number_key",
                schema: "public",
                table: "customer_contracts",
                column: "contract_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_contracts_customer_id",
                schema: "public",
                table: "customer_contracts",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_contracts_order_id",
                schema: "public",
                table: "customer_contracts",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "customers_tax_code_key",
                schema: "public",
                table: "customers",
                column: "tax_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_epods_order_id",
                schema: "public",
                table: "delivery_epods",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "driver_licenses_license_number_key",
                schema: "public",
                table: "driver_licenses",
                column: "license_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_driver_licenses_driver_id",
                schema: "public",
                table: "driver_licenses",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "expense_advances_advance_code_key",
                schema: "public",
                table: "expense_advances",
                column: "advance_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expense_advances_approved_by",
                schema: "public",
                table: "expense_advances",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_expense_advances_driver_id",
                schema: "public",
                table: "expense_advances",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_advances_trip_id",
                schema: "public",
                table: "expense_advances",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_receipts_advance_id",
                schema: "public",
                table: "expense_receipts",
                column: "advance_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_receipts_verified_by",
                schema: "public",
                table: "expense_receipts",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "IX_geo_fences_location_id",
                schema: "public",
                table: "geo_fences",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_reported_by",
                schema: "public",
                table: "incident_reports",
                column: "reported_by");

            migrationBuilder.CreateIndex(
                name: "IX_incident_reports_trip_id",
                schema: "public",
                table: "incident_reports",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_invoice_id",
                schema: "public",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_order_id",
                schema: "public",
                table: "invoice_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "invoices_invoice_code_key",
                schema: "public",
                table: "invoices",
                column: "invoice_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_customer_id",
                schema: "public",
                table: "invoices",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_iot_devices_vehicle_id",
                schema: "public",
                table: "iot_devices",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "IX_locations_customer_id",
                schema: "public",
                table: "locations",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tickets_created_by",
                schema: "public",
                table: "maintenance_tickets",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_tickets_vehicle_id",
                schema: "public",
                table: "maintenance_tickets",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "maintenance_tickets_ticket_code_key",
                schema: "public",
                table: "maintenance_tickets",
                column: "ticket_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_destination_location_id",
                schema: "public",
                table: "master_trips",
                column: "destination_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_driver_id",
                schema: "public",
                table: "master_trips",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_origin_location_id",
                schema: "public",
                table: "master_trips",
                column: "origin_location_id");

            migrationBuilder.CreateIndex(
                name: "IX_master_trips_vehicle_id",
                schema: "public",
                table: "master_trips",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_templates_type_id",
                schema: "public",
                table: "notification_templates",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_order_id",
                schema: "public",
                table: "notifications",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_sender_id",
                schema: "public",
                table: "notifications",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_template_id",
                schema: "public",
                table: "notifications",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_user_id",
                schema: "public",
                table: "notifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "permissions_perm_code_key",
                schema: "public",
                table: "permissions",
                column: "perm_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotations_order_id",
                schema: "public",
                table: "quotations",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_returned_items_epod_id",
                schema: "public",
                table: "returned_items",
                column: "epod_id");

            migrationBuilder.CreateIndex(
                name: "IX_returned_items_processed_by",
                schema: "public",
                table: "returned_items",
                column: "processed_by");

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_perm_id",
                schema: "public",
                table: "role_permissions",
                column: "perm_id");

            migrationBuilder.CreateIndex(
                name: "roles_role_name_key",
                schema: "public",
                table: "roles",
                column: "role_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seals_stop_id",
                schema: "public",
                table: "seals",
                column: "stop_id");

            migrationBuilder.CreateIndex(
                name: "IX_seals_trip_id",
                schema: "public",
                table: "seals",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_logs_device_id",
                schema: "public",
                table: "telemetry_logs",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_logs_trip_id",
                schema: "public",
                table: "telemetry_logs",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_transport_documents_order_id",
                schema: "public",
                table: "transport_documents",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_transport_documents_uploaded_by",
                schema: "public",
                table: "transport_documents",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_transport_documents_verified_by",
                schema: "public",
                table: "transport_documents",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_customer_id",
                schema: "public",
                table: "transport_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_dest_location",
                schema: "public",
                table: "transport_orders",
                column: "dest_location");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_master_trip_id",
                schema: "public",
                table: "transport_orders",
                column: "master_trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_pickup_location",
                schema: "public",
                table: "transport_orders",
                column: "pickup_location");

            migrationBuilder.CreateIndex(
                name: "transport_orders_tracking_code_key",
                schema: "public",
                table: "transport_orders",
                column: "tracking_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trip_stops_location_id",
                schema: "public",
                table: "trip_stops",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_trip_stops_trip_id",
                schema: "public",
                table: "trip_stops",
                column: "trip_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                schema: "public",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "users_username_key",
                schema: "public",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_documents_vehicle_id",
                schema: "public",
                table: "vehicle_documents",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "vehicles_chassis_number_key",
                schema: "public",
                table: "vehicles",
                column: "chassis_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "vehicles_engine_number_key",
                schema: "public",
                table: "vehicles",
                column: "engine_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "vehicles_truck_plate_key",
                schema: "public",
                table: "vehicles",
                column: "truck_plate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_receipt_items_receipt_id",
                schema: "public",
                table: "warehouse_receipt_items",
                column: "receipt_id");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_receipts_order_id",
                schema: "public",
                table: "warehouse_receipts",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_receipts_receiver_id",
                schema: "public",
                table: "warehouse_receipts",
                column: "receiver_id");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_receipts_warehouse_id",
                schema: "public",
                table: "warehouse_receipts",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "warehouse_receipts_receipt_code_key",
                schema: "public",
                table: "warehouse_receipts",
                column: "receipt_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim_evidences",
                schema: "public");

            migrationBuilder.DropTable(
                name: "customer_contracts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "driver_licenses",
                schema: "public");

            migrationBuilder.DropTable(
                name: "expense_receipts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "geo_fences",
                schema: "public");

            migrationBuilder.DropTable(
                name: "incident_reports",
                schema: "public");

            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "public");

            migrationBuilder.DropTable(
                name: "maintenance_tickets",
                schema: "public");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "public");

            migrationBuilder.DropTable(
                name: "pricing_matrix",
                schema: "public");

            migrationBuilder.DropTable(
                name: "quotations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "returned_items",
                schema: "public");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "seals",
                schema: "public");

            migrationBuilder.DropTable(
                name: "telemetry_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "vehicle_documents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "warehouse_receipt_items",
                schema: "public");

            migrationBuilder.DropTable(
                name: "alert_logs",
                schema: "public");

            migrationBuilder.DropTable(
                name: "claims",
                schema: "public");

            migrationBuilder.DropTable(
                name: "transport_documents",
                schema: "public");

            migrationBuilder.DropTable(
                name: "expense_advances",
                schema: "public");

            migrationBuilder.DropTable(
                name: "invoices",
                schema: "public");

            migrationBuilder.DropTable(
                name: "notification_templates",
                schema: "public");

            migrationBuilder.DropTable(
                name: "delivery_epods",
                schema: "public");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "trip_stops",
                schema: "public");

            migrationBuilder.DropTable(
                name: "iot_devices",
                schema: "public");

            migrationBuilder.DropTable(
                name: "warehouse_receipts",
                schema: "public");

            migrationBuilder.DropTable(
                name: "messagetype",
                schema: "public");

            migrationBuilder.DropTable(
                name: "transport_orders",
                schema: "public");

            migrationBuilder.DropTable(
                name: "users",
                schema: "public");

            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "public");

            migrationBuilder.DropTable(
                name: "master_trips",
                schema: "public");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "public");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "drivers",
                schema: "public");

            migrationBuilder.DropTable(
                name: "vehicles",
                schema: "public");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "public");
        }
    }
}
