# Kế hoạch OCR toàn bộ nội dung PDF bằng VietOCR

## 1. Mục tiêu và phạm vi

Đọc toàn bộ nội dung nhìn thấy trên tất cả các trang PDF: tiêu đề, đoạn văn, đầu trang, chân trang, số trang, chú thích, chữ trong bảng, chữ trong ảnh và phần chữ đọc được trên con dấu. Lưu vị trí của nội dung để người dùng đối chiếu với trang gốc.

Giai đoạn này tập trung số hóa toàn văn, chưa trích xuất riêng số văn bản, ngày ban hành, cơ quan ban hành hoặc trích yếu. Không giới hạn OCR ở trang đầu hoặc các vùng thông tin định sẵn.

“Toàn bộ thông tin” được thực hiện bằng cách giữ ảnh trang gốc và các vùng nội dung cùng kết quả OCR. Ảnh, biểu đồ, chữ ký và công thức được giữ làm vùng hình ảnh; không cam kết chuyển chúng thành mô tả ngữ nghĩa hoặc công thức có cấu trúc. Chữ viết tay, chữ bị che, dấu đỏ và bản scan mờ phải có trạng thái cần kiểm tra, không suy đoán nội dung bị mất.

## 2. Kiến trúc đề xuất

- WinForms .NET Framework 4.8 phụ trách chọn file, cấu hình, xem trang, tiến độ và kết quả.
- Thêm dịch vụ OCR toàn văn riêng, dự kiến `FullDocumentOcrService`; tái sử dụng cơ chế điều phối tác vụ hiện có khi phù hợp.
- Python worker chạy cục bộ, nạp bộ phát hiện chữ và VietOCR một lần cho mỗi phiên; trao đổi JSON qua stdin/stdout, log qua stderr.
- VietOCR nhận ảnh dòng chữ, không nhận trực tiếp toàn bộ PDF. Cần bộ phát hiện chữ và bước phân tích bố cục trước nhận dạng.
- Thử nghiệm detector DBNet/CRAFT và hai cấu hình VietOCR `vgg_seq2seq`, `vgg_transformer`; chọn tổ hợp sau khi đo trên dữ liệu thực tế. Khóa phiên bản thư viện, model và checksum khi đóng gói.
- Giữ chức năng OCR thông tin văn bản và PDF hai lớp hiện có; bản kế hoạch này bổ sung luồng OCR toàn văn riêng.

## 3. Quy trình xử lý một file

### Bước 1 — Kiểm tra đầu vào

- Kiểm tra PDF đọc được, mật khẩu, số trang, kích thước trang và quyền ghi thư mục kết quả.
- Tạo mã tác vụ, dấu vân tay file, cấu hình và thư mục kết quả riêng để tránh trùng tên.
- Ghi trạng thái từng trang; mỗi trang phải có kết quả hoặc lý do lỗi/bỏ qua rõ ràng.

### Bước 2 — Đọc và render tất cả các trang

- Render lần lượt từng trang, mặc định 300 DPI, có giới hạn số pixel và bộ nhớ.
- Giữ kích thước trang PDF, kích thước ảnh và phép biến đổi tọa độ.
- Bản đầu mặc định OCR mọi trang để có kết quả nhất quán, kể cả file đã có lớp chữ.
- Có thể bổ sung chế độ tận dụng lớp chữ PDF sau khi có kiểm tra chất lượng và độ phủ theo vùng. Trang có cả chữ và ảnh phải OCR vùng ảnh còn thiếu; không được bỏ qua cả trang chỉ vì tìm thấy một ít chữ.

### Bước 3 — Tiền xử lý ảnh

- Xác định chiều trang 0/90/180/270 độ và chỉnh nghiêng khi đủ bằng chứng.
- Giảm nhiễu, tăng tương phản ở mức bảo toàn dấu tiếng Việt và nét chữ mảnh.
- Giữ ảnh gốc để xem và so sánh; chỉ xóa đường kẻ trên bản ảnh dùng nhận dạng nếu cần, giữ bản riêng để phân tích bảng.
- Lưu phép biến đổi thuận/nghịch để đưa vùng OCR về đúng vị trí trên trang gốc.

### Bước 4 — Phát hiện chữ và bố cục

