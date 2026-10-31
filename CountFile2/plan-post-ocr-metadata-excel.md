> Kế hoạch cũ, đã thay bộ chọn trường bằng Gemini/LLM local ngày 26/10/2026 theo yêu cầu. Xem `plan-llm-extraction-gemini-local.md`, `post-ocr-metadata-excel.md` và `verify-llm-extraction.md`.

# Kế hoạch bóc tách thông tin sau OCR và báo cáo Excel

Ngày lập: 25/10/2026. Đây là kế hoạch, chưa thay đổi mã xử lý.

**Cập nhật triển khai 25/10/2026:** đã có bản đầu kết nối VietOCR/PaddleOCR, rà soát ứng viên/phạm vi, giữ bản sửa và báo cáo năm sheet. Xem `post-ocr-metadata-excel.md` và `verify-post-ocr-metadata-excel.md`. Việc đánh giá độ chính xác trên tập PDF thực tế được gán nhãn vẫn cần bộ mẫu; không coi kiểm thử tổng hợp là nghiệm thu tỷ lệ chính xác.

## 1. Mục tiêu và quy ước đã chốt

- Nhận dữ liệu OCR đã có từ Việt OCR/PaddleOCR, bóc tách thông tin văn bản và xuất báo cáo `.xlsx`.
- **Sheet báo cáo chính có đúng một dòng cho mỗi file PDF**, theo xác nhận của người dùng. Các sheet chi tiết có thể có nhiều dòng cho cùng file.
- Phân biệt thứ tự trang PDF, số trang in sẵn và số trang ghi tay. Giữ nguyên nhãn dạng `003`; không thay bằng số thứ tự PDF.
- Mỗi giá trị trích xuất phải có dữ liệu nguồn để đối chiếu: file, trang, dòng/vùng chữ, văn bản gốc và trạng thái xác nhận.
- Không OCR lại mặc định khi đã có dữ liệu OCR phù hợp. Bóc tách là bước sau khi hoàn thành OCR các trang của một file, vì người ký có thể nằm ở trang cuối.
- Bản đầu tập trung văn bản hành chính, công văn, quyết định, thông báo, báo cáo và các mẫu có thông tin tương tự. Tài liệu ngoài phạm vi vẫn có một dòng báo cáo, ghi rõ trường thiếu hoặc cần kiểm tra.

## 2. Thành phần hiện có có thể tái sử dụng

| Thành phần | Hiện trạng | Phần cần bổ sung |
| --- | --- | --- |
| `AdministrativeExtractionService` | Có các quy tắc đọc số/ký hiệu, ngày, cơ quan, trích yếu, người ký, chức vụ và quyền hạn ký | Nhận trực tiếp dữ liệu OCR có cấu trúc; cải thiện chọn ứng viên và các bố cục nhiều trang |
| `EvidenceLine`, `ExtractedField`, `AdministrativeDocument` | Có giá trị tự động, giá trị đã sửa, xác nhận và bằng chứng | Thêm mã trường ổn định, nguồn OCR, ứng viên, trạng thái thiếu/mơ hồ và phiên bản kết quả |
| `ExtractionExportService` | Đã xuất Excel bằng EPPlus, có sheet thông tin và bằng chứng | Thiết kế lại cột báo cáo và thêm sheet trang số/người ký/cần kiểm tra |
| `frmTrichXuatVanBan` | Có bảng trường, sửa/xác nhận, xem PDF và xuất Excel | Kết nối với JSON/kết quả từ Việt OCR/PaddleOCR, giữ một dòng cho mỗi PDF |
| `document_page_number` | Đã có nhãn số ghi tay, vị trí, ứng viên và xác nhận | Chuyển trực tiếp sang dữ liệu trích xuất và Excel, ưu tiên giá trị đã xác nhận |

Dịch vụ trích xuất hiện tại có luồng đọc PDF/Tesseract riêng. Phần trọng tâm của lần phát triển này là kết nối kết quả OCR hiện có vào bộ trích xuất, tránh tạo một luồng OCR độc lập khác.

## 3. Danh mục trường cần bóc tách

