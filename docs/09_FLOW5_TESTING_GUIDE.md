# 09 — Hướng Dẫn Kiểm Thử Flow 5: Giao Hàng & Quyết Toán COD

Tài liệu này hướng dẫn chi tiết cách kiểm thử thủ công và tự động cho **Flow 5: Giao nhận hàng (ePOD) & Quyết toán COD** trên môi trường Local thông qua Swagger UI.

---

## 📊 THÔNG TIN SEED DATA (DÙNG ĐỂ COPY-PASTE)
* **Trip ID (Chuyến đi):** `77777777-7777-7777-7777-777777777777` (Trạng thái ban đầu: `DISPATCHED`)
* **Stop ID (Điểm dừng):** `66666666-6666-6666-6666-666666666666` (Trạng thái ban đầu: `PLANNED`, tọa độ GPS: `10.8465, 106.8042`)
* **Order ID (Đơn hàng):** `56c5cf5b-e403-4ab7-bc8e-b686dea7675b` (Trạng thái ban đầu: `SHIPPING`, giá trị COD: `2000.00`)
* **LPN 1 ID (Nhận hàng):** `88888888-8888-8888-8888-888888888888`
* **LPN 2 ID (Trả hàng):** `99999999-9999-9999-9999-999999999999`

---

## ☀️ KỊCH BẢN HAPPY CASE (GIAO HÀNG THÀNH CÔNG MỘT PHẦN)

