# API Endpoints Summary

## Authentication Endpoints

### 1. GET /api/auth/roles
**Authorization:** ❌ No authentication required  
**Description:** Get all available roles from database

**Response:**
```json
{
  "success": true,
  "message": "Roles retrieved successfully",
  "data": [
    {
      "roleId": "guid",
      "roleName": "Admin",
      "description": null
    },
    {
      "roleId": "guid",
      "roleName": "Customer",
      "description": null
    },
    {
      "roleId": "guid",
      "roleName": "Driver",
      "description": null
    },
    {
      "roleId": "guid",
      "roleName": "Manager",
      "description": null
    }
  ]
}
```

---

### 2. POST /api/auth/register
**Authorization:** ❌ No authentication required (Public endpoint)  
**Content-Type:** `multipart/form-data`  
**Description:** Public registration - can create any role (Admin, Customer, Driver, Manager)

**Request Fields:**
- `username` (optional) - Defaults to email if not provided
- `fullName` (required) - Full name
- `email` (required) - Email address
- `password` (required) - Password (min 6 characters)
- `role` (required) - Role name (Admin, Customer, Driver, Manager) - **Dropdown with values from database**
- `companyName` (optional) - For Customer role
- `dateOfBirth` (optional) - For Driver role (format: YYYY-MM-DD)

**Response:**
```json
{
  "success": true,
  "message": "Registration successful",
  "data": {
    "userId": "guid",
    "username": "string",
    "email": "string",
    "fullName": "string",
    "role": "Admin/Customer/Driver/Manager",
    "customerId": "guid (if Customer)",
    "accessToken": "jwt_token",
    "refreshToken": "refresh_token",
    "accessTokenExpiresAt": "datetime"
  }
}
```

---

### 3. POST /api/auth/create-customer
**Authorization:** 🔒 Required - Admin role only  
**Content-Type:** `multipart/form-data`  
**Description:** Create customer account with full details (Admin only)

**Behavior:**
- Automatically sets **role_id = 2 (Customer)** - hardcoded, not changeable
- Creates User record + Customer record
- All customer information must be provided

**Request Fields:**

**User Information:**
- `username` (optional) - Defaults to email if not provided
- `fullName` (required) - Full name
- `email` (required) - Email address
- `password` (required) - Password (min 6 characters)
- `phone` (optional) - Phone number

**Customer Information:**
- `companyName` (required) - Company name
- `taxCode` (required) - Tax identification code
- `address` (optional) - Company address
- `paymentTerm` (optional) - Payment term in days (defaults to 30)

**Response:**
```json
{
  "success": true,
  "message": "Customer account created successfully",
  "data": {
    "userId": "guid",
    "username": "string",
    "email": "string",
    "fullName": "string",
    "role": "Customer",
    "customerId": "guid",
    "accessToken": "jwt_token",
    "refreshToken": "refresh_token",
    "accessTokenExpiresAt": "datetime"
  }
}
```

---

### 4. POST /api/auth/create-driver
**Authorization:** 🔒 Required - Admin role only  
**Content-Type:** `multipart/form-data`  
**Description:** Create driver account with full details (Admin only)

**Behavior:**
- Automatically sets **role_id = 3 (Driver)** - hardcoded, not changeable
- Creates User record + Driver record + (optional) DriverLicense record
- All driver information must be provided

**Request Fields:**

**User Information:**
- `username` (optional) - Defaults to email if not provided
- `fullName` (required) - Full name
- `email` (required) - Email address
- `password` (required) - Password (min 6 characters)
- `phone` (optional) - Phone number

**Driver Information:**
- `dateOfBirth` (required) - Date of birth (format: YYYY-MM-DD)

**Driver License Information (Optional - if one provided, all required):**
- `licenseNumber` (optional) - License number
- `licenseClass` (optional) - License class (e.g., B, C, CE)
- `issueDate` (optional) - Issue date (format: YYYY-MM-DD)
- `expiryDate` (optional) - Expiry date (format: YYYY-MM-DD)
- `documentUrl` (optional) - Document URL

**Response:**
```json
{
  "success": true,
  "message": "Driver account created successfully",
  "data": {
    "userId": "guid",
    "username": "string",
    "email": "string",
    "fullName": "string",
    "role": "Driver",
    "accessToken": "jwt_token",
    "refreshToken": "refresh_token",
    "accessTokenExpiresAt": "datetime"
  }
}
```

---

## Key Differences

| Feature | register | create-customer | create-driver |
|---------|----------|-----------------|---------------|
| **Authorization** | ❌ Public | 🔒 Admin only | 🔒 Admin only |
| **Role Selection** | ✅ Choose any role | ❌ Fixed: Customer (ID=2) | ❌ Fixed: Driver (ID=3) |
| **User Fields** | Basic | Full (with phone) | Full (with phone) |
| **Additional Data** | Optional | Full Customer data | Full Driver data + License |
| **Use Case** | Self-registration | Admin creates customer | Admin creates driver |

---

## Role IDs in Database

| ID | Role Name | Description |
|----|-----------|-------------|
| 1  | Admin     | System administrator |
| 2  | Customer  | Customer accounts |
| 3  | Driver    | Driver accounts |
| 4  | Manager   | Manager accounts |

---

## Swagger UI Display

### `/api/auth/register`
- ❌ No lock icon (public)
- Dropdown for `role` field with values: Admin, Customer, Driver, Manager
- All fields shown as form data

### `/api/auth/create-customer`
- 🔒 Lock icon (requires Admin JWT)
- Role is hardcoded to Customer (not shown in form)
- Complete User + Customer fields shown

### `/api/auth/create-driver`
- 🔒 Lock icon (requires Admin JWT)
- Role is hardcoded to Driver (not shown in form)
- Complete User + Driver + License fields shown

---

## Testing Flow

### Public Registration (No Auth)
1. Call `GET /api/auth/roles` to see available roles
2. Call `POST /api/auth/register` with chosen role
3. Receive JWT tokens in response

### Admin Creating Customer (With Auth)
1. Login as Admin to get JWT token
2. Click "Authorize" in Swagger and enter token
3. Call `POST /api/auth/create-customer` with full customer details
4. Customer account created with role_id = 2

### Admin Creating Driver (With Auth)
1. Login as Admin to get JWT token
2. Click "Authorize" in Swagger and enter token
3. Call `POST /api/auth/create-driver` with full driver details
4. Driver account created with role_id = 3
