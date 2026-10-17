# Triển khai và xác minh WinForms PDF - 2026-10-13

## Đã triển khai

- `Program.Main` mở `frmMain`; mặc định hiển thị TIFF/PDF.
- `FormNavigationController` tạo form theo nhu cầu, giữ instance/cấu hình/kết quả, dispose khi thoát. Các form cũ có thanh cuộn khi nội dung lớn hơn vùng hiển thị.
- Bốn màn hình: Thống kê, TIFF/PDF, PDF hai lớp/PDF/A, OCR thông tin văn bản. Nút TIFF/PDF trên màn hình thống kê điều hướng về instance hiện có.
- Form PDF dùng chung phần chọn nguồn/đích, tiến độ và bảng kết quả với TIFF/PDF; thêm ba chế độ, DPI 72-600, quét thư mục con, ghi đè và cấu hình OCR/ICC nâng cao.
- Bộ điều phối giới hạn một tác vụ, hiển thị tác vụ trên shell, cho phép đổi màn hình trong lúc xử lý và hủy từ mọi màn hình.
- Thống kê, OCR một file, OCR thư mục và xuất Excel OCR chạy nền. Các control của hai màn hình cũ bị khóa khi có tác vụ để bảo vệ dữ liệu.
- Thoát khi đang chạy: xác nhận dừng, yêu cầu hủy, đợi cleanup rồi dispose form; không hiển thị thông báo OCR/thống kê mới trong quá trình thoát.
- Service PDF phát danh sách file chờ trước khi chuyển đổi, giúp bảng giữ các file chưa xử lý sau khi hủy.
- Đưa hai template thống kê Excel vào project, tự sao chép khi build Debug/Release; sửa lỗi thiếu template của bản Release.

## Kiểm thử

| Bộ kiểm thử | Cấu hình | Kết quả |
| --- | --- | --- |
| Run-WinFormsNavigationTests.ps1 | Debug / x86 | PASS |
| Run-WinFormsNavigationTests.ps1 | Release / x86 | PASS |
| Run-ConversionTests.ps1 | Debug / x86 | PASS |
| Run-ConversionTests.ps1 | Release / x64 | PASS |
| PdfProcessingTests.exe | Debug / x86 | PASS |

Kiểm thử giao diện bật `Control.CheckForIllegalCrossThreadCalls`, dùng message pump và synchronization context WinForms. Các kiểm tra bao gồm chuyển form nhiều lần, giữ trạng thái, chuyển form khi tác vụ chạy, ngăn tác vụ thứ hai, ba chế độ PDF, bật/tắt tùy chọn tương ứng, file lỗi, thư mục con, ghi đè, xuất cạnh nguồn, hủy, dọn file tạm và thoát an toàn. Kiểm tra thêm OCR cũ chạy nền, cập nhật số lượng thống kê từ worker và xuất Excel OCR.

Ảnh giao diện được lưu dưới `tmp/winforms-navigation-tests/run-*`: cửa sổ chính, màn hình PDF với cấu hình nâng cao, form thống kê/OCR, mô phỏng tỷ lệ control 125%/150%. Đã xem ảnh bố cục; màn hình cũ dùng cuộn ngang để truy cập phần nằm ngoài cửa sổ nhỏ.

## Giới hạn xác minh

- Tỷ lệ 125%/150% được mô phỏng bằng `Control.Scale`, chưa đổi DPI Windows thực tế.
- Dialog chọn file/thư mục và mở Explorer chưa được kiểm thử tự động.
- Kiểm thử PDF/A kiểm tra metadata, ICC và writer; chưa chạy veraPDF.
- Các warning còn lại thuộc code cũ: ConfigurationSettings lỗi thời, biến không dùng và catch không dùng biến exception.

## Chạy ứng dụng

Mở `CountFile2/bin/Release/CountFile2.exe`, chọn **PDF hai lớp / PDF/A**, chọn chế độ, nguồn và đích, sau đó **Bắt đầu**. Có thể đổi chức năng từ thanh điều hướng bất cứ lúc nào; tác vụ hiện tại tiếp tục chạy. Hướng dẫn tùy chọn và triển khai thư viện nằm trong `CountFile2/Services/PdfProcessing.md`.