| Trường | Cách xác định dự kiến | Điểm cần kiểm tra |
| --- | --- | --- |
| File PDF nguồn | Đường dẫn file được chọn, mã file ổn định trong phiên/báo cáo | Không gắn dữ liệu chỉ theo tên file |
| Tổng số trang PDF | Metadata file/đầu ra OCR | Trang lỗi vẫn được tính, trạng thái xử lý ghi riêng |
| Số trang ghi tay | `document_page_number` của từng trang, giữ nhãn đã sửa/xác nhận | Không tự tạo số thiếu hoặc coi các nhãn rời rạc là một dãy liên tục |
| Số trang in sẵn | Vùng đầu/chân trang nếu có, lưu riêng | Không nhầm với số ghi tay, số văn bản hoặc tổng số trang |
| Đơn vị/tổ chức ban hành | Nhóm chữ phía trên bên trái, đối chiếu vị trí với số văn bản và phần tiêu đề | Tách cơ quan chủ quản và cơ quan trực tiếp ban hành |
| Ngày ban hành | Cụm ngày tháng thuộc phần đầu văn bản chính; hỗ trợ ngày viết bằng chữ hoặc dấu phân cách | Phân biệt ngày được dẫn chiếu trong nội dung, ngày ký nhận và ngày scan |
| Số/ký hiệu văn bản đầy đủ | Cụm gắn với nhãn `Số:` và phần tiếp nối của nó | Không lấy một số văn bản được nhắc trong phần căn cứ/nội dung |
| Số văn bản | Thành phần số của số/ký hiệu khi phân tách được rõ ràng | Giữ số 0 đầu; không ép thành kiểu số Excel |
| Ký hiệu văn bản | Phần ký hiệu trong cụm số/ký hiệu đầy đủ | Không tự sửa chữ viết tắt hoặc suy ra cơ quan từ ký hiệu |
| Loại văn bản | Tiêu đề như QUYẾT ĐỊNH, THÔNG BÁO, BÁO CÁO hoặc dấu hiệu rõ của công văn | Không tự gán loại khi chưa đủ bằng chứng |
| Tiêu đề văn bản | Cụm tiêu đề nhìn thấy trên văn bản chính | Không dùng quốc hiệu/tiêu ngữ làm tiêu đề |
| Trích yếu | Dòng/cụm dòng mô tả nội dung dưới tên loại, hoặc sau `V/v`, `Về việc` | Ghép dòng hợp lý, dừng trước phần nội dung; không tự viết bản tóm tắt mới |
| Chức vụ người ký | Chữ in trong khối ký, thường ở phần cuối văn bản chính | Ghép đúng với người ký, không lấy chức vụ được nhắc trong nội dung |
| Tên người ký | Họ tên in trong cùng khối ký | Không suy ra danh tính từ nét chữ ký hoặc tên người ở mục nơi nhận |
| Quyền hạn/cách ký | Các cụm nhìn thấy như `KT.`, `TL.`, `TUQ.`, `TM.`, `Q.` | Lưu tách biệt với chức vụ thực tế; giữ nguyên chữ nguồn |

Các trường bổ sung tùy chọn: cơ quan chủ quản, nơi nhận, kính gửi, người duyệt kết quả, thời điểm xác nhận và ghi chú. Không bắt buộc tìm được mọi trường trên mọi loại tài liệu.

## 4. Luồng xử lý dự kiến

```text
Kết quả OCR của một PDF hoặc thư mục kết quả OCR
  → Kiểm tra schema và đối chiếu file nguồn
  → Chuẩn hóa dòng chữ, tọa độ, thứ tự đọc và metadata trang số
  → Xác định văn bản chính / phát hiện khả năng có nhiều văn bản
  → Khoanh nhóm đầu văn bản, tiêu đề, nội dung, khối ký
  → Tạo ứng viên cho từng trường
  → Kiểm tra định dạng, ngữ cảnh và sự liên kết giữa các trường
  → Bảng dữ liệu có thể sửa/xác nhận
  → Lưu JSON trích xuất và xuất báo cáo Excel
```

### 4.1. Nhận dữ liệu sau OCR

