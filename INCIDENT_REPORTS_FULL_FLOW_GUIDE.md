# Hướng dẫn full flow IncidentReports

Tài liệu này mô tả luồng kiểm thử từ lúc tài xế báo cáo sự cố, Admin/Manager ghi nhận khoản kế toán đã chuyển lại và resolve sự cố, hệ thống sinh PDF trên Cloudinary, đến lúc truy xuất lại `FileUrl` bằng API GET. Hiện không có endpoint hoặc role `Accountant` riêng cho bước resolve.

## 1. Phạm vi thay đổi

### Trường dữ liệu mới

| Trường JSON | Kiểu | Thời điểm nhập | Ý nghĩa |
|---|---:|---|---|
| `driverPaidAmount` | decimal | Khi tạo incident | Số tiền ban đầu tài xế tự chi trả |
| `reimbursedAmount` | decimal | Khi resolve incident | Số tiền kế toán đã chuyển lại |

Trong database:

- `incident_reports.driver_paid_amount`: `numeric(15,2)`, không null, mặc định `0`.
- `incident_reports.reimbursed_amount`: `numeric(15,2)`, nullable cho đến khi incident được resolve.

### Dropdown trên Swagger

`incidentType` nhận một trong các giá trị:

- `ACCIDENT`
- `VEHICLE_BREAKDOWN`
- `TEMP_EXCURSION`
- `DAMAGE_CARGO`
- `DELAY`

`severity` nhận một trong các giá trị:

- `LOW`
- `MEDIUM`
- `HIGH`
- `CRITICAL`

Hai trường này là enum string nên Swagger hiển thị dropdown. Contract API và các ví dụ bên dưới dùng đúng tên enum string; client nên luôn gửi tên enum thay vì số.

## 2. Điều kiện trước khi test

### Database

Migration cần áp dụng:

```text
20260716081148_AddIncidentFinancialsAndResolutionEvidence
```

Migration mới chỉ thêm hai cột tiền. Bảng `public."IncidentEvidences"` đã được tạo từ migration `20260706074457_RemoveWarehouseLocationAndZone3`, vì vậy database mới phải chạy đầy đủ chuỗi migration, không chỉ chạy riêng hai câu SQL thêm cột.

Trong môi trường Development, nếu tự động migrate đang tắt, có thể cập nhật database bằng PowerShell:

```powershell
$env:ConnectionStrings__LocalConnection = "<POSTGRES_CONNECTION_STRING>"
dotnet ef database update `
  --project ColdChainX.Infrastructure `
  --startup-project ColdChainX.Infrastructure `
  --context ApplicationDbContext
