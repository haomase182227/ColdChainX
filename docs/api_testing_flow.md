# ColdChainX API Testing Flow & Progress Note

This document tracks the steps and sample payloads required to verify the core backend workflows (Inbound, Inventory Hold, Outbound Shipping) using Swagger UI. It acts as a status sheet so you can track where the verification stands and what endpoints have been tested.

---

## 📋 General Setup

Before running tests, ensure the local environment is initialized:
1. **Start Docker Desktop**: Docker must be running to host the PostgreSQL container.
2. **Start PostgreSQL Database**: Run `docker compose up -d coldchainx-db`.
3. **Start API Backend**: Run `dotnet run` from the `ColdChainX.API` project directory.
4. **Database Bootstrapping**: At startup, the API automatically runs database migrations and seeds roles, default users, a customer, a warehouse, and a location.
5. **Access Swagger**: Open `http://localhost:5244/swagger` (or the local ASP.NET Core port indicated in console output).
6. **Authentication**: Use `POST /api/auth/login` to obtain a JWT token, and click the **Authorize** button in Swagger to enter the bearer token (`Bearer <token>`).

---

## 🔄 Flow 1: Inbound & Inventory Setup

This flow establishes warehouses, locations, and brings items into stock.

### Step 1.1: Verify Seeded Customer & Location
- **Endpoint**: `GET /api/customers`
  - *Verify*: Check if the default seeded customer exists. Copy their `customerId`.
- **Endpoint**: `GET /api/v1/warehouses`
  - *Verify*: Retrieve the default warehouse. Copy its `warehouseId`.
- **Endpoint**: `GET /api/v1/warehouses/{warehouseId}/zones`
  - *Verify*: Get the default storage zone. Copy its `zoneId`.
- **Endpoint**: `GET /api/v1/warehouses/locations`
  - *Verify*: Find a location in the zone. Copy its `locationId` (e.g., matching code `LOC-01`).

### Step 1.2: Create Inbound ASN (Advanced Shipping Notice)
- **Endpoint**: `POST /api/v1/asns`
- **Request Body**:
  ```json
  {
    "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "requestedDropoffTime": "2026-06-20T08:00:00Z"
  }
  ```
- *Output*: Copy the created `asnId` and `asnCode`.

### Step 1.3: Receive Goods & Put Away (Warehouse Receipt)
- **Endpoint**: `POST /api/v1/warehouse-receipts`
- **Request Body**:
  ```json
  {
    "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "warehouseId": "<YOUR_WAREHOUSE_ID>",
    "delivererName": "Cold Transport Co.",
    "recordedTemperature": 4.5,
    "note": "Temperature verified within limits.",
    "items": [
      {
        "itemCode": "ITEM-SF-2",
        "itemName": "Salmon Box",
        "unit": "BOX",
        "expectedQty": 100.0,
        "actualQty": 100.0,
        "productCategory": "SEAFOOD",
        "batchNumber": "B-BATCH-002",
        "countryOfOrigin": "Vietnam",
        "note": "Perfect condition"
      }
    ]
  }
  ```
- *Verify*: This receipt will automatically create a `WarehouseReceipt`, generate an `InventoryBatch`, and place `100.0` units in the default location (`AVAILABLE` status).

---

## 🔒 Flow 2: Inventory Hold & Quarantine

Verify placing a portion of the stock under containment.

### Step 2.1: Find the Created Stock ID
- **Endpoint**: `GET /api/v1/inventory/stocks`
  - Filter by `itemCode = ITEM-SF-2`.
  - Copy the target `stockId` from the response.

### Step 2.2: Place Partial Stock on Hold (Quarantine)
- **Endpoint**: `POST /api/v1/inventory-holds`
- **Request Body**:
  ```json
  {
    "stockId": "<YOUR_STOCK_ID>",
    "quantity": 30.0,
    "reasonCode": "TEMP_EXCURSION",
    "notes": "Temp sensor alert log",
    "targetQuarantineLocationId": "<YOUR_QUARANTINE_LOCATION_ID>"
  }
  ```
