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

---

### Nhóm 7: Thiết Lập Cấu Trúc Kho (Warehouse Setup & Layout Master Data)

Nhóm API quản trị cấu hình thông số vật lý của kho chứa, phân vùng dải nhiệt độ vận hành và toạ độ kệ lưu trữ chi tiết.

#### 1. Quản lý thông tin nhà kho (Warehouses)
Cấu hình danh mục kho bãi chính thức trong chuỗi cung ứng lạnh.
*   **API Endpoints:**
    *   **Tạo mới kho:** `POST /api/v1/warehouses`
    *   **Cập nhật kho:** `PUT /api/v1/warehouses/{id}`
    *   **Xoá mềm kho:** `DELETE /api/v1/warehouses/{id}`
    *   **Xem chi tiết kho:** `GET /api/v1/warehouses/{id}`
    *   **Xem danh sách kho phân trang:** `GET /api/v1/warehouses`
*   **Controller:** [WarehouseController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseController.cs)
*   **Quyền hạn (Role):** Khởi tạo, cập nhật, xoá chỉ dành cho `Admin`, `Manager`. Xem danh sách mở cho mọi tài khoản đã đăng nhập.

#### 2. Phân chia phân khu dải nhiệt độ (Warehouse Zones)
Chia nhỏ mặt bằng nhà kho thành các vùng kiểm soát nhiệt độ riêng biệt (ví dụ: Deep Freeze cho đồ đông lạnh, Chilled cho rau củ quả).
*   **API Endpoints:**
    *   **Tạo mới zone:** `POST /api/v1/warehouses/{warehouseId}/zones`
    *   **Cập nhật cấu hình zone:** `PUT /api/v1/zones/{id}`
    *   **Xoá mềm zone:** `DELETE /api/v1/zones/{id}`
    *   **Xem chi tiết zone:** `GET /api/v1/zones/{id}`
    *   **Xem danh sách zone thuộc kho:** `GET /api/v1/warehouses/{warehouseId}/zones`
*   **Controller:** [WarehouseZonesController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseZonesController.cs)
*   **Cấu hình nhiệt độ:** Thuật toán gợi ý cất hàng sẽ dựa trên `TemperatureMin` và `TemperatureMax` của zone để đối chiếu với yêu cầu bảo quản của sản phẩm.

#### 3. Thiết lập tọa độ kệ chi tiết (Warehouse Locations)
Định nghĩa chi tiết từng ô/kệ lưu trữ (bao gồm mã dãy Rack, khoang Bay, tầng Level) thuộc một zone cụ thể.
*   **API Endpoints:**
    *   **Tạo mới vị trí kệ:** `POST /api/v1/zones/{zoneId}/locations`
    *   **Cập nhật vị trí kệ:** `PUT /api/v1/locations/{id}`
    *   **Xoá mềm vị trí kệ:** `DELETE /api/v1/locations/{id}`
    *   **Xem chi tiết vị trí kệ:** `GET /api/v1/locations/{id}`
    *   **Xem danh sách kệ thuộc zone:** `GET /api/v1/zones/{zoneId}/locations`
*   **Controller:** [WarehouseLocationsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseLocationsController.cs)
*   **Trạng thái bảo trì:** Khi một kệ bị hư hỏng vật lý hoặc bảo trì đột xuất, cấp quản lý có thể cập nhật `Status = "INACTIVE"` hoặc `"DAMAGED"`. Hệ thống sẽ tự động loại bỏ vị trí này ra khỏi danh sách gợi ý cất hàng tự động để đảm bảo an toàn vận hành.

---

### Nhóm 8: Quản Lý Sự Cố & Khiếu Nại Đền Bù (Incidents & Claims)

Nhóm API này quản trị toàn bộ các sự cố vận hành phát sinh trong quá trình vận chuyển hoặc lưu trữ (trong kho), cùng các hồ sơ khiếu nại đền bù thiệt hại từ phía khách hàng/chủ hàng.

