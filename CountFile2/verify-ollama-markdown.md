# Xác minh luồng OCR → Markdown → Ollama

Ngày 04/11/2026. Model mặc định: `qwen3.5:4b`; endpoint `http://localhost:11434`.

## Thay đổi

- Luôn lưu Markdown trước bước LLM khi bật trích xuất; không phụ thuộc lựa chọn xuất JSON OCR.
- Đọc lại Markdown, thêm mã dòng vào nội dung gửi model. Bỏ metadata xuất và tên file; tọa độ chỉ giữ khi chữ nguồn khớp trên cùng trang. Escape Markdown/HTML được giải mã để kiểm tra giá trị.
- Ollama native `/api/chat` với JSON Schema; Qwen3 tắt thinking mặc định và có tùy chọn bật. Thêm nút chọn cấu hình `qwen3.5:4b` và lấy model local qua `/api/tags`.
- Prompt phiên bản `vi-administrative-markdown-2`: hướng dẫn vai trò trong bản khai, bỏ ví dụ chứa tên/ngày cụ thể.
- Cache và xác nhận phụ thuộc dấu vân tay Markdown. Markdown mất/đổi trong lúc xử lý không được báo hoàn tất. Giữ OCR, bản sửa và lịch sử nếu LLM lỗi/hủy.

## Kết quả

| Kiểm tra | Kết quả | Bằng chứng |
| --- | --- | --- |
| Build + suite LLM Debug | PASS 84 assertions | `tmp/ollama-llm-debug.log` |
| Build + suite LLM Release | PASS 84 assertions | `tmp/ollama-llm-release.log` |
| OCR fixture → Markdown đã lưu → Ollama HTTP giả lập → metadata → Excel | PASS trong suite trên; 5 sheet, 1 dòng/PDF | `tmp/llm-tests/llm-44b97231041a4873ab75a6c9e4cb032f/ollama-pipeline.xlsx` và `ollama-output/` |
| Escape/entity, ánh xạ tọa độ, thay đổi/xóa Markdown, cache, xác nhận | PASS trong suite trên | Assertions 46–53 |
| Lấy danh sách model, lọc cloud, payload Qwen, giao diện chọn model | PASS trong suite trên | Assertions 54–58 |
| Bố cục form cấu hình | Đã xem PNG: nhãn, nút chọn model và kiểm tra API đọc được, không chồng lấn | `tmp/llm-tests/llm-44b97231041a4873ab75a6c9e4cb032f/llm-settings.png` |
| Hồi quy OCR, Markdown và rà soát số trang | PASS integration; geometry 2/2, số trang ghi tay 7/7 | `tmp/ollama-vietocr-regression.log`, `tmp/vietocr-tests/run-be9ac838e59b46eb8f366e6646c5d037` |
| Ollama thật tại `127.0.0.1:11434/api/tags` | Chưa kết nối: TCP connection refused | Kiểm tra trực tiếp bằng HTTP client bỏ qua proxy; chưa gửi tài liệu |

Các kiểm thử LLM dùng HTTP/model giả lập và worker OCR fixture; không phải đánh giá độ chính xác `qwen3.5:4b`. Chưa phát hiện Ollama CLI ở PATH/vị trí cài đặt Windows thông thường. Chưa cài Ollama hoặc tải model trong lần triển khai này. Hướng dẫn khởi chạy có trong [ollama-ocr-markdown.md](ollama-ocr-markdown.md).

Lần build Debug đầu gặp file trung gian đang được ánh xạ; lần build và suite Debug/Release sau hoàn tất. Các cảnh báo build còn lại thuộc mã cũ (`ConfigurationSettings`, biến chưa dùng).

Hồi quy VietOCR lần đầu bị sandbox chặn thay thế cache trong TEMP mặc định. Chạy lại với TEMP/TMP trong workspace đã PASS; không thay logic ghi file của sản phẩm để né giới hạn sandbox.

## Tái lập

```powershell
.\tests\Run-LlmExtractionTests.ps1 -Configuration Debug
.\tests\Run-LlmExtractionTests.ps1 -Configuration Release
$env:TEMP = Join-Path (Get-Location).Path 'tmp/ollama-ocr-test-temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
.\tests\Run-VietOcrTests.ps1 -Configuration Debug
```

Để kiểm tra model thật, khởi chạy Ollama, tải `qwen3.5:4b`, chọn preset local trong Cấu hình LLM, kiểm tra API/JSON và chạy PDF mẫu. Rà soát đúng vai trò của từng trường trước khi chạy hàng loạt.
