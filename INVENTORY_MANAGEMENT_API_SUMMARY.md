# Hướng Dẫn Vận Hành & Danh Sách API Quản Lý Kho (Inventory Management)

Tài liệu này tổng hợp toàn bộ các API liên quan đến hoạt động quản lý, dịch chuyển, khóa giữ, và kiểm kê hàng hóa sau khi đã thực hiện nhập kho thành công trong hệ thống **ColdChainX**.

---

## 1. Sơ Đồ Vòng Đời Hàng Hóa Trong Kho

Hàng hóa sau khi nhập kho sẽ trải qua các trạng thái lưu trữ và kiểm soát nghiêm ngặt theo sơ đồ dưới đây:

```mermaid
flowchart TD
    A[Hàng ở khu đệm RCV-STAGE-01] -->|B1: Gợi ý cất hàng & Di chuyển<br>POST /api/v1/inventory/relocations| B(Kệ kho chính thức: AVAILABLE)
    B -->|B2: Giữ chỗ xuất hàng<br>POST /api/v1/inventory/allocations| C(Trạng thái: ALLOCATED)
    C -->|Xuất kho thành công| D[Ra khỏi kho]
    C -->|Hủy đơn hàng<br>DELETE /api/v1/inventory/allocations| B
    
    B -->|B3: Phát hiện lỗi/QC kém<br>POST /api/v1/inventory-holds| E(Trạng thái: HOLD)
    E -->|QA kiểm tra đạt<br>POST /api/v1/inventory-holds/{id}/release| B
    E -->|QA kiểm tra lỗi/Thanh lý<br>POST /api/v1/inventory-holds/{id}/adjust-out| F[Hủy/Viết giảm tồn kho]
    
    B -->|B4: Lệch kiểm kê định kỳ<br>POST /api/v1/cycle-counts| G{Review Chênh Lệch}
    G -->|Duyệt chênh lệch<br>POST /api/v1/inventory-adjustments/{id}/approve| B
```

---

## 2. Chi Tiết Các Nhóm API Vận Hành Kho

### Nhóm 1: Dịch Chuyển & Gợi Ý Cất Hàng (Relocations & Suggestions)

#### 1. Gọi gợi ý vị trí cất hàng tối ưu (Putaway Suggestions)
Giúp thủ kho tìm kệ chứa hàng phù hợp nhất dựa trên sức chứa và nhiệt độ yêu cầu của sản phẩm.
*   **API Endpoint:** 
    *   Theo từng lô hàng: `GET /api/v1/putaway-suggestions?stockId={stockId}`
    *   Theo cả phiếu nhập: `GET /api/v1/putaway-suggestions/receipt/{receiptId}`
*   **Controller:** [PutawaySuggestionsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/PutawaySuggestionsController.cs)
*   **Điểm tối ưu (Suitability Score) được tính như sau:**
    *   `100 điểm (SAME_BATCH):` Vị trí đã có sẵn hàng cùng sản phẩm và cùng số lô (Ưu tiên gom lô).
    *   `80 điểm (SAME_ITEM):` Vị trí đã có sẵn hàng cùng sản phẩm nhưng khác số lô.
    *   `50 điểm (EMPTY):` Vị trí kệ trống hoàn toàn.
    *   `20 điểm (COMPATIBLE):` Vị trí chứa hàng khác loại nhưng tương thích nhiệt độ.