#### 1. Báo cáo sự cố phát sinh (Report Incident)
Ghi nhận sự cố vận chuyển hoặc vận hành kho (ví dụ: va chạm xe, mất nhiệt độ công ten nơ, đổ vỡ pallet hàng).
*   **API Endpoint:** `POST /api/v1/incidents`
*   **Controller:** [IncidentReportsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/IncidentReportsController.cs#L32-L48)
*   **Quyền hạn (Role):** `Admin`, `Manager`, `Driver`, `WarehouseOperator`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "tripId": "guid-chuyến-đi-nếu-bị-sự-cố-khi-vận-chuyển",
      "incidentType": "DAMAGE_CARGO", // ACCIDENT, VEHICLE_BREAKDOWN, TEMP_EXCURSION, DAMAGE_CARGO, DELAY
      "severity": "HIGH", // LOW, MEDIUM, HIGH, CRITICAL
      "description": "Bể vỡ 5 thùng carton do va quệt xe cẩu",
      "currentLatitude": 10.762622,
      "currentLongitude": 106.660172
    }
    ```
*   **Phản hồi thành công (Response):** Chi tiết sự cố kèm mã người báo cáo, trạng thái mặc định ban đầu là `REPORTED`.

#### 2. Giải quyết đóng sự cố (Resolve Incident)
Xác nhận đã xử lý xong sự cố thực địa (ví dụ: dọn dẹp hiện trường đổ vỡ, sửa xe xong, hạ nhiệt độ về mức an toàn).
*   **API Endpoint:** `POST /api/v1/incidents/{id}/resolve`
*   **Controller:** [IncidentReportsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/IncidentReportsController.cs#L53-L70)
*   **Quyền hạn (Role):** `Admin`, `Manager`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "resolutionNote": "Đã điều xe cứu hộ kéo xe về và dọn dẹp vệ sinh xong"
    }
    ```
*   **Quy chế:** Trạng thái sự cố chuyển sang `RESOLVED`.