- *Verify*:
  - The original stock reduces to `70.0` available units.
  - A new quarantined stock of `30.0` units is created at the target location with status `HOLD`.
  - An `InventoryHold` record is created.

---

## 🚢 Flow 3: Outbound Order & Compliance Shipping

Verify picking, validating compliance documentation, and shipping.

### Step 3.1: Create Outbound Order
- **Endpoint**: `POST /api/v1/outbound-orders`
- **Request Body**:
  ```json
  {
    "customerId": "<YOUR_CUSTOMER_ID>",
    "receiverName": "Seafood Distributor",
    "receiverPhone": "0987654321",
    "destinationAddress": "456 Ocean Boulevard",
    "items": [
      {
        "itemCode": "ITEM-SF-2",
        "itemName": "Salmon Box",
        "unit": "BOX",
        "quantity": 40.0
      }
    ]
  }
  ```
- *Output*: Copy the created `outboundOrderId`.

### Step 3.2: Allocate Stock
- **Endpoint**: `POST /api/v1/outbound-orders/{id}/allocate`
- *Verify*: The engine allocates `40.0` units from the available `70.0` units in stock.

### Step 3.3: Upload Verified Compliance Attachments
Outbound shipment of `SEAFOOD` requires verified evidence documentation:
1. `WAREHOUSE_ISSUE_NOTE`
2. `GOODS_CONDITION_PHOTO`
3. `TEMPERATURE_PHOTO`

- **Endpoint**: `POST /api/v1/attachments/upload` (Form-data containing the file and metadata).
- Repeat for each required category.
- **Endpoint**: `POST /api/v1/attachments/{id}/verify` (Set status to `VERIFIED` by an administrator).

### Step 3.4: Complete Pick and Execute Shipment
- **Endpoint**: `POST /api/v1/outbound-orders/{id}/ship`
- *Verify*: Checks compliance documentation. If verified documents are present, updates status to `SHIPPED` and decrements physical inventory stock.

---

## 📦 Flow 4: Partial Delivery with Mandatory Evidence Images

This section guides you through testing the LPN-level partial delivery features using seeded DB or custom data.

### Setup and Authentication

1. **Get Driver JWT Token**:
   ```http
   POST /api/Auth/login
   Content-Type: application/json

   {
     "username": "driver01",
     "password": "Password123!"
   }
   ```
   Save the returned `token`. In your curl requests, replace `PASTE_DRIVER_JWT_TOKEN` with this value.

2. **Verify Port**: The Swagger UI runs at `http://localhost:5244/swagger`. Update the port in the endpoints if your environment uses a different one.

---

### Happy Case Testing

#### Step 4.1: View Trip Delivery Progress
Retrieve all LPNs for the trip to see their initial states.
- **Endpoint**: `GET /api/Delivery/trips/{tripId}/lpns`
- **Headers**:
  ```
  Authorization: Bearer PASTE_DRIVER_JWT_TOKEN
  ```
- **PowerShell / Bash Curl**:
  ```bash
  curl -X GET "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN"
  ```
- **Expected Response (200 OK)**:
  ```json
  {
    "success": true,
    "data": {
      "tripId": "TRIP_ID_HERE",
      "totalLpns": 2,
      "deliveredCount": 0,
      "rejectedCount": 0,
      "pendingCount": 2,
      "isComplete": false,
      "lpnStatuses": [
        {
          "lpnId": "LPN_ID_1_HERE",
          "lpnCode": "LPN-001",
          "state": "SHIPPING",
          "outcomeType": null,
          "confirmedAt": null
        },
        {
          "lpnId": "LPN_ID_2_HERE",
          "lpnCode": "LPN-002",
          "state": "SHIPPING",
          "outcomeType": null,
          "confirmedAt": null
        }
      ]
    }
  }
  ```

#### Step 4.2: Confirm LPN Delivery (Accept)
Confirm that LPN 1 is successfully accepted.
- **Endpoint**: `POST /api/Delivery/trips/{tripId}/lpns/{lpnId}/confirm`
- **Request Type**: `multipart/form-data`
- **Headers**:
  ```
  Authorization: Bearer PASTE_DRIVER_JWT_TOKEN
  ```
