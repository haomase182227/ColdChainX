# Incident Report — Full operational flow

Tài liệu này mô tả contract backend cho mobile Driver, Dispatcher, Admin/Manager,
rescue vehicle, MQTT và telemetry.

## 1. State machine

### Không cần đổi xe

```text
REPORTED
  -> Dispatcher xử lý tại chỗ
CONTINUED
  -> duyệt/hoàn chi phí nếu có
RESOLVED
```

### Cần đổi xe

```text
REPORTED
  -> rescue-candidates
  -> dispatch-rescue
RESCUE_DISPATCHED + Trip DELAYED
  -> người phụ trách xác nhận sang hàng
  -> kiểm tra toàn bộ IoT xe mới online
  -> publish MQTT START_STREAMING thành công
TRANSLOAD_COMPLETED + Trip IN_TRANSIT
  -> duyệt/hoàn chi phí nếu có
RESOLVED
```

Expense state độc lập:

```text
NOT_REQUIRED

hoặc

PENDING_APPROVAL -> APPROVED -> REIMBURSED
```

Incident có `driverPaidAmount > 0` chỉ được đóng sau khi expense đạt
`REIMBURSED`.

## 2. Driver báo sự cố

### JSON, không gửi file trong cùng request

```http
POST /api/v1/incidents
Content-Type: application/json
Authorization: Bearer <DRIVER_TOKEN>
```

```json
{
  "tripId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "incidentType": "VEHICLE_BREAKDOWN",
  "severity": "HIGH",
  "requiresRescue": true,
  "description": "Xe mất khả năng vận hành.",
  "currentLatitude": 10.762622,
  "currentLongitude": 106.660172,
  "driverPaidAmount": 1500000
}
```

### Multipart cho mobile, có ảnh/hóa đơn

```http
POST /api/v1/incidents/with-evidence
Content-Type: multipart/form-data
Authorization: Bearer <DRIVER_TOKEN>
```

Form fields:

```text
tripId
incidentType
severity
requiresRescue
description
currentLatitude
currentLongitude
driverPaidAmount
evidenceFiles (0..5 files, image hoặc PDF, tối đa 10 MB/file)
```

Backend kiểm tra Driver được gán vào Trip và bắt buộc Driver gửi vị trí.
Sau khi lưu thành công, Dispatcher và Admin nhận SignalR event:

```text
IncidentReported
```

Đồng thời hệ thống tạo notification lưu trong database để người nhận vẫn thấy
khi đang offline.

### Upload evidence bổ sung

```http
POST /api/v1/incidents/{incidentId}/evidences
Content-Type: multipart/form-data
```

```text
evidenceType = INCIDENT_ATTACHMENT | INCIDENT_PHOTO | DRIVER_RECEIPT
files = 1..5 image/PDF
```

## 3. Nhánh không cần đổi xe

```http
POST /api/v1/incidents/{incidentId}/continue-trip
Authorization: Dispatcher | Admin | Manager
Content-Type: application/json
```

```json
{
  "handlingNote": "Đã xử lý lỗi điện, kiểm tra nhiệt độ và cho xe tiếp tục."
}
```

Điều kiện:

- `requiresRescue = false`.
- Incident chưa `RESOLVED`.
- Trip còn trong trạng thái vận hành.

Kết quả:

- Incident -> `CONTINUED`.
- Trip -> `IN_TRANSIT`.
- `Trip.VehicleId` giữ nguyên.
- Lưu người xử lý, thời gian và ghi chú.
- SignalR event `IncidentTripContinued`.

## 4. Nhánh cần đổi xe

### 4.1 Danh sách xe cứu hộ

```http
GET /api/v1/incidents/{incidentId}/rescue-candidates
Authorization: Dispatcher | Admin | Manager
```

Backend chỉ trả xe:

- `Vehicle.Status = ACTIVE`.
- Khác xe hiện tại của Trip.
- Chứa được tổng `ActualWeightKg` của toàn bộ LPN `SHIPPING`.
- Chứa được tổng `ActualCbm` của toàn bộ LPN `SHIPPING`.
- Dải nhiệt bao phủ `Trip.TargetTemperature`.
- Có ít nhất một `IotDevice` với `DeviceCode`.

Response còn trả:

```text
iotDeviceCount
onlineIotDeviceCount
hasOnlineIot
```

Nếu không có xe đáp ứng toàn bộ tải:

```text
Không có xe thay thế phù hợp
```

Phiên bản hiện tại không chia tải, không tạo nhiều xe và không tạo Trip mới.

### 4.2 Phát lệnh cứu hộ

```http
POST /api/v1/incidents/{incidentId}/dispatch-rescue
Authorization: Dispatcher | Admin | Manager
Content-Type: application/json
```

```json
{
  "replacementVehicleId": "9578709d-088b-4958-9719-725d25e61fd3",
  "transloadMinutes": 45,
  "note": "Sang toàn bộ LPN tại hiện trường."
}
```

Backend kiểm tra lại toàn bộ điều kiện của candidate trong transaction, sau đó:

