# Kế hoạch tích hợp PDF hai lớp / PDF/A và chuyển form

## Mục tiêu và lựa chọn

Một cửa sổ chính `frmMain` có thanh điều hướng và vùng nội dung. Người dùng chuyển giữa Thống kê, TIFF/PDF, PDF hai lớp/PDF/A và OCR thông tin văn bản. Mỗi màn hình được tạo khi mở lần đầu, giữ lại instance để không mất đường dẫn, tùy chọn và kết quả khi chuyển màn hình. Mặc định mở TIFF/PDF để giữ hành vi khởi động hiện tại.

Phạm vi là triển khai giao diện và điều hướng trên .NET Framework 4.8, dùng `PdfProcessingService` hiện có. Không thay thuật toán OCR/chuyển đổi của service.

## Hiện trạng

- `Program.Main` đang chạy trực tiếp `frmChuyenDoiFile`.
- `Form1` là màn hình thống kê, mở TIFF/PDF bằng `ShowDialog` nên chặn chuyển màn hình.
- `frmChuyenDoiFile` đã có xử lý async, tiến độ, hủy và kết quả từng file; có thể tái sử dụng mẫu tương tác.
- `frmXuLyOCR` là OCR thông tin văn bản, khác với chức năng tạo PDF hai lớp. Có xử lý đồng bộ cần xem xét trước khi cho phép chạy cùng các màn hình khác.

## 1. Cửa sổ chính và vòng đời form

- Thêm `frmMain`, thanh điều hướng bên trái, tiêu đề màn hình, vùng `Panel` nội dung `Dock = Fill` và nhãn trạng thái tác vụ.
- Thêm `FormNavigationController` quản lý instance theo màn hình. Form con đặt `TopLevel = false`, `FormBorderStyle = None`, `Dock = Fill`; chuyển bằng `Hide/Show/BringToFront`. Không gọi `Close` hoặc `Dispose` khi chuyển màn hình.
- Chuyển `Application.Run` sang `frmMain`. Thay nút mở TIFF/PDF bằng sự kiện yêu cầu điều hướng về shell, bỏ mở bản sao bằng `ShowDialog`.
- Tạo OCR thông tin văn bản theo yêu cầu sử dụng; bắt lỗi khởi tạo/thiếu tessdata và hiển thị thông báo ngay trong shell, không làm đóng ứng dụng.
- Điều chỉnh kích thước tối thiểu, `AutoScroll` và layout form cũ để không bị cắt khi nhúng vào panel. Giữ các dialog chọn file/thư mục là modal, với owner là cửa sổ chính.
- Shell sở hữu và dispose mọi form con khi thoát; giải phóng PDF viewer, ảnh và OCR engine của màn hình cũ.

## 2. Form `frmPdfProcessing`

Các trường và nút:

| Nhóm | Nội dung |
| --- | --- |
| Chế độ | PDF hai lớp; PDF/A-1b; PDF/A-1b có OCR |
| Nguồn | Nhập path, chọn một file PDF hoặc chọn thư mục |
| Đích | Chọn thư mục kết quả, cho phép để trống để xuất cạnh nguồn |
| Tùy chọn | DPI 72-600, mặc định 300; quét thư mục con; ghi đè |
| OCR | Ngôn ngữ mặc định vie; cấu hình tessdata và font trong phần nâng cao |
| PDF/A | ICC sRGB trong phần nâng cao |
| Thao tác | Bắt đầu, Hủy, Mở thư mục kết quả |
| Tiến độ | File đang xử lý, trang hiện tại/tổng trang, số file hoàn tất/tổng file, thời gian |
| Kết quả | File nguồn, file đích, số trang, trạng thái, chi tiết lỗi |

- Chỉ bật tùy chọn OCR/PDF/A khi chế độ tương ứng cần chúng.
- Hiển thị ngắn gọn tác động raster hóa: không giữ vector, form và chữ ký số gốc. Thể hiện PDF/A-1b rõ ràng.
- Khuyến nghị output ngoài cây input để lần chạy sau không quét lại kết quả; cảnh báo nếu chọn output bên trong input.
- Không hiển thị nút hoặc nhãn gợi ý rằng kết quả đã được veraPDF xác nhận.

## 3. Kết nối service và trạng thái tác vụ

