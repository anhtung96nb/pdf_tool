# Kế hoạch chuyển bóc tách thông tin sang Gemini API hoặc LLM local

Ngày lập: 25/10/2026. Cập nhật triển khai: 26/10/2026. Đã thay bộ chọn trường cũ bằng LLM; kiểm tra model thực tế được để sau theo yêu cầu. Xem `post-ocr-metadata-excel.md` và `verify-llm-extraction.md` cho hành vi và giới hạn đã triển khai.

## 1. Mục tiêu đã chốt

- Dùng LLM để phân tích ngữ cảnh và chọn thông tin văn bản, thay vai trò chọn trường của bộ quy tắc hiện tại.
- Hỗ trợ hai lựa chọn của người dùng: **Google Gemini API** và **LLM host local/nội bộ**. Ollama là kết nối local tham chiếu; có adapter riêng cho server tương thích OpenAI.
- Giữ VietOCR/PaddleOCR để nhận dạng, metadata trang và bằng chứng. Không phải OCR lại khi đã có JSON hợp lệ.
- Giữ **một dòng cho mỗi PDF** trong sheet Báo cáo; các khối ký/trang/bằng chứng nằm ở sheet chi tiết.
- Giữ chỉnh sửa và xác nhận thủ công. Số trang PDF lấy từ file; số trang ghi tay đã xác nhận không bị LLM ghi đè.
- Đo cải thiện trên tài liệu thực tế. Các kiểm thử luồng trước đây không chứng minh độ chính xác của từng trường.

LLM đọc văn bản có thể vẫn chịu ảnh hưởng của lỗi OCR. Vì vậy kế hoạch bao gồm đối chiếu ảnh/PDF gốc với model có vision; thay engine bóc tách riêng lẻ không bảo đảm khắc phục được mọi lỗi đọc chữ.

## 2. Kết nối và cách lựa chọn

| Lựa chọn | Kết nối dự kiến | Cấu hình cần có | Phạm vi hỗ trợ |
| --- | --- | --- | --- |
| Gemini | REST API chính thức, adapter riêng | API key, model, giới hạn sử dụng | Chữ và ảnh/PDF theo model đã kiểm tra |
| Ollama local | API native `/api/chat`, địa chỉ mặc định `http://localhost:11434` | Tên/tag model, context, giới hạn sinh; RAM/VRAM phù hợp | Chữ; ảnh khi model có vision |
| Server local tương thích OpenAI | Adapter `/v1/chat/completions` theo server được cấu hình | Base URL, model, xác thực nếu server yêu cầu | Kiểm tra riêng structured output, context và vision |

