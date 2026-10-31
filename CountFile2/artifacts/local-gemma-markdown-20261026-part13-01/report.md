# Kiểm tra Gemma local với Markdown

**Kết luận: kết nối thành công; kết quả bóc tách chưa đạt và chưa được chấp nhận.**

## Cấu hình đã xác minh

- Server llama.cpp qua LlamaApp đang chạy; model đã được nạp.
- Provider trong Việt OCR: **API local tương thích OpenAI**.
- Base URL: `http://127.0.0.1:9931/v1`.
- Model: `ggml-org/gemma-3-1b-it-qat-GGUF:Q4_0`.
- API key local: để trống cho server hiện tại.
- Chế độ đầu vào: **Chỉ nội dung OCR**. Server báo model chỉ nhận text.
- Context đang nạp: **32.768 token**.
- Chỉ gọi localhost, không gửi tài liệu tới Gemini/cloud. Không đổi cấu hình đã lưu của ứng dụng.

## Kiểm tra kết nối

JSON mẫu `{"status":"ok"}` nhận HTTP 200 trong **0,70 giây**, 25 input token và 11 output token. Đã kiểm tra thêm bằng chính `HttpLlmExtractionProvider` trong bản Release của ứng dụng .NET: **PASS**, đúng model và endpoint trên.

## Kiểm tra bóc tách

Đầu vào là toàn bộ `Scan2026-03-10_141605_Part13.pdf.ocr.md`: **2 trang, 119 dòng có mã bằng chứng**. Không gửi ảnh/PDF. Markdown nguồn giữ nguyên SHA-256 `8344f20c4186163217ea1feab449c2cb725efacbb8a056c9b6ee7cdbdaa0a9da`.

| Thử nghiệm | HTTP | Thời gian | Kết quả kiểm tra |
|---|---|---|---|
| Prompt/schema đầy đủ, lượt 1 | 200 | 108,48 giây | JSON parse được nhưng có giá trị không kèm bằng chứng; từ chối |
| Một lượt yêu cầu sửa schema/bằng chứng | 200 | 89,31 giây | Tạo nhóm trang 3–4 ngoài tài liệu 2 trang; từ chối |
| Prompt/schema rút gọn còn 7 trường chính | 200 | 28,30 giây | Thiếu mã bằng chứng ở cả 7 trường và có giá trị chọn sai; từ chối |

Các request đã hoàn tất `finish_reason=stop`; đây không phải lỗi dịch vụ 503 hoặc JSON bị cắt dở. JSON hợp lệ cú pháp không bảo đảm nội dung đúng.

## Lỗi nội dung quan sát được

- Lấy ngày trong tên file `2026-03-10` làm ngày ban hành; lượt rút gọn còn trả phần tên file vào trường ngày.
- Nhầm **Mẫu số 12** hoặc số đầu trang thành số/ký hiệu văn bản.
- Lượt đầu đưa câu ví dụ **Căn cứ Quyết định ngày 01/01/2020** từ prompt vào kết quả dù câu này không có trong Markdown tài liệu.
- Lượt yêu cầu sửa tạo thêm nhóm văn bản ở trang **3–4**, trong khi nguồn chỉ có trang **1–2**.
- Lượt rút gọn nhận được tiêu đề/trích yếu có chữ tương ứng trong nguồn, nhưng không cung cấp bằng chứng; các trường khác còn nhầm người được nhắc trong hồ sơ với người ký và lấy nhãn trường làm giá trị.

## Diễn giải

Model này chạy local và dùng được giao thức JSON của ứng dụng. Với mẫu Markdown có lỗi OCR và các prompt đã thử, chưa phù hợp để tự động điền các trường cơ quan/ngày/số/người ký mà thiếu bước đối chiếu. Không đánh đồng việc API trả HTTP 200 với việc trích xuất chính xác.

Thử nghiệm rút gọn là thí nghiệm riêng, không sửa prompt/schema sản phẩm. Chưa đánh giá một model lớn hơn hoặc kiểm tra ảnh/PDF gốc. Đây là quan sát trên một tài liệu, không phải tỷ lệ chính xác tổng quát của model.

## File đối chiếu

- `connection-check.json`: kết quả kiểm tra API và adapter .NET.
- `request-01.json`, `request-02.json`: toàn bộ request dùng prompt/schema đầy đủ.
- `model-output-01.json`, `model-output-02.json`: phản hồi model gốc, chưa được chấp nhận.
- `result.json`: trạng thái thử schema đầy đủ và lỗi kiểm tra.
- `compact-request.json`, `compact-model-output.json`, `compact-result.json`: thử prompt/schema rút gọn.
- `input-with-line-ids.md`, `evidence-map.json`: toàn văn đã gửi và mã dòng/trang.
- `review.json`: kết luận tổng hợp, tách kết nối thành công khỏi kết quả trích xuất không đạt.