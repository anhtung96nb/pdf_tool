# Làm sạch và kiểm tra chất lượng PDF

## Sử dụng trong ứng dụng

Mở **Làm sạch PDF**, chọn một PDF hoặc thư mục nguồn và một thư mục đầu ra khác nguồn. Mặc định **Chỉ phân tích** được bật: không tạo PDF, nhưng giao diện tự lưu hai báo cáo CSV (toàn bộ kết quả và các trang chất lượng thấp). Chọn file/trang để đối chiếu ảnh gốc với ảnh dự kiến. Bỏ chọn **Chỉ phân tích** và chạy lại để xuất bản chỉnh sửa.

Các tùy chọn độc lập: xóa trang trắng, căn thẳng, kiểm tra chất lượng, thư mục con, ghi đè đầu ra. Bật **OCR hướng / chất lượng** để dò hướng 90/180/270 độ; mặc định dùng `vie` và thư mục `tessdata` cạnh ứng dụng. Có thể nhập ngôn ngữ và đường dẫn khác với traineddata tương ứng. OCR được dùng để phân tích, không thêm hoặc thay thế lớp chữ PDF.

PDF xuất có tên `*.clean.pdf`, giữ cấu trúc thư mục con và không thay thế file nguồn. Thư mục đầu ra nằm trong nguồn và file `*.clean.pdf` được bỏ qua khi quét thư mục. Báo cáo tự động có tên duy nhất; nút Xuất CSV cho phép lưu lại kết quả hiện tại hoặc chỉ danh sách chất lượng thấp. Báo cáo ghi số trang gốc/giữ, số trang gốc/đích, hành động dự kiến, góc nghiêng, điểm tin cậy, DPI ảnh, tương phản, độ sắc nét, OCR, cảnh báo và lỗi. `TrangDich=0` là trang được đề xuất xóa; nếu chỉ phân tích, đây là ánh xạ dự kiến.

Hủy giữ các PDF hoàn tất và kết quả của chúng. File đang xử lý không được công bố; file tạm được dọn. Một file hỏng, cần mật khẩu hoặc lỗi xuất không dừng các file khác. Tác vụ dùng chung khóa với OCR và chuyển đổi để tránh chạy đồng thời trên giao diện.

## Chính sách bảo toàn nội dung

- Chỉ tự xóa trang có nền sáng đồng nhất ở ảnh render, không có chữ PDF và không có chú thích/form. Không suy ra trang trắng từ OCR rỗng. Trang trắng scan có nhiễu, chữ cực mờ và trang có vài điểm nội dung được giữ lại; đây là lựa chọn bảo thủ để tránh xóa nhầm.
- Căn nghiêng dùng phép chiếu ngang các điểm ảnh. Mặc định phân tích 150 DPI, tìm góc ±7°, bước 0,25°, chỉ chỉnh góc từ 0,3° khi độ cải thiện ít nhất 0,12 và đỉnh đủ nổi bật. Góc chạm biên phạm vi hoặc độ tin cậy thấp được giữ nguyên và ghi cảnh báo.
- Dò hướng OCR thử bốn hướng bằng chế độ khối chữ ngang. Chỉ đổi hướng nếu hướng tốt nhất có confidence ≥0,80, cao hơn hướng kế tiếp ≥0,15 và có ít nhất 60 ký tự chữ. Trang ít chữ hoặc bố cục phức tạp có thể cần kiểm tra thủ công.
- Căn nghiêng biến đổi content stream PDF và mở rộng MediaBox/CropBox để không cắt vùng nhìn thấy của trang gốc. Giữ chữ có thể chọn, vector, ảnh và resources; không raster hóa toàn bộ tài liệu. CropBox gốc được áp dụng làm clipping path để nội dung vốn bị crop không xuất hiện trở lại. Kích thước trang sau chỉnh có thể tăng.
- Trang có annotations/form được giữ nguyên hình học. PDF có dấu hiệu chữ ký số chỉ được phân tích và không xuất bản chỉnh sửa. Đây là kiểm tra sự hiện diện chữ ký, không xác minh tính hợp lệ mật mã của chữ ký.
- Kiểm tra PDF đầu ra bằng cả iTextSharp và PDFium, kiểm tra số trang và render thử mọi trang trước khi công bố file. Ghi tạm rồi di chuyển/thay thế đầu ra đã tồn tại khi có tùy chọn ghi đè.

## Chất lượng thấp

Đánh giá từng trang, sau đó file có ít nhất một trang bị gắn cờ sẽ xuất hiện trong danh sách chất lượng thấp:

