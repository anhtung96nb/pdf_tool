# Việt OCR — OCR toàn văn PDF

Màn hình **Việt OCR** nhận một PDF hoặc thư mục PDF và xuất Markdown (`.md`), JSON hoặc cả hai. OCR tất cả trang, kể cả PDF đã có lớp chữ; không trích xuất riêng các trường văn bản hành chính.

## Sử dụng

1. Mở CountFile2, chọn **Việt OCR** trong thanh chức năng.
2. Chọn **Chọn PDF** hoặc **Chọn thư mục**; bật **Gồm thư mục con** nếu cần.
3. Chọn thư mục đích, hoặc để trống để lưu vào thư mục `viet-ocr` cạnh nguồn.
4. Chọn **Markdown + JSON**, **Markdown (.md)** hoặc **JSON (.json)**; mặc định Markdown + JSON, 300 DPI và CPU.
5. Kiểm tra đường dẫn Python, nhấn **Bắt đầu OCR**. Có thể chuyển màn hình khi chạy và dùng **Hủy** để dừng.
6. Chọn dòng kết quả để xem mã Markdown/JSON hoặc mở file. Xem trước tối đa 200.000 ký tự; file xuất giữ đầy đủ dữ liệu. Khi chỉ xuất JSON, khung xem trước tự chọn JSON.

Ví dụ `input/sub/a.pdf` xuất thành `output/sub/a.pdf.ocr.md` và `output/sub/a.pdf.ocr.json`. Giữ phần `.pdf` trong tên kết quả để dễ truy nguồn. Không thay đổi PDF nguồn. Mặc định không ghi đè kết quả cũ. File TXT đã xuất trước đây vẫn được giữ; service còn hỗ trợ `WriteText` để tương thích, nhưng màn hình mới dùng Markdown/JSON.

## Dữ liệu Markdown

- Có tiêu đề tài liệu, tổng số trang, tiêu đề `## Trang n / tổng số trang`, trạng thái từng trang và nội dung OCR theo thứ tự đọc.
- Giữ ngắt dòng bằng cú pháp Markdown hai dấu cách cuối dòng; giữ dòng trống giữa các đoạn và phân cách trang bằng đường kẻ.
- Lỗi của từng trang được ghi trong phần trích dẫn, không bỏ qua trang bị lỗi.
- Chữ OCR được xuất như nội dung nguyên văn, escape các ký tự Markdown/HTML để không tự tạo tiêu đề, link, ảnh hoặc bảng từ ký tự trong tài liệu.
- Chưa suy luận cấp tiêu đề và cấu trúc bảng của PDF. Đây là toàn văn được trình bày theo trang; JSON tiếp tục giữ tọa độ và điểm nhận dạng chi tiết.

File hỏng không làm dừng cả lô. Lỗi ở một trang được ghi trong dữ liệu; các trang khác tiếp tục xử lý. Khi hủy, giữ các PDF đã xuất hoàn chỉnh và bỏ kết quả tạm của PDF đang chạy. Không hỗ trợ tiếp tục từ giữa một PDF trong phiên bản này.

## Số trang viết tay ở góc trên bên phải

