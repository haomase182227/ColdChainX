TÀI LIỆU VẬN HÀNH & API: QUY TRÌNH XỬ LÝ ĐƠN HÀNG CHÊNH LỆCH (DISCREPANCY_HOLD)

Tài liệu này tổng hợp toàn bộ các API, các bảng cơ sở dữ liệu bị tác động, và luồng trạng thái khi đơn hàng bị giữ lại do phát hiện chênh lệch thông số QC khi nhập kho (Discrepancy Hold).

==================================================

1. KHỞI ĐẦU QUY TRÌNH: PHÁT HIỆN CHÊNH LỆCH QC (>5%)

Khi hàng đến kho và nhân viên QC tiến hành đo đạc kích thước/trọng lượng thực tế, nếu sai số vượt quá 5% so với khai báo ban đầu của khách hàng:

- API kích hoạt: POST /api/Inbound/qc
- Tham số chính: AsnId, ActualWeightKg, LengthCm, WidthCm, HeightCm, Temperature.
- Logic xử lý:
  + Tính toán chênh lệch trọng lượng thực tế và thể tích (CBM) thực tế.
  + Nếu chênh lệch > 5%, hệ thống chuyển trạng thái đơn hàng sang giữ lại để đối chiếu giá cước.

Thay đổi cơ sở dữ liệu:
- public.transport_orders: Cột status cập nhật thành "DISCREPANCY_HOLD".
- public.lpns: Cột state chuyển sang 2 (tương ứng enum LpnState.DISCREPANCY_HOLD).
- public.contract_appendices: Tự động tạo bản nháp phụ lục điều chỉnh cước mới:
  + Trạng thái status = "DRAFT".
  + Tính toán lại giá cước chênh lệch (adjusted_price) dựa trên biểu giá lũy tiến (weight_tiers).
  + Kết xuất file HTML thô vào trường draft_html_content.
- public.notifications: Tạo thông báo mã NOTI_QC_DISCREPANCY gửi tới Khách hàng kèm link PDF biên bản bất thường và ID phụ lục nháp.

==================================================

2. GIAI ĐOẠN THƯƠNG LƯỢNG & CẬP NHẬT PHỤ LỤC

Nhân viên Sales tiến hành xem xét, chỉnh sửa bản nháp phụ lục và gửi tới khách hàng duyệt ký.

API 2.1: Xem trước phụ lục hợp đồng (Preview)
- API: GET /api/contracts/appendices/preview
- Query params: orderId, adjustedPrice, reason.
- Mô tả: Trả về mã HTML render động của phụ lục để xem trước trên giao diện mà không lưu vào DB.

API 2.2: Cập nhật nội dung phụ lục nháp (Edit)
- API: PUT /api/contracts/appendices/{appendixId}
- Body: Chuỗi HTML đã được chỉnh sửa.
- Mô tả: Chỉ cho phép gọi khi phụ lục có trạng thái là "DRAFT".
- Thay đổi DB: Cập nhật cột draft_html_content trong bảng public.contract_appendices.

API 2.3: Gửi phụ lục cho khách hàng (Send)
- API: POST /api/contracts/appendices/{appendixId}/send
- Mô tả: Đóng băng bản nháp, tạo PDF chính thức và gửi yêu cầu duyệt ký cho khách.
- Thay đổi DB:
  + public.contract_appendices: Cột status chuyển thành "SENT", lưu đường dẫn PDF đã tạo vào cột pdf_url, cập nhật thời gian gửi sent_at.
  + public.notifications:
    1. Xóa toàn bộ các thông báo cũ liên quan đến đơn hàng này trong hòm thư của Sales/Customer để tránh rác hộp thư.
    2. Tạo thông báo NOTI_APPENDIX_PENDING_SIGNATURE gửi cho Khách hàng.
    3. Tạo thông báo NOTI_APPENDIX_SENT gửi cho chính tài khoản Sales.
  + SignalR (Real-time): Bắn sự kiện AppendixPendingSignature (tới Customer) và AppendixSent (tới Group Group_Sales).