- Ưu tiên kết quả OCR có cấu trúc đang có trong phiên hoặc file `.ocr.json`: nội dung, dòng, tọa độ, thứ tự đọc, engine, kích thước trang và số ghi tay.
- Khi chỉ xuất Markdown nhưng bật bóc tách, vẫn giữ dữ liệu có cấu trúc nội bộ phục vụ trích xuất; không bắt người dùng xuất thêm JSON công khai chỉ để có báo cáo Excel. Cache trích xuất không phụ thuộc việc bật/tắt đọc số ghi tay.
- Markdown/TXT nhập lại chỉ là nguồn dự phòng vì thiếu tọa độ và điểm OCR. Đánh dấu nguồn văn bản thuần và mức độ cần kiểm tra; không dựng tọa độ giả.
- Mỗi PDF được ánh xạ vào đúng một bản ghi bằng đường dẫn nguồn, dấu vân tay nội dung và phiên bản OCR. File cùng tên ở hai thư mục vẫn là hai dòng báo cáo riêng.
- Với JSON cũ chưa có dấu vân tay PDF, ghi trạng thái nguồn chưa được kiểm chứng đầy đủ, không coi việc trùng tên/kích thước là bằng chứng chắc chắn.
- Nếu có nhiều kết quả engine cho cùng PDF, chọn nguồn theo cấu hình rõ ràng. Không chọn engine bằng cách so trực tiếp điểm số của hai model khác nhau.

### 4.2. Chuẩn hóa phục vụ bóc tách

- Giữ toàn bộ chữ gốc; tạo bản chuẩn hóa Unicode NFC, khoảng trắng và ngắt dòng để tìm mẫu.
- Ghép các mảnh của cùng dòng dựa trên tọa độ; tránh trộn hai cột thành một câu.
- Quy tắc nhận dạng nhãn có thể linh hoạt với lỗi OCR như `Số`/`So`, nhưng giá trị nguồn không được tự sửa số, tên riêng hoặc ký hiệu.
- Giữ hai hệ tọa độ rõ ràng: tọa độ trang PDF gốc dùng đối chiếu, và tọa độ bố cục theo chiều đọc dùng xác định đầu/cuối/trái/phải. Không áp dụng phép xoay hai lần vào tọa độ đã được worker ánh xạ về bản gốc.
- Đọc số ghi tay từ metadata riêng, không dò lại bằng regex trên toàn văn khi metadata đã có.

### 4.3. Tạo và chọn ứng viên theo bố cục + ngữ cảnh

- Tận dụng các quy tắc hiện có, mở rộng để thu thập nhiều ứng viên trước khi chọn.
- Cụm số/ký hiệu, cơ quan và ngày phải thuộc cùng phần đầu của văn bản chính; chức vụ/tên người ký phải thuộc cùng khối ký của văn bản đó.
- Trích yếu ghép từ các dòng liên quan, loại phần căn cứ, điều khoản, kính gửi và nơi nhận ra khỏi vùng chọn.
- Ngày chỉ chuẩn hóa sang ISO khi đầy đủ và hợp lệ. Ngày thiếu thành phần hoặc sai lịch được giữ nguyên văn và đánh dấu cần kiểm tra, không bổ sung từ ngày tạo file.
- Nhiều người ký: giữ thành danh sách từng cặp tên–chức vụ–quyền hạn, không ghép chức vụ của người này vào tên người khác.
- Không tìm được thì để trống có lý do. Có nhiều ứng viên xung đột thì giữ các ứng viên và đánh dấu mơ hồ.
- Bản đầu dùng quy tắc kết hợp bố cục. Đánh giá trên bộ mẫu trước khi quyết định có cần thêm mô hình ngôn ngữ hay không.

### 4.4. PDF có nhiều văn bản hoặc phụ lục

- Vẫn giữ **một dòng trong báo cáo chính cho mỗi PDF**.
- Phát hiện dấu hiệu như nhiều cụm `Số:`, nhiều phần đầu độc lập hoặc nhiều khối ký đi kèm tiêu đề mới. Không coi một dấu hiệu đơn lẻ là kết luận đã có nhiều văn bản.
- Nếu xác định được văn bản chính và phụ lục gắn kèm, lấy bộ trường của văn bản chính; giữ thông tin vùng/trang đã dùng và ghi chú phần đính kèm.
- Nếu không phân biệt được, báo “Có thể có nhiều văn bản”, cho người dùng chọn phạm vi trang của văn bản chính để bóc tách lại.
- Không trộn cơ quan/ngày/số của văn bản thứ nhất với người ký của văn bản thứ hai. Không tự tạo thêm dòng trong sheet chính hoặc tách file PDF.