- **PowerShell / Bash Curl**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_1_HERE/confirm" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "ReceiverName=Nguyen Van A" \
       -F "ReceiverPhone=0901234567" \
       -F "EvidenceImage=@path_to_test_image.jpg;type=image/jpeg"
  ```
- **Expected Response (200 OK)**:
  ```json
  {
    "success": true,
    "data": {
      "confirmationId": "CONFIRMATION_ID_HERE",
      "lpnId": "LPN_ID_1_HERE",
      "lpnCode": "LPN-001",
      "outcomeType": "DELIVERED",
      "receiverName": "Nguyen Van A",
      "receiverPhone": "0901234567",
      "evidenceImageUrl": "https://res.cloudinary.com/...",
      "confirmedAt": "2026-06-26T14:30:00Z"
    }
  }
  ```
- **Database Verification**:
  ```sql
  -- LPN state should transition to DELIVERED (11)
  SELECT state, evidence_image_url FROM lpns WHERE lpn_id = 'LPN_ID_1_HERE';
  -- Result: state = 11, evidence_image_url = 'https://res.cloudinary.com/...'
  ```

#### Step 4.3: Reject LPN Delivery
Reject LPN 2 due to damage.
- **Endpoint**: `POST /api/Delivery/trips/{tripId}/lpns/{lpnId}/reject`
- **Request Type**: `multipart/form-data`
- **Headers**:
  ```
  Authorization: Bearer PASTE_DRIVER_JWT_TOKEN
  ```
- **PowerShell / Bash Curl**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_2_HERE/reject" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "RejectReason=DAMAGED" \
       -F "RejectNote=Box crushed during transit" \
       -F "EvidenceImage=@path_to_test_image.jpg;type=image/jpeg"
  ```
- **Expected Response (200 OK)**:
  ```json
  {
    "success": true,
    "data": {
      "confirmationId": "CONFIRMATION_ID_HERE",
      "lpnId": "LPN_ID_2_HERE",
      "lpnCode": "LPN-002",
      "outcomeType": "REJECTED",
      "rejectReason": "DAMAGED",
      "rejectNote": "Box crushed during transit",
      "evidenceImageUrl": "https://res.cloudinary.com/...",
      "confirmedAt": "2026-06-26T14:31:00Z"
    }
  }
  ```
- **Database Verification**:
  ```sql
  -- LPN state should transition to DELIVERY_RETURNED (12)
  SELECT state, evidence_image_url FROM lpns WHERE lpn_id = 'LPN_ID_2_HERE';
  -- Result: state = 12, evidence_image_url = 'https://res.cloudinary.com/...'
  ```

#### Step 4.4: View Single LPN Delivery Detail
Retrieve the confirmation details for a specific processed LPN.
- **Endpoint**: `GET /api/Delivery/trips/{tripId}/lpns/{lpnId}`
- **PowerShell / Bash Curl**:
  ```bash
  curl -X GET "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_1_HERE" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN"
  ```
- **Expected Response (200 OK)**:
  ```json
  {
    "success": true,
    "data": {
      "lpnId": "LPN_ID_1_HERE",
      "lpnCode": "LPN-001",
      "outcomeType": "DELIVERED",
      "receiverName": "Nguyen Van A",
      "receiverPhone": "0901234567",
      "rejectReason": null,
      "rejectNote": null,
      "evidenceImageUrl": "https://res.cloudinary.com/...",
      "confirmedAt": "2026-06-26T14:30:00Z"
    }
  }
  ```

#### Step 4.5: Verify Trip Auto-Completion & Order Status Gating
Once both LPNs in the trip are processed, verify the trip status and order status.
- **Trip Status Verification**:
  - Run the progress check again:
    ```bash
    curl -X GET "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns" \
         -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN"
    ```
  - Expect: `"isComplete": true`, `"pendingCount": 0`, `"deliveredCount": 1`, `"rejectedCount": 1`.
  - In Database:
    ```sql
    SELECT status, completed_at FROM master_trips WHERE trip_id = 'TRIP_ID_HERE';
    -- Expect: status = 'COMPLETED', completed_at is populated
    ```
