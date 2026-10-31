# Kế hoạch nhận dạng số trang viết tay ở góc trên bên phải

Ngày lập: 25/10/2026. Cập nhật triển khai: tích hợp vào màn hình **Việt OCR**, vùng mặc định **20% bên phải × 10% phía trên**, đọc cùng mỗi trang OCR. Bộ mẫu thực tế và fine-tune vẫn là bước đánh giá tiếp theo khi có dữ liệu.

## 1. Mục tiêu đã thống nhất

- Đọc số trang được ghi tay ở phía trên bên phải mỗi trang tài liệu.
- Chỉ nhận nhãn gồm chữ số, ví dụ `1`, `12`, `003`, theo xác nhận của người dùng. Giữ số 0 đầu; lưu giá trị dưới dạng chuỗi.
- Tách biệt **thứ tự trang PDF** và **số trang ghi tay trên tài liệu**. Ví dụ trang PDF thứ 5 có thể ghi tay số `003`.
- Xuất số trang ghi tay cùng nội dung OCR vào Markdown/JSON và cho phép xem vùng ảnh để đối chiếu.
- Không tự suy ra số chưa đọc được từ thứ tự PDF hoặc từ số của các trang bên cạnh.

## 2. Hiện trạng và hướng tích hợp

`CountFile2/VietOcr/worker.py` hiện phát hiện chữ trên toàn trang với detector giới hạn cạnh dài 1920 pixel, sau đó đưa ảnh cắt dòng vào VietOCR. Cơ chế này chưa dành riêng cho số ghi tay nhỏ ở sát mép trang. Khi ảnh trang bị thu nhỏ để phát hiện chữ, nét số nhỏ có thể bị bỏ sót.

Đề xuất thêm nhánh nhận dạng riêng cho vùng số trang, tái sử dụng model VietOCR đã nạp. OCR toàn văn tiếp tục chạy như hiện tại. Kết quả riêng được trả thêm trong dữ liệu trang, không ghi đè trường `page` hoặc xóa chữ đã nhận dạng trong toàn văn.

VietOCR công bố hỗ trợ chữ viết tay và chữ đánh máy tiếng Việt, nhưng chất lượng với số ghi tay của bộ tài liệu này vẫn cần đo bằng mẫu thực tế. Không coi model hiện có là đã đủ chính xác chỉ vì có hỗ trợ chữ viết tay.

## 3. Luồng nhận dạng dự kiến

```text
Ảnh trang PDF ở độ phân giải gốc
  → Áp dụng chiều xoay trang đã chọn
  → Cắt vùng góc trên bên phải
  → Tìm cụm nét/số trong vùng cắt
  → Cắt riêng ứng viên, có khoảng đệm
  → VietOCR đọc cả cụm chữ số
  → Kiểm tra định dạng, vị trí và độ nhất quán giữa các lần đọc
  → Trả nhãn số hoặc trạng thái cần kiểm tra
  → Ánh xạ tọa độ về trang PDF gốc
```

### 3.1. Khoanh vùng tìm kiếm

- Vùng mặc định theo điều chỉnh của người dùng: **20% chiều rộng bên phải và 10% chiều cao phía trên**, tức `x = 0,80–1,00`, `y = 0,00–0,10` trên trang đã đặt đúng chiều đọc.
- Các tỷ lệ trên là cấu hình ban đầu, sẽ điều chỉnh theo bộ mẫu. Dùng tỷ lệ thay vì pixel cố định để hỗ trợ A4, A3 và kích thước khác nhau.
- Cho phép điều chỉnh vùng tìm kiếm trong phần nâng cao, xem vùng đó trên một trang mẫu trước khi chạy lô.
- Cắt từ ảnh render gốc, không cắt từ bản toàn trang đã thu nhỏ còn cạnh dài 1920 pixel.
- Dùng DPI hiện có, mặc định 300. Chỉ render lại riêng vùng cần thiết ở DPI cao hơn khi thử nghiệm cho thấy có ích, có giới hạn kích thước và thời gian. Phóng to ảnh mờ không thể khôi phục thông tin đã mất.
- Vùng tìm kiếm đi theo chiều đọc sau xoay; toàn bộ tọa độ kết quả phải ánh xạ ngược về trang gốc bằng cơ chế hiện có.

