> Lịch sử kiểm thử bộ quy tắc trước 26/10/2026. Bộ chọn trường đó đã được gỡ; xem `verify-llm-extraction.md` cho bản LLM hiện tại.

# Xác minh bóc tách sau OCR / Excel — 25/10/2026

## Đã triển khai

- Adapter đọc JSON OCR schema 1 của VietOCR/PaddleOCR và cache nội bộ, không gọi lại nhận dạng. Có đầu vào MD/TXT ghép với PDF do người dùng chọn, thiếu tọa độ được ghi rõ.
- SHA-256 đối chiếu PDF, định danh theo đường dẫn, dấu vân tay OCR và phiên bản quy tắc. Dòng bằng chứng giữ vị trí ở PDF gốc; bố cục xoay được xử lý ở hệ tọa độ riêng.
- Tái sử dụng bộ trích xuất PDF hiện có; bổ sung ứng viên số/ngày, xử lý nhãn Số mất dấu hai chấm, ngày không hợp lệ, tiêu đề, phạm vi văn bản chính, nhiều khối ký và metadata số trang ghi tay.
- Tab Thông tin / Excel trong Việt OCR và PaddleOCR. Nhập JSON file/thư mục, lọc engine, chọn ứng viên/phạm vi, sửa/xác nhận/không áp dụng, giữ lịch sử và bản sửa khi chạy lại. Bản sửa đã lưu trong metadata được đọc trước khi ghi đè kết quả OCR.
- Số ghi tay dùng chung metadata nguồn; có thể mở lại phần rà soát từ JSON nhập vào. Xuất báo cáo đồng bộ nhãn mới nhất trong phiên. JSON kết quả và Excel là snapshot cần lưu/xuất lại sau chỉnh sửa.
- Một dòng/PDF ở Báo cáo, bốn sheet chi tiết, chuỗi giữ số 0 đầu, ngày Excel, công thức giả trong OCR vẫn là chuỗi, snapshot và thay thế nguyên tử, từ chối ô quá dài có thông báo, chia sheet khi vượt số dòng.

## Kiểm thử

1. `tests/Run-PostOcrExtractionTests.ps1 -Configuration Release`: **51 assertions PASS**. Các ca chính: adapter, SHA, cơ quan chủ quản/ban hành, số 0 đầu, ngày dẫn chiếu và ngày sai lịch, trích yếu nhiều dòng, nhiều người ký, nhiều văn bản/phạm vi chính, tọa độ xoay 90/180/270, chọn ứng viên và thành phần phụ thuộc, sửa qua DataGridView và xác nhận, giữ lịch sử, thay nguồn bỏ xác nhận, MD/TXT không tạo tọa độ, năm sheet, kiểu ô Excel, file khóa, ô quá dài, nhập lại không nhân đôi PDF, chỉ Markdown/tắt số vẫn có metadata, cập nhật số dùng chung, giữ bản sửa khi OCR ghi đè và danh sách trạng thái sau hủy.
2. `tests/Run-ExtractionTests.ps1 -Configuration Release -Architecture x64`: **37 assertions PASS**, bao gồm đọc PDF/Tesseract, scan/mixed/rotated, tiếp tục xử lý và cập nhật kỳ vọng workbook năm sheet. Artifacts: `tmp/extraction-tests/run-8c478d82120545acbab2d666245cd17e/`.
3. `tests/Run-VietOcrTests.ps1`: **VIET OCR INTEGRATION PASS**, thêm 2 geometry + 7 page-number Python tests. Artifacts: `tmp/vietocr-tests/run-7807019f5866495d81e64d889176edc6/`.
4. `tests/Run-WinFormsNavigationTests.ps1`: **PASS**, điều hướng dùng lại màn hình, giữ trạng thái, chặn tác vụ đồng thời, hủy và đóng. Artifacts: `tmp/winforms-navigation-tests/run-67b7ca1e7eb340f2aa0b7d4f4f088daa/`.
5. **Model VietOCR thật**, PDF kiểm thử tổng hợp hai trang, 150 DPI, số ghi tay tắt, bật metadata: OCR → bóc tách → JSON metadata → Excel hoàn tất khoảng **48,0 giây** gồm nạp model. Artifacts: `tmp/post-ocr-real/post-ocr-5980cc6e67c3440386c4d9c442a0a334/`. Model đọc đúng ngày và tên/chức vụ trong mẫu, có lỗi dấu cơ quan và mất dấu hai chấm ở nhãn Số. Đã bổ sung nhận dạng nhãn mất dấu hai chấm, giữ nguyên ký hiệu OCR `QD` và kiểm tra lại trên JSON thật mà không OCR lại: `tmp/post-ocr-real/post-ocr-a04bff36d7a941ed87f4a6731a4b14fe/reextracted.xlsx`.

Ảnh `review.png` và `integrated-review.png` trong các lần chạy bộ kiểm thử mới đã được xem trực tiếp: bảng trường, công cụ rà soát và PDF hiển thị được. Trên tab metadata, vùng cấu hình OCR được ẩn để dành diện tích rà soát; chọn Kết quả OCR để quay lại cấu hình.

Build Release thành công; chỉ có cảnh báo cũ trong Form1/frmXuLyOCR. File chạy: `CountFile2/bin/Release/CountFile2.exe`.

## Phạm vi kết luận

Kiểm thử trên fixture tổng hợp và một lần chạy model thật trên PDF tổng hợp xác minh luồng và hành vi; không phải đánh giá độ chính xác trên hồ sơ thực. Chưa có tập 30–50 PDF gán nhãn và tập đánh giá độc lập. Bố cục khác, nhiều tài liệu đính kèm, khối ký không rõ và lỗi OCR vẫn cần rà soát. Không fine-tune, không suy ra người ký từ nét chữ ký.

Môi trường sandbox Windows không cho File.Replace xóa file cache cũ trong AC/Temp, dù thư mục ghi được. Kiểm thử dùng TEMP/TMP riêng trong `tmp/` của workspace và qua được cùng phép thay thế nguyên tử; không thay đổi thuật toán ứng dụng để bỏ qua lỗi khóa file. Không cần tải thêm thư viện.