```

Không ghi connection string thật vào source code hoặc commit lên Git.

### Cloudinary và trình tạo PDF

Cấu hình một trong hai cách:

```text
CLOUDINARY_URL=cloudinary://<API_KEY>:<API_SECRET>@<CLOUD_NAME>
```

hoặc:

```text
Cloudinary__CloudName=<CLOUD_NAME>
Cloudinary__ApiKey=<API_KEY>
Cloudinary__ApiSecret=<API_SECRET>
```

`IFileService` được khởi tạo cùng `IncidentReportService`; vì vậy thiếu cấu hình Cloudinary có thể làm cả controller IncidentReports không khởi tạo được, kể cả khi chỉ gọi GET.

Nếu môi trường không tự tìm được Chrome/Chromium cho Puppeteer:

```text
PDF_CHROME_EXECUTABLE_PATH=<đường_dẫn_chrome_hoặc_chromium>
```

Dockerfile của dự án đã cấu hình Chromium tại `/usr/bin/chromium`.

### Chạy API và đăng nhập

Chạy API:

```powershell
dotnet run --project ColdChainX.API
```

Swagger mặc định:

```text
http://localhost:5244/swagger
```

Đăng nhập:

```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "<EMAIL>",
  "password": "<PASSWORD>"
}
```

Copy `data.accessToken`, bấm **Authorize** trong Swagger và dán token (không thêm chữ `Bearer`, vì Swagger tự thêm scheme):

```text
<ACCESS_TOKEN>
```

Quyền sử dụng:

- Tạo incident: `Admin`, `Manager`, `Driver`, hoặc `WarehouseOperator`.
- Resolve incident: `Admin` hoặc `Manager`.
- GET incident: người dùng đã đăng nhập.

Để test trọn luồng qua Swagger, có thể dùng token Driver để tạo incident, sau đó đăng nhập lại bằng tài khoản Admin/Manager để resolve.

## 3. Bước 1 — Báo cáo incident

API:

```http
POST /api/v1/incidents
```

Request mẫu:

```json
{
  "tripId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "incidentType": "VEHICLE_BREAKDOWN",
  "severity": "HIGH",
  "description": "Hệ thống làm lạnh dừng hoạt động giữa chuyến.",
  "currentLatitude": 10.762622,
  "currentLongitude": 106.660172,
  "driverPaidAmount": 1500000
}
```

Lưu ý:

- `tripId` có thể bỏ qua nếu incident không thuộc một chuyến cụ thể.
- Nếu có `tripId`, ID phải tồn tại trong `master_trips`.
- `driverPaidAmount` phải lớn hơn hoặc bằng `0`.
- Client nên luôn gửi rõ `driverPaidAmount`; nếu bỏ field, model binding hiện gán mặc định `0`.
- Latitude hợp lệ từ `-90` đến `90`; longitude từ `-180` đến `180`.

Response thành công cần có các dữ liệu chính:

```json
{
  "success": true,
  "statusCode": 200,
  "message": "Incident reported successfully.",
  "data": {
    "incidentId": "<INCIDENT_ID>",
    "tripId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "incidentType": "VEHICLE_BREAKDOWN",
    "severity": "HIGH",
    "description": "Hệ thống làm lạnh dừng hoạt động giữa chuyến.",
    "driverPaidAmount": 1500000,
    "reimbursedAmount": null,
    "status": "REPORTED",
    "resolutionNote": null,
    "evidences": []
  }
}
```

Lưu lại `data.incidentId` cho các bước tiếp theo.

## 4. Bước 2 — Resolve và ghi nhận hoàn tiền

API:

```http
POST /api/v1/incidents/{id}/resolve
```

Request mẫu:

```json
{
  "resolutionNote": "Đã sửa hệ thống làm lạnh, kiểm tra nhiệt độ an toàn và cho xe tiếp tục hành trình.",
  "reimbursedAmount": 1500000
}
```

`reimbursedAmount` phải lớn hơn hoặc bằng `0` và client nên luôn gửi rõ field này. Rule hiện tại không chặn `reimbursedAmount` lớn hơn `driverPaidAmount`; nếu nghiệp vụ cần giới hạn đó thì phải bổ sung rule riêng.

Response thành công:

```json
{
  "success": true,
  "statusCode": 200,
  "message": "Incident resolved successfully.",
  "data": true
}
```

Khi API này thành công, hệ thống đã thực hiện đầy đủ:

1. Sinh PDF từ template `IncidentResolution.html`.
2. Upload PDF với tên gốc dạng `incident_resolution_{incidentId:N}.pdf`, trong đó GUID có 32 ký tự hex và không có dấu `-`. `FileService` còn thêm một suffix GUID vào Cloudinary public ID, nên URL cuối không nhất thiết giữ nguyên chính xác tên gốc.
3. Cập nhật incident thành `RESOLVED`.
4. Lưu `reimbursedAmount` và `resolvedAt`.
5. Tạo một `IncidentEvidence` có `evidenceType = "RESOLUTION_PDF"` và `fileUrl` là URL Cloudinary.
6. Lưu incident và evidence trong cùng một lần `SaveChanges`.

Nếu sinh PDF hoặc upload Cloudinary lỗi, API trả thất bại và incident không bị lưu thành `RESOLVED`.

Một incident đã `RESOLVED` không thể resolve lần hai; nhờ vậy hệ thống không sinh evidence trùng trong luồng thông thường.

## 5. Bước 3 — GET chi tiết và kiểm tra FileUrl

API:

```http
GET /api/v1/incidents/{id}
```

Các trường cần kiểm tra:

```json
{
  "success": true,
  "data": {
    "incidentId": "<INCIDENT_ID>",
    "incidentType": "VEHICLE_BREAKDOWN",
    "severity": "HIGH",
    "driverPaidAmount": 1500000,
    "reimbursedAmount": 1500000,
    "status": "RESOLVED",
    "resolutionNote": "Đã sửa hệ thống làm lạnh, kiểm tra nhiệt độ an toàn và cho xe tiếp tục hành trình.",
    "evidences": [
      {
        "evidenceId": "<EVIDENCE_ID>",
        "evidenceType": "RESOLUTION_PDF",
        "fileUrl": "https://res.cloudinary.com/.../incident_resolution_....pdf"
      }
    ]
  }
}
```

Mở trực tiếp `data.evidences[0].fileUrl` để kiểm tra PDF. PDF phải hiển thị:

- Mã incident và trip.
- Loại và mức độ sự cố.
- Nội dung báo cáo và ghi chú giải quyết.
- Người báo cáo, người xác nhận resolve, thời gian báo cáo/resolve.
- Số tiền tài xế đã tự chi.
- Số tiền kế toán đã hoàn lại.

## 6. Bước 4 — GET danh sách và kiểm tra FileUrl

API:

```http
GET /api/v1/incidents?tripId=<TRIP_ID>&pageNumber=1&pageSize=10
```

`tripId` là tùy chọn. Response phân trang đặt danh sách incident tại `data.data`:

```json
{
  "success": true,
  "data": {
    "totalRecords": 1,
    "totalPages": 1,
    "currentPage": 1,
    "pageSize": 10,
    "data": [
      {
        "incidentId": "<INCIDENT_ID>",
        "driverPaidAmount": 1500000,
        "reimbursedAmount": 1500000,
        "status": "RESOLVED",
        "evidences": [
          {
            "evidenceId": "<EVIDENCE_ID>",
            "evidenceType": "RESOLUTION_PDF",
            "fileUrl": "https://res.cloudinary.com/.../incident_resolution_....pdf"
          }
        ]
      }
    ]
  }
}
```

## 7. Kiểm tra trực tiếp database

Kiểm tra hai trường tiền:

```sql
SELECT
    incident_id,
    status,
    driver_paid_amount,
    reimbursed_amount,
    resolved_at
