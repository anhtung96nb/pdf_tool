# Trích xuất văn bản PDF bằng LLM

Cập nhật 04/11/2026: mặc định OCR → Markdown → Ollama `qwen3.5:4b`; [khởi chạy và sử dụng](../../ollama-ocr-markdown.md). Bộ bóc tách theo quy tắc cũ đã bị gỡ theo yêu cầu. Màn hình được dùng trong **Thông tin / Excel** của Việt OCR/PaddleOCR và có thể mở độc lập.

Hướng dẫn đầy đủ: [Bóc tách sau OCR và Excel](../../post-ocr-metadata-excel.md). Kết quả kiểm thử hiện tại: [Xác minh LLM](../../verify-llm-extraction.md).

## Sử dụng

1. **Cấu hình LLM**: chọn Gemini API, Ollama hoặc server local tương thích; nhập model và key/endpoint tương ứng. Có thể để cấu hình model cho lần sau.
2. Nhập JSON OCR, MD/TXT hoặc PDF/thư mục. JSON dùng lại toàn văn/tọa độ sẵn có; PDF được đọc lớp chữ hoặc OCR trước khi gọi LLM.
3. Rà soát đề xuất LLM và bằng chứng. Sửa/xác nhận rồi **Lưu JSON** hoặc **Xuất Excel**. Năm sheet giữ một dòng/PDF trong bảng chính.
4. Với PDF ghép nhiều văn bản, chọn phạm vi văn bản chính và chạy lại. Bản sửa thuộc văn bản trước được giữ trong lịch sử.
5. Dùng **Chạy lại LLM** sau khi đã cấu hình model; không cần OCR lại. Nếu chưa cấu hình hoặc API lỗi, ứng dụng giữ kết quả OCR và trạng thái riêng của bước LLM.

## Thành phần

- `AdministrativeExtractionService`: chỉ đọc lớp chữ/PDF hoặc OCR tiếng Việt, ghép dòng/tọa độ. Không chọn cơ quan/ngày/số/người ký bằng các mẫu cũ.
- `PostOcrExtractionService`: nhập JSON/cache/MD/TXT, xác minh nguồn và hệ tọa độ.
- `ExtractionMetadata`: tạo trường trống, số trang PDF và liên kết nhãn ghi tay dùng chung.
- `LlmExtractionService`: chuẩn bị dữ liệu, chia/tổng hợp tài liệu, kiểm tra JSON/bằng chứng, cache, đối chiếu ảnh, bảo toàn sửa tay.
- `LlmMarkdownInput`: đọc file Markdown, tách nội dung theo trang, giải mã escape, ánh xạ đúng dòng về tọa độ và gắn mã nguồn khi gửi model. Markdown có dấu vân tay riêng.
- `LlmProviders`: payload riêng cho Gemini Interactions, Ollama native và local tương thích. Không chuyển cloud tự động.
- `LlmSettingsForm`/`LlmSettingsStore`: cấu hình và khóa được bảo vệ theo tài khoản Windows.
- `ExtractionReview`/`PostOcrReportExport`: rà soát, JSON và Excel năm sheet.

## Kiểm thử

```powershell
.\tests\Run-LlmExtractionTests.ps1 -Configuration Release
.\tests\Run-VietOcrTests.ps1
.\tests\Run-WinFormsNavigationTests.ps1
```

Hai script `Run-ExtractionTests.ps1` và `Run-PostOcrExtractionTests.ps1` cũ chuyển tới suite LLM mới. Các kiểm thử chọn trường theo quy tắc đã được xóa. Kết quả lịch sử 37/51 assertions chỉ áp dụng cho bản cũ và không chứng minh chất lượng bản LLM.

Chưa cài model local và chưa gửi tài liệu thật. Ngày 26/10/2026 đã kiểm tra Gemini bằng JSON mẫu theo yêu cầu: metadata model trả HTTP 200, sinh nội dung gặp 503/quá tải hoặc timeout; xem `verify-gemini-connection.md`. API giả lập xác minh hành vi chương trình; độ chính xác model, vision, context thực tế và tài nguyên local cần được kiểm tra sau trên tài liệu đã gán nhãn. Phần OCR vẫn cần PDFium/Tesseract và môi trường VietOCR/PaddleOCR như trước.
