# Giảm dung lượng PDF theo phần trăm và ngưỡng DPI

Mở **Giảm dung lượng PDF** trong menu ứng dụng. Chọn PDF hoặc thư mục, đặt **DPI tối thiểu** và **Giảm dung lượng (%)**, rồi bấm **Bắt đầu**. Mặc định: 150 DPI, giảm 40%, duyệt thư mục con, không ghi đè đầu ra. Để trống đầu ra sẽ lưu vào thư mục `compressed` cạnh nguồn. Ngưỡng DPI hỗ trợ 1–2400; tỷ lệ giảm phải lớn hơn 0 và nhỏ hơn 100.

Mỗi file có mục tiêu riêng: `floor(byte_nguồn × (1 - phần_trăm / 100))`. Ví dụ 20 MB giảm 40% có mục tiêu tối đa 12 MB. Đầu ra có hậu tố `.compressed.pdf`, giữ cấu trúc thư mục con. Batch bỏ qua file đã có hậu tố này, thư mục đầu ra và junction/symlink thư mục. File lỗi được báo riêng; các file còn lại tiếp tục. Không ghi đè file nguồn.

## Quy trình

1. Đọc cấu trúc, văn bản, hình học trang và DPI hiệu dụng của các ảnh raster trong nội dung trang, bao gồm ảnh trong Form XObject và ảnh inline.
2. Tối ưu cấu trúc và nén lại Flate không mất dữ liệu trước. Nếu đạt mục tiêu thì dừng để giữ chất lượng tối đa.
3. Nếu chưa đạt, thử tối đa ba mức ảnh từ nguồn gốc: DPI bằng 80%, 60% và ngưỡng cấu hình (luôn giới hạn dưới bởi ngưỡng). Chất lượng JPEG tương ứng 95, 90, 85. `MinJpegQuality` có thể tăng lên tới 100 qua API; không cho phép dưới 85. Giới hạn sai số RMSE lấy mẫu khi mã hóa JPEG là 0,035 trên thang 0–1, so với ảnh đã resize.
4. Mở lại từng ứng viên, kiểm tra số trang, văn bản trích xuất, kích thước trang/crop/rotation/UserUnit, số ảnh và DPI. Chỉ giữ ứng viên nhỏ hơn kết quả tốt nhất đã kiểm tra. Nếu tất cả ứng viên lớn hơn nguồn, lưu bản sao nguồn.
5. Chỉ công bố file hoàn tất bằng thao tác thay thế/di chuyển file tạm. Hủy tác vụ dọn file tạm và giữ các kết quả đã hoàn tất.

DPI được đo từ ma trận pixel → trang theo chiều bị giãn lớn nhất, tính cả xoay, shear và `/UserUnit`. Với ảnh dùng nhiều lần, lần hiển thị lớn nhất quyết định mức resize. Làm tròn số pixel lên để không rơi dưới ngưỡng. Văn bản/vector không bị raster hóa; biểu mẫu, liên kết và metadata được giữ bởi PdfStamper.

iText lưu ma trận hiển thị bằng float; kích thước thập phân như 596,88 point có thể khiến DPI đúng 300 bị tính thành 299,99999. Service chuẩn hóa về DPI nguyên gần nhất chỉ trong phạm vi sai số số học tương đối 0,00002% (khoảng 0,00006 DPI ở 300). DPI thực sự thấp, ví dụ 299,99, vẫn được báo dưới ngưỡng. Khi DPI bằng ngưỡng, ảnh giữ nguyên số pixel và vẫn được thử nén JPEG.

## Không đạt mục tiêu

Trạng thái **Không đạt tỷ lệ giảm yêu cầu** xuất hiện khi file đã lưu còn lớn hơn mục tiêu. Bảng/CSV ghi dung lượng byte trước/sau/mục tiêu, phần trăm yêu cầu/thực tế, DPI cấu hình, DPI thấp nhất trước/sau và lý do. Kết quả là ứng viên nhỏ nhất qua kiểm tra trong các mức đã thử, không phải tối ưu toán học trên mọi thuật toán nén.

Ghi chú còn nêu kết quả thử JPEG ở mức cuối: chất lượng mã hóa, số byte ảnh thử so với ảnh gốc, hoặc lỗi kiểm tra chất lượng. Nếu JPEG nguồn đã nén mạnh hơn mức cho phép, mã hóa lại có thể làm ảnh lớn hơn dù vẫn giữ DPI; service giữ ảnh gốc và không tự hạ chất lượng dưới 85 để ép đạt tỷ lệ.

Nếu ảnh nguồn đã dưới ngưỡng DPI, chỉ tối ưu không mất chất lượng và ghi rõ không thể xác nhận đạt ngưỡng cấu hình. Không upscale để tạo DPI giả. `DpiSatisfied` và `QualitySatisfied` là false trong trường hợp này. Nếu đạt tỷ lệ giảm nhưng DPI nguồn thấp, trạng thái là **DPI đầu vào dưới ngưỡng**. PDF không có ảnh trong nội dung trang ghi **Không áp dụng** cho DPI.

## Giới hạn

- Nén ảnh có mất dữ liệu chỉ áp dụng cho ảnh DeviceRGB/DeviceGray 8 bit với bộ lọc Flate/DCT đơn giản. Ảnh inline, ảnh có mask/soft mask, ảnh có Decode, CMYK/ICC, ảnh được tham chiếu bởi annotation/form và ảnh trên 40 megapixel được giữ nguyên hoặc chỉ nén lại Flate không mất dữ liệu.
- DPI báo cáo áp dụng cho ảnh trong nội dung trang, không đo ảnh riêng trong appearance của annotation/form hoặc tệp đính kèm. Các ảnh đó được bảo vệ khỏi resize/nén JPEG.
- DPI và giới hạn JPEG/RMSE không bảo đảm đọc được mọi chữ nhỏ. Tài liệu nhạy cảm về hình ảnh vẫn cần đối chiếu bằng mắt. Kiểm tra tự động không thay thế thẩm định nội dung/OCR.
- PDF có chữ ký số hoặc mã hóa/mật khẩu được báo lỗi và không tạo bản chỉnh sửa. Chưa xác nhận conformance PDF/A sau nén; dùng kiểm định độc lập nếu cần lưu trữ PDF/A.
- Hủy được kiểm tra giữa các trang/ảnh/mức nén, không ngắt được thao tác giải mã ảnh đang chạy. Lỗi truy cập khi duyệt thư mục được báo ở cấp tác vụ.

## API và kiểm thử

```csharp
var options = new PdfCompressionOptions { MinDpi = 150, ReductionPercent = 40 };
var results = await new PdfCompressionService().ProcessAsync(@"D:\Input", @"D:\Output", options);
PdfCompressionService.ExportCsv(@"D:\Output\report.csv", results);
```

Giao diện tự xuất CSV sau khi chạy; có nút xuất lại. Chạy kiểm thử từ thư mục solution:

```powershell
./tests/Run-PdfCompressionTests.ps1 -Configuration Debug -Architecture x86
./tests/Run-PdfCompressionTests.ps1 -Configuration Release -Architecture x64
```

Các kiểm thử tạo PDF mẫu, xác minh dung lượng/DPI độc lập, văn bản/form/liên kết/metadata, ảnh dùng lại/inline/nested Form/mask/UserUnit, thư mục tiếng Việt, lỗi PDF, ghi đè, hủy và dọn file tạm. PDFium render trước/sau để kiểm tra trực quan. WinForms được kiểm tra bằng message pump với kiểm tra truy cập chéo luồng; ảnh giao diện được lưu ở kích thước tối thiểu và scale mô phỏng 125%/150%.