Gemini hỗ trợ JSON Schema và xử lý PDF bằng vision. Chọn model bằng thử nghiệm tiếng Việt và dữ liệu mẫu, không cố định một model chỉ vì có context lớn. Tài liệu API hiện tại khuyến nghị Interactions API cho tích hợp mới; adapter dự kiến sử dụng REST của giao diện này và chế độ không duy trì hội thoại. [Structured outputs](https://ai.google.dev/gemini-api/docs/structured-output), [Document understanding](https://ai.google.dev/gemini-api/docs/document-processing), [Interactions API](https://ai.google.dev/gemini-api/docs/interactions-overview).

Ollama native nhận JSON Schema ở `format`; ảnh được gửi theo giao thức vision của Ollama. Giao diện tương thích OpenAI chỉ hỗ trợ một phần API, nên không dùng chung payload với Gemini hoặc mặc định mọi server đều hỗ trợ cùng schema. [Ollama structured outputs](https://docs.ollama.com/capabilities/structured-outputs), [Vision](https://docs.ollama.com/capabilities/vision), [API compatibility](https://docs.ollama.com/api/openai-compatibility).

Khi chọn local, ứng dụng chỉ gọi endpoint được chọn. Không tự chuyển sang Gemini khi model local lỗi/chậm. Với nhu cầu chạy hoàn toàn tại máy, chọn model đã tải về và chế độ local-only của Ollama; kết nối localhost có thể dùng cloud model nếu runtime được cấu hình như vậy. [Ollama FAQ](https://docs.ollama.com/faq#how-do-i-disable-ollamas-cloud-features).

## 3. Luồng xử lý mới

```text
PDF hoặc JSON OCR đã có
  → Chuẩn hóa dòng OCR, mã dòng/trang, kiểm tra nguồn
  → Tạo gói tài liệu cho LLM và kiểm tra giới hạn context
  → Gemini / Ollama / server local được chọn
  → Nhận JSON theo schema
  → Kiểm tra giá trị, bằng chứng, phạm vi văn bản và metadata đã khóa
  → Đối chiếu thêm ảnh khi được bật và khi cần
  → Bảng rà soát, sửa/xác nhận
  → JSON trích xuất + báo cáo Excel năm sheet
```

### 3.1. Chuẩn bị dữ liệu

- Đầu vào ưu tiên JSON/cache có cấu trúc hiện tại, bao gồm tất cả các trang, nội dung gốc, line ID, tọa độ, rotation, trạng thái OCR và nhãn trang đã xác nhận.
- Gán ID ổn định trong một phiên bản OCR; có `source_hash`, `ocr_hash` và `page_id`. Tọa độ cung cấp để hiểu bố cục; tọa độ đối chiếu luôn do ứng dụng quản lý.
- Đầu vào LLM chứa văn bản OCR, thay vì chỉ đưa các trường đã bị quy tắc cũ chọn sai. Quy tắc cũ không được lọc bỏ trước các dòng mà LLM cần đọc.
- Giữ phân tách từng PDF. Không gom nhiều PDF vào một prompt để tạo một bộ trường dùng chung.
- Nếu chỉ có MD/TXT: tạo line ID văn bản và đánh dấu thiếu bố cục; không dựng vị trí trên PDF. Nếu nguồn OCR có trang lỗi, LLM phải thấy trạng thái thiếu nguồn này.

### 3.2. Tài liệu ngắn và dài

- Khi vừa context: gửi toàn bộ văn bản của PDF trong một lượt trích xuất có cấu trúc; có danh sách văn bản/nhóm trang và các trường của văn bản chính.
- Khi vượt context: chia theo trang/nhóm trang, có phần chồng lấn ở ranh giới; mọi trang phải được xét. Mỗi phần trả ứng viên và mã bằng chứng, sau đó lượt tổng hợp chọn theo cùng nhóm văn bản.
- Lượt tổng hợp được cung cấp ứng viên, đoạn nguồn và bản đồ trang; không chỉ dựa trên bản tóm tắt có thể làm mất số/ký hiệu hoặc tên riêng.
- Dự trù token đầu ra và giới hạn payload/ảnh. Với local, dùng context thực tế được cấu hình của server/model, không lấy context tối đa quảng cáo làm kích thước đang chạy.
- Nếu không đủ giới hạn đã đặt, đánh dấu chưa phân tích đầy đủ; không âm thầm bỏ giữa tài liệu hoặc chỉ đọc trang đầu/cuối.

### 3.3. Đối chiếu ảnh khi OCR sai

Hai chế độ người dùng có thể chọn:

1. **Chỉ nội dung OCR**: nhanh hơn về lượng dữ liệu gửi; thông tin bị đọc sai hoặc mất trong OCR cần được đánh dấu cần kiểm tra.
2. **OCR + ảnh khi cần**: dùng model có vision để đối chiếu phần đầu văn bản, khối ký hoặc vùng chữ đang mâu thuẫn. Hỗ trợ yêu cầu thêm trang trong phạm vi tài liệu, với số lượt/ảnh bị giới hạn.

Ảnh được xoay đúng chiều, có mã trang/vùng do ứng dụng cấp và giữ phép ánh xạ về trang gốc. Trang đầu/cuối là ngữ cảnh gợi ý, không phải toàn bộ phạm vi có thể chứa thông tin.

Gemini có thể nhận thêm PDF gốc làm một cấu hình đối chiếu toàn tài liệu. Đây là lựa chọn thay cho gửi ảnh trang, không mặc định gửi cả PDF lẫn toàn bộ ảnh gây trùng chi phí. Local nhận ảnh render nếu model hỗ trợ; không giả định server local đọc được PDF trực tiếp.

Nếu model đọc được giá trị khác với OCR từ ảnh, lưu riêng giá trị LLM đề xuất, chữ OCR gốc và nguồn ảnh; đánh dấu khác biệt để người dùng xem. Không ép giá trị ảnh phải giống chữ OCR sai, cũng không coi sự khác biệt là đã xác nhận.

## 4. Nội dung yêu cầu LLM bóc tách

Các trường chính:

- Cơ quan ban hành, cơ quan chủ quản.
- Ngày ban hành; tách khỏi ngày viện dẫn, ngày nhận, ngày scan.
- Số/ký hiệu đầy đủ, số văn bản, ký hiệu; giữ nguyên số 0 đầu.
- Loại văn bản, tiêu đề, trích yếu được in trên tài liệu.
- Danh sách người ký; mỗi phần tử chứa tên, chức vụ và quyền hạn ký trong cùng khối.
- Phạm vi văn bản chính, dấu hiệu văn bản/phụ lục khác và các vấn đề cần kiểm tra.
- Số in sẵn nếu nhận diện được; nhãn ghi tay chỉ lấy từ metadata OCR dùng chung; LLM không được trả hay ghi đè trường này.

Hướng dẫn model cần có ví dụ tiếng Việt cho: cơ quan chủ quản/cơ quan ban hành; ngày dẫn chiếu/ngày ban hành; người được nhắc trong nội dung/người ký; chức vụ được ký thay/chức vụ thực tế; nhiều người ký/nhiều văn bản.

Thông tin không xuất hiện hoặc không đủ bằng chứng phải trả `null`, kèm lý do ngắn. Trích yếu là nội dung nguồn, không phải bản tóm tắt mới. Không suy danh tính từ nét chữ ký; không tự sửa tên, ký hiệu, chữ số dựa trên suy đoán.

OCR/PDF là dữ liệu để đọc; chỉ dẫn nằm trong tài liệu không được thực hiện như lệnh hệ thống. LLM chỉ trả JSON, không được cấp công cụ duyệt web, thực thi chương trình hoặc gửi dữ liệu sang dịch vụ khác.

## 5. Hợp đồng JSON và kiểm tra bằng chứng

Schema chung của ứng dụng được chuyển thành schema phù hợp từng provider. Dùng cấu trúc đơn giản, đủ các khóa cần thiết, giá trị có thể null; không coi JSON hợp lệ là kết quả đúng về nội dung. Gemini cũng yêu cầu ứng dụng kiểm tra các giá trị sau khi nhận structured output. [Hướng dẫn validation](https://ai.google.dev/gemini-api/docs/structured-output#best-practices).

Ví dụ minh họa một trường, không phải toàn bộ schema:

```json
{
  "field": "issue_date",
  "value": "2026-10-12",
  "raw_value": "ngày 12 tháng 10 năm 2026",
  "state": "found",
  "document_group_id": "doc-1",
  "evidence": [
    { "source_type": "ocr_line", "page": 1, "source_id": "p1:l12" }
  ],
  "alternatives": [],
  "issue": null
}
```

Các trạng thái model đề xuất: `found`, `missing`, `ambiguous`, `unreadable`. `confirmed` chỉ do người dùng/metadata đã được xác nhận quyết định.

Ứng dụng kiểm tra:

- Tên trường/kiểu dữ liệu/enum phù hợp schema; không chấp nhận JSON dang dở hoặc phản hồi bị chặn như kết quả hoàn tất.
- Mã dòng/trang/vùng tồn tại trong chính gói nguồn đã gửi. LLM không tạo tọa độ hoặc mã bằng chứng mới.
- Giá trị cần có bằng chứng có thể đối chiếu. Với OCR, kiểm tra chữ nguồn và phép chuẩn hóa hợp lệ; với ảnh, dùng region ID do ứng dụng cấp và luôn cho phép kiểm tra trực quan.
- Ngày đủ thành phần và hợp lệ mới chuẩn hóa ISO. Số/ký hiệu không đổi nội dung khi tách; tên/chức vụ/người ký thuộc cùng khối và cùng nhóm văn bản.
- Tổng số trang, hash nguồn và các nhãn ghi tay đã xác nhận là dữ liệu khóa. Mâu thuẫn từ LLM được ghi thành vấn đề/ứng viên để xem, không thay metadata nguồn.
- Bằng chứng hợp lệ là điều kiện cần, vẫn có thể chọn sai vai trò của một ngày hoặc tên. Không dùng việc có line ID làm bằng chứng đạt độ chính xác ngữ nghĩa.
- Không lấy điểm tự chấm của model làm xác suất đúng. Trạng thái rà soát dựa trên validation, mâu thuẫn, nguồn ảnh/OCR và xác nhận người dùng.

Lưu ba lớp: chữ OCR gốc, đề xuất LLM, giá trị báo cáo sau rà soát. Kèm provider/model thực tế, prompt/schema version, dấu vân tay đầu vào, bằng chứng, thời điểm, lượng token nếu có và lịch sử sửa.

## 6. Một PDF có nhiều văn bản

- LLM nhận diện các nhóm trang/văn bản và gắn mọi ứng viên với `document_group_id`.
- Nếu văn bản chính rõ, chọn bộ trường cùng nhóm; giữ thông tin các phần đính kèm để đối chiếu.
- Nếu chưa rõ, để trạng thái cần chọn phạm vi/văn bản chính; không ghép cơ quan của văn bản đầu với người ký của văn bản sau.
- Người dùng có thể chọn lại phạm vi, rồi gọi lại LLM trong phạm vi đó. Vẫn chỉ một dòng/PDF trong Báo cáo.
- Chỉnh sửa đã xác nhận trong phạm vi khác được giữ trong lịch sử, không tự mang sang văn bản mới như một giá trị đã xác nhận.

## 7. Tích hợp với ứng dụng hiện tại

### Thành phần dự kiến

| Thành phần | Công việc |
| --- | --- |
| `LlmExtractionOptions` | Provider, endpoint, model, chế độ ảnh, timeout, giới hạn và phiên bản prompt/schema |
| `ILlmExtractionProvider` | Hợp đồng gọi bất đồng bộ và mô tả capability |
| `GeminiExtractionProvider` | Request/response Gemini, structured output, usage, lỗi và multimodal |
| `OllamaExtractionProvider` | API native, schema `format`, context/output thực tế, vision và lỗi nạp model |
| `CompatibleLocalExtractionProvider` | Kết nối server local theo protocol được kiểm tra, không giả định mọi tính năng giống nhau |
| `LlmDocumentPlanner` | Chuẩn bị nguồn, chia phần, giữ phạm vi trang và tổng hợp cùng nhóm |
| `LlmEvidenceValidator` | Kiểm tra JSON, giá trị, bằng chứng và dữ liệu đã khóa |
| `LlmExtractionService` | Điều phối, checkpoint/cache, retry, hủy và ánh xạ về mô hình dữ liệu hiện tại |
| `LlmSettingsStore` | Lưu cấu hình và khóa bí mật riêng khỏi kết quả OCR |

Dự án đang dùng .NET Framework 4.8 và có System.Net.Http. Dự kiến gọi REST bằng HttpClient, tránh buộc cài thêm Python chỉ để gọi LLM.

Cần tách phần **nạp/chuẩn hóa OCR** khỏi `PostOcrExtractionService` đang gọi bộ quy tắc trực tiếp. Sau đó thêm lựa chọn engine bóc tách ở mọi điểm vào: tự động sau OCR, nhập JSON, MD/TXT và bóc tách lại phạm vi.

Tái sử dụng `AdministrativeDocument`, `ExtractedField`, bằng chứng, màn hình rà soát, xuất JSON/Excel và cơ chế một tác vụ tài liệu tại một thời điểm. Theo yêu cầu cập nhật, xóa bộ chọn trường theo quy tắc cũ khỏi mã sản phẩm. Chỉ giữ kiểm tra định dạng/bằng chứng sau LLM và khả năng đọc dữ liệu đã lưu; không có chế độ fallback quy tắc.

## 8. Giao diện và báo cáo

Trong Việt OCR/PaddleOCR và Trích xuất văn bản PDF:

- **Phương pháp bóc tách:** Gemini API / Ollama local / Endpoint local tương thích.
- **Cấu hình LLM:** model, endpoint local, API key Gemini dạng che, nút kiểm tra kết nối/schema và khả năng đọc ảnh.
- **Dữ liệu gửi:** chỉ OCR / OCR + ảnh khi cần / PDF đối chiếu nếu provider hỗ trợ.
- **Giới hạn:** timeout, context/output, số lượt tối đa/file và mức song song. Local khởi đầu một tác vụ để đo bộ nhớ thực tế.
- **Hành động:** chạy LLM từ OCR đã có, chạy lại file lỗi/trường cần kiểm tra, hủy/tiếp tục, so sánh bản đề xuất với bản sửa hiện tại.
- Tiến độ tách OCR, chờ LLM, đang gọi, đang kiểm tra và cần rà soát. Chưa cấu hình API thì giữ OCR hoàn tất và ghi bước LLM đang chờ cấu hình.

Giữ năm sheet **Báo cáo, Trang số, Người ký, Cần kiểm tra, Bằng chứng**. Bổ sung nguồn bóc tách/model, phiên bản, trạng thái LLM và usage nếu provider có; phân biệt với engine OCR. API lỗi vẫn có dòng PDF, không xóa bản đã xác nhận trước đó. Giá trị đề xuất chưa đủ bằng chứng xuất với trạng thái cần kiểm tra hoặc để trống theo quy tắc dữ liệu, không gắn nhãn đã xác nhận.

## 9. Khóa API, dữ liệu, chi phí và lỗi

- API key nhập trong ứng dụng khi triển khai, lưu bằng cơ chế bảo vệ theo tài khoản Windows; không ghi vào code, JSON OCR, Excel hoặc log. Không cần cung cấp key trong cuộc trao đổi lập kế hoạch này.
- Gemini: request độc lập, `store=false` khi dùng Interactions; đây là điều khiển lưu interaction, không đồng nghĩa không có mọi hình thức xử lý/lưu dữ liệu. Chọn chế độ dữ liệu/gói phù hợp theo điều khoản Gemini; chính sách giữa unpaid/paid khác nhau. [Chế độ stateless](https://ai.google.dev/gemini-api/docs/interactions-overview#data-storage-and-retention), [Điều khoản dữ liệu](https://ai.google.dev/gemini-api/terms).
- Local: endpoint/model do người dùng chọn, không có đường tự động chuyển dữ liệu sang cloud. Địa chỉ và credential ràng buộc theo từng cấu hình; không gửi khóa Gemini sang local.
- Khóa cache bao gồm hash PDF/OCR, phạm vi, ảnh nếu có, provider/endpoint/model, prompt/schema và cấu hình sinh. Thay một yếu tố cần xác minh/chạy lại; giữ bản sửa thủ công và lịch sử.
- Gemini lưu token/usage thực tế nếu API trả về, ước tính chi phí theo cấu hình giá có thời điểm; chưa chốt chi phí/file trước thử nghiệm. Local đo thời gian và bộ nhớ, không gọi là miễn phí hoàn toàn về tài nguyên vận hành.
- Retry có giới hạn cho lỗi mạng/rate limit/server tạm thời, chờ theo chỉ dẫn server nếu có. Không retry vòng lặp với sai key, hết quota, model không tồn tại, thiếu RAM/VRAM hoặc schema không được hỗ trợ.
- Phản hồi bị chặn, cắt vì hết output hoặc sai cấu trúc không được parse một phần rồi báo thành công. Cho tối đa một lượt sửa/bổ sung trong ngân sách tổng đã đặt; còn lỗi thì giữ trạng thái và bằng chứng để kiểm tra.
- Hủy dừng request/việc tiếp theo và giữ checkpoint. Request đã gửi có thể vẫn tiêu thụ tài nguyên hoặc bị tính phí; không hứa hủy tại máy là hủy mọi chi phí phía server.
- Log mặc định chỉ có trạng thái, mã lỗi đã làm sạch và usage; lưu nội dung phản hồi chỉ trong dữ liệu kết quả được quản lý, không đưa chữ tài liệu vào log chẩn đoán đại trà.

## 10. Các giai đoạn và điều kiện nghiệm thu

| Giai đoạn | Công việc | Đầu ra kiểm chứng |
| --- | --- | --- |
| 1. Thu thập lỗi và bộ mẫu | Chọn 30–50 PDF đại diện, ưu tiên trường hiện sai; gán nhãn và chia tập điều chỉnh/đánh giá độc lập | Định nghĩa trường và kết quả đúng, baseline quy tắc cũ trên cùng dữ liệu |
| 2. Prototype hai provider | Kết nối Gemini và Ollama; kiểm tra model, JSON Schema, context và vision bằng mẫu không nhạy cảm | Request/response hợp lệ, biết capability thật và hạn chế phần cứng local |
| 3. Bóc tách có bằng chứng | Prompt tiếng Việt, schema, chia tài liệu, nhóm văn bản và kiểm tra nguồn; thêm đối chiếu ảnh | Trường có nguồn đối chiếu; không ghi đè metadata đã khóa hoặc ghép chéo tài liệu |
| 4. Tích hợp giao diện | Thay engine chọn trường, cấu hình, review, cache/checkpoint, giữ bản sửa và năm sheet | Chạy được từ PDF/JSON hiện có, một dòng/PDF, hủy/tiếp tục không mất dữ liệu |
| 5. Đánh giá và chọn cấu hình | So quy tắc cũ, Gemini chữ, Gemini có ảnh, local chữ/vision trên cùng tập giữ lại | Độ chính xác từng trường, lỗi điền thông tin không có, tỷ lệ cần sửa, thời gian và chi phí/tài nguyên |
| 6. Đóng gói | Chốt provider/model/profile được dùng, tài liệu cấu hình và kiểm thử hồi quy | Bản Release và báo cáo chất lượng thực tế; chưa đạt thì chưa coi JSON đúng định dạng là nghiệm thu |

Chưa chốt model local, tỷ lệ chính xác hoặc thời gian xử lý trước khi biết RAM/VRAM, endpoint/model sẵn có và kết quả bộ mẫu. Model/tag và cấu hình được ghi lại để so sánh được giữa các lần chạy.

## 11. Kiểm thử cần có

- Ngày viện dẫn cạnh ngày ban hành; cơ quan nhiều cấp; số/ký hiệu bị mất dấu; trích yếu nhiều dòng.
- Chữ OCR sai tên/ký hiệu nhưng ảnh đọc rõ; trường thật sự không xuất hiện; chữ ký nét không có tên in.
- Nhiều khối ký, phụ lục, nhiều văn bản trong một PDF, chữ ở trang giữa, trang xoay và trang OCR lỗi.
- Tài liệu dài vượt context local, ranh giới chia phần, không bỏ trang và không ghép sai nhóm khi tổng hợp.
- JSON hợp lệ nhưng bịa bằng chứng; line ID không tồn tại, page ID khác tài liệu, chuẩn hóa làm đổi số/tên, ảnh không có trong request.
- Số trang `003`, vùng 20% bên phải × 10% phía trên và cấu hình tùy tài liệu; nhãn đã xác nhận không đổi khi LLM đề xuất khác.
- Sai key/hết quota/rate limit/timeout; server local không chạy, model thiếu hoặc thiếu bộ nhớ; model không hỗ trợ vision/schema; từ chối/cắt output; hủy và tiếp tục.
- Cùng nguồn chạy lại giữ sửa; đổi nguồn/phạm vi/provider/prompt không tự dùng cache cũ hoặc chuyển xác nhận sang văn bản khác.
- Chọn local kiểm tra không phát sinh request Gemini. Không lộ khóa qua file xuất/log; lỗi LLM không tự biến thành kết quả từ quy tắc cũ.
- Báo cáo đúng một dòng/PDF, giữ số 0 đầu, kiểu ngày, bằng chứng và trạng thái lỗi/chưa xử lý.

Đánh giá chất lượng gồm exact match sau chuẩn hóa hợp lệ cho số/ngày/tên/cơ quan; độ đầy đủ và đúng nguồn của trích yếu; lỗi điền giá trị khi trường không có; thời gian sửa thủ công và chi phí/thời gian mỗi PDF. Báo riêng lỗi OCR và lỗi chọn trường, cùng số lượng mẫu mỗi nhóm. Chỉ chọn cấu hình mặc định sau khi kết quả thực tế cải thiện ở các trường ưu tiên và mức sử dụng phù hợp.
