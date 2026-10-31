# Thử trích xuất từ Markdown bằng Gemini

- Model yêu cầu: `gemini-3.5-flash`.
- Model API báo: `None`.
- Trạng thái: **failed**.
- Chỉ gửi Markdown của 2 trang; chưa gửi hoặc đối chiếu ảnh/PDF.
- Tên gemini-3.5-flash đã deprecated; Google công bố tự định tuyến sang gemini-3.6-flash.
- Dữ liệu OCR có lỗi; mọi giá trị đều cần rà soát, chưa được xác nhận.

- Lượt 1: interactions; HTTP —; 4.47 giây. ('Connection aborted.', RemoteDisconnected('Remote end closed connection without response'))
- Lượt 2: interactions; HTTP 503; 2.44 giây. gemini-3.5-flash is currently experiencing high demand, spikes in demand are usually temporary. Please try again later.
- Lượt 3: generateContent; HTTP 503; 1.50 giây. This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later.

Chưa nhận được kết quả trích xuất hợp lệ. Không dùng phản hồi một phần làm kết quả.

Đây là thử nghiệm một tài liệu; chưa so sánh độ chính xác hoặc token với đầu vào JSON trên cùng model.