## 5. Dữ liệu trích xuất và cơ chế xác nhận

Mỗi file có mã bản ghi, đường dẫn nguồn, dấu vân tay PDF/OCR, engine, phiên bản quy tắc và phạm vi trang của văn bản chính nếu đã xác định.

Mỗi trường có:

- `raw_value`: chữ nguồn/giá trị máy đọc ban đầu.
- `normalized_value`: giá trị đã chuẩn hóa có kiểm soát, ví dụ ngày ISO.
- `value`: giá trị hiện tại dùng báo cáo, có thể do người dùng sửa.
- `status`: tìm thấy, cần kiểm tra, không tìm thấy hoặc không áp dụng đã được xác nhận.
- `confirmed`, thời điểm xác nhận, giá trị trước khi sửa và lịch sử thay đổi.
- `candidates`: các ứng viên khác và lý do xếp hạng.
- `evidence`: mã dòng OCR, trang PDF, vùng chữ, nguyên văn nguồn, engine và điểm nhận dạng nếu có.

Phân biệt **điểm OCR** và **mức độ tin cậy của việc chọn trường**. Điểm chữ cao không bảo đảm đã chọn đúng ngày ban hành hoặc đúng người ký. Điểm xếp hạng quy tắc không được gọi là xác suất chính xác đã hiệu chuẩn.

Khi chạy lại trích xuất cùng nguồn, giữ các trường đã được người dùng xác nhận. Khi OCR/file nguồn thay đổi, đánh dấu cần đối chiếu và lưu phiên bản; không âm thầm ghi đè bản sửa thủ công.

Trạng thái cấp file thể hiện riêng: lỗi xử lý, đang chờ xử lý, cần kiểm tra, trích xuất xong chưa xác nhận, đã xác nhận. Trường bắt buộc được cấu hình theo nhóm tài liệu; thông tin thực sự không có có thể được xác nhận là không áp dụng.

## 6. Thiết kế báo cáo Excel

### Sheet 1 — Báo cáo

**Một dòng cho mỗi file PDF**, gồm các cột chính:

| Nhóm | Cột |
| --- | --- |
| Nhận diện nguồn | STT, mã bản ghi, tên file, đường dẫn/liên kết PDF |
| Trang | Tổng số trang PDF, phạm vi văn bản chính, nhãn số ghi tay hoặc chỉ dẫn đến sheet Trang số |
| Ban hành | Đơn vị/tổ chức ban hành, cơ quan chủ quản nếu có, ngày ban hành |
| Số/ký hiệu | Số/ký hiệu đầy đủ, số văn bản, ký hiệu |
| Nội dung | Loại văn bản, tiêu đề, trích yếu |
| Người ký | Chức vụ, tên người ký, quyền hạn ký; chỉ dẫn đến sheet Người ký nếu có nhiều người |
| Chất lượng | Trạng thái bản ghi, số trường cần kiểm tra, cảnh báo, người/thời điểm xác nhận nếu có |

Nếu số ghi tay không liên tục hoặc có trang chưa đọc được, không rút gọn thành khoảng giả như `001–010`. Trường hợp nhiều nhãn, sheet Trang số là nguồn chi tiết đầy đủ.

### Sheet 2 — Trang số

Một dòng cho mỗi trang của mỗi PDF: mã file, tên file, thứ tự trang PDF, số in sẵn nếu có, số ghi tay hiện tại, số máy đọc gốc, điểm model, trạng thái, xác nhận và cảnh báo. Tất cả nhãn số trang lưu dạng text.

### Sheet 3 — Người ký

Một dòng cho mỗi người ký/khối ký: mã file, thứ tự người ký, tên, chức vụ, quyền hạn ký, trang nguồn, trạng thái xác nhận và mã bằng chứng. Giữ các cặp tương ứng khi một PDF có nhiều người ký.

