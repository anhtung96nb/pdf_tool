# Bóc tách thông tin bằng LLM và báo cáo Excel

Cập nhật 04/11/2026: luồng mặc định **OCR → Markdown → Ollama `qwen3.5:4b` → JSON/Excel**. Xem [hướng dẫn Ollama](ollama-ocr-markdown.md) để khởi chạy và chuyển cấu hình cũ. Bộ chọn trường theo quy tắc cũ đã được gỡ. Không tự cài model hay chuyển nhà cung cấp khi gặp lỗi.

## Sử dụng

1. Mở **Việt OCR** hoặc **PaddleOCR**, chọn PDF/thư mục và bật **Trích xuất LLM sau khi lưu Markdown**.
2. Mở **Cấu hình LLM** để chọn **Gemini API**, **Ollama local**, hoặc **API local tương thích OpenAI**. Nhập tên model chính xác. Gemini cần API key; local cần địa chỉ server và token nếu server yêu cầu. Có thể để model trống và cấu hình sau.
3. Chọn dữ liệu gửi: **Markdown OCR (văn bản)**, **Markdown OCR + ảnh khi cần**, hoặc **Markdown OCR + PDF (Gemini)**. Hai chế độ sau cần model có vision và bật khả năng đọc ảnh. PDF đối chiếu chỉ dùng toàn bộ phạm vi PDF, tối đa 10 MB/30 trang và phải vừa context; với phạm vi trang riêng, dùng ảnh.
4. Chạy OCR. Mỗi PDF luôn lưu `.pdf.ocr.md` trước khi trích xuất, thêm các định dạng toàn văn đã chọn và `.pdf.ocr.metadata.json`. Nếu thiếu dịch vụ/model, kết quả OCR vẫn được lưu và bước LLM báo trạng thái riêng.
5. Mở **Thông tin / Excel** hoặc **Rà soát / Xuất Excel**, chọn PDF, rồi **Chạy lại LLM** khi đã cấu hình. Nút chạy lại bỏ qua cache. Chỉ mở tab rà soát không tự gọi API lần nữa.
6. Chọn từng trường để đối chiếu bằng chứng/PDF, sửa và xác nhận hoặc chọn **Không áp dụng**. Đề xuất LLM, giá trị hiện tại và lịch sử được lưu riêng. LLM không tự xác nhận.
7. Nếu PDF chứa nhiều văn bản, đặt trang bắt đầu/kết thúc văn bản chính rồi **Bóc tách lại phạm vi**. Bản sửa thuộc phạm vi cũ chỉ được lưu vào lịch sử, không chép sang văn bản khác.
8. **Lưu JSON** để giữ kết quả rà soát; **Xuất Excel** để tạo báo cáo. Mỗi dòng sheet **Báo cáo** là một PDF. Excel là bản chụp tại thời điểm xuất; xuất lại sau khi sửa.

## Nhập nội dung đã có

- **Chọn JSON OCR** hoặc chọn thư mục và nguồn **JSON sau OCR**: đọc toàn văn, dòng, trang và tọa độ của `.ocr.json`; không chạy lại VietOCR/PaddleOCR. Khi nhập thư mục, chọn engine nếu có nhiều kết quả cho cùng PDF.
- **Mở JSON / tiếp tục**: mở JSON trích xuất, gồm `.metadata.json`, cùng các sửa tay/xác nhận đã lưu. **Chạy lại LLM** dùng nội dung OCR đã lưu.
- **Nhập MD/TXT**: chọn nội dung và PDF liên quan. LLM có thể đọc nội dung nhưng nguồn được đánh dấu chưa kiểm chứng và không có tọa độ dòng. Không tạo khung chữ giả; muốn đối chiếu ảnh cần JSON OCR khớp PDF hoặc OCR lại.
- **PDF / lớp chữ**: đọc lớp chữ hoặc OCR tiếng Việt của luồng đọc PDF sẵn có, sau đó dùng cùng bộ trích xuất LLM.

## Phân tích và kiểm tra

Ứng dụng đọc Markdown đã lưu, gửi nội dung theo trang kèm mã dòng, bố cục đối chiếu, trạng thái trang và phạm vi. Không gửi tên file/trạng thái xuất như nội dung của văn bản. Tài liệu dài được chia phần rồi tổng hợp các ứng viên kèm đoạn nguồn. Không chỉ đọc trang đầu/cuối. Nếu vượt context, số lượt hoặc giới hạn ảnh, tác vụ báo chưa hoàn tất; không nhận kết quả một phần là thành công.

LLM nhóm văn bản và trả JSON gồm cơ quan ban hành/chủ quản, ngày ban hành, số/ký hiệu, loại, tiêu đề, trích yếu, nơi nhận/kính gửi, từng cặp tên/chức vụ/quyền hạn ký và số in sẵn. Ứng dụng kiểm tra schema, mã bằng chứng, phạm vi nhóm, ngày hợp lệ và nội dung giá trị có trong nguồn. Giá trị không khớp được bỏ trống và yêu cầu rà soát; đây là kiểm tra kết quả, không phải bộ quy tắc chọn trường cũ.

Khi bật ảnh, model có thể yêu cầu trang trong phạm vi. Ảnh được render đúng chiều; bằng chứng `image:pN` chỉ xác nhận nguồn toàn trang N, không giả lập vị trí chữ chính xác. Đề xuất đọc từ ảnh giữ riêng OCR gốc và cần đối chiếu thủ công. Model chỉ đọc chữ in tên người ký, không suy danh tính từ nét chữ ký.

Một nhóm văn bản chính duy nhất mới được đưa vào báo cáo; người ký của văn bản/phụ lục khác không được ghép với cơ quan/ngày của nhóm chính. Trường thiếu vẫn để trống. Nhiều người ký giữ từng cặp riêng, có thể sửa trường `#2`, `#3`…