- Trên màn hình Việt OCR, **Đọc số trang viết tay** được bật sẵn. Có thể tắt nếu tài liệu không có số ghi tay. Tính năng này không bật cho màn hình PaddleOCR.
- Vùng mặc định là **20% chiều rộng bên phải × 10% chiều cao phía trên** của trang đã xoay đúng chiều đọc. Thay hai tỷ lệ 1–100% theo tài liệu và dùng **Xem vùng số** để kiểm tra trước khi OCR. Với thư mục, chọn một PDF trong bảng kết quả để xem vùng.
- Đây là một bước nhỏ trong **cùng yêu cầu OCR mỗi trang**, dùng lại ảnh render và model đã nạp. Cắt vùng số từ ảnh gốc, tìm cụm nét và đọc bằng VietOCR; không chạy lại toàn bộ PDF hoặc tải model khác.
- Nhãn chỉ gồm chữ số `0–9`, lưu dạng chuỗi để giữ `003`. Không tự đổi `O` thành `0`, ghép các nhóm số cách rời hoặc suy ra nhãn từ thứ tự PDF.
- Thử ảnh màu và ảnh xám trên tối đa sáu vùng ứng viên. Hai biến thể cùng đọc một chuỗi, điểm đều từ 0,9 và không có nhãn cạnh tranh thì ghi `recognized`; các trường hợp còn lại cần kiểm tra. Đây là ngưỡng ban đầu chưa hiệu chuẩn trên bộ chữ viết tay của người dùng; hai biến thể không phải phép đo độc lập.
- Tab **Số trang ghi tay** có thứ tự PDF, nhãn máy đọc, trạng thái và điểm. Chọn dòng để xem ảnh vùng số; khung đỏ là vùng ứng viên. Có thể lọc trang cần kiểm tra, nhập nhãn đúng rồi **Lưu / xác nhận**, hoặc chọn **Không có số** khi trang không đánh số.
- **Đọc lại số trang** chỉ nhận dạng lại nhãn của trang đang chọn với vùng/DPI/góc xoay hiện tại, không OCR lại toàn văn. Lệnh này nạp worker riêng cho thao tác kiểm tra; quá trình OCR lô vẫn chỉ dùng một worker đã nạp cho tất cả trang.
- Lưu/xác nhận cập nhật đồng thời các file đầu ra đã chọn, có rollback nếu một file bị khóa. Lịch sử và kết quả máy được giữ trong JSON. Không thay số thứ tự PDF, đổi tên file hoặc sắp lại trang.
- Markdown thêm dòng `**Số trang ghi tay:** 003`; JSON thêm `document_page_number` trong mỗi trang. Các trường chính: `raw_text`, `value`, `status`, `confidence`, `confirmed`, `source`, `candidates`, `warnings`, `roi_polygon_points`, `polygon_points`, `rotation_clockwise`; sửa thủ công có `confirmed_at_utc` và `history`.
- Trạng thái nhãn: `recognized`, `needs_review`, `not_found`, `error`, `disabled`. Nhãn máy đọc vẫn chưa được xác nhận thủ công. Vùng trống trả `not_found`, không tạo số; lỗi nhánh số trang không làm mất nội dung OCR toàn văn.
- Đối chiếu/sửa trong phiên làm việc dùng cache JSONL riêng trong thư mục tạm hệ thống, không tạo thêm file JSON đầu ra khi chỉ chọn Markdown. Cache được xóa khi xóa bảng bằng lần chạy mới hoặc đóng form bình thường. Lưu lượng bộ nhớ chỉ giữ metadata nhãn; việc cập nhật đọc lại cache từng trang để không nạp toàn bộ tài liệu vào RAM. Caller dùng service trực tiếp nên dispose các `VietOcrResult` sau khi sử dụng.
- Kiểm tra PDF nguồn bằng kích thước và thời điểm sửa để tránh đối chiếu nhầm khi file đã thay đổi. Các nhãn đã lưu vẫn nằm trong Markdown/JSON; giao diện kiểm tra hiện dùng kết quả của phiên chạy đang mở, chưa có chức năng mở lại kết quả cũ để sửa.

Chưa có bộ mẫu chữ viết tay thực tế được gán nhãn để đo độ chính xác hoặc fine-tune. Mẫu số mô phỏng chỉ kiểm tra luồng xử lý, không đại diện cho tất cả nét chữ, số gạch sửa hoặc tài liệu mờ.

## Cài môi trường trên máy khác