FROM public.incident_reports
WHERE incident_id = '<INCIDENT_ID>';
```

Kiểm tra evidence. Bảng hiện được EF migration tạo với tên có phân biệt hoa/thường:

```sql
SELECT
    "EvidenceId",
    "IncidentId",
    "EvidenceType",
    "FileUrl"
FROM public."IncidentEvidences"
WHERE "IncidentId" = '<INCIDENT_ID>';
```

Kết quả mong đợi:

- `status = 'RESOLVED'`.
- Hai trường tiền đúng với request.
- Có đúng một evidence loại `RESOLUTION_PDF`.
- `FileUrl` không rỗng và trỏ đến Cloudinary.

## 8. Test tự động đã chạy

Test full flow nằm trong:

```text
ColdChainX.UnitTests/IncidentAndClaimServiceTests.cs
```

Case `IncidentFullFlow_ReportResolveAndRetrieveEvidence` kiểm tra liên tục:

```text
Report → Resolve → tạo PDF → upload fake Cloudinary → lưu IncidentEvidence
       → GET by id → GET list → kiểm tra FileUrl
```

Lệnh test đã dùng trong trạng thái repository hiện tại:

```powershell
dotnet test ColdChainX.UnitTests/ColdChainX.UnitTests.csproj `
  --no-restore `
  --filter "FullyQualifiedName~IncidentAndClaimServiceTests" `
  -p:DefaultItemExcludesInProjectFolder="**/ManualDispatchFlowTests.cs"
```

Kết quả ngày 16/07/2026:

```text
Passed: 7
Failed: 0
Skipped: 0
```

Trong unit test, PDF generator và Cloudinary được fake để test ổn định, không tải Chromium và không tạo file rác trên tài khoản Cloudinary thật. Bước kiểm tra Cloudinary thật cần thực hiện bằng Swagger theo mục 2–6.

Ngoài unit test, template `IncidentResolution.html` đã được render bằng `PdfGeneratorService` với Chrome cục bộ. Kết quả là payload PDF hợp lệ (`%PDF`) có kích thước `73.850` byte.

Migration SQL đã được kiểm tra và chỉ tạo hai cột:

```sql
ALTER TABLE public.incident_reports
ADD driver_paid_amount numeric(15,2) NOT NULL DEFAULT 0.0;

ALTER TABLE public.incident_reports
ADD reimbursed_amount numeric(15,2);
```

Template PDF cũng đã được xác nhận có mặt trong output build tại `Templates/IncidentResolution.html`.

## 9. Lỗi thường gặp

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| Swagger không có dropdown | API đang chạy bản build cũ | Stop API, rebuild và refresh cứng trang Swagger |
| `400 Incident type is invalid` | Gửi giá trị ngoài enum | Chọn đúng giá trị trong dropdown |
| `400 Trip not found` | `tripId` không tồn tại | Dùng trip thật hoặc bỏ `tripId` |
| `403` khi resolve | Token không có role Admin/Manager | Đăng nhập bằng tài khoản đúng role |
| `Incident is already resolved` | Resolve cùng incident lần hai | Dùng incident mới hoặc chỉ gọi GET |
| Controller IncidentReports lỗi khởi tạo hoặc báo thiếu Cloudinary | Thiếu biến môi trường `Cloudinary:CloudName/ApiKey/ApiSecret` | Cấu hình `CLOUDINARY_URL` hoặc ba khóa Cloudinary trước khi gọi cả POST/GET |
| Lỗi tải/chạy Chromium | Puppeteer không tìm thấy browser | Cấu hình `PDF_CHROME_EXECUTABLE_PATH` |
| Resolve trả lỗi upload | Sai Cloudinary credential hoặc mất mạng | Sửa cấu hình rồi gọi resolve lại; incident vẫn là `REPORTED` |
| GET không có evidence | Resolve chưa thành công hoặc DB chưa cập nhật đúng | Kiểm tra status, migration và bảng `IncidentEvidences` |
| Số tiền âm bị từ chối | Validation yêu cầu amount ≥ 0 | Gửi `0` nếu không phát sinh tiền |

## 10. Lưu ý về full solution build hiện tại

Các project Incident/Application/Infrastructure và API khi cô lập `DispatchController` đã build thành công. Tuy nhiên, lệnh `dotnet build ColdChainX.sln` hiện vẫn bị chặn bởi hai tham chiếu cũ ngoài phạm vi Incident:

- `ColdChainX.API/Controllers/DispatchController.cs`: còn dùng `RouteSchedule.DayOfWeek`.
- `ColdChainX.UnitTests/ManualDispatchFlowTests.cs`: còn gán `RouteSchedule.DayOfWeek`.

Entity `RouteSchedule` hiện đã đổi sang `DepartureDate`. Cần cập nhật riêng luồng dispatch/manual-dispatch trước khi full solution có thể build và chạy toàn bộ test mà không dùng bộ lọc ở trên.