==================================================

3. KHÁCH HÀNG PHẢN HỒI (DUYỆT KÝ HOẶC TỪ CHỐI)

Kịch bản A: Khách hàng Chấp nhận ký phụ lục (ACCEPT)
- API: POST /api/contracts/appendices/{appendixId}/accept
- Thay đổi DB:
  + public.contract_appendices: Trạng thái status cập nhật thành "ACCEPTED", cập nhật thời gian resolved_at.
  + public.notifications: Xóa thông báo cũ của đơn hàng đó và tạo thông báo NOTI_APPENDIX_ACCEPTED gửi tới Sales.
  + SignalR: Bắn sự kiện AppendixAccepted tới Group Group_Sales.

Kịch bản B: Khách hàng Từ chối ký phụ lục (REJECT)
- API: POST /api/contracts/appendices/{appendixId}/reject
- Thay đổi DB:
  + public.contract_appendices: Trạng thái chuyển trực tiếp sang "EXECUTED", cập nhật thời gian resolved_at.
  + public.transport_orders: Cột status cập nhật thành "RETURN_PENDING".
  + public.lpns: Cột state chuyển sang 3 (LpnState.RETURN_PENDING - Chờ trả lại).
  + public.penalty_bills: Tạo biên bản phạt tiền mới với mức phạt 200,000 VNĐ, trạng thái thanh toán is_paid = false.
  + public.inbound_return_slips: Tạo phiếu xuất trả kho mới chứa thông số đo đạc thực tế của kiện hàng, kèm đường dẫn PDF phiếu trả hàng đã tải lên Cloudinary.
  + public.notifications: Xóa thông báo cũ của đơn hàng đó, chèn 2 thông báo mới gửi tới Sales: NOTI_APPENDIX_REJECTED (Báo khách từ chối) và NOTI_APPENDIX_EXECUTED (Báo thực thi trả hàng thành công).
  + SignalR: Phát sự kiện AppendixRejected và AppendixExecuted tới Group Group_Sales.

==================================================

4. THỰC THI XỬ LÝ CHÊNH LỆCH (EXECUTE)

Lưu ý: Đối với trường hợp khách hàng Từ chối (Reject), quy trình đã được tự động thực thi ở bước trên. Endpoint này chủ yếu được nhân viên Sales chạy thủ công khi khách hàng Đồng ý ký (Accepted) để hoàn tất thủ tục nhập kho.

- API: POST /api/contracts/appendices/{appendixId}/execute
- Mô tả: Đóng hồ sơ phụ lục cước và kết chuyển tài chính.

Thay đổi DB khi Thực thi phụ lục được duyệt (ACCEPTED):
- public.contract_appendices: Trạng thái chuyển thành "EXECUTED".
- public.transport_orders: Trạng thái status cập nhật thành "RECEIVING" (Tiếp tục nhập kho cất hàng).
- public.lpns: Cột state chuyển thành 1 (LpnState.RECEIVING - Kho bắt đầu thực hiện putaway).
- public.invoices & public.invoice_lines: Khởi tạo hóa đơn thu tiền cước chênh lệch bổ sung:
  + Loại phí: INBOUND_MEASUREMENT_ADJUSTMENT.
  + Số tiền (sub_total): Lấy bằng giá trị cước chênh lệch adjusted_price.
  + Thuế GTGT: 8%.
  + Trạng thái hóa đơn: "UNPAID".
  + Xuất file PDF hóa đơn điều chỉnh và đính kèm vào cột pdf_url.
- public.notifications: Xóa thông báo cũ của đơn hàng đó và tạo thông báo NOTI_APPENDIX_EXECUTED gửi tới Sales.
- SignalR: Phát sự kiện AppendixExecuted tới Group Group_Sales.