- Chụp cấu hình vào một `PdfProcessingOptions` mới khi bắt đầu; validate path, DPI, tessdata/ngôn ngữ, font và ICC theo chế độ.
- Gọi `ConvertToSearchablePdfAsync` hoặc `ConvertToPdfAAsync`, bật `searchable` cho chế độ PDF/A có OCR.
- Dùng `Progress<ConversionProgress>` tạo trên UI thread; dùng mã lần chạy để bỏ callback cũ. Trong giai đoạn quét, hiển thị tiến độ chưa xác định.
- Khóa trường cấu hình và nút Bắt đầu trong khi chạy; giữ nút Hủy và điều hướng sử dụng được.
- Khi đổi màn hình, tác vụ PDF đang chạy tiếp tục; quay lại hiển thị trạng thái và kết quả hiện tại. Không dispose form hoặc cancellation token do chuyển màn hình.
- Sau khi service trả về, đồng bộ bảng bằng snapshot kết quả, kể cả `ConversionCanceledException.Results`. Phân biệt Hoàn tất, Hoàn tất có lỗi, Đã hủy và Lỗi cấu hình.
- Tái sử dụng `ConversionResult`, `ConversionProgress`, `ConversionCanceledException`; không tạo các model trùng chức năng.
- Giới hạn một tác vụ xử lý tài liệu chạy tại một thời điểm trong phiên bản đầu bằng bộ điều phối dùng chung. Các màn hình khác vẫn xem được; nút chạy của chúng báo tác vụ hiện tại.
- Kiểm tra và chuyển các handler thống kê/OCR cũ đang xử lý dài trên UI thread sang luồng nền trước khi áp dụng cam kết chuyển màn hình trong lúc các tác vụ đó chạy. Mọi cập nhật control phải về UI thread.
- Khi đóng ứng dụng lúc đang chạy: yêu cầu xác nhận dừng, gửi cancellation và đợi cleanup rồi đóng; không dùng `.Wait()`/`.Result` trên UI thread. Thông báo việc hủy có thể phải đợi render/OCR trang hiện tại kết thúc.

## 4. Thứ tự triển khai

1. Tạo shell và controller; chuyển startup; nhúng các form cũ; loại bỏ mở trùng màn hình.
2. Tạo form PDF và ánh xạ ba chế độ sang service; hoàn thiện validation, kết quả và mở output.
3. Thêm điều phối tác vụ, giữ trạng thái khi switch, hủy và thoát an toàn; xử lý handler cũ đang chặn UI.
4. Kiểm tra bố cục và vòng đời tài nguyên; hoàn thiện thông báo, tài liệu và kiểm thử.

File dự kiến: thêm `frmMain.cs`, `FormNavigationController.cs`, `frmPdfProcessing.cs`, `Services/DocumentJobCoordinator.cs`; sửa `Program.cs`, `Form1.cs`, `frmChuyenDoiFile.cs`, `frmXuLyOCR.cs`, `CountFile2.csproj`. Tách Designer/resx nếu dùng WinForms Designer; thống nhất cách dựng form mới với dự án khi triển khai.

## 5. Kiểm thử và tiêu chí nghiệm thu

- Build Debug/Release; chạy lại kiểm thử PDF service và TIFF/PDF.
- Mở/chuyển qua mọi màn hình nhiều lần: không tạo instance trùng, không mất cấu hình/kết quả, không tự thoát ứng dụng.
- Chạy ba chế độ với file một/nhiều trang và thư mục con; kiểm tra tên đích, lỗi từng file và checkbox tương ứng.
- Chuyển màn hình khi PDF chạy rồi quay lại: UI phản hồi, tác vụ tiếp tục, kết quả cập nhật đúng; tác vụ thứ hai bị ngăn.
- Kiểm tra lỗi path, thiếu tài nguyên, file hỏng, output tồn tại và quyền ghi; người dùng có thể sửa và chạy lại.
- Hủy và đóng khi đang chạy: giữ kết quả đã hoàn tất, dọn file tạm, không dispose control trước callback cuối và không treo UI.
- Kiểm tra font/DPI Windows 100%, 125%, 150% và cửa sổ nhỏ: thanh điều hướng, bảng kết quả và nút thao tác không bị cắt.
- Kiểm tra thủ công dialog hệ thống và mở thư mục; kiểm thử tự động cho chuyển màn hình, trạng thái tác vụ và snapshot kết quả bằng WinForms message pump.

Hoàn thành khi người dùng có thể chọn chức năng từ cửa sổ chính, tạo PDF theo cả ba chế độ và chuyển form mà không mất trạng thái hoặc ngắt tác vụ PDF.
