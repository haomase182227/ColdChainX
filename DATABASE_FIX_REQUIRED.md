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

## Next Steps

1. **Apply migrations** to create all tables
2. **Seed roles data** if not already present:
```sql
INSERT INTO roles (role_id, role_name, created_at) VALUES
(gen_random_uuid(), 'Admin', CURRENT_TIMESTAMP),
(gen_random_uuid(), 'Customer', CURRENT_TIMESTAMP),
(gen_random_uuid(), 'Driver', CURRENT_TIMESTAMP),
(gen_random_uuid(), 'Manager', CURRENT_TIMESTAMP)
ON CONFLICT DO NOTHING;
```
3. **Test the register endpoint** again
4. **Verify** data is being inserted correctly

## Connection String
Your current connection (from appsettings.json):
```
Host=coldchainx-db-server.postgres.database.azure.com;
Port=5432;
Database=postgres;
Username=postgres;
Password=ColdChainX@2026;
Include Error Detail=true
```

Contact your database administrator if you don't have permissions to run migrations or create tables.