Yêu cầu Python 3.11 x64. Chạy trong PowerShell từ thư mục dự án:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File CountFile2\VietOcr\setup.ps1
```

Script tạo `.vietocr-venv`, cài thư viện và tải cấu hình/trọng số VietOCR. Có thể truyền `-Python` và `-EnvironmentPath` để chọn môi trường. Nếu dùng GPU, cài bản PyTorch/torchvision tương thích CUDA vào môi trường trước, rồi chọn **CUDA GPU**. Mặc định CPU hoạt động không cần CUDA.

Build lại ứng dụng sau khi chuẩn bị model. MSBuild sao chép worker và `VietOcr/models/` vào cạnh EXE. Khi phân phối, cần cả thư mục VietOcr và môi trường Python có các dependency; Python venv không phải một runtime có thể tùy ý di chuyển sang máy khác. Chọn đường dẫn `python.exe` trên màn hình nếu không dùng môi trường mặc định.

Chỉ bước cài đặt cần mạng. Worker dùng cấu hình/trọng số cục bộ, không tải model khi đang OCR. Trọng số được kiểm tra đọc được trước khi lưu; manifest ghi SHA-256. Không tự động cập nhật model.

## Dữ liệu JSON

- Cấp file: `schema_version`, `source`, `engine`, `model`, `device`, `dpi`, `rotation_clockwise`, `coordinate_origin`, `page_count`, `status`, `pages`.
- Cấp trang: số trang, kích thước PDF theo point, kích thước ảnh theo pixel, `status`, `text`, `lines`, `warnings`, và `error` khi lỗi.
- Cấp dòng: `id`, `reading_order`, `raw_text`, `text` chuẩn hóa NFC, `confidence`, `polygon_pixels`, `polygon_points`, `crop_rotation`, `warnings`.
- Tọa độ có gốc trên trái, theo ảnh PDF render ban đầu; được ánh xạ ngược khi chọn xoay trang. Một point bằng 1/72 inch.
- Trạng thái trang: `completed`, `needs_review`, `failed`. Trạng thái file: `completed`, `needs_review`, `completed_with_errors`.

Điểm nhận dạng chưa được hiệu chuẩn; ngưỡng 0,8 chỉ dùng đánh dấu cần kiểm tra. Không coi `completed` là chứng nhận chính xác nội dung. Markdown có nhãn trang và trạng thái; lỗi trang được ghi rõ. JSON vẫn giữ các dòng điểm thấp hoặc nhận dạng rỗng để người dùng kiểm tra.

## Giới hạn hiện tại

- Detector DBNet của RapidOCR phát hiện chữ, VietOCR `vgg_seq2seq` đọc chữ tiếng Việt; bộ phân loại góc hỗ trợ xoay dòng 180 độ. Trang nằm ngang có tùy chọn xoay 90/270 độ; chưa tự xác định góc xoay toàn trang.
- Dòng nghiêng được cắt theo tứ giác. Thứ tự đọc dùng khoảng trắng và nhóm dòng, cần kiểm tra với nhiều cột hoặc bố cục phức tạp.
- Bảng được xuất dưới dạng chữ và tọa độ; chưa khôi phục ô/hàng/cột. Không mô tả ảnh/biểu đồ/chữ ký hay chuyển công thức sang LaTeX. PDF nguồn là bản đối chiếu cho các vùng này.
- Chưa có trình sửa dòng/overlay, ảnh vùng độc lập hoặc fine-tune. Detector bỏ sót chữ vẫn có thể xảy ra; không cam kết đạt độ phủ tuyệt đối.
- Giới hạn 40 megapixel mỗi trang; trang vượt giới hạn được báo lỗi, cần giảm DPI. Mặc định chờ tối đa 300 giây cho lúc nạp model và mỗi trang; có thể chỉnh trên màn hình.

## Kiểm tra

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1 -RealOcr
```

Bộ kiểm thử mặc định dùng worker giả riêng để kiểm tra giao thức, render PDF, xuất Markdown/JSON/TXT, Unicode, escape Markdown/HTML, thư mục con, ghi đè/rollback, lỗi, timeout, hủy và điều hướng giao diện. Chế độ `-RealOcr` dùng detector và VietOCR thật trên PDF hai trang tiếng Việt và kiểm tra file Markdown/JSON. Kết quả mẫu và ảnh chụp layout nằm trong `tmp/vietocr-tests/`.

Nguồn mô hình: https://github.com/pbcquoc/vietocr ; detector: https://github.com/RapidAI/RapidOCR .
