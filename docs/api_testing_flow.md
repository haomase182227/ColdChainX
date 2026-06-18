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

## 📈 Execution & Progress Tracking

| Step ID | Description | Tested (Y/N) | Status / Notes |
| :--- | :--- | :---: | :--- |
| **0.1** | Docker running and PostgreSQL started | **Y** | Active local Docker PG database. |
| **0.2** | API local compile & database migration | **Y** | Compiled and migrated successfully. |
| **1.1** | Verify default seeded data | **Y** | Confirmed admin01 and demo orders exist. |
| **1.2** | Create Inbound ASN | **Y** | Verified through order status QC and receipt generation. |
| **1.3** | Process Inbound Receipt & Stock | **Y** | QC recorded, package measurements updated, PDF generated successfully. |
| **2.2** | Place Stock on Hold & Release | **Y** | Created quarantine hold (status HOLD), verified and successfully released (status RELEASED). |
| **3.1** | Create Outbound Order | **N** | Pending outbound integration testing. |
| **3.2** | Allocate Outbound Stock | **N** | Pending outbound integration testing. |
| **3.3** | Verify Compliance Evidence | **N** | Pending outbound integration testing. |
| **3.4** | Execute Outbound Shipment | **N** | Pending outbound integration testing. |