## Trang số và bảo toàn bản sửa

Số trang PDF lấy từ metadata. Số ghi tay dùng chung kết quả OCR vùng mặc định **20% chiều rộng bên phải × 10% chiều cao phía trên**, có thể điều chỉnh trong màn hình OCR. Nhãn như `003` giữ nguyên số 0 đầu. LLM không được ghi đè nhãn ghi tay hoặc tự xác nhận.

Sửa/xác nhận nhãn tại **Số trang ghi tay**, sau đó lưu JSON trích xuất và xuất lại Excel. Với JSON OCR nhập lại, xác nhận cập nhật JSON OCR gốc và Markdown cạnh nó nếu có.

Bản sửa thủ công cùng phạm vi được giữ khi chạy lại. PDF/OCR thay đổi sẽ bỏ xác nhận để đối chiếu lại. Mã SHA-256 liên kết nội dung OCR với PDF; nguồn thiếu hash/không khớp không được tự gửi ảnh/PDF hoặc hiển thị bằng chứng chồng lên PDF khác. Hủy LLM giữ file OCR đã xuất và các sửa tay trước đó.

## Cấu hình, dữ liệu và giới hạn

- Gemini dùng REST Interactions với JSON Schema, `store:false`, không dùng công cụ ngoài tài liệu. Nội dung/ảnh/PDF được chọn gửi tới Google; `store:false` không thay thế điều khoản xử lý dữ liệu của tài khoản Google.
- Ollama dùng native `/api/chat`, mặc định `http://localhost:11434`. Server tương thích dùng `/v1/chat/completions`; phải hỗ trợ structured JSON và vision nếu bật ảnh. Không âm thầm hạ xuống JSON tự do khi server từ chối schema.
- Local chỉ gọi endpoint đã cấu hình và bỏ qua proxy Internet của hệ thống. Không tự chuyển sang Gemini. Không dùng tag model có `cloud`; để xử lý hoàn toàn tại máy, quản trị runtime cũng cần cấu hình chạy local, vì ứng dụng không kiểm soát cách server xử lý nội bộ.
- HTTP chỉ mặc định cho localhost. Endpoint nội bộ HTTP cần bật rõ; HTTPS được hỗ trợ. Đổi endpoint sẽ xóa token local trên giao diện để nhập lại.
- API key Gemini và token local tách riêng, được bảo vệ bằng Windows DPAPI theo tài khoản hiện tại tại `%LOCALAPPDATA%\CountFile2\llm-settings.json`. Không đưa key vào JSON OCR/Excel/prompt/cache. Chuyển cấu hình sang tài khoản/máy khác cần nhập lại key.
- Cache mặc định ở `%LOCALAPPDATA%\CountFile2\llm-cache`, chứa thông tin trích xuất của tài liệu, không chứa key. Cache phụ thuộc hash nguồn/OCR, phạm vi, model, endpoint, prompt/schema và cấu hình đầu vào. Tắt cache hoặc **Chạy lại LLM** nếu thay trọng số model dưới cùng tên/tag. Không xóa cache người dùng khi nâng cấp.
- Cấu hình context thực tế, token đầu ra, timeout, số request/ảnh tối đa theo model. Theo dõi request/token trong JSON/Excel; token không được server báo sẽ để trống. Chưa tính chi phí tiền tự động. Retry có giới hạn cho lỗi tạm thời; không retry vô hạn, không retry lỗi schema/quyền/quota ngày/thiếu RAM.

## Workbook

| Sheet | Nội dung |
| --- | --- |
| Báo cáo | Một dòng/PDF; các trường, trạng thái, provider/model/prompt, request/token và trang đã phân tích |
| Trang số | Từng trang PDF, số in và số ghi tay, nhãn gốc/hiện tại và xác nhận |
| Người ký | Từng cặp tên/chức vụ/quyền hạn ký, trang và bằng chứng |
| Cần kiểm tra | Trường thiếu/chưa xác nhận, cảnh báo nguồn, lỗi hoặc cấu hình LLM |
| Bằng chứng | Đề xuất LLM, giá trị hiện tại, nguồn OCR/ảnh, mã dòng/tọa độ và lịch sử |

Số/ký hiệu và nhãn trang dùng chuỗi; ngày hợp lệ dùng kiểu ngày Excel, hiển thị `dd/mm/yyyy`. Nội dung bắt đầu bằng `=` vẫn là chuỗi. Ghi tạm rồi thay thế; file đang khóa hoặc ô quá 32.767 ký tự báo lỗi và giữ bản cũ. JSON lưu đầy đủ khi vượt giới hạn Excel.

## Kiểm tra model sau

Chưa cài LLM local và chưa gửi tài liệu thật tới API. Ngày 26/10/2026 đã kiểm tra Gemini theo yêu cầu: khóa đọc metadata `gemini-3.8-flash` thành công HTTP 200; sinh JSON mẫu gặp HTTP 503/quá tải hoặc timeout ở cả Interactions và generateContent. Xem `verify-gemini-connection.md`.

**Kiểm tra API / JSON** hiển thị mã HTTP và lý do đã che khóa; thử lại có giới hạn với lỗi tạm thời, không thử lại lỗi xác thực. Nếu vẫn 503, thử lại sau; tăng timeout không khắc phục được việc dịch vụ quá tải. Không tự chuyển model hay nhà cung cấp.

Kiểm thử mock kiểm tra hành vi ứng dụng, không chứng minh độ chính xác trích xuất. Khi JSON mẫu thành công, đánh giá trên bộ PDF đã gán nhãn theo từng trường, đặc biệt số/ký hiệu, ngày, cơ quan và cặp người ký.