- Phát hiện các vùng/dòng chữ trên toàn trang; cắt ảnh dòng với khoảng đệm phù hợp.
- Xử lý chữ xoay và vùng nghiêng; kiểm tra dòng quá dài trước khi chia nhỏ, tránh mất hoặc lặp ký tự ở điểm nối.
- Nhóm dòng thành khối, cột, đầu trang, chân trang, bảng và chú thích.
- Xác định thứ tự đọc theo khối/cột. Không chỉ sắp toàn trang theo tọa độ Y vì có thể trộn nội dung nhiều cột.
- Lưu vùng nghi có chữ nhưng không nhận dạng được để kiểm tra lại; detector bỏ sót không thể được khắc phục chỉ bằng đổi model VietOCR.

### Bước 5 — Nhận dạng bằng VietOCR

- Đọc lần lượt hoặc theo lô các ảnh dòng nếu backend đã kiểm chứng hỗ trợ.
- Lưu nguyên văn kết quả nhận dạng, cấu hình model và điểm nhận dạng nếu backend cung cấp.
- Không coi điểm model là xác suất chính xác đã hiệu chuẩn; xác định ngưỡng cảnh báo từ bộ mẫu.
- Vùng lỗi được thử lại có giới hạn với ảnh/cấu hình thay thế; không thử vô hạn.
- Không tự sửa tên riêng, mã số, số tiền hoặc ngày tháng bằng suy đoán.

### Bước 6 — Khôi phục cấu trúc tài liệu

- Tạo cấu trúc file → trang → khối → dòng; giữ thứ tự đọc và tọa độ.
- Giữ đầu/chân trang và số trang; có thể gắn nhãn lặp lại nhưng không xóa khỏi dữ liệu đầy đủ.
- Bảng: nhận diện hàng/cột và ô gộp khi đủ bằng chứng, gắn chữ vào ô. Bảng chưa khôi phục được phải giữ ảnh vùng bảng và danh sách dòng, ghi trạng thái cần kiểm tra.
- Không tuyên bố VietOCR tự khôi phục cấu trúc bảng; đây là bước riêng cần đánh giá.
- Chuẩn hóa Unicode NFC và khoảng trắng trên bản văn bản chuẩn hóa; giữ bản OCR gốc để truy vết.

### Bước 7 — Lưu kết quả

- `document.json`: dữ liệu có cấu trúc, nguồn, cấu hình, trang, vùng, dòng, bảng, cảnh báo và trạng thái.
- `document.txt`: toàn văn UTF-8, có ranh giới trang, theo thứ tự đọc.
- `document.md`: đoạn văn và bảng khi cấu trúc đủ tin cậy; liên kết vùng ảnh chưa chuyển thành chữ.
- `pages/`: ảnh trang; `regions/`: vùng hình ảnh hoặc vùng cần kiểm tra.
- `manifest.json`: phiên bản schema, danh sách đầu ra và kết quả hoàn thành từng trang.
- Ghi tạm rồi đổi tên khi hoàn tất; kết quả một phần phải có trạng thái rõ ràng. Không ghi đè PDF nguồn.
- PDF có lớp chữ tìm kiếm là bước bổ sung sau khi toàn văn và tọa độ đạt yêu cầu; không thuộc điều kiện hoàn tất bản đầu.

## 4. Hợp đồng dữ liệu và vận hành

- Mỗi yêu cầu worker có phiên bản giao thức, request ID, page ID, đường dẫn ảnh và cấu hình. C# render trang ra ảnh để worker tập trung vào bố cục/nhận dạng.
- Mỗi dòng có ID, polygon trên ảnh trang gốc, tọa độ tương ứng trên trang PDF, `raw_text`, `normalized_text`, `reading_order`, điểm model có thể null và cảnh báo.
- Mỗi trang có trạng thái `pending/running/completed/needs_review/failed/canceled`, thời gian và lỗi; `needs_review` không có nghĩa là đã nhận dạng chính xác.
- Worker trả sự kiện tiến độ và kết quả theo từng trang, có timeout, kiểm tra phiên bản và xử lý worker thoát bất thường.
- Hủy ở ranh giới xử lý an toàn; nếu worker không phản hồi quá thời gian chờ thì kết thúc đúng process do ứng dụng tạo. Giữ kết quả các trang đã hoàn tất.
- Xử lý tuần tự hoặc hàng đợi giới hạn; không render toàn bộ PDF vào RAM. GPU xử lý một worker trước, tăng song song sau khi đo bộ nhớ.
- Cho phép tiếp tục tác vụ khi dấu vân tay đầu vào và cấu hình/model khớp; thay đổi đầu vào hoặc cấu hình cần chạy lại phần bị ảnh hưởng.
- Log tập trung vào trạng thái và lỗi, mặc định không chép toàn văn tài liệu vào log. Xác định chính sách giữ/xóa ảnh tạm khi triển khai.