- **Order Status Verification (Gating Logic)**:
  - If any of the delivered LPNs has a `CodAmount > 0` and has not been verified yet (`IsCodVerified = false`), the parent order status **remains as SHIPPING** in the database:
    ```sql
    SELECT status FROM transport_orders WHERE order_id = 'ORDER_ID_HERE';
    -- Expect: status = 'SHIPPING' (even though all LPNs are processed, because COD verification is pending)
    ```

#### Step 4.6: COD Payment Verification by Backoffice (Accountant/Manager)
Once the driver has uploaded COD details (such as the bank receipt image or cash notes), the backoffice accountant or manager must verify it to finalize the order status.
- **Get Admin/Manager Token**:
  Login using an administrator or manager account to retrieve the JWT token.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_1_HERE/verify-cod" \
       -H "Authorization: Bearer PASTE_ADMIN_JWT_TOKEN"
  ```
- **Expected Response (200 OK)**:
  ```json
  {
    "success": true,
    "data": {
      "lpnId": "LPN_ID_1_HERE",
      "lpnCode": "LPN-001",
      "state": "DELIVERED",
      "outcomeType": "DELIVERED",
      "isCodVerified": true,
      "codVerifiedAt": "2026-06-27T10:00:00Z"
    }
  }
  ```
- **Order Status Sync**:
  After verification, check the order status in the database:
  ```sql
  SELECT status FROM transport_orders WHERE order_id = 'ORDER_ID_HERE';
  -- Expect: status = 'PARTIALLY_DELIVERED' (since COD is now verified, order status finishes syncing)
  ```

---

### Bad Case Testing (Error Scenarios)

#### Bad Case 1: LPN not in SHIPPING state
Attempt to confirm an LPN that is already `DELIVERED` or still `IN_STOCK`.
- **Command**: Run Step 4.2 confirm request on LPN 1 again.
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "LPN 'LPN-001' is not eligible for delivery confirmation. Current state: DELIVERED. Only SHIPPING LPNs can be confirmed."
  }
  ```

#### Bad Case 2: LPN does not belong to trip
Attempt to confirm an LPN using a trip ID it does not belong to.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/WRONG_TRIP_ID/lpns/LPN_ID_1_HERE/confirm" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "ReceiverName=Test" \
       -F "EvidenceImage=@path_to_test_image.jpg;type=image/jpeg"
  ```
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "LPN 'LPN-001' does not belong to trip 'WRONG_TRIP_ID'."
  }
  ```

#### Bad Case 3: Double confirmation (Conflict)
Attempt to confirm the same LPN twice when it is no longer in shipping.
- **Command**: Run Step 4.2 again on LPN 1.
- **Expected Response (409 Conflict)**:
  ```json
  {
    "success": false,
    "error": "LPN 'LPN-001' has already been confirmed as DELIVERED at 2026-06-26T14:30:00Z. Cannot confirm again."
  }
  ```

#### Bad Case 4: Missing Evidence Image
Attempt to confirm without attaching the mandatory evidence photo.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_1_HERE/confirm" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "ReceiverName=Nguyen Van A"
       # EvidenceImage is omitted
  ```
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "Evidence image is required. Please attach a photo of the delivery."
  }
  ```

