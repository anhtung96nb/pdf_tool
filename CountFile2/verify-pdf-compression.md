# Kết quả kiểm chứng giảm dung lượng PDF

## Kiểm chứng bổ sung: thư mục 01942, ngưỡng 300 DPI

Đã kiểm tra 15 PDF trong thư mục người dùng cung cấp, với DPI tối thiểu 300 và yêu cầu giảm 20%. Đầu ra kiểm thử được lưu riêng ở `tmp/pdf-compression-01942-verified/`, không ghi vào Downloads và không sửa file nguồn. Báo cáo chi tiết: [report.csv](tmp/pdf-compression-01942-verified/report.csv).

- 15/15 file được xử lý thành công, giữ nguyên dữ liệu JPEG nhúng (kiểm tra hash), số pixel và ma trận hiển thị ảnh; DPI đầu ra vẫn >= 300. Hash SHA-256 toàn bộ file nguồn trước/sau khớp nhau.
- Mức giảm thực tế chỉ 0,0130–0,0333%, không đạt mục tiêu 20%. Phần giảm đến từ tối ưu cấu trúc, ảnh JPEG gốc được giữ nguyên.
- Ví dụ Part10: ảnh nguồn 856.208 byte, mã hóa JPEG ở chất lượng 85 tạo 1.144.217 byte nên bị loại. Dung lượng PDF chỉ giảm từ 857.380 xuống 857.197 byte; giữ nguyên 300 DPI.
- Các mức JPEG 95/90/85 hiện tại không giảm được ảnh đã nén mạnh của bộ dữ liệu này. Việc DPI nguồn bằng ngưỡng 300 không tự chặn thử JPEG; giới hạn chất lượng hiện tại mới là điều kiện khiến các ứng viên bị loại do lớn hơn nguồn.

Đã sửa lỗi sai số float của ma trận iText: nguồn đúng 300 DPI có thể tính thành 299,99999 và bị nhận nhầm là dưới ngưỡng. Chỉ chuẩn hóa sai số sát DPI nguyên trong phạm vi tương đối 2e-7; nguồn thật sự 299,99 vẫn bị đánh dấu dưới 300 và chỉ tối ưu không mất chất lượng. Đã bổ sung ghi chú số byte JPEG thử/gốc vào báo cáo, kể cả khi giữ ứng viên tối ưu cấu trúc từ mức đầu.

Kiểm thử cập nhật: **PASS 61 assertions** trên Debug x86 và Release x64. Bao gồm PDF scan tại đúng 300 DPI với ma trận kích thước thập phân, nén JPEG khi DPI bằng ngưỡng, bảo toàn nội dung khi JPEG có metadata 300 DPI, nguồn thực sự 299,99 DPI và lý do giữ ảnh JPEG đã nén mạnh.

Artifacts mới: `tmp/pdf-compression-tests/run-Debug-x86-3708d050b05f455d9dd25a8f8bb8526a/` và `tmp/pdf-compression-tests/run-Release-x64-28dc02e354c7475aa4a168bdcb4a6ff0/`. Bộ kiểm chứng dữ liệu thực: `tests/PdfCompressionRealFileCheck.cs`. Đã render và kiểm tra trực quan Part10 bằng Poppler; hash dữ liệu ảnh giống nhau xác nhận nội dung ảnh giữ nguyên trên toàn bộ 15 file. Các kết quả ở những mục dưới là lần kiểm chứng triển khai ban đầu, trước khi bổ sung fixture JPEG metadata 300 DPI.

## Build và kiểm thử

- `tests/Run-PdfCompressionTests.ps1 -Configuration Debug -Architecture x86`: **PASS, 54 assertions**.
- `tests/Run-PdfCompressionTests.ps1 -Configuration Release -Architecture x64`: **PASS, 54 assertions**.
- `tests/Run-WinFormsNavigationTests.ps1 -Configuration Debug`: **PASS**, gồm điều hướng cache, giữ trạng thái, các chế độ PDF hiện có, tác vụ độc quyền, chuyển màn hình khi đang chạy, hủy, shutdown và dispose.
- Debug và Release build thành công. Cảnh báo build còn lại thuộc mã có sẵn (`ConfigurationSettings` obsolete, biến/field không sử dụng); không có lỗi build.

Artifacts của lần chạy cuối:

- Debug: `tmp/pdf-compression-tests/run-Debug-x86-df3b62e950134a85a37e8dc29acf8477/`
- Release: `tmp/pdf-compression-tests/run-Release-x64-e0a24ea51f4a4d68832f90e902a90d1b/`
- Navigation: `tmp/winforms-navigation-tests/run-505c766ca7b64217aa95cb69bf020a10/`

## Các tình huống đã xác minh