### Sheet 4 — Cần kiểm tra

Một dòng cho mỗi vấn đề: mã file, tên trường, giá trị hiện tại, loại vấn đề, nguyên văn nguồn, trang và gợi ý kiểm tra. Bao gồm trường thiếu, ứng viên xung đột, ngày không hợp lệ, số trang chưa xác định và khả năng có nhiều văn bản trong PDF.

### Sheet 5 — Bằng chứng

Chi tiết trường/ứng viên và vị trí nguồn: giá trị gốc, giá trị chuẩn hóa/đã sửa, trang, mã dòng, tọa độ, phương pháp, điểm OCR, lý do chọn và xác nhận. Có thể có nhiều dòng bằng chứng cho một trường nhiều dòng.

### Định dạng và độ an toàn dữ liệu

- Bảng có header rõ, bộ lọc, cố định hàng tiêu đề, wrap text và độ rộng cột phù hợp; tô màu trạng thái kèm nhãn chữ.
- Ngày hợp lệ lưu bằng kiểu ngày Excel, hiển thị `dd/mm/yyyy`; ngày không đầy đủ/sai giữ nguyên văn ở phần kiểm tra/bằng chứng.
- Số/ký hiệu, tên, trích yếu và nhãn như `003` ghi dưới dạng chuỗi. Nội dung OCR không được thực thi như công thức Excel.
- Không cắt âm thầm các ô quá dài hoặc vượt giới hạn số dòng của Excel; báo và chia phần chi tiết/sheet/tệp khi cần.
- File lỗi vẫn có một dòng trong Báo cáo. Khi hủy, báo cáo phân biệt file đã xử lý, lỗi và chưa xử lý; không bỏ qua âm thầm.
- Xuất từ một snapshot dữ liệu nhất quán; ghi tạm rồi thay thế khi lưu thành công. Kiểm tra file Excel đang mở/khóa và giữ bản cũ nếu xuất thất bại.
- Lưu phiên bản dữ liệu và thời điểm lập báo cáo để nhận biết Excel có trước hay sau lần sửa/xác nhận mới nhất. Bản đầu chưa tự đồng bộ ngược các ô người dùng sửa trong Excel vào ứng dụng.

## 7. Giao diện và cách sử dụng dự kiến

1. Chạy OCR PDF/thư mục như hiện tại, có thể bật **Bóc tách thông tin sau OCR**.
2. Hoặc chọn file/thư mục JSON OCR đã có để bóc tách lại mà không chạy nhận dạng chữ.
3. Sau khi một PDF hoàn thành OCR, tự tạo/cập nhật đúng một bản ghi trích xuất cho file đó.
4. Xem bảng tổng hợp và chi tiết các trường; chọn trường để thấy trang/vùng chữ nguồn.
5. Sửa, chọn ứng viên, xác nhận hoặc đánh dấu không áp dụng; giữ giá trị máy đọc để đối chiếu.
6. Chọn **Xuất báo cáo Excel**, xuất toàn bộ danh sách hoặc bộ lọc được người dùng chọn rõ ràng.

Ưu tiên tái sử dụng màn hình **Trích xuất văn bản PDF**, thêm nguồn đầu vào từ kết quả OCR và điều hướng từ Việt OCR/PaddleOCR. Tiếp tục dùng chạy nền, tiến độ, hủy và cơ chế một tác vụ tài liệu tại một thời điểm.

Không chỉnh số trang ghi tay thành một bản dữ liệu khác với tab số trang hiện có. Dùng cùng giá trị đã xác nhận; thay đổi nhãn phải làm báo cáo trích xuất liên quan được cập nhật/đánh dấu cần xuất lại.

## 8. Thứ tự triển khai