#### 3. Tạo hồ sơ khiếu nại đền bù (Lodge Claim)
Khách hàng hoặc nhân viên đại diện tạo hồ sơ khiếu nại đền bù hàng hóa bị hư hỏng, thất thoát, hoặc giao trễ.
*   **API Endpoint:** `POST /api/v1/claims`
*   **Controller:** [ClaimsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/ClaimsController.cs#L32-L48)
*   **Quyền hạn (Role):** `Admin`, `Manager`, `Customer`, `WarehouseOperator`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "orderId": "guid-đơn-hàng-bị-ảnh-hưởng",
      "claimType": "DAMAGE", // DAMAGE, LOSS, TEMP_VIOLATION, DELAY
      "description": "Yêu cầu đền bù 5 thùng thịt bò bị rã đông do xe mất nhiệt",
      "evidenceImages": [
        "https://cloudinary.com/evidence1.jpg",
        "https://cloudinary.com/evidence2.jpg"
      ]
    }
    ```
*   **Quy chế:** Hệ thống tự động sinh mã khiếu nại dạng `CLM-YYYYMMDD-XXXX` và lưu trữ các đường dẫn hình ảnh bằng chứng vào bảng tài liệu minh chứng (`ClaimEvidences`) với trạng thái ban đầu là `OPEN`.

#### 4. Phê duyệt/Giải quyết khiếu nại (Resolve Claim)
Ban quản lý xác minh trách nhiệm lỗi và đưa ra phương án xử lý đền bù cuối cùng.
*   **API Endpoint:** `POST /api/v1/claims/{id}/resolve`
*   **Controller:** [ClaimsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/ClaimsController.cs#L53-L70)
*   **Quyền hạn (Role):** `Admin`, `Manager`
*   **Tham số yêu cầu (Request Body):**
    ```json
    {
      "status": "RESOLVED", // RESOLVED (chấp nhận đền bù) hoặc REJECTED (từ chối)
      "faultOwner": "CARRIER", // CARRIER (nhà vận chuyển), WAREHOUSE (nhà kho), CUSTOMER (khách hàng), FORCE_MAJEURE (bất khả kháng)
      "resolutionNote": "Duyệt bồi thường 100% giá trị hàng hóa bị hỏng. Phạt trừ lương lái xe do tự ý tắt máy lạnh."
    }
    ```
*   **Quy chế:** Hồ sơ được đóng vĩnh viễn ở trạng thái kết quả mong muốn (`RESOLVED` / `REJECTED`).

---

## 3. Quy Trình Vận Hành & Xử Lý Các Sự Cố Phát Sinh (Incident Handling Workflows)

Dưới đây là các kịch bản chi tiết xử lý sự cố từ lúc phát sinh đến lúc giải quyết đền bù và ghi nhận kế toán/kho bãi:

### Sơ đồ quy trình tổng quan:
```mermaid
flowchart TD
    subgraph "1. Khai báo & Phát hiện"
        A1[Sự cố ngoài đường / trong kho] -->|POST /api/v1/incidents| A2(Incident: REPORTED)
    end
    
    subgraph "2. Cách ly & Kiểm soát"
        A2 -->|Hàng hỏng ở kho| B1[Tạo lệnh Hold hàng<br>POST /api/v1/inventory-holds]
        B1 --> B2(Trạng thái: HOLD tại kệ cách ly)
    end
    
    subgraph "3. Khiếu nại từ khách hàng"
        B2 -->|Chủ hàng gửi yêu cầu đền bù| C1[Tạo hồ sơ khiếu nại<br>POST /api/v1/claims]
        C1 --> C2(Claim: OPEN kèm Evidence)
    end
    
    subgraph "4. Điều tra & Xử lý"
        C2 -->|Xác minh & Phân định lỗi| D1[Resolve Claim<br>POST /api/v1/claims/{id}/resolve]
        D1 -->|1. Trả lời khiếu nại| D2(Status: RESOLVED/REJECTED + FaultOwner)
        D1 -->|2. Tiêu huỷ hàng hỏng| D3[Adjust Out Hold<br>POST /api/v1/inventory-holds/{holdId}/adjust-out]
        D1 -->|3. Đóng sự cố thực địa| D4[Resolve Incident<br>POST /api/v1/incidents/{incidentId}/resolve]
    end
