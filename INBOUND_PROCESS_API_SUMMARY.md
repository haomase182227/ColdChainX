# Hướng Dẫn Quy Trình & Danh Sách API Inbound (Nhập Kho)

Tài liệu này tổng hợp toàn bộ quy trình nghiệp vụ nhập hàng (Inbound) trong hệ thống **ColdChainX**, mô tả chi tiết từng bước gọi API, các vai trò (Roles) thực hiện, các quy tắc kiểm tra (Rules) và cách hệ thống xử lý các tình huống phát sinh (chênh lệch kích thước hàng hóa, lỗi nhiệt độ, thiếu chứng từ).

---

## 1. Sơ Đồ Quy Trình Inbound Tổng Quan

Dưới đây là luồng đi của hàng hóa từ lúc khách hàng đăng ký đến khi hàng hóa được cất vào kệ kho chính thức:

```mermaid
flowchart TD
    A[B1: Khách hàng tạo ASN<br>POST /api/v1/asns] -->|Hàng đến kho| B[B2: Kiểm tra QC & Nhận hàng đầu vào<br>POST /api/v1/warehouse-receipts]
    B -->|Phiếu nhập: PENDING_MEASUREMENT| C[B3: Đo đạc kích thước thực tế<br>PUT /api/v1/warehouse-receipts/orders/{id}/measurements]
    C -->|Phiếu nhập: PENDING_COMPLETE| D[B4: Tải chứng từ bắt buộc<br>POST /api/v1/attachments]
    D -->|Tài liệu: PENDING| E[B5: Manager duyệt chứng từ<br>PATCH /api/v1/attachments/{id}/verify]
    E -->|Tài liệu: VERIFIED| F[B6: Hoàn tất nhập kho<br>POST /api/v1/warehouse-receipts/orders/{id}/completion]
    F -->|Chạy Compliance Engine| G{Đạt điều kiện?}
    G -->|Không đạt| H[Bị chặn hoàn tất<br>Yêu cầu bổ sung/duyệt lại tài liệu]
    G -->|Đạt| I[Ghi nhận tồn kho thực tế<br>Tự động tính cước lại & tạo Hóa đơn chênh lệch]
    I --> J[B7: Gợi ý cất hàng vào kệ<br>GET /api/v1/putaway-suggestions]
    H --> D
```

---

## 2. Chi Tiết Từng Bước & Danh Sách API

### Bước 1: Khởi tạo thông tin lô hàng chuẩn bị đến kho (ASN)
Khách hàng thông báo trước cho kho về thông tin chuyến hàng sắp tới.
* **API Endpoint:** `POST /api/v1/asns`
* **Controller:** [AsnController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/AsnController.cs)
* **Quyền hạn (Role):** `Customer`
* **Các trường dữ liệu chính:**
  ```json
  {
    "expectedArrivalDate": "2026-06-25T08:00:00Z",
    "warehouseId": "guid-kho-den",
    "note": "Thông tin ghi chú lô hàng nhập",
    "items": [
      {
        "itemName": "Tên sản phẩm",
        "itemCode": "Mã sản phẩm",
        "expectedQty": 100,
        "unit": "BOX"
      }
    ]
  }
  ```
* **Kết quả:** Tạo bản ghi ASN trong hệ thống để kho chuẩn bị mặt bằng đón hàng.

#### API Tra cứu Lịch Nhập Kho (Inbound Delivery Schedule)
Tra cứu danh sách lịch nhập hàng dự kiến của khách hàng hoặc kho để hiển thị lên lịch trình/calendar.
* **API Endpoint:** `GET /api/v1/asns`
* **Controller:** [AsnController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/AsnController.cs)
* **Quyền hạn (Role):** Mọi người dùng đã đăng nhập.
  * Với `Customer`: Hệ thống tự động lọc chỉ trả về danh sách lịch nhập của chính khách hàng đó.
  * Với `Admin`, `Manager`, `WarehouseOperator`: Có thể tra cứu lịch nhập của tất cả các khách hàng hoặc lọc theo `warehouseId` / `customerId` cụ thể.
