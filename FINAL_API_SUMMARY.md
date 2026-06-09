# Final API Endpoints Summary

## Overview
3 endpoints để tạo tài khoản với role khác nhau:
- **POST /api/auth/register** - Tạo Admin/Manager (chỉ User table)
- **POST /api/auth/create-customer** - Tạo Customer (User + Customer tables) 
- **POST /api/auth/create-driver** - Tạo Driver (User + Driver + DriverLicense tables)

---

## 1. POST /api/auth/register
**Authorization:** ❌ Public (không cần authorization)  
**Content-Type:** `multipart/form-data`  
**Mục đích:** Tạo tài khoản **Admin** hoặc **Manager**

### Đặc điểm:
- ✅ Chỉ tạo được Admin hoặc Manager
- ✅ CHỈ điền thông tin bảng User
- ✅ KHÔNG tạo bảng Customer hay Driver
- ✅ Chọn role từ dropdown (Admin, Manager)

### Request Fields:
```
User Information:
├── username (optional) - Username, mặc định là email
├── fullName (required) - Họ tên đầy đủ
├── email (required) - Email address
├── password (required) - Mật khẩu (min 6 ký tự)
├── phone (optional) - Số điện thoại
└── role (required) - Chọn: Admin hoặc Manager
```

### Example Request:
```bash
curl -X 'POST' \
  'https://localhost:7010/api/auth/register' \
  -H 'Content-Type: multipart/form-data' \
  -F 'username=admin01' \
  -F 'fullName=Super Admin' \
  -F 'email=admin@coldchainx.com' \
  -F 'password=Admin@123' \
  -F 'phone=0123456789' \
  -F 'role=Admin'
```

### Response:
```json
{
  "success": true,
  "message": "Admin account created successfully",
  "data": {
    "userId": "guid",
    "username": "admin01",
    "email": "admin@coldchainx.com",
    "fullName": "Super Admin",
    "role": "Admin",
    "accessToken": "jwt_token",
    "refreshToken": "refresh_token",
    "accessTokenExpiresAt": "2026-06-09T08:00:00Z"
  }
}
```

### Database Operations:
```
✅ INSERT INTO users (user_id, username, full_name, email, phone, password_hash, role_id, status, created_at)
❌ NO Customer table insert
❌ NO Driver table insert
```

---

## 2. POST /api/auth/create-customer
**Authorization:** 🔒 Required - Admin role only  
**Content-Type:** `multipart/form-data`  
**Mục đích:** Admin tạo tài khoản Customer với đầy đủ thông tin

### Đặc điểm:
- 🔒 Cần Admin JWT token
- ✅ **Tự động gán role_id = 2 (Customer)** - không thể thay đổi
- ✅ Điền ĐẦY ĐỦ thông tin User
- ✅ Điền ĐẦY ĐỦ thông tin Customer
- ✅ Tạo 2 records: User + Customer

### Request Fields:
```
User Information:
├── username (optional) - Username, mặc định là email
├── fullName (required) - Họ tên đầy đủ
├── email (required) - Email address
├── password (required) - Mật khẩu (min 6 ký tự)
└── phone (optional) - Số điện thoại

Customer Information:
├── companyName (required) - Tên công ty
├── taxCode (required) - Mã số thuế
├── address (optional) - Địa chỉ công ty
└── paymentTerm (optional) - Thời hạn thanh toán (ngày, mặc định 30)
```

### Example Request:
```bash
curl -X 'POST' \
  'https://localhost:7010/api/auth/create-customer' \
  -H 'Authorization: Bearer YOUR_ADMIN_JWT_TOKEN' \
  -H 'Content-Type: multipart/form-data' \
  -F 'username=customer01' \
  -F 'fullName=Nguyen Van A' \
  -F 'email=customer@company.com' \
  -F 'password=Customer@123' \
  -F 'phone=0987654321' \
  -F 'companyName=ABC Company Ltd' \
  -F 'taxCode=0123456789' \
  -F 'address=123 Street, City' \
  -F 'paymentTerm=45'
```