```

### Chi Tiết Xử Lý Theo Từng Case Phát Sinh:

#### Case 1: Đổ vỡ / Hư hỏng hàng hóa trong kho (Warehouse Spillage/Damage)
*   **Hiện tượng:** Thủ kho phát hiện pallet hàng bị đổ vỡ hoặc rách bao bì trong lúc xe nâng di chuyển hàng lên kệ.
*   **Cách giải quyết:**
    1.  **Ghi nhận sự cố:** Thủ kho gọi API `POST /api/v1/incidents` với `incidentType: "DAMAGE_CARGO"` và `severity: "LOW"` hoặc `"MEDIUM"`.
    2.  **Khóa giữ hàng lỗi:** Để tránh hàng hỏng này bị xuất nhầm cho khách, thủ kho lập tức gọi API `POST /api/v1/inventory-holds` để khoá số tồn kho bị ảnh hưởng, di chuyển chúng sang vị trí cách ly (`targetQuarantineLocationId`). Trạng thái tồn kho lúc này là `HOLD`.
    3.  **Xử lý hiện trường:** Thủ kho dọn dẹp vệ sinh khu vực kệ bị đổ vỡ, sửa sang lại bao bì. Gọi API `POST /api/v1/incidents/{id}/resolve` để đóng sự cố.
    4.  **Xử lý hàng hóa:**
        *   *Nếu phục hồi được:* Gọi API `POST /api/v1/inventory-holds/{holdId}/release` để đưa hàng trở lại kệ thường ở trạng thái `AVAILABLE`.
        *   *Nếu hỏng hoàn toàn:* Quản lý gọi API `POST /api/v1/inventory-holds/{holdId}/adjust-out` để huỷ bỏ/tiêu huỷ vĩnh viễn hàng lỗi ra khỏi kho, hệ thống tự động ghi giảm tồn kho.

#### Case 2: Đổ vỡ / Hỏng hóc hàng hóa trong quá trình vận chuyển (Transit Damage/Incident)
*   **Hiện tượng:** Xe tải gặp sự cố giữa đường (tai nạn giao thông hoặc thùng lạnh bị hỏng máy phát làm nhiệt độ tăng cao vượt mức bảo quản cho phép).
*   **Cách giải quyết:**
    1.  **Báo cáo khẩn cấp:** Tài xế gọi API `POST /api/v1/incidents` đính kèm `tripId`, chọn `incidentType: "TEMP_EXCURSION"` hoặc `"ACCIDENT"`, và điền toạ độ GPS xảy ra sự cố. Trạng thái sự cố là `REPORTED`.
    2.  **Khắc phục sự cố:** Điều phối viên gửi xe cứu hộ hoặc thợ đến sửa chữa. Sau khi khắc phục xong, gọi API `POST /api/v1/incidents/{id}/resolve` với note ghi rõ nguyên nhân và cách xử lý.
    3.  **Nhận hàng & Cách ly:** Khi xe về đến kho đích, bộ phận QA đo đạc nhiệt độ và phát hiện hàng có dấu hiệu suy giảm chất lượng. Thủ kho thực hiện nhập kho nhưng đưa thẳng số lượng hàng này vào khu cách ly ở trạng thái `HOLD` (gọi `POST /api/v1/inventory-holds`).
    4.  **Khách hàng khiếu nại:** Chủ hàng nhận được thông tin hàng hỏng, gửi đơn khiếu nại thông qua API `POST /api/v1/claims` đính kèm ảnh chụp hàng móp méo/chảy nước làm bằng chứng, liên kết với mã đơn `orderId`.
    5.  **Duyệt đền bù:** Ban quản lý điều tra chéo log nhiệt độ của xe tải. Xác định lỗi do lái xe tắt máy lạnh tiết kiệm dầu (`FaultOwner: "CARRIER"`). Gọi API `POST /api/v1/claims/{claimId}/resolve` chuyển trạng thái sang `RESOLVED` và phê duyệt số tiền bồi thường.
    6.  **Tiêu huỷ hàng hoá:** Gọi API `POST /api/v1/inventory-holds/{holdId}/adjust-out` để xoá số hàng hỏng này khỏi kho lưu trữ.

#### Case 3: Thất thoát / Lệch hàng khi kiểm kê định kỳ (Inventory Discrepancy)
*   **Hiện tượng:** Khi thực hiện kiểm kê (Cycle Count) phát hiện số lượng thực tế đếm được ít hơn số lượng ghi nhận trên sổ sách hệ thống (nghi ngờ mất cắp hoặc đếm sót trước đó).
*   **Cách giải quyết:**
    1.  **Gửi dữ liệu kiểm kê:** Thủ kho gửi số lượng thực tế đếm được qua API `POST /api/v1/cycle-counts/{planId}/submit`.
    2.  **Tự động tạo lệnh điều chỉnh:** Hệ thống phát hiện chênh lệch và tự động tạo một yêu cầu điều chỉnh tồn kho ở trạng thái chờ duyệt.
    3.  **Duyệt chênh lệch:** Quản lý xem xét lý do hao hụt và gọi API `POST /api/v1/inventory-adjustments/{id}/approve`. Tồn kho trên hệ thống lập tức được cập nhật giảm xuống trùng khớp với thực tế.
    4.  **Khiếu nại bồi thường (nếu có):** Nếu số lượng thất thoát quá lớn và do lỗi bảo vệ hoặc vận hành kho làm mất mát, chủ hàng có thể gửi khiếu nại đền bù qua API `POST /api/v1/claims` với loại `LOSS` để yêu cầu đơn vị vận hành kho bồi thường thiệt hại.