#### Bad Case 5: Evidence Image file size too large
Attach a file exceeding 10MB limit.
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "Image file size (12.50MB) exceeds the 10MB limit. Please compress the image and try again."
  }
  ```

#### Bad Case 6: Invalid Image file format
Attach a PDF or text file instead of an image.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_1_HERE/confirm" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "ReceiverName=Nguyen Van A" \
       -F "EvidenceImage=@document.pdf;type=application/pdf"
  ```
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "Invalid file type 'application/pdf'. Only image files are accepted (jpg, jpeg, png, webp)."
  }
  ```

#### Bad Case 7: Confirm with missing Receiver Name
Attempt to confirm a delivery without specifying who received it.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_1_HERE/confirm" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "EvidenceImage=@path_to_test_image.jpg;type=image/jpeg"
       # ReceiverName is omitted
  ```
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "Receiver name is required."
  }
  ```

#### Bad Case 8: Reject with missing Reject Reason
Attempt to reject a delivery without providing a reason.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_2_HERE/reject" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "EvidenceImage=@path_to_test_image.jpg;type=image/jpeg"
       # RejectReason is omitted
  ```
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "Reject reason is required."
  }
  ```

#### Bad Case 9: Reject with reason OTHER but missing Reject Note
Selecting "OTHER" reason requires a describing note.
- **Command**:
  ```bash
  curl -X POST "http://localhost:5244/api/Delivery/trips/TRIP_ID_HERE/lpns/LPN_ID_2_HERE/reject" \
       -H "Authorization: Bearer PASTE_DRIVER_JWT_TOKEN" \
       -F "RejectReason=OTHER" \
       -F "EvidenceImage=@path_to_test_image.jpg;type=image/jpeg"
       # RejectNote is omitted
  ```
- **Expected Response (400 Bad Request)**:
  ```json
  {
    "success": false,
    "error": "A rejection note is required when reject reason is 'OTHER'. Please describe the issue."
  }
  ```

#### Bad Case 10: Non-driver user calls endpoints
Attempt to confirm a delivery using an Administrator or Customer token.
- **Expected Response (403 Forbidden)**: (Handled automatically by the API middleware auth layer returning HTTP 403 Forbidden).

#### Bad Case 11: Driver not assigned to the trip calls endpoints
A driver who is logged in but trying to confirm/reject LPNs belonging to a trip they are not assigned to.
- **Expected Response (403 Forbidden)**:
  ```json
  {
    "success": false,
    "error": "You are not authorized to confirm deliveries for this trip."
  }
  ```

#### Bad Case 12: Trip or LPN does not exist
Using a fake or non-existent GUID for trip ID or LPN ID.
- **Expected Response (404 Not Found)**:
  ```json
  {
    "success": false,
    "error": "Trip with ID '00000000-0000-0000-0000-000000000000' was not found."
  }
  ```

---

## 📈 Execution & Progress Tracking

| Step ID | Description | Tested (Y/N) | Status / Notes |
| :--- | :--- | :---: | :--- |
| **0.1** | Docker running and PostgreSQL started | **Y** | Active local Docker PG database. |
| **0.2** | API local compile & database migration | **Y** | Compiled and migrated successfully. |
| **1.1** | Verify default seeded data | **Y** | Confirmed admin01 and demo orders exist. |
| **1.2** | Create Inbound ASN | **Y** | Verified through order status QC and receipt generation. |
| **1.3** | Process Inbound Receipt & Stock | **Y** | QC recorded, package measurements updated, PDF generated successfully. |
| **2.2** | Place Stock on Hold & Release | **Y** | Created quarantine hold (status HOLD), verified and successfully released (status RELEASED). |
| **3.1** | Create Outbound Order | **Y** | Created draft order for 40 units successfully. |
| **3.2** | Allocate Outbound Stock | **Y** | Allocated FEFO stock, assigned picker, and completed picking successfully. |
| **3.3** | Verify Compliance Evidence | **Y** | Blocked shipment without documents; uploaded and verified WAREHOUSE_ISSUE_NOTE, GOODS_CONDITION_PHOTO, and TEMPERATURE_PHOTO successfully. |
| **3.4** | Execute Outbound Shipment | **Y** | Completed shipment successfully and verified that stock decremented from 100.00 to 60.00. |
| **4.1** | View Trip Delivery Progress | **Y** | Retrieve LPN status summary and progress of a trip. |
| **4.2** | Confirm LPN Delivery (Accept) | **Y** | Confirm LPN delivery with receiver info and mandatory image; double-confirm blocked. |
| **4.3** | Reject LPN Delivery | **Y** | Reject LPN delivery with reason and mandatory image; OTHER reason requires note. |
| **4.4** | View Single LPN Delivery Details | **Y** | Retrieve single confirmation detail by LPN ID. |
| **4.5** | Verify Trip Auto-Completion & Order Gating | **Y** | Trip status auto-completes; Order status remains SHIPPING if unverified COD is pending. |
| **4.6** | COD Payment Verification | **Y** | Accountant/Manager verifies COD payments to unlock parent order status updates. |