#### 2. Dịch chuyển vị trí hàng hóa (Put-away / Relocation)
Thực hiện chuyển hàng từ khu nhận hàng đệm lên kệ chính hoặc di chuyển hàng giữa các kệ.
*   **API Endpoint:** `POST /api/v1/inventory/relocations`
*   **Controller:** [InventoryController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryController.cs#L51-L67)
*   **Quyền hạn (Role):** `WarehouseOperator`, `Manager`, `Admin`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "sourceLocationId": "guid-kệ-nguồn",
      "destinationLocationId": "guid-kệ-đích",
      "itemCode": "BEEF-01",
      "batchId": "guid-lô-hàng",
      "quantity": 50,
      "pallets": 1
    }
    ```
*   **Các quy tắc kiểm tra tự động (Rules):**
    *   **Nhiệt độ:** Nhiệt độ yêu cầu của hàng hóa phải nằm trong dải nhiệt độ tối thiểu/tối đa cho phép của Zone đích.
    *   **Sức chứa:** Pallet thêm vào không được vượt quá giới hạn trống của cả Kệ đích (`warehouse_locations.max_capacity_pallets`) và Zone đích (`warehouse_zones.max_capacity_pallets`).

---

### Nhóm 2: Giữ Chỗ & Giải Phóng Để Xuất Kho (Allocations)

#### 1. Giữ chỗ tồn kho theo FEFO (Allocate Stock)
Khóa trước số lượng hàng hóa trong kho để phục vụ cho các đơn xuất hàng (Outbound), ngăn việc xuất trùng.
*   **API Endpoint:** `POST /api/v1/inventory/allocations`
*   **Controller:** [InventoryController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryController.cs#L156-L172)
*   **Quyền hạn (Role):** `Dispatcher`, `Manager`, `Admin`
*   **Quy tắc FEFO tự động:** Hệ thống tự động chọn các lô hàng có hạn sử dụng gần nhất (`ExpiryDate` tăng dần), nếu hạn trùng nhau sẽ ưu tiên lô nhập kho trước (`InboundDate` tăng dần).
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "referenceDocumentId": "guid-đơn-xuất-hoặc-chuyến-đi",
      "items": [
        {
          "itemCode": "BEEF-01",
          "quantity": 10
        }
      ]
    }
    ```

#### 2. Giải phóng hàng đã giữ chỗ (Release Allocation)
Trả số lượng hàng đã giữ chỗ về trạng thái khả dụng tự do nếu đơn hàng bị hủy hoặc sửa đổi.
*   **API Endpoint:** `DELETE /api/v1/inventory/allocations`
*   **Controller:** [InventoryController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryController.cs#L191-L207)
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "referenceDocumentId": "guid-đơn-xuất-hoặc-chuyến-đi"
    }
    ```

---

### Nhóm 3: Cách Ly & Giải Phóng Hàng Lỗi (Inventory Holds)

#### 1. Tạo lệnh khóa hàng cách ly (Create Hold)
Chuyển trạng thái tồn kho thành `HOLD` khi phát hiện hàng hỏng hoặc lỗi nhiệt độ, ngăn không cho di chuyển hay xuất bán.
*   **API Endpoint:** `POST /api/v1/inventory-holds`
*   **Controller:** [InventoryHoldsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryHoldsController.cs#L50-L65)
*   **Quyền hạn (Role):** `Manager`, `Admin`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "stockId": "guid-tồn-kho",
      "quantity": 20,
      "reasonCode": "DAMAGE_CONTAINER", 
      "notes": "Hộp móp méo chảy nước dịch",
      "targetQuarantineLocationId": "guid-kệ-cách-ly" // Bắt buộc nếu chỉ hold một phần lô hàng
    }
    ```