## 5. Giao diện dự kiến

- Chọn file PDF hoặc thư mục, chọn thư mục đích, DPI và thiết bị CPU/GPU khả dụng.
- Nút Bắt đầu, Hủy, Mở kết quả; hiển thị file, trang, bước hiện tại, số trang hoàn tất và số trang cần kiểm tra.
- Xem ảnh trang cùng vùng OCR và toàn văn; chọn dòng để làm nổi vùng tương ứng.
- Hiển thị lỗi/trang cần kiểm tra; sửa kết quả phải giữ bản gốc và bản sửa riêng.
- Chạy nền, cập nhật control trên UI thread, giữ trạng thái khi chuyển màn hình.

## 6. Thứ tự triển khai và đầu ra

| Giai đoạn | Công việc | Điều kiện hoàn thành |
| --- | --- | --- |
| 1. Bộ mẫu | Chọn 30–50 trang, gồm chữ rõ/mờ, nhiều trang, nhiều cột, bảng, dấu, chữ xoay; chép chuẩn một phần đại diện | Có dữ liệu chuẩn và nhóm độ khó; tách tập điều chỉnh và tập đánh giá |
| 2. Thử nghiệm pipeline | Render, detector, VietOCR, tọa độ và thứ tự đọc | Xuất JSON/TXT đầy đủ cho một file nhiều trang; có báo cáo lỗi và benchmark |
| 3. Bố cục và bảng | Nhóm khối/cột, phân loại vùng, khôi phục bảng và xuất Markdown | Không mất nội dung khi bảng/bố cục không khôi phục được; có ảnh dự phòng |
| 4. WinForms | Kết nối worker, tiến độ, xem vùng chữ, hủy và chạy lô | Giao diện phản hồi; file lỗi không làm dừng toàn bộ lô |
| 5. Đóng gói | Khóa dependency/model, kiểm tra CPU/GPU và chạy trên máy đích | Cài đặt có thể lặp lại, thông báo rõ thiếu model/runtime và chạy thử thành công |

Chưa ước lượng tốc độ hoặc thời gian hoàn thành trước khi đo dữ liệu và phần cứng thực tế. Chỉ fine-tune sau khi phân loại lỗi cho thấy nguyên nhân thuộc nhận dạng chữ; không dùng fine-tune để thay cho sửa detector hoặc bố cục.

## 7. Kiểm thử và tiêu chí nghiệm thu

- Tất cả trang đều được liệt kê và xử lý hoặc báo lý do cụ thể; không bỏ qua âm thầm.
- Đo CER có tính dấu tiếng Việt, tỷ lệ dòng đúng hoàn toàn và độ phủ vùng chữ trên tập đánh giá độc lập. CER chỉ trên vùng đã cắt đúng không đủ phản ánh chất lượng toàn file.
- Đo thứ tự đọc, tỷ lệ dòng thiếu/trùng và chất lượng gắn chữ vào ô bảng; báo cáo riêng cho scan rõ, mờ, nhiều cột và bảng.
- Kiểm tra tọa độ bằng overlay sau xoay/chỉnh nghiêng, trang ngang và kích thước trang khác nhau.
- Đo giây/trang, RAM/VRAM cực đại trên máy đích; xác lập ngưỡng chất lượng/tốc độ sau giai đoạn thử nghiệm.
- Kiểm tra PDF rỗng/hỏng/có mật khẩu, trang trắng, ảnh lớn, đường dẫn tiếng Việt, trùng tên file, worker lỗi, hủy và tiếp tục tác vụ.
- Đối chiếu nội dung giữa JSON/TXT/Markdown và manifest; file dở dang không được hiển thị là hoàn tất.

## 8. Nguồn tham khảo

- VietOCR, mã nguồn và cấu hình mô hình: https://github.com/pbcquoc/vietocr

Đây là kế hoạch triển khai; chưa thay đổi mã xử lý OCR hiện tại.