- Xe hỏng -> `MAINTENANCE`.
- Tạo `MaintenanceTicket` -> `OPEN`.
- Lưu `BrokenVehicleId`, `ReplacementVehicleId`, `MaintenanceTicketId`.
- `Trip.VehicleId` -> xe mới.
- Xe mới -> `ONTRIP`.
- Trip -> `DELAYED`.
- Incident -> `RESCUE_DISPATCHED`.
- Giữ nguyên `Lpn.TripId`.
- Giữ nguyên `TransportOrder.MasterTripId`.
- Không tạo Trip mới.
- Tính lại ETA và báo khách hàng phía trước.
- SignalR event `IncidentRescueDispatched`.

### 4.3 Xác nhận đã sang hàng

```http
POST /api/v1/incidents/{incidentId}/confirm-transload
Authorization: Dispatcher | Admin | Manager | WarehouseOperator | Loader
Content-Type: application/json
```

```json
{
  "confirmationNote": "Đã sang đủ toàn bộ LPN và kiểm tra cửa thùng."
}
```

Backend chỉ cho tiếp tục nếu:

- Incident đang `RESCUE_DISPATCHED`.
- Trip đang `DELAYED`.
- `Trip.VehicleId` đúng bằng xe cứu hộ đã lưu.
- Xe mới có thiết bị IoT riêng.
- Tất cả thiết bị có `DeviceCode` đều `IsOnline = true`.
- MQTT `START_STREAMING` publish thành công cho từng thiết bị.

Kết quả:

- Incident -> `TRANSLOAD_COMPLETED`.
- Trip -> `IN_TRANSIT`.
- Lưu người xác nhận, thời gian và ghi chú.
- SignalR `IncidentTransloadCompleted` cho nhân sự.
- SignalR `TripResumed` cho khách hàng.

Nếu IoT offline hoặc MQTT publish lỗi, Trip vẫn giữ `DELAYED`.

## 5. Duyệt và hoàn chi phí

### 5.1 Admin duyệt

```http
POST /api/v1/incidents/{incidentId}/expenses/approve
Authorization: Admin
```

```json
{
  "approvedAmount": 1500000,
  "approvalNote": "Hóa đơn hợp lệ."
}
```

`approvedAmount` phải lớn hơn `0` và không vượt `driverPaidAmount`.

### 5.2 Admin hoàn tiền và gửi biên lai

```http
POST /api/v1/incidents/{incidentId}/expenses/reimburse
Authorization: Admin
Content-Type: multipart/form-data
```

```text
reimbursedAmount = 1500000
note = Đã chuyển khoản cho tài xế
receiptFile = image hoặc PDF
```

Điều kiện:

- Expense đã `APPROVED`.
- `reimbursedAmount` bằng đúng `approvedAmount`.
- Có `receiptFile`.

Kết quả:

- Expense -> `REIMBURSED`.
- Lưu `ReimbursedBy`, `ReimbursedAt`.
- Tạo evidence `REIMBURSEMENT_RECEIPT`.
- Tạo notification bền vững cho Driver.
- SignalR `IncidentExpenseReimbursed` chứa URL biên lai.

## 6. Đóng incident

```http
POST /api/v1/incidents/{incidentId}/resolve
Authorization: Admin | Manager
```

```json
{
  "resolutionNote": "Đã hoàn tất xử lý sự cố và đối soát chi phí."
}
```

Điều kiện:

- Không đổi xe: incident đã `CONTINUED`.
- Có đổi xe: incident đã `TRANSLOAD_COMPLETED`.
- Nếu Driver có ứng tiền: expense đã `REIMBURSED`.

Kết quả:

- Sinh và upload `RESOLUTION_PDF`.
- Incident -> `RESOLVED`.
- Lưu `ResolvedBy`, `ResolvedAt`, `ResolutionNote`.
- Gửi notification và SignalR `IncidentResolved` cho Driver.

## 7. Telemetry không đổi lõi

Trước sự cố:

```text
Device xe cũ -> Vehicle cũ -> Trip hiện tại
```

Sau sự cố:

```text
Device xe mới -> Vehicle mới -> cùng Trip hiện tại
```

Rescue flow không cập nhật `IotDevice.VehicleId`. Nó chỉ cập nhật
`MasterTrip.VehicleId`. `ColdChainMonitoringService` tìm Trip hiện tại qua
Vehicle của thiết bị và ghi `TelemetryLog.TripId` bằng Trip cũ.

Các API chart tiếp tục lấy dữ liệu theo `TripId`:

```http
GET /api/trip/{tripId}/chart
GET /api/trip/{tripId}/chart/temperature
GET /api/trip/{tripId}/chart/route
GET /api/trip/{tripId}/chart/route-goong
```

Vì vậy lịch sử trước sự cố đến từ device xe cũ, lịch sử sau sự cố đến từ
device xe mới, nhưng bản đồ vẫn là một hành trình.

## 8. Migration và test

Migration:

```text
20260723120000_CompleteIncidentWorkflow
```

Test chính:

```text
IncidentAndClaimServiceTests
IncidentRescueFlowTests
```

Các test rescue xác nhận:

- Candidate bắt buộc ACTIVE, đủ kg/CBM, đúng nhiệt độ, có IoT.
- Không có xe trả đúng thông báo.
- Xe hỏng/ticket/Trip được cập nhật đúng.
- LPN và Order giữ nguyên TripId.
- Không tạo Trip mới.
- Không chuyển device giữa xe.
- IoT offline không cho Trip chạy.
- MQTT thành công rồi mới chuyển `IN_TRANSIT`.