#### 2. Mở khóa giải phóng hàng cách ly (Release Hold)
Giải phóng hàng cách ly trở lại trạng thái khả dụng bán bình thường sau khi QA kiểm định đạt chuẩn.
*   **API Endpoint:** `POST /api/v1/inventory-holds/{holdId}/release`
*   **Controller:** [InventoryHoldsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryHoldsController.cs#L148-L163)
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "releaseNotes": "Đã kiểm nghiệm vi sinh đạt chuẩn",
      "targetReleaseLocationId": "guid-kệ-thường" // Chuyển hàng ra khỏi khu cách ly
    }
    ```

#### 3. Hủy bỏ tiêu hủy hàng cách ly (Adjust Out Hold)
Tiêu hủy vĩnh viễn hàng hóa hỏng/hết hạn ra khỏi kho chứa.
*   **API Endpoint:** `POST /api/v1/inventory-holds/{holdId}/adjust-out`
*   **Controller:** [InventoryHoldsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryHoldsController.cs#L183-L198)
*   **Quyền hạn (Role):** `Admin`
*   **Tham số yêu cầu (Request Body):** `"Lý do tiêu hủy / Viết giảm hao hụt"` (Flat string)

---

### Nhóm 4: Hiệu Chỉnh & Duyệt Lệch Kho (Adjustments)

#### 1. Tạo yêu cầu điều chỉnh tồn kho (Create Adjustment Request)
Khai báo chênh lệch tồn kho phát hiện đột xuất cần cấp quản lý phê duyệt.
*   **API Endpoint:** `POST /api/v1/inventory-adjustments`
*   **Controller:** [InventoryAdjustmentsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryAdjustmentsController.cs#L51-L64)
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "stockId": "guid-tồn-kho",
      "quantity": -5, // Giảm 5 hộp do hao hụt
      "pallets": 0,
      "isAbsoluteCount": false, // true nếu nhập số lượng thực tế đếm được, false nếu nhập số chênh lệch delta
      "adjustmentType": "CYCLE_COUNT_DISCREPANCY", // Hoặc DAMAGED, THEFT, EXPIRED
      "reason": "Mất mát trong quá trình vận hành"
    }
    ```

#### 2. Phê duyệt điều chỉnh kho (Approve/Reject Adjustment)
Quản lý duyệt yêu cầu điều chỉnh để hệ thống cập nhật lại tồn kho thực tế và ghi sổ cái dịch chuyển hàng.
*   **API Endpoints:**
    *   **Phê duyệt:** `POST /api/v1/inventory-adjustments/{id}/approve`
    *   **Từ chối:** `POST /api/v1/inventory-adjustments/{id}/reject`