### 3.2. Tìm đúng cụm số

- Ưu tiên phát hiện chữ riêng trên vùng cắt nhỏ, không phụ thuộc hoàn toàn vào kết quả detector toàn trang.
- Nếu detector bỏ sót, thử phương án dự phòng dựa trên nét mực và nhóm thành phần liên thông; chỉ giữ ứng viên hình học phù hợp để đưa vào bước nhận dạng.
- Ghép các chữ số gần nhau thành một cụm. Nhận dạng toàn bộ chuỗi trước; chưa dùng phương án tách từng chữ số trong bản đầu vì số viết tay có thể dính nét.
- Có khoảng đệm quanh cụm để giữ nét móc, nét xiên và chữ số sát mép.
- Vùng trống không được đưa thẳng vào model rồi mặc nhiên chấp nhận chuỗi do model trả về.
- Cần kiểm tra nhầm với số văn bản, ngày tháng, chữ trong tiêu đề hoặc dấu đóng gần góc trên phải. Nếu có nhiều cụm số đều hợp lý, giữ các ứng viên và đánh dấu mơ hồ.

### 3.3. Nhận dạng và xác định mức độ tin cậy

- Thử VietOCR hiện có với ảnh màu gốc, ảnh xám tăng tương phản và tối đa một biến thể nhị phân khi cần. Không xóa mực màu mặc định vì số có thể viết bằng bút xanh/đỏ.
- Giữ `raw_text` nguyên bản. Chỉ chấp nhận nhãn chuẩn hóa gồm chữ số; bỏ khoảng trắng bao ngoài nhưng không ghép các nhóm số rời rạc một cách tùy tiện.
- Không tự thay `O → 0`, `I/l → 1` hoặc ép chuỗi bất kỳ thành số. Trường hợp đó cần kiểm tra hoặc nhận dạng lại.
- Giữ nhãn dạng chuỗi: `003` vẫn là `003`. Giá trị số nguyên `3`, nếu cần để kiểm tra chuỗi, chỉ là dữ liệu phụ.
- Xếp hạng dựa trên định dạng hợp lệ, vị trí, chất lượng vùng cắt, sự đồng thuận giữa các biến thể và điểm nhận dạng.
- Không lấy ứng viên có điểm VietOCR cao nhất làm kết quả chắc chắn. Điểm model hiện chưa được hiệu chuẩn, các biến thể ảnh cũng không phải những phép đo độc lập.
- Ngưỡng tự chấp nhận được chốt từ tập kiểm thử riêng. Mọi kết quả máy đọc vẫn có `confirmed = false` cho đến khi người dùng xác nhận.

### 3.4. Đối chiếu giữa các trang

- Có thể bổ sung cảnh báo số trùng, số giảm hoặc nhảy số để hỗ trợ kiểm tra.
- Đây là kiểm tra phụ: file PDF có thể gồm nhiều tài liệu, trang bìa, trang chèn hoặc các phần đánh số lại từ đầu.
- Không sửa kết quả OCR, điền số thiếu, đổi tên file hoặc sắp xếp lại trang dựa trên tính liên tiếp.

## 4. Dữ liệu JSON và Markdown

Giữ trường `page` hiện có là số thứ tự PDF bắt đầu từ 1. Thêm đối tượng tùy chọn `document_page_number` trong mỗi trang; các bộ đọc JSON cũ vẫn đọc được các trường trước đây.

Ví dụ trang PDF thứ 5 có số ghi tay `003`:

```json
{
  "page": 5,
  "document_page_number": {
    "raw_text": "003",
    "value": "003",
    "status": "recognized",
    "confidence": 0.93,
    "confirmed": false,
    "source": "vietocr_roi",
    "polygon_points": [[510, 18], [560, 18], [560, 45], [510, 45]],
    "warnings": []
  }
}
```

Điểm và tọa độ trong ví dụ chỉ minh họa cấu trúc. `polygon_points` dùng point, gốc trên trái của trang PDF ban đầu, thống nhất với dữ liệu dòng OCR hiện tại.

Các trạng thái dự kiến:

| Trạng thái | Ý nghĩa | Giá trị nhãn |
| --- | --- | --- |
| `recognized` | Có một kết quả đủ điều kiện theo ngưỡng đã kiểm chứng | Chuỗi chữ số, chưa xác nhận thủ công |
| `needs_review` | Có nét hoặc ứng viên nhưng chưa thể chốt | `null`, giữ các ứng viên và lý do |
| `not_found` | Không tìm được cụm số trong vùng cấu hình | `null`; không khẳng định tài liệu chắc chắn không có số |
| `error` | Lỗi khi chạy nhánh nhận dạng số trang | `null`, có chi tiết lỗi |
| `disabled` | Người dùng không bật tính năng | `null` |

Thêm `candidates` khi có nhiều kết quả để giữ văn bản đọc được, điểm, tọa độ và biến thể ảnh tương ứng. Không tạo ứng viên bằng cách suy đoán từ chuỗi số trang.

Khi người dùng sửa/xác nhận, giữ kết quả máy trong `raw_text`/`candidates`, lưu nhãn đã sửa trong `value`, nguồn `manual` và thời điểm xác nhận. Nhãn sửa vẫn phải gồm chữ số và giữ số 0 đầu.

Markdown vẫn có tiêu đề `## Trang 5 / 20` để chỉ vị trí trong PDF, thêm dòng riêng:

```markdown
**Số trang ghi tay:** 003
```

Nếu chưa chốt, hiển thị “Cần kiểm tra” hoặc “Không tìm thấy trong vùng cấu hình”; không hiển thị một ứng viên mơ hồ như số đã xác định. Không thay tiêu đề trang PDF bằng nhãn ghi tay.

Nếu người dùng chọn xuất cả Markdown và JSON, thao tác sửa/xác nhận phải cập nhật cả hai từ cùng dữ liệu, dùng cơ chế ghi tạm/rollback để tránh hai file có nhãn khác nhau. Không cần tạo file ảnh phụ trong bản đầu; vùng đối chiếu được render từ PDF nguồn và tọa độ đã lưu.

## 5. Giao diện trên màn hình Việt OCR

- Thêm tùy chọn **Đọc số trang viết tay**, bật sẵn trên Việt OCR để đọc nhãn trong mỗi lần OCR trang; có thể tắt khi không cần. PaddleOCR không bật nhánh này.
- Phần nâng cao cho phép đặt vùng góc trên phải và xem vùng trên trang mẫu. Thay vùng chỉ áp dụng cho lần chạy tiếp theo, không đổi kết quả đang xử lý.
- Thêm bảng chi tiết theo trang: thứ tự PDF, số ghi tay, trạng thái, điểm nhận dạng và xác nhận.
- Chọn một dòng để xem ảnh vùng số; người dùng sửa nhãn và xác nhận bằng nút rõ ràng. Không đánh dấu tự xác nhận chỉ vì người dùng chọn dòng.
- Cho phép lọc các trang cần kiểm tra và nhận dạng lại trang đang chọn sau khi chỉnh vùng.
- Không tạo màn hình chức năng chính mới. Tiếp tục dùng tiến độ, hủy và bộ điều phối tác vụ tài liệu hiện có.
- Lỗi riêng của số trang không làm mất nội dung OCR toàn văn; thể hiện lỗi riêng trong JSON và giao diện. Nếu worker/model chung bị dừng thì báo lỗi tác vụ tương ứng.

## 6. Thứ tự triển khai

| Giai đoạn | Công việc | Kết quả cần có |
| --- | --- | --- |
| 1. Bộ mẫu | Thu thập khoảng 100–200 trang đại diện; gán đúng nhãn dạng chuỗi và vùng số; có trang không đánh số | Tập mẫu gồm số 1–nhiều chữ số, số 0 đầu, nhiều nét bút và chất lượng scan |
| 2. Thử nghiệm | Cắt ROI, phát hiện cụm số, thử VietOCR hiện có và các biến thể ảnh có giới hạn | Báo cáo lỗi phát hiện, lỗi đọc và tỷ lệ nhầm; chưa tích hợp tự chấp nhận đại trà |
| 3. Worker/service | Thêm tham số bật/tắt và vùng tìm kiếm; trả metadata số trang, ánh xạ tọa độ, xuất Markdown/JSON | Đọc tự động được trên luồng file/thư mục, giữ tương thích dữ liệu cũ |
| 4. Giao diện | Bảng theo trang, xem vùng cắt, sửa/xác nhận, lưu và nhận dạng lại | Kết quả kiểm tra thủ công được lưu nhất quán vào các định dạng đã chọn |
| 5. Đánh giá | Chạy tập kiểm thử tách biệt, chốt ngưỡng, đo thời gian CPU | Quyết định đưa vào sử dụng hoặc cần huấn luyện bổ sung |