| Giai đoạn | Công việc | Kết quả nghiệm thu |
| --- | --- | --- |
| 1. Bộ mẫu và schema | Chọn khoảng 30–50 PDF đại diện; gán đúng các trường cần báo cáo, bao gồm các trường không có | Chốt định nghĩa trường, một dòng/file, mẫu cột và bộ dữ liệu đối chiếu |
| 2. Adapter sau OCR | Nạp JSON/kết quả trong phiên, đối chiếu nguồn, chuẩn hóa hệ tọa độ, đọc metadata số trang | Bóc tách được mà không gọi lại OCR; giữ `003`, engine và vị trí nguồn |
| 3. Bộ trích xuất | Tái sử dụng/mở rộng quy tắc, xử lý đầu văn bản, trích yếu, khối ký và nhiều ứng viên | Các trường có nguồn, không ghép chéo văn bản/khối ký; nhận diện rõ thiếu/mơ hồ |
| 4. Kiểm tra/xác nhận | Tích hợp bảng trường, xem vùng nguồn, chọn ứng viên, sửa và giữ lịch sử | Chạy lại không mất bản sửa; nhận biết nguồn/phiên bản thay đổi |
| 5. Excel | Bổ sung các sheet và kiểu dữ liệu, snapshot xuất, ghi tạm/khóa file | Một dòng/PDF ở Báo cáo, số 0 đầu và ngày giữ đúng, sheet chi tiết đối chiếu được |
| 6. Đánh giá và đóng gói | Kiểm thử độc lập trên tập chưa dùng điều chỉnh quy tắc, đo thời gian và lỗi | Có báo cáo chất lượng từng trường và hướng dẫn sử dụng |

Chưa chốt thời gian hoặc tỷ lệ chính xác trước khi có bộ mẫu. Không fine-tune model OCR chỉ để thay cho lỗi chọn vùng/trường; phân loại nguyên nhân trước khi quyết định cải tiến.

## 9. Kiểm thử cần có

- JSON từ VietOCR/PaddleOCR, nhãn ghi tay đã sửa, nhãn thiếu và OCR có trang lỗi; file cùng tên ở nhiều thư mục.
- Bản chuẩn có cơ quan chủ quản/cơ quan ban hành khác nhau; cơ quan nhiều dòng; bố cục không theo vị trí tuyệt đối cố định.
- Ngày ban hành và nhiều ngày được dẫn chiếu; ngày không hợp lệ/thiếu thành phần; số văn bản có số 0 đầu và OCR nhầm ký tự.
- Trích yếu nhiều dòng, công văn có `V/v`, tiêu đề dài, phần căn cứ sát tiêu đề và quốc hiệu/tiêu ngữ.
- Người ký ở trang cuối, ký thay/thừa lệnh, nhiều người ký và tên người xuất hiện trong nội dung/nơi nhận.
- Một PDF có phụ lục hoặc nhiều văn bản: vẫn một dòng báo cáo, không trộn các bộ trường.
- Trang xoay/nhiều cột; quy tắc dùng tọa độ theo chiều đọc, bằng chứng vẫn hiện đúng trên PDF gốc.
- Chạy lại sau khi sửa thủ công, nguồn OCR thay đổi, hủy giữa lô và xuất khi Excel đích đang mở.
- Đọc lại workbook để xác nhận số dòng file, nhãn `003`/số ký hiệu ở kiểu text, ngày đúng kiểu, liên kết bằng chứng và nội dung bắt đầu bằng ký tự công thức vẫn là chữ.

Chỉ số đánh giá: tỷ lệ đúng từng trường và toàn bộ bản ghi, tỷ lệ tìm được trường, trường không có nhưng bị điền nhầm, chọn nhầm ứng viên, tỷ lệ cần kiểm tra và thời gian/file. Tách lỗi OCR khỏi lỗi bóc tách. Tập đánh giá phải tách theo tài liệu/nguồn mẫu, tránh cùng một mẫu gần trùng xuất hiện ở cả lúc điều chỉnh và nghiệm thu.

## 10. Phạm vi bản đầu đề xuất

Hoàn thiện các trường người dùng yêu cầu, số trang ghi tay hiện có, loại văn bản và quyền hạn ký; dùng kết quả OCR có cấu trúc và quy tắc theo bố cục; có sửa/xác nhận và workbook năm sheet. Các bố cục quá khác mẫu, dữ liệu chữ ký không có tên in và PDF không xác định được văn bản chính được đưa vào danh sách kiểm tra, không suy đoán nội dung.
