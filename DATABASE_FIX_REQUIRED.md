# Database Error Fix

## Error Message
```
42P01: relation "users" does not exist
POSITION: 209
```

## Root Cause
The PostgreSQL database doesn't have the `users` table created yet. This happens when:
1. The database is new and migrations haven't been applied
2. The migrations exist but haven't been run against the database

## Solution Options

### Option 1: Apply Migrations (Recommended)
Run the Entity Framework migrations to create all database tables:

```bash
# From the solution root directory
dotnet ef database update --project ColdChainX.Infrastructure --startup-project ColdChainX.API
```

If migrations don't exist or need to be recreated:
```bash
# Create a new migration
dotnet ef migrations add InitialCreate --project ColdChainX.Infrastructure --startup-project ColdChainX.API

# Apply the migration
dotnet ef database update --project ColdChainX.Infrastructure --startup-project ColdChainX.API
```

### Option 2: Check Database Schema
If tables exist but in a different schema:

1. Connect to your PostgreSQL database:
   - Host: `coldchainx-db-server.postgres.database.azure.com`
   - Port: `5432`
   - Database: `postgres`
   - Username: `postgres`

2. Check if tables exist:
```sql
SELECT table_schema, table_name 
FROM information_schema.tables 
WHERE table_name = 'users';
```

3. If tables are in a different schema (not `public`), update `ApplicationDbContext.cs`:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema("your_schema_name");
    // ... rest of configuration
}
```

### Option 3: Script Out Database (If tables need to be created manually)
```bash
# Generate SQL script from migrations
dotnet ef migrations script --project ColdChainX.Infrastructure --startup-project ColdChainX.API --output database_schema.sql
```

Then run the generated SQL script on your Azure PostgreSQL database.

## Changes Made to Code

### 1. Added Phone Property to User Entity
File: `ColdChainX.Core/Entities/User.cs`
```csharp
public string? Phone { get; set; }
```

### 2. Updated Database Mapping
File: `ColdChainX.Infrastructure/Persistence/ApplicationDbContext.cs`
```csharp
entity.Property(e => e.Phone)
    .HasMaxLength(20)
    .HasColumnName("phone");
```

### 3. Updated Service Methods
Files: `ColdChainX.Application/Services/AuthService.cs`
- Added `Phone = request.Phone?.Trim()` to all User creation methods
- `CreateCustomerAsync()`
- `CreateDriverAsync()`

## Verification Steps

After applying migrations, verify tables exist:

```sql
-- Check users table
SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_name = 'users'
ORDER BY ordinal_position;

-- Check roles table  
SELECT * FROM roles;

-- Should show:
-- 1 | Admin    | null
-- 2 | Customer | null
-- 3 | Driver   | null
-- 4 | Manager  | null
```

## Expected Database Schema

### users table columns:
- user_id (uuid, PK)
- username (varchar(50), unique)
- password_hash (varchar(255))
- email (varchar(255))
- phone (varchar(20)) ← **NEWLY ADDED**
- role_id (uuid, FK to roles)
- full_name (varchar(100))
- status (varchar(20), default 'ACTIVE')
- refresh_token (varchar(255))
- refresh_token_expiry_time (timestamp)
- created_at (timestamp, default CURRENT_TIMESTAMP)
- updated_at (timestamp)

### roles table columns:
- role_id (uuid, PK)
- role_name (varchar(50))
- description (text)
- created_at (timestamp)

### customers table columns:
- customer_id (uuid, PK)
- company_name (varchar(200))
- tax_code (varchar(50))
- address (text)
- email (varchar(255))
- payment_term (integer)
- status (varchar(20))
- created_at (timestamp)

### drivers table columns:
- driver_id (uuid, PK)
- date_of_birth (date)
- status (varchar(20))
- created_at (timestamp)

### driver_licenses table columns:
- license_id (uuid, PK)
- driver_id (uuid, FK to drivers)
- license_number (varchar(50))
- license_class (varchar(20))
- issue_date (date)
- expiry_date (date)
- document_url (varchar(500))
- status (varchar(20))
- created_at (timestamp)

## Resolution Status (Updated: 2026-07-06)

### 1. Local Database Reset & Synchronization (SUCCESS)
The Local Docker database has been successfully reset and updated:
- **Action**: Cleaned local volume and executed `dotnet ef database update`.
- **Applied Migrations**: All **40 codebase migrations** have been successfully applied in chronological order.
- **Verification**: Verified that key tables and columns are correctly created on `localhost:5432`.

### 2. Specific Columns & Tables Confirmed Locally
- **`IotDevice.IsOnline`**: Confirmed to exist as a `boolean NOT NULL` column named `IsOnline` in the `iot_devices` table.
- **Vehicle & Maintenance-related Tables**: Confirmed existence of `vehicles`, `vehicle_documents`, and `maintenance_tickets` tables. Note that `maintenance_logs` and `fleet_maintenance_schedules` are not defined in the C# codebase and therefore do not exist (this matches entity mappings).

### 3. Schema Synchronization (Local vs. Azure) (SUCCESS)
The Local DB has been fully aligned with the Deployed Azure DB schema by applying the following SQL updates on `localhost:5432` to resolve column and timezone mismatches without losing local data:
```sql
-- 1. URL fields to varchar(500)
ALTER TABLE customer_contracts ALTER COLUMN file_url TYPE varchar(500);
ALTER TABLE delivery_epods ALTER COLUMN pdf_url TYPE varchar(500);
ALTER TABLE invoices ALTER COLUMN pdf_url TYPE varchar(500);
ALTER TABLE quotations ALTER COLUMN file_url TYPE varchar(500);
ALTER TABLE warehouse_receipts ALTER COLUMN pdf_url TYPE varchar(500);

-- 2. outbound_orders.receiver_phone to varchar(100)
ALTER TABLE outbound_orders ALTER COLUMN receiver_phone TYPE varchar(100);

-- 3. users.refresh_token to varchar(255)
ALTER TABLE users ALTER COLUMN refresh_token TYPE varchar(255);

-- 4. users.refresh_token_expiry_time and users.updated_at to timestamp without time zone (timestamptz -> timestamp)
ALTER TABLE users ALTER COLUMN refresh_token_expiry_time TYPE timestamp without time zone USING refresh_token_expiry_time AT TIME ZONE 'UTC';
ALTER TABLE users ALTER COLUMN updated_at TYPE timestamp without time zone USING updated_at AT TIME ZONE 'UTC';

-- 5. Add legacy/missing columns if they don't exist
ALTER TABLE users ADD COLUMN IF NOT EXISTS "WarehouseId" uuid;
ALTER TABLE lpns ADD COLUMN IF NOT EXISTS temperature numeric;
```

### 4. Local Connection Reverted
The application's default database connection in `appsettings.json` has been pointed back to the local database to use the local Docker container for development:
```json
"LocalConnection": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=ColdChainX@2026;Include Error Detail=true"
```
It is now safe to proceed with local development and testing using the local database.


