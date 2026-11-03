# OCR Markdown và trích xuất bằng Ollama local

Cập nhật 04/11/2026. Model mặc định theo lựa chọn người dùng: `qwen3.5:4b`.

## Khởi chạy

Cài Ollama từ [trang chính thức](https://ollama.com/download/windows), mở ứng dụng Ollama và tải model:

```powershell
ollama pull qwen3.5:4b
ollama list
```

Nếu Ollama chưa chạy nền, chạy `ollama serve`. Không chạy thêm server nếu cổng 11434 đã được Ollama sử dụng. Model được tải một lần; ứng dụng OCR không tự tải model. Tên/tag tham chiếu: [qwen3.5:4b](https://ollama.com/library/qwen3.5:4b).

Mở `CountFile2/bin/Release/CountFile2.exe` cùng các DLL/thư mục đi kèm. Trong **Việt OCR → Cấu hình LLM**:

1. Bấm **Dùng Ollama · qwen3.5:4b** để thay cấu hình nhà cung cấp cũ nếu có.
2. Địa chỉ gốc `http://localhost:11434`, model `qwen3.5:4b`, không cần API key trên cài đặt local mặc định.
3. Bấm **Lấy danh sách model Ollama**, chọn đúng tag, rồi **Kiểm tra API / JSON** và **Lưu cấu hình**.
4. Đầu vào **Markdown OCR (văn bản)**; context 16384, đầu ra 4096, timeout gợi ý 300 giây. Thinking tắt mặc định cho Qwen3; có thể bật trong cấu hình. Tăng timeout nếu CPU xử lý chậm.

Kiểm tra JSON mẫu chỉ kiểm tra giao thức; cần chạy tài liệu thật để đánh giá độ chính xác.

## Luồng xử lý

1. Chọn PDF hoặc thư mục trong **Việt OCR**, bật **Trích xuất LLM sau khi lưu Markdown**.
2. OCR từng trang, đọc số trang ghi tay theo vùng cấu hình. Xuất `.pdf.ocr.md` UTF-8; nếu bật trích xuất, Markdown luôn được lưu, kể cả khi định dạng OCR được chọn chỉ là JSON. JSON OCR vẫn được xuất nếu đã chọn.
3. Sau khi lưu xong Markdown của PDF, đọc lại file này. Bỏ tiêu đề tên file/trạng thái/số ghi tay do ứng dụng thêm; giữ toàn bộ nội dung tài liệu theo trang và thêm `[mã dòng]` khi gửi. Giải mã escape/entity để kiểm tra giá trị theo đúng chữ gốc. Tọa độ chỉ được giữ với dòng khớp chính xác nội dung OCR trên cùng trang.
4. Gửi nội dung Markdown tới Ollama native `POST /api/chat`, `stream:false`, JSON Schema trong `format`, không gửi key Gemini, không tự chuyển sang cloud. Yêu cầu trích xuất được giới hạn trong phạm vi trang đã chọn. Tài liệu dài được chia phần và tổng hợp với bằng chứng gốc.
5. Kiểm tra schema, mã dòng, nhóm văn bản, ngày và chữ nguồn. Không đủ bằng chứng thì để trống/cần kiểm tra. LLM không tự xác nhận trường dữ liệu. Prompt phân biệt người khai/người xác nhận và ngày lập/ngày xác nhận của bản khai.
6. Lưu `.pdf.ocr.metadata.json`. Mở **Thông tin / Excel** để rà soát, sửa/xác nhận, lưu JSON và **Xuất Excel**. Bảng chính giữ **một dòng cho mỗi PDF**, cùng các sheet Trang số, Người ký, Cần kiểm tra, Bằng chứng.

Số trang PDF và nhãn ghi tay được bảo vệ riêng. Thiếu Ollama, thiếu model, hết thời gian chờ, hủy hoặc JSON không hợp lệ đều giữ file OCR đã lưu; có thể **Chạy lại LLM** mà không OCR lại. Markdown đã bị xóa không được tự thay bằng nội dung cũ trong bộ nhớ.

## Nội dung đã có và thay đổi nguồn

- **Nhập MD/TXT** trong màn hình trích xuất: chọn Markdown và PDF tương ứng. Nguồn ghép thủ công cần đối chiếu, không tự gán tọa độ.
- **JSON sau OCR**: nếu có `.md` cùng tên cạnh `.ocr.json`, dùng Markdown làm nội dung LLM và dùng JSON để đối chiếu tọa độ. JSON cũ không kèm Markdown được biểu diễn thành Markdown trong bộ nhớ để tiếp tục hỗ trợ dữ liệu cũ.
- Cache gồm dấu vân tay PDF/OCR/Markdown, phạm vi, endpoint, model, prompt, chế độ và thinking. Chạy lại từ nút riêng bỏ qua cache.
- Markdown đổi thì bỏ xác nhận của các trường ngữ nghĩa đã sửa để rà soát lại; giữ giá trị sửa và lịch sử. Dòng thay đổi không nhận tọa độ của dòng cũ. Markdown đổi trong khi model chạy thì không công nhận lượt đó là hoàn tất.

## Xác minh và giới hạn

Xem [biên bản kiểm thử](verify-ollama-markdown.md). Kiểm thử HTTP giả lập xác minh đường đi PDF → OCR → Markdown đã lưu → Ollama → metadata → Excel. Nó không chứng minh chất lượng trích xuất của `qwen3.5:4b`.

Tham chiếu API chính thức: [chat](https://docs.ollama.com/api/chat), [danh sách model](https://docs.ollama.com/api/tags), [structured outputs](https://docs.ollama.com/capabilities/structured-outputs).