* **Query Parameters:**
  * `status`: Lọc trạng thái đăng ký (e.g. `SCHEDULED`, `ARRIVED`, `CANCELLED`).
  * `dateFrom` & `dateTo`: Lọc theo khoảng thời gian giao hàng dự kiến (`RequestedDropoffTime`).
  * `searchQuery`: Tìm kiếm nhanh theo mã ASN, mã vận đơn, tên sản phẩm, tên khách hàng hoặc địa chỉ giao hàng.
  * `warehouseId`: Lọc theo kho. Hệ thống tự động đối chiếu thông tin địa chỉ giao hàng (`DestAddress`) với thông tin Kho tương ứng.
  * `orderId`: Lọc chính xác theo ID đơn hàng vận chuyển (`transport_orders.order_id`).
  * `pageNumber` & `pageSize`: Phân trang dữ liệu (mặc định trang 1, 10 bản ghi/trang).
* **Định dạng dữ liệu trả về (Response JSON):**
  ```json
  {
    "success": true,
    "message": "Inbound schedules retrieved successfully.",
    "data": {
      "totalRecords": 1,
      "totalPages": 1,
      "currentPage": 1,
      "pageSize": 10,
      "data": [
        {
          "asnId": "guid-asn",
          "asnCode": "ASN-20260620120000-1234",
          "orderId": "guid-don-hang",
          "trackingCode": "TRK-XYZ123",
          "customerId": "guid-khach-hang",
          "customerName": "Tên công ty khách hàng",
          "itemName": "Thịt bò Mỹ",
          "category": "FOOD",
          "quantity": 100,
          "tempCondition": "frozen",
          "expectedWeightKg": 1200.0,
          "expectedCbm": 3.5,
          "destAddress": "Địa chỉ Kho HCM Central",
          "requestedDropoffTime": "2026-06-25T10:00:00Z",
          "status": "SCHEDULED",
          "qrCodeValue": "ASN|ASN-20260620120000-1234|ORDER|...",
          "createdAt": "2026-06-20T12:00:00Z",
          "warehouseId": "guid-kho-hcm",
          "warehouseName": "HCM Central Warehouse"
        }
      ]
    }
  }
  ```

---