Nếu cần fine-tune, dùng dữ liệu cụm số viết tay từ tài liệu thực tế và tách train/validation/test theo tài liệu, người viết hoặc nguồn scan khi có thể. Không chia ngẫu nhiên các biến thể ảnh của cùng một vùng số sang nhiều tập. Chỉ fine-tune khi xác định lỗi thuộc nhận dạng; lỗi khoanh vùng cần sửa ở bước phát hiện trước.

## 7. Kiểm thử và nghiệm thu

- Đo riêng độ phủ tìm vùng số, tỷ lệ đúng toàn bộ chuỗi, tỷ lệ trang tự chấp nhận, tỷ lệ đọc sai nhưng được tự chấp nhận và tỷ lệ cần kiểm tra.
- Kiểm thử `1`, `12`, `003`, chữ số dính nét, nét mờ, bút xanh/đỏ, số bị gạch sửa, trang không có số và số nằm ngoài vùng mặc định.
- Bao gồm các cặp dễ nhầm như `1/7`, `0/6`, `3/8`, `5/9` trong dữ liệu thực tế; dấu `/` ở đây chỉ cặp cần phân biệt, không phải định dạng nhãn.
- Có mẫu ngày tháng/số văn bản/dấu đóng ở góc trên phải để đo nhận nhầm.
- Kiểm tra PDF thứ 5 ghi `003`, số không liên tiếp, đánh số lại giữa file, ảnh xoay 90/180/270 độ, trang ngang/A3 và tọa độ overlay.
- Xác nhận không mất số 0 đầu, không lấy thứ tự PDF thay nhãn ghi tay, không tự điền nhãn thiếu và không xóa chữ khỏi toàn văn.
- Kiểm tra bật/tắt, sửa/xác nhận, xuất riêng Markdown/JSON, đồng bộ khi xuất cả hai, ghi đè, hủy, thư mục con và lỗi một trang.
- Đo chi phí tăng thêm theo trang trên máy CPU hiện tại. Chốt ngưỡng độ chính xác và hiệu năng sau giai đoạn thử nghiệm, chưa cam kết tỷ lệ trước khi có dữ liệu.

## 8. Thành phần dự kiến chỉnh sửa

- `CountFile2/VietOcr/worker.py`: nhánh đọc số ghi tay, kết quả ứng viên và tọa độ.
- `CountFile2/Services/VietOcrService.cs`: cấu hình ROI, truyền tùy chọn, nhận metadata và phối hợp lưu kết quả.
- `CountFile2/Services/VietOcrMarkdownExport.cs`: hiển thị số ghi tay tách biệt với thứ tự PDF.
- `CountFile2/frmVietOCR.cs`: tùy chọn và giao diện kiểm tra theo trang.
- Bộ kiểm thử VietOCR và tài liệu hướng dẫn: bổ sung ca mẫu, tương thích dữ liệu và nghiệm thu.

Lưu ý service hiện có cấu hình engine dùng chung; trường metadata số trang là tùy chọn, không bắt các worker khác phải trả trường này. Tính năng thử nghiệm triển khai trước cho worker VietOCR.

## Tham khảo

- [VietOCR — hỗ trợ nhận dạng chữ viết tay/chữ đánh máy](https://github.com/pbcquoc/vietocr).
- [Predictor — API nhận dạng và điểm model](https://github.com/pbcquoc/vietocr/blob/master/vietocr/tool/predictor.py).

Các kích thước vùng, cách chọn ứng viên và ngưỡng quyết định trong kế hoạch là đề xuất triển khai, không phải cam kết chất lượng của model.
