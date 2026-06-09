# Implementation Summary: Customer and Driver Registration APIs

## Overview
Implemented separate APIs for creating Customer and Driver accounts with role-specific information and automatic role assignment.

## Changes Made

### 1. New DTOs Created

#### `CreateCustomerRequest.cs`
```csharp
- Username (optional)
- FullName (required)
- Email (required)
- Password (required)
- CompanyName (required) - Customer specific
- TaxCode (required) - Customer specific
- Address (optional) - Customer specific
- PaymentTerm (optional) - Customer specific
```

#### `CreateDriverRequest.cs`
```csharp
- Username (optional)
- FullName (required)
- Email (required)
- Password (required)
- DateOfBirth (required) - Driver specific
- LicenseNumber (optional) - Driver License
- LicenseClass (optional) - Driver License
- IssueDate (optional) - Driver License
- ExpiryDate (optional) - Driver License
- DocumentUrl (optional) - Driver License
```

### 2. New Service Methods

#### `AuthService.CreateCustomerAsync()`
- Automatically assigns role "Customer" (which maps to role ID 2 in database)
- Creates User record with Customer role
- Creates Customer entity with all customer-specific fields:
  - CompanyName
  - TaxCode
  - Address
  - Email
  - PaymentTerm (defaults to 30 if not provided)
  - Status set to "ACTIVE"
- Returns authentication tokens

#### `AuthService.CreateDriverAsync()`
- Automatically assigns role "Driver" (which maps to role ID 3 in database)
- Creates User record with Driver role
- Creates Driver entity with driver-specific fields:
  - DateOfBirth
  - Status set to "ACTIVE"
- Optionally creates DriverLicense entity if license information is provided
- Returns authentication tokens

### 3. Updated Interfaces

#### `IAuthService`
Added two new methods:
- `Task<ApiResponse<AuthResponseDto>> CreateCustomerAsync(CreateCustomerRequest request)`
- `Task<ApiResponse<AuthResponseDto>> CreateDriverAsync(CreateDriverRequest request)`

#### `IUserRepository`
Added:
- `Task<Role?> GetRoleByIdAsync(Guid roleId)`

#### `IDriverRepository`
Added:
- `Task AddLicenseAsync(DriverLicense license)`

### 4. Controller Updates

#### `AuthController.cs`
Updated endpoints to use new DTOs and service methods:

```csharp
[Authorize(Roles = "Admin,ADMIN")]
[HttpPost("create-customer")]
public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerRequest request)

[Authorize(Roles = "Admin,ADMIN")]
[HttpPost("create-driver")]
public async Task<IActionResult> CreateDriver([FromBody] CreateDriverRequest request)
```

### 5. Validators Created

#### `CreateCustomerRequestValidator.cs`
- Validates all customer-specific fields
- Ensures company name and tax code are required
- Validates email format
- Validates password minimum length (6 characters)
- Validates optional fields when provided

#### `CreateDriverRequestValidator.cs`
- Validates all driver-specific fields
- Ensures date of birth is in the past
- Validates email format
- Validates password minimum length (6 characters)
- Validates license information as a group (if one field provided, all required fields must be provided)
- Ensures expiry date is after issue date

### 6. Repository Implementations

#### `UserRepository`
- Implemented `GetRoleByIdAsync()` method

#### `DriverRepository`
- Implemented `AddLicenseAsync()` method to add driver license records

## API Endpoints

### POST /api/auth/create-customer
**Authorization:** Admin role required  
**Request Body:**
```json
{
  "username": "string (optional)",
  "fullName": "string (required)",
  "email": "string (required)",
  "password": "string (required)",
  "companyName": "string (required)",
  "taxCode": "string (required)",
  "address": "string (optional)",
  "paymentTerm": 30 (optional, integer)
}
```

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
    "accessToken": "string",
    "refreshToken": "string",
    "accessTokenExpiresAt": "datetime"
  }
}
```

### POST /api/auth/create-driver
**Authorization:** Admin role required  
**Request Body:**
```json
{
  "username": "string (optional)",
  "fullName": "string (required)",
  "email": "string (required)",
  "password": "string (required)",
  "dateOfBirth": "2000-01-01 (required)",
  "licenseNumber": "string (optional)",
  "licenseClass": "string (optional)",
  "issueDate": "2020-01-01 (optional)",
  "expiryDate": "2030-01-01 (optional)",
  "documentUrl": "string (optional)"
}
```

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
    "accessToken": "string",
    "refreshToken": "string",
    "accessTokenExpiresAt": "datetime"
  }
}
```

## Role Assignment

- **Customer accounts:** Automatically assigned role "Customer" which corresponds to role ID 2 in the database
- **Driver accounts:** Automatically assigned role "Driver" which corresponds to role ID 3 in the database

The role assignment is done by fetching the role by name from the database, ensuring the correct role ID is used.

## Security

- Both endpoints require Admin authorization
- Passwords are hashed using ASP.NET Core Identity's password hasher
- Email uniqueness is validated
- Username uniqueness is validated
- JWT tokens are generated for authentication

## Database Entities

### Customer Table
- CustomerId (Guid, PK)
- CompanyName (string, required)
- TaxCode (string, required)
- Address (string, optional)
- Email (string, optional)
- PaymentTerm (int, optional)
- Status (string)
- CreatedAt (DateTime)

### Driver Table
- DriverId (Guid, PK)
- DateOfBirth (DateOnly, required)
- Status (string)
- CreatedAt (DateTime)

### DriverLicense Table
- LicenseId (Guid, PK)
- DriverId (Guid, FK)
- LicenseNumber (string, required)
- LicenseClass (string, required)
- IssueDate (DateOnly, required)
- ExpiryDate (DateOnly, required)
- DocumentUrl (string, required)
- Status (string)
- CreatedAt (DateTime)

## Notes

1. The application is currently running, so the build shows file locking warnings. Stop the application and rebuild to complete the deployment.

2. Customer registration now requires full customer information (CompanyName, TaxCode) instead of generating temporary values.

3. Driver license information is optional during driver creation but if any license field is provided, all required license fields must be filled.

4. Both endpoints return authentication tokens, allowing immediate login after account creation.

5. The role names "Customer" and "Driver" are used to fetch the appropriate role from the database, ensuring the correct role IDs (2 and 3 respectively) are assigned.