### Bước 2: Kiểm tra QC đầu vào khi hàng đến cửa kho (Inbound QC)
Đo nhiệt độ thực tế của xe tải/thùng hàng khi tài xế giao hàng đến và ghi nhận thông tin bàn giao ban đầu.
* **API Endpoint:** `POST /api/v1/warehouse-receipts`
* **Controller:** [WarehouseReceiptController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseReceiptController.cs#L49-L77)
* **Quyền hạn (Role):** `WarehouseOperator`, `Manager`, `Admin`
* **Các trường dữ liệu chính (Shopify-style wrapped body payload):**
  ```json
  {
    "warehouse_receipt": {
      "orderId": "guid-don-hang",
      "warehouseId": "guid-kho-den",
      "recordedTemperature": -18.5,
      "delivererName": "Nguyễn Văn Tài Xế",
      "note": "Thông tin tình trạng xe/thùng hàng"
    }
  }
  ```
* **Quy tắc kiểm tra tự động (Temperature Check):**
  * Hệ thống đối chiếu `recordedTemperature` với nhiệt độ yêu cầu của đơn hàng (`TempCondition`):
    * Hàng đông lạnh (`frozen` - target temp < 0°C): Cho phép chênh lệch tối đa +3°C so với nhiệt độ mục tiêu. Nếu vượt quá, đánh dấu thất bại `[QC FAILED]`.
    * Hàng mát (`chill` - target temp >= 0°C): Bắt buộc nhiệt độ phải nằm trong khoảng từ `0°C` đến `8°C`. Nếu nằm ngoài khoảng này, đánh dấu thất bại `[QC FAILED]`.
* **Cảnh báo tương kỵ mùi (Odor Warning):**
  * Nếu hàng nhập có mùi mạnh (như sầu riêng, hải sản) mà trong kho đang lưu trữ hàng nhạy cảm mùi (sữa, trứng, chocolate) hoặc ngược lại, hệ thống sẽ trả về cảnh báo `warningMessage` trong response (chỉ cảnh báo, không chặn nhận hàng).
* **Kết quả:** Tạo mới phiếu nhận hàng `WarehouseReceipt` ở trạng thái nháp `PENDING_MEASUREMENT`.

---

### Bước 3: Đo đạc và Cập nhật kích thước thực tế (Update Measurements)
Đo đạc kích thước (Dài - Rộng - Cao), cân nặng, kiểm đếm số lượng thực tế và khai báo thông tin FEFO (Số lô, ngày sản xuất, hạn sử dụng).
* **API Endpoint:** `PUT /api/v1/warehouse-receipts/orders/{orderId}/measurements`
* **Controller:** [WarehouseReceiptController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseReceiptController.cs#L97-L121)
* **Quyền hạn (Role):** `WarehouseOperator`, `Manager`, `Admin`
* **Các trường dữ liệu chính (Shopify-style wrapped body payload):**
  ```json
  {
    "warehouse_receipt": {
      "items": [
        {
          "itemName": "Thịt bò Mỹ",
          "itemCode": "BEEF-01",
          "unit": "BOX",
          "actualQty": 98,          // Số lượng thực tế kiểm đếm được
          "weightKg": 25.5,          // Cân nặng thực tế của mỗi thùng
          "lengthCm": 40,            // Chiều dài
          "widthCm": 30,             // Chiều rộng
          "heightCm": 20,            // Chiều cao
          "conditionStatus": "GOOD", // Trạng thái cảm quan hàng hóa (GOOD / DAMAGED)
          "batchNumber": "LOT202606",// Số lô sản xuất (Bắt buộc với một số ngành hàng)
          "manufacturedDate": "2026-05-01",
          "expiryDate": "2026-11-01",
          "countryOfOrigin": "USA",  // Quốc gia xuất xứ (Dùng để xác định hàng nhập khẩu)
          "productCategory": "SEAFOOD" // Danh mục sản phẩm (FOOD, SEAFOOD, PHARMA, VACCINE, AGRICULTURE, etc.)
        }
      ]
    }
  }
  ```
* **Kết quả:** Lưu thông tin các mặt hàng thực tế nhận được (`WarehouseReceiptItem`), tự động sinh mã vạch (`Barcode`) và mã QR (`QrCode`) cho từng dòng hàng. Trạng thái phiếu nhập chuyển thành `PENDING_COMPLETE`.

---

### Bước 4: Tải chứng từ đính kèm bắt buộc (Upload Attachments)
Tải lên các tài liệu minh chứng (file ảnh, PDF) bắt buộc để hệ thống xác thực tính tuân thủ pháp lý và an toàn thực phẩm.
* **API Endpoint:** `POST /api/v1/attachments`
* **Format:** `multipart/form-data`
* **Controller:** [AttachmentController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/AttachmentController.cs#L51-L68)
* **Quyền hạn (Role):** `WarehouseOperator`, `Manager`, `Admin`
* **Các trường dữ liệu chính:**
  * `File` (Binary file): File đính kèm.
  * `Category`: Danh mục lớn (ví dụ: `COMPLIANCE` hoặc `INBOUND_EVIDENCE`).
  * `SubCategory`: Loại chứng từ chi tiết (ví dụ: `QUARANTINE_CERTIFICATE`, `COA_CERTIFICATE`, `CUSTOMS_DECLARATION`, `CERTIFICATE_OF_ORIGIN`, `SEAL_PHOTO`, etc.).
  * `WarehouseReceiptId`: Liên kết tới phiếu nhập kho.
  * `WarehouseReceiptItemId` (Optional): Liên kết chi tiết tới dòng hàng SKU.
  * `SealNumber` (Optional): Điền số niêm phong container (chỉ áp dụng cho chứng từ `SEAL_PHOTO`).
* **Kết quả:** Tạo bản ghi đính kèm lưu trên ổ đĩa/cloud với trạng thái mặc định ban đầu là `PENDING` (Chờ duyệt).

---

### Bước 5: Phê duyệt chứng từ đính kèm (Verify Attachments)
Quản lý hoặc Quản trị viên duyệt các giấy tờ đã tải lên để xác nhận tính hợp lệ của chúng.
* **API Endpoint:** `PATCH /api/v1/attachments/{attachmentId}/verify`
* **Controller:** [AttachmentController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/AttachmentController.cs#L184-L203)
* **Quyền hạn (Role):** `Manager`, `Admin`
* **Các trường dữ liệu chính:**
  ```json
  {
    "status": "VERIFIED", // Hoặc "REJECTED" nếu chứng từ không đạt
    "rejectionReason": "Lý do từ chối nếu có"
  }
  ```
* **Kết quả:** Trạng thái tài liệu chuyển sang `VERIFIED` hoặc `REJECTED`.

---

### Bước 6: Hoàn tất nhập kho & Tự động chạy Compliance Engine (Complete Inbound)
Kiểm tra chốt tất cả điều kiện tuân thủ giấy tờ và chính thức đưa hàng hóa vào lưu trữ trong kho, đồng thời tính toán lại cước chênh lệch nếu có.
* **API Endpoint:** `POST /api/v1/warehouse-receipts/orders/{orderId}/completion`
* **Controller:** [WarehouseReceiptController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/WarehouseReceiptController.cs#L123-L135)
* **Quyền hạn (Role):** `WarehouseOperator`, `Manager`, `Admin`
* **Quy tắc hoạt động cốt lõi của ComplianceRulesEngine:**
  Hệ thống quét toàn bộ chứng từ đính kèm liên quan đến phiếu nhận hàng này và so sánh với danh mục sản phẩm:
  * **Hàng FOOD / SEAFOOD:** Yêu cầu có ngày sản xuất (`ManufacturedDate`) và hạn sử dụng (`ExpiryDate`). Riêng mặt hàng `SEAFOOD` yêu cầu phải có chứng từ `QUARANTINE_CERTIFICATE` ở trạng thái `VERIFIED`.
  * **Hàng dược phẩm PHARMA / VACCINE:** Yêu cầu có ngày sản xuất, ngày hết hạn và chứng từ `COA_CERTIFICATE` đã được duyệt. Riêng `VACCINE` yêu cầu thêm chứng từ `PRODUCT_LICENSE` đã duyệt.
  * **Hàng nhập khẩu (Import Goods - được xác định khi `CountryOfOrigin` khác "Vietnam"):** Yêu cầu bắt buộc phải có `CUSTOMS_DECLARATION`, `CERTIFICATE_OF_ORIGIN` và `SEAL_PHOTO` (kèm số niêm phong hợp lệ) ở trạng thái `VERIFIED`.
  
  > [!CAUTION]
  > Nếu có bất kỳ tài liệu bắt buộc nào bị **Thiếu (Missing)**, đang **Chờ duyệt (PENDING)** hoặc **Bị từ chối (REJECTED)**, API sẽ trả về lỗi chi tiết các giấy tờ chưa đạt và **ngăn chặn không cho hoàn tất nhập kho**.

* **Kết quả khi hoàn tất thành công (Passed Compliance):**
  1. Hàng hóa được đưa vào khu vực đệm nhận hàng (`RECEIVING` zone, vị trí mặc định `RCV-STAGE-01`).
  2. Ghi nhận số lượng thực tế nhận được vào bảng tồn kho (`InventoryStock`).
  3. Trạng thái phiếu nhập chuyển thành `COMPLETED` và tự động sinh bản PDF phiếu biên nhận kho điện tử (`PdfUrl`).
  4. **Tự động đối chiếu chênh lệch kích thước & trọng lượng và tạo Hóa đơn điều chỉnh** (Xem chi tiết ở Phần 3).

---

### Bước 7: Gợi ý vị trí cất hàng tối ưu (Putaway Suggestions)
Tra cứu vị trí lưu trữ kệ hàng tốt nhất dựa trên sức chứa và dải nhiệt độ yêu cầu của sản phẩm.
* **API Endpoint:** 
  * Theo từng mặt hàng tồn kho: `GET /api/v1/putaway-suggestions?stockId={stockId}`
  * Hoặc hàng loạt theo phiếu nhập: `GET /api/v1/putaway-suggestions/receipt/{receiptId}`
* **Controller:** [PutawaySuggestionsController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/PutawaySuggestionsController.cs)
* **Quyền hạn (Role):** Mọi người dùng đã xác thực.
* **Kết quả:** Trả về danh sách các ô kệ phù hợp trong kho được xếp hạng theo điểm tối ưu (suitability score).

---

### Bước 8: Truy vấn hóa đơn chênh lệch (Invoices API)
Sau khi hoàn tất nhập kho (Bước 6), nếu kích thước/trọng lượng thực tế đo đạc ở Bước 3 có chênh lệch so với kỳ vọng ban đầu, hệ thống sẽ tự động tạo hóa đơn điều chỉnh chênh lệch (`INV-ADJ-...`). Kế toán hoặc khách hàng có thể gọi các API sau để kiểm tra:
* **API Endpoints:**
  * **Tra cứu danh sách hóa đơn:** `GET /api/v1/invoices`
    * *Query Params:* `status` (UNPAID/PAID), `pageNumber`, `pageSize`.
    * *Cơ chế hoạt động:* Khách hàng chỉ xem được hóa đơn của mình, Admin/Manager xem được toàn bộ.
  * **Xem chi tiết một hóa đơn và các dòng phí:** `GET /api/v1/invoices/{invoiceId}`
    * *Cơ chế hoạt động:* Trả về chi tiết các dòng phí (`InvoiceLines`) mô tả rõ nội dung điều chỉnh chênh lệch.
  * **Xem hóa đơn theo đơn hàng:** `GET /api/v1/orders/{orderId}/invoices` (RESTful Shopify Style)
    * *Cơ chế hoạt động:* Trả về toàn bộ hóa đơn (bao gồm cả hóa đơn gốc và hóa đơn điều chỉnh chênh lệch) liên kết với Đơn hàng.
* **Controller:** [InvoicesController.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Controllers/InvoicesController.cs)
* **Quyền hạn (Role):** Mọi người dùng đã xác thực (Tự động lọc theo sở hữu cho `Customer`).
* **Kết quả:** Trả về dữ liệu chi tiết số tiền và lý do chênh lệch hóa đơn.


---

## 3. Cách Xử Lý Chi Tiết Cho Các Tình Huống Phát Sinh (Arising Scenarios)

### Tình huống A: Lỗi kiểm tra nhiệt độ khi giao hàng (Recorded Temp vi phạm quy chuẩn)
* **Hiện tượng:** Tại Bước 2 (QC), nhiệt độ ghi nhận vượt ngưỡng đông lạnh hoặc mát.
* **Quy trình xử lý trong code:**
  1. Hệ thống vẫn cho phép tạo phiếu nhập nháp và tiến hành các bước cập nhật kích thước hay nộp giấy tờ bình thường.
  2. Tuy nhiên, khi gọi API Hoàn tất nhập kho (Bước 6), hệ thống sẽ kiểm tra lại nhiệt độ. Tại dòng 500 của [WarehouseReceiptService.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.Application/Services/WarehouseReceiptService.cs#L500), trạng thái của hàng hóa đưa vào tồn kho sẽ bị gán là **`HOLD`** (Khóa lưu thông) thay vì `AVAILABLE`.
  3. Đồng thời tại dòng 606-619 của [WarehouseReceiptService.cs](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.Application/Services/WarehouseReceiptService.cs#L606-L619), hệ thống tự động chèn một bản ghi vào bảng `inventory_holds` với mã lý do là `QC_TEMP_VIOLATION` kèm ghi chú vi phạm nhiệt độ để cảnh báo và khóa không cho xuất bán lô hàng này cho đến khi có quyết định xử lý thủ công.

---

### Tình huống B: Kích thước đo đạc thực tế có chênh lệch so với Đơn đặt hàng ban đầu
* **Hiện tượng:** Trọng lượng thực tế (`totalActualWeight`) hoặc thể tích thực tế (`totalActualCbm`) sau khi cân đo đong đếm ở Bước 3 khác biệt so với ước tính trên Đơn hàng (`TransportOrder`).
* **Quy trình xử lý trong code (khi bấm Hoàn tất ở Bước 6):**
  1. Tính toán lại cước vận chuyển thực tế cuối cùng (`finalAmount`) bằng cách lấy giá trị lớn hơn giữa cước theo trọng lượng thực tế và thể tích thực tế nhân với đơn giá trong ma trận giá (`PricingMatrix`).
  2. Đối chiếu `finalAmount` thực tế này với số tiền báo giá gốc (`originalQuotation.FinalAmount`).
  3. **Nếu có sự chênh lệch (khác biệt tiền):**
     * Hệ thống tự động tạo một hóa đơn điều chỉnh bổ sung (`Invoice`) với mã code dạng `INV-ADJ-[Ngày]-[Số ngẫu nhiên]`.
     * Số tiền trên hóa đơn này chính là phần chênh lệch (`diffGrandTotal = finalAmount_thuc_te - originalQuotation.FinalAmount`).
       * **Nếu thừa kích thước/tiền tăng:** Hóa đơn điều chỉnh mang giá trị **Dương (+)** và ở trạng thái **`UNPAID`** (Khách hàng phải trả thêm tiền).
       * **Nếu hụt kích thước/tiền giảm:** Hóa đơn điều chỉnh mang giá trị **Âm (-)** (Ghi nhận giảm trừ công nợ/hoàn tiền cho khách hàng).
     * Dòng chi tiết hóa đơn (`InvoiceLine`) được gắn mã lý do chênh lệch là: `INBOUND_MEASUREMENT_ADJUSTMENT`.
     * **Tự động sinh hóa đơn PDF:** Song song với việc tạo bản ghi hóa đơn, hệ thống sẽ tự động tạo một tệp PDF hóa đơn điện tử dựa trên mẫu giao diện HTML chuyên nghiệp tại [InvoiceTemplate.html](file:///c:/Users/tranl/OneDrive/Desktop/6-11-2026/ColdChainX/ColdChainX.API/Templates/InvoiceTemplate.html). Tệp PDF được kết xuất thông qua thư viện PuppeteerSharp (`SimplePdfService`), lưu trữ trên máy chủ và đường dẫn URL của PDF được cập nhật trực tiếp vào trường `PdfUrl` của hóa đơn (`invoices.pdf_url`). Khách hàng có thể tải và xem bản in PDF trực tiếp từ liên kết này.
  
  > [!IMPORTANT]
  > **API để kiểm tra chi tiết hóa đơn chênh lệch và xuất PDF:**
  > Hệ thống hỗ trợ tra cứu hóa đơn điều chỉnh thông qua bộ API quản lý hóa đơn mới:
  > * `GET /api/v1/invoices` — Xem danh sách hóa đơn (trả về trường `pdfUrl`).
  > * `GET /api/v1/invoices/{invoiceId}` — Xem chi tiết hóa đơn, các dòng phí chênh lệch và liên kết PDF (`pdfUrl`).
  > * `GET /api/v1/orders/{orderId}/invoices` — Tra cứu nhanh danh sách hóa đơn và liên kết PDF đi kèm của một đơn hàng cụ thể.
  >
  > **Mối liên kết trong Cơ sở dữ liệu:**
  > * Một đơn hàng có thể có nhiều hóa đơn (Hóa đơn gốc từ Báo giá ban đầu + Hóa đơn điều chỉnh chênh lệch thực tế).
  > * Mối liên kết được thực hiện thông qua bảng `invoice_lines`:
    * Trường `invoice_lines.order_id` liên kết ngoại đến `transport_orders.order_id`.
    * Trường `invoice_lines.invoice_id` liên kết ngoại đến `invoices.invoice_id`.
  > * Khi gọi API `GET /api/v1/orders/{orderId}/invoices`, hệ thống sẽ dựa vào `OrderId` để quét tất cả các dòng chi tiết `invoice_lines` liên quan, sau đó gộp và trả về danh sách các `invoices` đầy đủ.
  >
  > **Công thức & Logic tính toán số tiền chênh lệch:**
  > 1. Tổng trọng lượng thực tế ($W_{\text{act}}$) và thể tích thực tế ($V_{\text{act}}$ - tính bằng CBM) được tính toán từ đo đạc ở Bước 3.
  > 2. Tính lại cước vận chuyển cơ bản thực tế:
  >    $$baseFreight = \max(W_{\text{act}} \times Price_{\text{Kg}}, V_{\text{act}} \times Price_{\text{CBM}})$$
  >    *(Nếu $baseFreight$ < 300,000 VND thì lấy 300,000 VND)*
  > 3. Tính phụ phí last-mile thực tế ($lastMileSurcharge$): Nếu quãng đường giao hàng lớn hơn 10km, phần km vượt tính 15,000 VND/km.
  > 4. Tính tổng tiền thực tế gồm thuế VAT:
  >    $$Total_{\text{act}} = (baseFreight + lastMileSurcharge + VasAmount) \times 1.08$$
  > 5. Số tiền chênh lệch ghi nhận vào hóa đơn điều chỉnh:
  >    $$GrandTotal_{\text{ADJ}} = Total_{\text{act}} - GrandTotal_{\text{ban đầu}}$$
  >    * Nếu $GrandTotal_{\text{ADJ}} > 0$: Tạo hóa đơn `UNPAID` yêu cầu khách hàng đóng thêm.
  >    * Nếu $GrandTotal_{\text{ADJ}} < 0$: Tạo hóa đơn âm tiền ghi nhận giảm trừ/hoàn tiền.


---

### Tình huống C: Bị chặn hoàn thành nhập kho do chưa tải hoặc chưa duyệt giấy tờ bắt buộc
* **Hiện tượng:** Khi gọi API Hoàn tất (Bước 6) thì nhận được thông báo lỗi trả về các danh mục giấy tờ bị thiếu/chưa duyệt/bị từ chối.
* **Quy trình xử lý:**
  1. Kiểm tra mã lỗi trả về để xem cụ thể loại tài liệu nào đang bị lỗi (ví dụ: thiếu `CERTIFICATE_OF_ORIGIN`).
  2. Thực hiện gọi lại API tải tài liệu đính kèm `POST /api/v1/attachments` (Bước 4) để tải lên tệp tin chứng từ còn thiếu.
  3. Liên hệ Quản lý/Admin duyệt tài liệu vừa tải lên bằng API `PATCH /api/v1/attachments/{id}/verify` (Bước 5) chuyển trạng thái thành `VERIFIED`.
  4. Gọi lại API Hoàn tất nhập kho `POST /api/v1/warehouse-receipts/orders/{orderId}/completion` (Bước 6) để hệ thống chạy lại bộ quy tắc kiểm tra.
