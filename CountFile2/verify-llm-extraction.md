# Xác minh chuyển trích xuất sang LLM

> Cập nhật 04/11/2026: đã triển khai OCR → Markdown → Ollama `qwen3.5:4b`, PASS 84 kiểm tra Debug và Release. Ollama thật tại localhost:11434 chưa chạy; chưa đánh giá model này trên tài liệu. Xem [biên bản mới](verify-ollama-markdown.md).

> Cập nhật 26/10/2026 theo yêu cầu kiểm tra Gemini: đọc metadata model bằng khóa được cung cấp thành công HTTP 200; sinh JSON gặp 503/quá tải hoặc timeout. Đã sửa thông báo/retry và chạy 67 kiểm thử trên Debug/Release. Xem `verify-gemini-connection.md`. Báo cáo 58 assertions bên dưới là lần xác minh trước đó bằng mock, không phải kết quả kiểm tra API mới.

Ngày: 26/10/2026. Theo yêu cầu, không cài LLM, không dùng API key thật và không gửi tài liệu thật đến Gemini/local model. Không công bố độ chính xác trích xuất.

## Bản triển khai

Đã xóa `PostOcrExtractionRules.cs`, `AdministrativeExtractionService.Extract/ExtractBaseline` và hai bộ test chọn trường cũ. Đọc PDF/OCR, nhập JSON/cache/MD/TXT, rà soát, sửa/xác nhận, số ghi tay và xuất Excel được giữ để dùng với LLM. Không có fallback ngầm về quy tắc.

Tích hợp cấu hình Gemini Interactions, Ollama native và endpoint local tương thích, JSON Schema và bằng chứng; có chia/tổng hợp tài liệu dài, ảnh khi cần, PDF Gemini có giới hạn, ngân sách request/ảnh/timeout/retry, cache theo nguồn/cấu hình và DPAPI cho khóa. Các bước OCR và LLM có trạng thái riêng.

## Đã chạy

| Kiểm tra | Kết quả | Bằng chứng |
| --- | --- | --- |
| Release build và `tests/Run-LlmExtractionTests.ps1 -Configuration Release` | PASS 58 assertions | `tmp/llm-release.log`, `tmp/llm-tests/llm-94caf1ed422848e1978754cb1adadca5/verification.txt` |
| Debug build và `tests/Run-VietOcrTests.ps1` | PASS integration; geometry 2/2; handwritten page numbers 7/7 | `tmp/vietocr-regression.log`, `tmp/vietocr-tests/run-4d9200bb470c4eda9d7a211617716903` |
| `tests/Run-WinFormsNavigationTests.ps1` | PASS cached screens/state, job guard, cancellation, shutdown | `tmp/navigation-llm.log`, `tmp/winforms-navigation-tests/run-d29546f0f09040e5a95801fe9a2c1cc2` |
| Kiểm tra bố cục form cấu hình/rà soát | Đã xem PNG render; nhãn/nút đọc được, không chồng lấn | `tmp/llm-tests/llm-94caf1ed422848e1978754cb1adadca5/llm-settings.png`, `llm-review.png` |
| Kiểm tra tham chiếu mã cũ | Không còn `PostOcrExtractionRules`, `ExtractBaseline`, `AdministrativeExtractionService.Extract` trong C#/script sản phẩm và tests | `rg` không có kết quả |

Build không có lỗi. Các cảnh báo `ConfigurationSettings.AppSettings` obsolete và biến không dùng trong `Form1`/`frmXuLyOCR` đã tồn tại trước thay đổi này.

## Nội dung 58 assertions

- Nạp OCR chỉ chuẩn bị nguồn, không chạy bộ chọn trường cũ; thiếu model báo chưa cấu hình và không gọi API.
- Ánh xạ cơ quan/số/ngày/người ký từ phản hồi giả lập, giữ `003`, ghép đúng signer ID; bảo vệ số ghi tay đã xác nhận.
- Từ chối schema thiếu khóa, mã bằng chứng bịa, trường metadata khóa, ngày sai lịch và giá trị không có trong chữ nguồn. Một lượt sửa JSON tối đa trong ngân sách tổng.
- Không ghép signer của nhóm văn bản phụ vào nhóm chính; không có nhóm chính rõ thì cần kiểm tra.
- Giữ sửa tay, kể cả người ký thứ hai model không tìm thấy lại; đổi OCR bỏ xác nhận; đổi phạm vi lưu bản sửa cũ vào lịch sử.
- Cache cùng nguồn/model có hiệu lực; đổi model gọi lại. Ảnh thật được render từ PDF fixture; đề xuất từ ảnh được đánh dấu chưa xác nhận. Vượt giới hạn ảnh không báo hoàn tất giả.
- Chia/tổng hợp đủ 20 trang fixture; hết ngân sách không chấp nhận kết quả một phần. Retry lỗi tạm thời có giới hạn; lỗi cấu hình không retry; hủy giữ bản sửa.
- DPAPI round-trip và khóa không có trong JSON thuần của options. Payload, auth, schema, image mapping, usage, trạng thái hoàn tất của ba giao thức qua `HttpMessageHandler` giả lập; local không nhận Gemini key. Phản hồi cắt dở/server từ chối schema được báo lỗi.
- Excel năm sheet, một dòng/PDF, ngày kiểu ngày, số kiểu chuỗi, provenance, bằng chứng/lịch sử; nội dung `=` là text; file khóa/ô vượt giới hạn giữ nguyên file trước.
- Nhập JSON/MD/TXT, nguồn/tọa độ; giao diện chạy lại qua HTTP handler giả lập, nhập lại không reset kết quả LLM mới; job guard được giải phóng.
- OCR worker giả lập tự động tạo metadata ngay cả khi chỉ xuất Markdown và chưa có LLM; OCR ghi đè giữ sửa tay; hủy bước bóc tách giữ file OCR đã xuất; PDF thay đổi bị phát hiện.

## Phạm vi chưa xác nhận

Request/response được kiểm tra bằng provider và HTTP handler giả lập, không phải kết nối thành công tới server/model đang hoạt động. Thử loopback HTTP riêng chưa kết nối được trong môi trường thực thi, nên không dùng nó làm bằng chứng nghiệm thu mạng. Người dùng đã yêu cầu để kiểm tra LLM thực tế sau.

Chưa đo chất lượng Gemini/local, vision, độ chính xác theo trường, context thực tế, tốc độ, RAM/VRAM hoặc chi phí trên tài liệu đơn vị. Chưa cài model, chọn model thay người dùng hay tự bật dịch vụ. Model/schema cụ thể phải được thử bằng **Kiểm tra API / JSON**, sau đó bộ PDF đã gán nhãn. Các kiểm thử tổng hợp ở trên không chứng minh khả năng đọc đúng cơ quan/ngày/người ký của model thật.

Bản chạy: `CountFile2/bin/Release/CountFile2.exe` cùng toàn bộ DLL/tài nguyên cạnh nó. Hướng dẫn: `post-ocr-metadata-excel.md`.