- Mục tiêu giảm theo phần trăm được tính riêng cho mỗi file; số byte đầu ra khớp kích thước file thực tế.
- Giữ file gốc, không lưu kết quả lớn hơn nguồn, giữ bản sao gốc nếu không có phương án nhỏ hơn.
- Khi không đạt mục tiêu, vẫn lưu kết quả nhỏ nhất qua kiểm tra và báo chính xác **Không đạt tỷ lệ giảm yêu cầu**, kèm DPI cấu hình/trước/sau và tỷ lệ yêu cầu/thực tế.
- Nguồn đã dưới ngưỡng DPI được tối ưu không mất chất lượng, không upscale hoặc giảm DPI thêm, đánh dấu không đạt DPI.
- PDF văn bản/vector không có ảnh trả DPI không áp dụng; văn bản vẫn trích xuất được sau nén.
- Form, hyperlink, metadata được giữ; không raster hóa toàn bộ trang.
- Ảnh dùng chung được tính theo lần hiển thị lớn nhất. Kiểm thử riêng nested Form XObject, inline, soft mask, UserUnit, shear và xoay.
- Ảnh dùng chung với appearance của annotation không bị resize. PDF có signature dictionary hoặc mã hóa không bị xuất bản chỉnh sửa.
- Thư mục tiếng Việt, đuôi `.PDF`, thư mục con, tên file giống nhau ở thư mục khác, PDF lỗi, loại trừ đầu ra và file đã nén.
- Ghi đè đầu ra cần cấu hình; lỗi khi publish được xử lý và file tạm được dọn.
- Cấu hình không hợp lệ (NaN/Infinity, tỷ lệ 0/100/âm) bị từ chối.
- Hủy trước xử lý, giữa mức nén và giữa các file; giữ kết quả hoàn tất và không rò file tạm.
- UI chạy bất đồng bộ với kiểm tra truy cập chéo luồng bật; khóa cấu hình khi chạy, chuyển màn hình vẫn chạy tiếp, giữ cấu hình, xuất CSV tự động, hủy và dispose.

## Kết quả mẫu

| Mẫu | Byte trước | Byte sau | Giảm thực tế | DPI trước → sau | Kết quả |
| --- | ---: | ---: | ---: | --- | --- |
| Scan RGB chưa nén tối ưu, yêu cầu giảm 40%, ngưỡng 150 | 4,322,603 | 26,409 | 99.39% | 360 → 360 | Đạt bằng tối ưu không mất dữ liệu |
| Cùng scan, yêu cầu giảm 99.99%, ngưỡng 360 | 4,322,603 | 26,409 | 99.39% | 360 → 360 | Không đạt tỷ lệ giảm yêu cầu; lưu kết quả tốt nhất và ghi chú DPI |
| JPEG dùng chung trên hai trang, yêu cầu giảm 99.99%, ngưỡng 150 | 271,311 | 29,407 | 89.16% | 360 → 150 | Không đạt tỷ lệ giảm yêu cầu; ảnh resize từ 1200 × 1200 xuống 500 × 500 |

Các file mẫu được tạo để kiểm thử, không đại diện cho tỷ lệ giảm của mọi tài liệu thực tế.

## Kiểm tra trực quan

PDFium đã mở và render các bản trước/sau, bao gồm ảnh JPEG resize và soft mask. Đã xem preview và ảnh UI ở kích thước tối thiểu, scale mô phỏng 150%; bố cục và các thao tác chính hiển thị đầy đủ, bảng có thanh cuộn ngang cho các cột DPI.

Poppler được dùng độc lập để render bản nguồn/đích của scan và trang thứ hai của mẫu JPEG tại 120 DPI. Artifacts so sánh ở `tmp/pdf-compression-tests/run-Debug-x86-6ba9b2bbae4b46438f082cb820b22305/poppler-*.png`:

- Scan nén không mất dữ liệu: ảnh render **giống nhau từng pixel**.
- JPEG: văn bản/vector và trường form vẫn giữ vị trí; chữ trong mẫu scan vẫn đọc được khi xem trước/sau. RMSE toàn trang chuẩn hóa là 0.04721, bao gồm tác động resize. Giới hạn RMSE 0.035 trong service chỉ áp dụng cho bước mã hóa JPEG so với ảnh đã resize, không phải so với ảnh nguồn ở độ phân giải ban đầu.
- Poppler báo thiếu font thay thế `Symbol`/`ArialUnicode` ở cả nguồn và đích; không làm thất bại render. PDFium cũng được dùng để đối chiếu độc lập.

Không khẳng định chất lượng thị giác/OCR của mọi tài liệu chỉ từ DPI hoặc JPEG/RMSE. Ảnh inline, mask, màu đặc biệt và appearance riêng có chính sách giữ nguyên; phạm vi DPI và giới hạn PDF/A được mô tả trong [hướng dẫn](CountFile2/Services/PdfCompression.md).