### Response:
```json
{
  "success": true,
  "message": "Customer account created successfully",
  "data": {
    "userId": "guid",
    "username": "customer01",
    "email": "customer@company.com",
    "fullName": "Nguyen Van A",
    "role": "Customer",
    "customerId": "guid",
    "accessToken": "jwt_token",
    "refreshToken": "refresh_token",
    "accessTokenExpiresAt": "2026-06-09T08:00:00Z"
  }
}
```

### Database Operations:
```
✅ INSERT INTO users (user_id, username, full_name, email, phone, password_hash, role_id=2, status, created_at)
✅ INSERT INTO customers (customer_id, company_name, tax_code, address, email, payment_term, status, created_at)
```

---

## 3. POST /api/auth/create-driver
**Authorization:** 🔒 Required - Admin role only  
**Content-Type:** `multipart/form-data`  
**Mục đích:** Admin tạo tài khoản Driver với đầy đủ thông tin

### Đặc điểm:
- 🔒 Cần Admin JWT token
- ✅ **Tự động gán role_id = 3 (Driver)** - không thể thay đổi
- ✅ Điền ĐẦY ĐỦ thông tin User
- ✅ Điền ĐẦY ĐỦ thông tin Driver
- ✅ Optional: Thông tin DriverLicense
- ✅ Tạo 2-3 records: User + Driver + (optional) DriverLicense

### Request Fields:
```
User Information:
├── username (optional) - Username, mặc định là email
├── fullName (required) - Họ tên đầy đủ
├── email (required) - Email address
├── password (required) - Mật khẩu (min 6 ký tự)
└── phone (optional) - Số điện thoại

Driver Information:
└── dateOfBirth (required) - Ngày sinh (format: YYYY-MM-DD)

Driver License Information (Optional - nếu có 1 field thì phải có tất cả):
├── licenseNumber (optional) - Số bằng lái
├── licenseClass (optional) - Hạng bằng (B, C, CE, etc.)
├── issueDate (optional) - Ngày cấp (format: YYYY-MM-DD)
├── expiryDate (optional) - Ngày hết hạn (format: YYYY-MM-DD)
└── documentUrl (optional) - URL file scan bằng lái
```

### Example Request (with License):
```bash
curl -X 'POST' \
  'https://localhost:7010/api/auth/create-driver' \
  -H 'Authorization: Bearer YOUR_ADMIN_JWT_TOKEN' \
  -H 'Content-Type: multipart/form-data' \
  -F 'username=driver01' \
  -F 'fullName=Tran Van B' \
  -F 'email=driver@coldchainx.com' \
  -F 'password=Driver@123' \
  -F 'phone=0912345678' \
  -F 'dateOfBirth=1990-05-15' \
  -F 'licenseNumber=B1-123456789' \
  -F 'licenseClass=CE' \
  -F 'issueDate=2020-01-01' \
  -F 'expiryDate=2030-01-01' \
  -F 'documentUrl=https://storage.com/license.pdf'
```

### Response:
```json
{
  "success": true,
  "message": "Driver account created successfully",
  "data": {
    "userId": "guid",
    "username": "driver01",
    "email": "driver@coldchainx.com",
    "fullName": "Tran Van B",
    "role": "Driver",
    "accessToken": "jwt_token",
    "refreshToken": "refresh_token",
    "accessTokenExpiresAt": "2026-06-09T08:00:00Z"
  }
}
```

### Database Operations:
```
✅ INSERT INTO users (user_id, username, full_name, email, phone, password_hash, role_id=3, status, created_at)
✅ INSERT INTO drivers (driver_id, date_of_birth, status, created_at)
✅ INSERT INTO driver_licenses (license_id, driver_id, license_number, license_class, issue_date, expiry_date, document_url, status, created_at)
   └─ (chỉ khi có thông tin license)
```

