# Trích xuất văn bản PDF

Mở **Trích xuất văn bản PDF** từ màn hình chính. Đây là form mới, độc lập với màn hình OCR vùng chọn cũ.

1. Chọn một PDF hoặc thư mục. Bật “Gồm thư mục con” nếu cần; các thư mục liên kết được bỏ qua.
2. Nhấn **Trích xuất**, chọn file JSON lưu kết quả. Mỗi file hoàn tất được lưu ngay. File lỗi được ghi nhận và các file sau tiếp tục.
3. Chọn tài liệu và trường trong bảng. Trang nguồn và vùng bằng chứng màu cam giúp đối chiếu. Có thể chuyển trang bằng ô “Trang PDF”.
4. Sửa giá trị, đánh dấu **Xác nhận** sau khi đối chiếu. Sửa lại giá trị sẽ bỏ xác nhận. Giá trị OCR ban đầu và đoạn nguồn vẫn được giữ.
5. **Lưu JSON** để lưu các sửa đổi; **Xuất Excel** tạo hai sheet thông tin và bằng chứng. Chỉnh sửa không tự lưu ngay khi gõ.
6. Để tiếp tục, mở JSON, chọn lại nguồn rồi trích xuất. Các file thành công được bỏ qua khi SHA-256 và chế độ OCR khớp; file thay đổi, file lỗi hoặc đổi chế độ được xử lý lại. Các kết quả đã mở được giữ trong bảng; dùng **Xóa bảng kết quả** trước một đợt độc lập.

## Xử lý

- .NET Framework 4.8; dùng iTextSharp đọc chữ/tọa độ, PDFium render theo tỷ lệ ở 300 DPI và Tesseract `vie` cục bộ. Không gửi tài liệu ra mạng.
- Quyết định đọc lớp chữ/OCR theo từng trang. Lớp chữ ít chữ, lỗi ký tự hoặc PDF có rotation metadata sẽ chuyển OCR. Có tùy chọn **Buộc OCR** cho lớp chữ sai nhưng vẫn vượt bộ lọc.
- OCR lấy từng từ, ghép theo dòng và khoảng cách cột. Khi chữ/bố cục có chất lượng thấp, thử xoay 90/180/270 độ rồi chọn kết quả theo điểm OCR và độ liền mạch dòng. Đây là heuristic, không bảo đảm mọi trang xoay được chọn đúng.
- Bằng chứng lưu trang PDF (bắt đầu từ 1), chữ gốc, phương pháp, độ tin cậy của OCR, X/Y/rộng/cao chuẩn hóa 0–1 với gốc trên trái. Khi có xoay OCR, tọa độ áp dụng trên ảnh sau khi xoay; góc xoay được lưu trong `RotationDegrees` và Excel. Preview dùng cùng hướng ảnh.
- Độ tin cậy OCR không phải xác suất trường đúng. Lớp chữ PDF mang giá trị 1 để chỉ nguồn đọc trực tiếp; kết quả trích xuất vẫn chưa được xác nhận.
- Trích xuất theo bố cục tương đối và quy tắc văn bản hành chính: số/ký hiệu; ngày ban hành; cơ quan/chủ quản; tên loại; trích yếu; nơi nhận/kính gửi; người ký, chức vụ và quyền hạn ký. Phân biệt trang PDF với trang in/tờ đánh số. Không tự suy ra số tờ hồ sơ không có trên tài liệu.
- Một PDF là một tài liệu. Số văn bản xuất hiện trên nhiều trang tạo cảnh báo tài liệu đính kèm; chưa tự tách thành nhiều bản ghi.
- JSON lưu nguyên văn OCR toàn bộ trang; Excel lưu giá trị hiện tại, giá trị tự động, trạng thái xác nhận và bằng chứng theo từng trường.
- Native OCR chỉ kiểm tra hủy giữa các lượt xử lý/từ/trang; phải đợi lượt OCR hiện tại hoàn tất. Các file hoàn tất đã lưu được giữ lại.

## Kiểm thử

Chạy từ thư mục repository:

```powershell
.\tests\Run-ExtractionTests.ps1 -Configuration Debug -Architecture x86
.\tests\Run-ExtractionTests.ps1 -Configuration Release -Architecture x64
```

Build/test dùng `tmp/extraction-build` để không đụng DLL của ứng dụng đang mở. Artifacts trong `tmp/extraction-tests/run-*` gồm PDF có chữ, scan, hỗn hợp, JSON, Excel, ảnh form và báo cáo. Tests kiểm tra đủ trang, tách cột, ngày viện dẫn/ngày ban hành, ngày sai, chữ ký ở trang cuối, tọa độ bằng chứng, scan xoay, xuất/đọc JSON, bản sửa thủ công, lỗi file, hủy, batch trên form và resume theo SHA-256. Dialog chọn file và tương tác bàn phím/chuột thực tế chưa được tự động kiểm tra.

Triển khai cần native PDFium/Tesseract đúng kiến trúc và `tessdata/vie.traineddata` như ứng dụng hiện tại. Có thể chạy bản build riêng tại `tmp/extraction-build/Release/CountFile2.exe`; rebuild bản bin thông thường sau khi đóng ứng dụng đang giữ DLL.

## Phần cần dữ liệu thực tế để nghiệm thu

Chưa có bộ PDF đại diện của đơn vị nên chưa công bố tỷ lệ chính xác. Chưa so sánh PaddleOCR, huấn luyện mô hình riêng, tích hợp AI hoặc tự tách văn bản ghép. Chữ viết tay, dấu đè, scan mờ/nghiêng nặng, khối ký nhiều người và mẫu chuyên ngành cần rà soát thủ công. Tiền xử lý hiện tại chưa có bộ chỉnh nghiêng/khử nhiễu chuyên dụng.

Bước nghiệm thu tiếp theo: gán nhãn khoảng 100–200 tài liệu thực tế, chia riêng tập điều chỉnh/nghiệm thu, đo chính xác từng trường, tỷ lệ cần sửa và tốc độ trên máy sử dụng; sau đó hiệu chỉnh bộ trích xuất hoặc thay `IOcrEngine`.

## Kết quả xác minh 2026-10-13

- Debug x86: PASS 37 assertions; artifacts `tmp/extraction-tests/run-6270e926a35640599aa2aa718d5fb4ee`.
- Release x64: PASS 37 assertions; artifacts `tmp/extraction-tests/run-1a644ec176814c65b2b486cd8facef31`.
- Regression chuyển đổi TIFF/PDF: PASS; artifacts `tmp/conversion-tests/run-8f4f11e5d42e46e1be713e39e919b37f`.
- Đã xem ảnh render form để kiểm tra bố cục. Build còn cảnh báo sẵn có trong Form1/frmXuLyOCR, không có lỗi build trong thư mục output riêng.
- Đây là xác minh chức năng bằng PDF sinh tự động, không phải nghiệm thu độ chính xác trên hồ sơ của đơn vị.
