# PaddleOCR PDF tiếng Việt

Mở menu **PaddleOCR**, chọn một PDF hoặc thư mục, chọn thư mục đích rồi Bắt đầu OCR.
Mặc định xuất JSON UTF-8 (`file.pdf.ocr.json`) vào `paddle-ocr` cạnh nguồn.
Có thể xuất thêm Markdown; giữ cấu trúc thư mục khi chạy hàng loạt.
JSON chứa engine/model, trang, văn bản NFC, điểm nhận dạng, polygon pixel và tọa độ PDF
(points, gốc trên trái), trạng thái/cảnh báo. Không khôi phục cấu trúc ô bảng.
Dòng có điểm dưới 0,8 và trang không phát hiện chữ được đánh dấu cần kiểm tra.
Điểm chưa hiệu chuẩn và không bảo đảm đúng dấu; cần đối chiếu bản gốc.

Worker dùng CPU 4 luồng, tắt MKLDNN để tránh lỗi oneDNN/PIR trên Windows
với PaddlePaddle 3.3.0. Ảnh phát hiện chữ được giới hạn cạnh dài 1.600 pixel;
tọa độ kết quả vẫn quy về ảnh render và trang PDF gốc. Chữ quá nhỏ cần kiểm tra.

## Cài trên Windows x64

Cần Python 3.11 x64 và Internet để cài thư viện, tải model lần đầu:

```powershell
powershell -ExecutionPolicy Bypass -File CountFile2/PaddleOcr/setup.ps1
```

Script tạo `.paddleocr-venv` riêng, không dùng môi trường VietOCR.
Trong màn hình chọn `.paddleocr-venv/Scripts/python.exe` nếu chưa được tìm tự động.
Model được PaddleX lưu trong cache người dùng; lần đầu khởi động có thể lâu.
Script mặc định cài CPU. GPU cần thay paddlepaddle bằng paddlepaddle-gpu phù hợp
CUDA theo hướng dẫn PaddlePaddle trước khi chọn CUDA GPU trong màn hình.
Nếu khởi động vượt thời gian chờ, tăng Chờ/trang hoặc chạy setup để tải trước.

## Lựa chọn phiên bản (24/10/2026)

Khóa PaddleOCR 3.7.0 và PaddlePaddle 3.3.0. Chọn tường minh detector
`PP-OCRv5_server_det` và recognizer `latin_PP-OCRv5_mobile_rec` vì tài liệu
chính thức liệt kê Vietnamese (`vi`) trong nhóm Latin PP-OCRv5.
Không dùng mặc định model ngôn ngữ khác và không tự đổi model khi nâng package.
Đây là lựa chọn có hỗ trợ tiếng Việt được công bố, chưa phải kết luận model
chính xác nhất trên tập scan của ứng dụng; cần đánh giá bằng PDF đại diện.

Nguồn:
- https://github.com/PaddlePaddle/PaddleOCR/blob/main/docs/version3.x/algorithm/PP-OCRv5/PP-OCRv5_multi_languages.en.md
- https://pypi.org/project/paddleocr/3.7.0/
- https://paddlepaddle.github.io/PaddleX/latest/en/installation/paddlepaddle_install.html
- https://github.com/PaddlePaddle/PaddleOCR/blob/v3.7.0/paddleocr/_pipelines/ocr.py

Kiểm tra adapter không cần tải model: `.vietocr-venv/Scripts/python.exe tests/test_paddleocr_adapter.py`.

Đã build Debug, qua bộ kiểm thử tích hợp VietOCR và 3 kiểm thử adapter.
Ngày 25/10/2026 đã chạy model thật qua dịch vụ PDF trên Windows CPU:
PDF tiếng Việt 2 trang, 2/2 trang hoàn tất, không lỗi, JSON ghi đúng engine,
nhận dạng được tiêu đề “THÔNG BÁO”. Đây là smoke test, chưa phải benchmark chất lượng.
Có thể chạy lại với PDF fixture 2 trang được tạo bởi bộ VietOcrTests:
`powershell -ExecutionPolicy Bypass -File tests/Run-PaddleOcrSmoke.ps1 -Pdf <PDF-fixture>`.