---

## Comparison Table

| Feature | register | create-customer | create-driver |
|---------|----------|-----------------|---------------|
| **Authorization** | ❌ Public | 🔒 Admin only | 🔒 Admin only |
| **Allowed Roles** | Admin, Manager | Customer (fixed) | Driver (fixed) |
| **Role Selection** | ✅ Dropdown | ❌ Auto = 2 | ❌ Auto = 3 |
| **User Fields** | Full (with phone) | Full (with phone) | Full (with phone) |
| **Additional Tables** | None | Customer | Driver + License |
| **Use Case** | Tạo Admin/Manager | Admin tạo khách hàng | Admin tạo tài xế |

---

## Role IDs in Database

| ID | Role Name | Created By | Description |
|----|-----------|------------|-------------|
| 1  | Admin     | `/register` | System administrator |
| 2  | Customer  | `/create-customer` | Customer accounts (auto-assigned) |
| 3  | Driver    | `/create-driver` | Driver accounts (auto-assigned) |
| 4  | Manager   | `/register` | Manager accounts |

---

## Swagger UI Display

### `/api/auth/register`
```
❌ No lock icon (public endpoint)
📋 Form fields:
   - username
   - fullName
   - email
   - password
   - phone
   - role [Dropdown: Admin, Manager]
```

### `/api/auth/create-customer`
```
🔒 Lock icon (requires Admin JWT)
📋 Form fields:
   User Info:
   - username
   - fullName
   - email
   - password
   - phone
   
   Customer Info:
   - companyName
   - taxCode
   - address
   - paymentTerm
```

### `/api/auth/create-driver`
```
🔒 Lock icon (requires Admin JWT)
📋 Form fields:
   User Info:
   - username
   - fullName
   - email
   - password
   - phone
   
   Driver Info:
   - dateOfBirth
   
   License Info (optional):
   - licenseNumber
   - licenseClass
   - issueDate
   - expiryDate
   - documentUrl
```

---

## Testing Flow

### 1. Create First Admin (No auth needed)
```bash
POST /api/auth/register
Body: {
  fullName: "Super Admin",
  email: "admin@coldchainx.com",
  password: "Admin@123",
  role: "Admin"
}
```

### 2. Login as Admin
```bash
POST /api/auth/login
Body: {
  email: "admin@coldchainx.com",
  password: "Admin@123"
}
Response: { accessToken: "..." }
```

### 3. Create Customer (With Admin token)
```bash
POST /api/auth/create-customer
Headers: Authorization: Bearer {admin_token}
Body: { ...customer details... }
```

### 4. Create Driver (With Admin token)
```bash
POST /api/auth/create-driver
Headers: Authorization: Bearer {admin_token}
Body: { ...driver details... }
```

---

## Important Notes

1. **Database Must Exist**: Roles table phải có 4 roles (Admin, Customer, Driver, Manager)
2. **Phone Field**: Đã thêm vào User entity và database mapping
3. **Role Assignment**: 
   - `/register`: User chọn (chỉ Admin/Manager)
   - `/create-customer`: Tự động = 2 (Customer)
   - `/create-driver`: Tự động = 3 (Driver)
4. **Validation**: FluentValidation kiểm tra tất cả fields
5. **multipart/form-data**: Tất cả 3 endpoints đều dùng form-data

---

## Error Handling

### Common Errors:
```json
{
  "success": false,
  "message": "Email already in use"
}

{
  "success": false,
  "message": "Username already in use"
}

{
  "success": false,
  "message": "Only Admin or Manager roles are allowed"
}

{
  "success": false,
  "message": "Customer role not found in the system"
}
```

### Database Error (if tables don't exist):
```json
{
  "success": false,
  "message": "42P01: relation \"users\" does not exist"
}
```
**Fix:** Run migrations (see DATABASE_FIX_REQUIRED.md)