Môi trường Local Swagger: **[http://localhost:5244/swagger/index.html](http://localhost:5244/swagger/index.html)**

### 🔑 BƯỚC 0: Đăng Nhập Tài Xế (Driver)
* **API:** `POST /api/auth/login`
* **Body:**
  ```json
  {
    "email": "driver01@coldchainx.com",
    "password": "Password@123"
  }
  ```
* **Thao tác:** Nhấn **Execute**, copy chuỗi `"accessToken"`. Chọn **Authorize** ở đầu trang Swagger, nhập `Bearer <token>` rồi chọn **Authorize**.

---

### 📍 BƯỚC 1: Check-in Tại Điểm Dừng
* **API:** `POST /api/stops/{stopId}/check-ins`
* **Tham số:**
  * `stopId` (path): `66666666-6666-6666-6666-666666666666`
  * Body:
    ```json
    {
      "latitude": 10.8465,
      "longitude": 106.8042
    }
    ```
* **Hành động hệ thống:** Chuyển trạng thái điểm dừng sang `ARRIVED`, ghi nhận thời gian đến thực tế, và cắt chì niêm phong cũ (`SEAL-START-01` $\rightarrow$ `REMOVED`).

---

### 📦 BƯỚC 2: Bàn Giao Hàng & Ký Nhận ePOD (Giao Một Phần)
Khách hàng nhận **LPN 1** và từ chối **LPN 2** (do bị móp rách vỏ hộp).
* **API:** `POST /api/stops/{stopId}/handover-confirmations` (Dạng `multipart/form-data`)
* **Tham số:**
  * `stopId` (path): `66666666-6666-6666-6666-666666666666`
  * `OrderId`: `56c5cf5b-e403-4ab7-bc8e-b686dea7675b`
  * `ReceiverName`: `Nguyen Van A`
  * `ReceiverPhone`: `0901234567`
  * `DeliveryRating`: `5`
  * `SignatureFile`: Chọn một file ảnh chữ ký bất kỳ từ máy tính.
  * `Lpns` (Mảng nhập JSON): Copy và dán đoạn mảng JSON sau:
    ```json
    [
      {
        "lpnId": "88888888-8888-8888-8888-888888888888",
        "isAccepted": true
      },
      {
        "lpnId": "99999999-9999-9999-9999-999999999999",
        "isAccepted": false,
        "rejectionReason": "DAMAGED",
        "rejectionNotes": "Thung hang mop vo"
      }
    ]
    ```
  * `EvidencePhotos` (Bằng chứng ảnh cho LPN bị từ chối): Nhấn **Add item**, một nút chọn File sẽ hiện ra $\rightarrow$ Chọn một file ảnh bất kỳ từ máy tính (ảnh này sẽ tự động gắn cho LPN 2 bị từ chối).
* **Hành động hệ thống:**
  * LPN 1 chuyển sang `DELIVERED`, LPN 2 chuyển sang `RETURN_PENDING`.
  * Tạo bản ghi trả hàng trong `returned_items` để đối soát kho.
  * Tạo ePOD tạm thời ở trạng thái `PENDING` với số tiền COD tự động tính theo tỉ lệ hàng nhận (`20.00` VND).
  * Trả về **`epodId`** và link PDF biên bản bàn giao.

---

### 💰 BƯỚC 3: Yêu Cầu Thanh Toán COD qua QR PayOS
* **API:** `POST /api/epods/{epodId}/payments`
* **Tham số:**
  * `epodId` (path): Điền mã `epodId` nhận được từ **Bước 2**.
  * Form data:
    * `PaymentMethod`: `QR`
    * `CodAmountPaid`: `2000`
* **Hành động hệ thống:** Tạo link thanh toán QR trên PayOS và chuyển trạng thái thanh toán ePOD thành `AWAITING_QR`. Trả về `checkoutUrl` để khách hàng quét.

---

### 🏦 BƯỚC 3.1: Giả Lập PayOS Xác Nhận Đã Nhận Tiền (Không cần Ngrok)
* **API:** `POST /api/payments/bank-webhook`
* **Body:**
  ```json
  {
    "orderCode": <Mã_Order_Code_Trả_Về_Ở_Bước_3>,
    "amount": 2000,
    "transactionId": "FT-QR-99999999",
    "status": "PAID"
  }
  ```
* **Hành động hệ thống:** Ghi nhận tiền thành công, chuyển trạng thái ePOD thành `PAID` và trạng thái đơn hàng thành `PARTIALLY_DELIVERED`, xuất hóa đơn ePOD PDF hoàn chỉnh và bắn tín hiệu qua SignalR.

---

### 🚚 BƯỚC 4: Rời Điểm Giao Hàng & Kẹp Chì Mới
* **API:** `POST /api/stops/{stopId}/departures`
* **Tham số:**
  * `stopId` (path): `66666666-6666-6666-6666-666666666666`
  * Body:
    ```json
    {
      "newSealCode": "SEAL-DEPART-99"
    }
    ```
* **Hành động hệ thống:** Chuyển trạng thái điểm dừng thành `DEPARTED`, kẹp chì mới `SEAL-DEPART-99` ở trạng thái `APPLIED`. Vì đây là điểm dừng cuối, chuyến xe `Trip` tự động chuyển thành `COMPLETED`, giải phóng xe và tài xế về trạng thái `AVAILABLE`.

---

### 💼 BƯỚC 5: Quyết Toán COD Cuối Ca (Admin/Kế Toán)
1. Đăng nhập tài khoản Admin: `POST /api/auth/login` với email `admin01@coldchainx.com` / `Password@123` $\rightarrow$ Đổi sang Token Admin trên Swagger.
2. **API:** `POST /api/trips/{tripId}/cod-handovers`
3. **Tham số:**
   * `tripId` (path): `77777777-7777-7777-7777-777777777777`
   * Body:
     ```json
     {
       "actualCashReceived": 0,
       "actualQrReceived": 2000,
       "note": "Reconciled QR payment"
     }
     ```
4. **Hành động hệ thống:** Đối soát số tiền thực nhận với hệ thống, cập nhật tất cả ePOD thuộc chuyến đi thành `COD_SETTLED` và ghi nhận chênh lệch (discrepancy) nếu có.

---

## 🌧️ CÁC KỊCH BẢN LỖI (SAD CASES) & CÁCH GIẢI QUYẾT

Dưới đây là các tình huống lỗi thường gặp khi kiểm thử và cách hệ thống xử lý (được định nghĩa chặt chẽ để đảm bảo tính an toàn dữ liệu):

### 🚨 Lỗi 1: Check-in quá xa địa điểm dừng (> 200m)
* **Tình huống:** Tài xế cố tình check-in ở tọa độ sai lệch lớn so với địa điểm giao (ví dụ nhập Lat `11.0`, Lon `107.0`).
* **Hệ thống xử lý:** API trả về lỗi **`400 Bad Request`** kèm thông báo khoảng cách thực tế và giới hạn cho phép (200m). Không cho phép thực hiện bất cứ bước giao nhận nào tiếp theo.
* **Cách giải quyết khi test:** Nhập đúng tọa độ điểm dừng đã seed trong DB: Lat `10.8465`, Lon `106.8042`.

### 🚨 Lỗi 2: Bàn giao hàng trước khi Check-in
* **Tình huống:** Tài xế chưa check-in điểm dừng nhưng đã gọi API `/handover-confirmations`.
* **Hệ thống xử lý:** Trả về lỗi **`400 Bad Request`** với thông báo `"Driver must check in at this stop first"`.
* **Cách giải quyết:** Thực hiện gọi API Check-in (Bước 1) thành công trước khi gọi API Bàn giao (Bước 2).

### 🚨 Lỗi 3: Từ chối LPN nhưng thiếu ảnh bằng chứng hỏng hóc
* **Tình huống:** Trong mảng JSON bạn gửi LPN 2 có `isAccepted = false` nhưng bạn không thêm file nào vào danh sách `EvidencePhotos`.
* **Hệ thống xử lý:** Trả về lỗi **`400 Bad Request`** báo lỗi xác thực: `"Evidence photo is required when LPN is rejected."`
* **Cách giải quyết:** Nhấn nút **Add item** dưới trường `EvidencePhotos` và chọn ít nhất một file ảnh bằng chứng.

### 🚨 Lỗi 4: Gửi trùng lặp yêu cầu Giao nhận (Double-submit)
* **Tình huống:** Tài xế đã giao nhận thành công đơn hàng (ePOD đã được tạo), nhưng do mạng lag hoặc cố tình gửi lại request Giao nhận lần 2.
* **Hệ thống xử lý:** Trả về lỗi **`409 Conflict`** kèm thông tin ePOD đã xác nhận trước đó và thời gian cụ thể. Không ghi đè hoặc tạo trùng lặp dữ liệu.
* **Cách giải quyết:** Sử dụng script `reset` để khôi phục dữ liệu sạch trước khi test lại luồng.

### 🚨 Lỗi 5: Tài xế cố tình rời điểm dừng khi chưa giao nhận xong hàng
* **Tình huống:** Tài xế chưa gọi API giao nhận `/handover-confirmations` hoặc chưa thu tiền COD nhưng đã gọi API rời đi `/departures`.
* **Hệ thống xử lý:** Trả về lỗi **`400 Bad Request`** ngăn chặn tài xế rời đi, bảo vệ quy trình nghiệp vụ không bị bỏ bước.
* **Cách giải quyết:** Hoàn thành nghiệm thu và thanh toán trước khi gọi lệnh rời đi.

### 🚨 Lỗi 6: Người khác cố tình thực hiện giao hàng cho Chuyến xe
* **Tình huống:** Tài xế đăng nhập hợp lệ nhưng tài khoản không phải là người được phân công lái chuyến xe `77777777-...`.
* **Hệ thống xử lý:** Trả về lỗi **`403 Forbidden`** `"You are not authorized to confirm handover for this trip."`.
* **Cách giải quyết:** Sử dụng đúng tài khoản tài xế đã được phân công (`driver01@coldchainx.com`) để lấy token test.
