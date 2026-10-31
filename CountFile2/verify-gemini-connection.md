# Kiểm tra lỗi Gemini — 26/10/2026

Người dùng yêu cầu kiểm tra khóa Gemini và model `gemini-3.8-flash`. Chỉ dùng khóa trong bộ nhớ để xác thực với `generativelanguage.googleapis.com`; không đưa khóa vào mã nguồn, fixture, báo cáo hoặc cấu hình lưu trên máy. Không gửi PDF, ảnh hoặc nội dung OCR thật.

## Kết quả kiểm tra trực tiếp

| Yêu cầu | Kết quả |
| --- | --- |
| Đọc metadata `GET /v1beta/models/gemini-3.8-flash` bằng khóa được cung cấp | HTTP 200; `name=models/gemini-3.8-flash`, có phương thức `generateContent` |
| JSON mẫu qua Interactions với payload ban đầu | HTTP 503; thông báo model đang có nhu cầu cao |
| JSON mẫu qua Interactions, bỏ tham số temperature | Hết thời gian chờ sau 40 giây |
| JSON mẫu qua `HttpLlmExtractionProvider` của bản Release đã sửa | Hết thời gian chờ sau 35 giây; mã báo lỗi đã được kiểm tra |
| Đối chiếu JSON mẫu qua `generateContent`, cùng khóa/model | HTTP 503 `UNAVAILABLE`; model đang có nhu cầu cao |

Khóa xác thực được cho thao tác đọc metadata và tên model đúng. Lỗi sinh JSON quan sát được là quá tải/timeout; chưa có một phản hồi sinh JSON thành công, nên không tuyên bố đã nghiệm thu toàn bộ quyền sinh nội dung, trích xuất hoặc độ chính xác model. Hai đường gọi đều gặp vấn đề dịch vụ; không tự đổi model hoặc provider.

Đối chiếu tài liệu chính thức: [Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash), [Interactions API](https://ai.google.dev/api/interactions-api), [generateContent](https://ai.google.dev/api/generate-content), [Gemini API keys](https://ai.google.dev/gemini-api/docs/api-key).

## Thay đổi ứng dụng

- Thông báo kiểm tra API giữ mã HTTP, mã lỗi Google và phần giải thích đã che khóa. Lỗi 503 được chỉ rõ là quá tải/tạm thời chưa sẵn sàng; phân biệt với 401/403, 404, 429, DNS và TLS.
- Nút kiểm tra thử lại có giới hạn theo `MaxAttempts` (mặc định 3), tôn trọng `Retry-After`, không retry lỗi thông tin xác thực/schema và không tự chuyển model/provider. Trạng thái có thể cuộn và sao chép.
- Bỏ `temperature` khỏi `generation_config` của Interactions để khớp hợp đồng được công bố hiện tại. Đây là điều chỉnh tương thích; không khẳng định tham số này gây ra lỗi 503 đã quan sát.
- Đổi phiên bản giao thức trong khóa cache Gemini để không dùng nhầm cache của payload cũ. Các sửa tay, xác nhận, OCR và local provider được giữ.

## Kiểm thử

- Release: PASS **67 assertions**, `tmp/gemini-fix-tests.log`, artifacts `tmp/llm-tests/llm-f281f32a8934490fbef4616273053cb0`.
- Debug: PASS **67 assertions**, `tmp/gemini-fix-debug.log`, artifacts `tmp/llm-tests/llm-9df9a04819a547a8bbf3d53b366544b0`.
- Thêm kiểm thử payload đúng hợp đồng; lỗi 503 có/thiếu status; che cả khóa Gemini/local; lỗi DNS; giao diện retry tối đa ba lần, phục hồi khi dịch vụ phản hồi và không retry 401.
- Đã xem ảnh `gemini-overload.png`: mã HTTP/model và lý do đọc được, vùng thông báo cuộn được. Ảnh chỉ dùng khóa giả đã che.
- Build không lỗi; chỉ còn cảnh báo sẵn có trong `Form1`/`frmXuLyOCR`.

Mở lại `CountFile2/bin/Release/CountFile2.exe`, chọn **Gemini API**, model `gemini-3.8-flash`, nhập khóa trong ứng dụng và bấm **Kiểm tra API / JSON**. Khi Google vẫn báo 503, thử lại sau; tăng timeout chỉ giúp trường hợp phản hồi chậm, không khắc phục được quá tải dịch vụ. Chưa đánh giá trên hồ sơ/PDF thật.