| Chỉ số | Giá trị mặc định | Ý nghĩa |
|---|---|---|
| DPI ảnh nguồn | <150 DPI, dung sai 2% | Tính từ kích thước pixel ảnh và ma trận đặt ảnh trong PDF; không dùng DPI render. Chỉ xét ảnh chiếm ít nhất 25% diện tích trang để tránh logo nhỏ. |
| Tương phản | <45 mức xám | Độ chênh giữa nền và vùng nét/chữ. |
| Độ sắc nét | <150 | Năng lượng Laplacian quanh vùng nét ở ảnh phân tích; gắn cờ ảnh có dấu hiệu mờ, cần đối chiếu. |
| OCR (khi bật) | confidence <0,45 | Dấu hiệu đọc kém; không phải kết luận chắc chắn về chất lượng. |

Trang chỉ có chữ/vector không bị đánh giá DPI ảnh hay độ mờ như scan. Các điểm số là heuristic, cần hiệu chỉnh trên hồ sơ thực tế. Không xóa trang vì chất lượng thấp. Không phục hồi thông tin đã mất, không sửa phối cảnh/trang sách cong, không tự căn lại lề.

## API

```csharp
var options = new PdfCleanupOptions {
    AnalyzeOnly = false,
    UseOcr = true,
    Recursive = true
};
var results = await new PdfCleanupService().ProcessAsync(
    @"D:\Input", @"D:\Output", options, cancellationToken, progress);
PdfCleanupService.ExportCsv(@"D:\Output\report.csv", results);
PdfCleanupService.ExportCsv(@"D:\Output\low-quality.csv", results, lowQualityOnly: true);
```

Service không tự xuất CSV; giao diện tự xuất hai báo cáo sau khi hoàn tất/hủy. `PdfCleanupCanceledException.Results` chứa snapshot kết quả đã hoàn tất. Lỗi đường dẫn/tùy chọn/quét thư mục được ném ra, lỗi từng PDF được ghi ở `PdfCleanupResult.Error`.

## Kiểm thử

```powershell
./tests/Run-PdfCleanupTests.ps1 -Configuration Debug -Architecture x86
./tests/Run-PdfCleanupTests.ps1 -Configuration Release -Architecture x64
```

Fixture tự tạo gồm PDF chữ/vector, trang trắng, chữ mờ, dấu/số trang nhỏ, annotations, scan DPI thấp, scan tương phản thấp, scan mờ, scan nghiêng và các hướng xoay. Kiểm tra số trang và ánh xạ gốc/đích, nội dung chữ, góc nghiêng sau render, bảo toàn nguồn, toggle, overwrite, quét đệ quy, loại trừ đầu ra, file hỏng/mật khẩu, dấu hiệu chữ ký, hủy/kết quả dở dang, dọn tạm khi xuất thất bại, CSV, chạy WinForms và điều hướng màn hình.

Ảnh `before.png`, `after.png`, `form.png` và PDF mẫu ở `tmp/pdf-cleanup-tests/run-*` phục vụ kiểm tra trực quan. Script build dùng thư viện sẵn có, không thêm dependency mới. Kết quả trên fixture không thay thế việc hiệu chỉnh ngưỡng bằng tập PDF nghiệp vụ.

### Kết quả xác minh ngày 2026-10-13

- Debug x86 và Release x64: mỗi cấu hình qua 49 assertions của `PdfCleanupTests`.
- Trang chữ/vector nghiêng 3° và trang scan nghiêng được chỉnh với sai lệch còn lại ≤0,5°; mẫu chữ/vector đo lại là 0°. Bốn hướng 0/90/180/270° đều xuất đúng hướng khi bật OCR.
- Fixture 9 trang chỉ loại 1 trang trắng; giữ chữ mờ, dấu/số trang nhỏ và chú thích. PDF đầu ra có 8 trang, giữ chữ có thể chọn và các trang dùng chung content stream.
- `Run-WinFormsNavigationTests.ps1 -Configuration Debug` qua: tái sử dụng màn hình, giữ trạng thái, ba chế độ PDF/OCR/PDF-A, khóa tác vụ, đổi màn hình khi chạy, hủy và đóng ứng dụng an toàn.
- Render độc lập cả 8 trang của `output/pdf/cleanup-demo.pdf` bằng Poppler và kiểm tra bằng pypdf: số trang, chữ và annotations đúng; đã xem contact sheet và ảnh trang căn thẳng. Trang mờ/tương phản thấp trong PDF mẫu là fixture chủ động và vẫn được giữ để báo cáo.
- Build thành công. Các warning có sẵn ở thống kê/OCR cũ vẫn còn; không phát sinh warning từ các file chức năng mới.