*   **Controller:** [InventoryAdjustmentsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryAdjustmentsController.cs#L176-L229)
*   **Quyền hạn (Role):** `Manager`, `Admin`

---

### Nhóm 5: Kiểm Kê Định Kỳ (Cycle Counts Audit)

#### 1. Tạo kế hoạch kiểm kê (Create Cycle Count Plan)
Tạo đợt kiểm toán/kiểm kê cho một danh sách kệ kho nhất định.
*   **API Endpoint:** `POST /api/v1/cycle-counts`
*   **Controller:** [CycleCountsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/CycleCountsController.cs#L51-L66)
*   **Quyền hạn (Role):** `Manager`, `Admin`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "warehouseId": "guid-kho",
      "assignedToUserId": "guid-thủ-kho-đếm",
      "zoneId": "guid-phân-khu", // Tùy chọn kiểm kê theo vùng
      "locationIds": ["guid-kệ-1", "guid-kệ-2"] // Danh sách kệ cần kiểm
    }
    ```

#### 2. Bắt đầu đợt kiểm kê & Khóa sổ sách (Start Cycle Count)
Đóng băng số lượng sổ sách hiện thời để tiến hành kiểm kê thực tế.
*   **API Endpoint:** `POST /api/v1/cycle-counts/{planId}/start`
*   **Controller:** [CycleCountsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/CycleCountsController.cs#L85-L98)

#### 3. Gửi số lượng thực tế đếm được (Submit Counted Quantities)
Thủ kho gửi số lượng đếm được tại các kệ về hệ thống.
*   **API Endpoint:** `POST /api/v1/cycle-counts/{planId}/submit`
*   **Controller:** [CycleCountsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/CycleCountsController.cs#L119-L133)
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "entries": [
        {
          "entryId": "guid-dòng-kiểm-kê",
          "countedQuantity": 95.0,
          "countedPallets": 2
        }
      ]
    }
    ```

#### 4. Phê duyệt lệch kiểm kê (Review & Reconcile Variance)
*   **API Endpoint:** `POST /api/v1/cycle-counts/entries/{entryId}/review`
*   **Controller:** [CycleCountsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/CycleCountsController.cs#L153-L168)
*   **Quy chế:** Nếu quản lý duyệt chênh lệch (ví dụ: mất 5 hộp), hệ thống **tự động sinh ra lệnh điều chỉnh tồn kho (Inventory Adjustment)** tương ứng để đưa số lượng hệ thống khớp hoàn toàn với thực tế đếm được.

---

### Nhóm 6: Báo Cáo & Phân Tích Tồn Kho (Analysis & Shopify-Style Reports)

Được thiết kế theo chuẩn RESTful lấy Shopify làm mẫu, giúp quản trị viên và thủ kho có cái nhìn toàn diện về hiệu suất lấp đầy, hạn sử dụng và tính tương thích nhiệt độ của hàng hóa trong kho.

#### 1. Cảnh báo hạn sử dụng (Expiry Alerts)
Xem danh sách các lô hàng trong kho có hạn sử dụng cận ngày cảnh báo.
*   **API Endpoint:** `GET /api/v1/inventory/expiry-alerts`
*   **Controller:** [InventoryAnalysisController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryAnalysisController.cs)
*   **Query Parameters:**
    *   `warehouseId` (Guid, optional): Lọc theo kho cụ thể.
    *   `warningDays` (int, mặc định: 30): Số ngày trước khi hết hạn để đưa ra cảnh báo.
    *   `pageNumber` (int, mặc định: 1): Số trang hiện tại.
    *   `pageSize` (int, mặc định: 10): Số dòng trên mỗi trang.

#### 2. Báo cáo hàng tồn lâu ngày (Aging Inventory Report)
Thống kê hàng hóa lưu trữ trong kho quá thời hạn định mức (hàng chậm luân chuyển).
*   **API Endpoint:** `GET /api/v1/inventory/aging-report`
*   **Controller:** [InventoryAnalysisController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryAnalysisController.cs)
*   **Query Parameters:**
    *   `warehouseId` (Guid, optional): Lọc theo kho cụ thể.
    *   `thresholdDays` (int, mặc định: 90): Số ngày lưu trữ định mức trong kho.
    *   `pageNumber` (int, mặc định: 1): Số trang hiện tại.
    *   `pageSize` (int, mặc định: 10): Số dòng trên mỗi trang.

#### 3. Kiểm tra tương thích nhiệt độ lưu trữ (Temperature Audits)
Phát hiện các lô hàng có dải nhiệt độ yêu cầu (`RequiredTempMin`/`Max`) không tương thích với dải nhiệt độ vận hành thực tế của phân khu Zone (`TemperatureMin`/`Max`) chứa nó.
*   **API Endpoint:** `GET /api/v1/inventory/temperature-audits`
*   **Controller:** [InventoryAnalysisController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InventoryAnalysisController.cs)
*   **Query Parameters:**
    *   `warehouseId` (Guid, optional): Lọc theo kho cụ thể.
    *   `pageNumber` (int, mặc định: 1): Số trang hiện tại.
    *   `pageSize` (int, mặc định: 10): Số dòng trên mỗi trang.

#### 4. Báo cáo hiệu suất lấp đầy & Sức chứa kho (Warehouse Capacity Utilization)
Báo cáo động tỷ lệ lấp đầy tổng kho và chi tiết của từng phân khu Zone bên trong dựa trên số pallet hiện tại (`CurrentPallets`) so với giới hạn tối đa (`MaxPallets` / `MaxCapacityPallets`).
*   **API Endpoint:** `GET /api/v1/warehouses/{id}/utilization`
*   **Controller:** [WarehouseUtilizationController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseUtilizationController.cs)
