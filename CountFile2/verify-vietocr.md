# Kiểm chứng màn hình Việt OCR

Ngày kiểm tra: 23/10/2026. Môi trường: Windows, .NET Framework 4.8, Python 3.11.9 x64, PyTorch CPU. Không có CUDA khả dụng trên máy kiểm thử.

## Phạm vi đã triển khai

- Menu **Việt OCR**, đầu vào một PDF hoặc thư mục, tùy chọn quét thư mục con.
- OCR tất cả trang qua ảnh render, detector DBNet (RapidOCR 1.4.4) và VietOCR 0.3.13 / vgg_seq2seq.
- Xuất TXT, JSON hoặc cả hai, giữ đường dẫn thư mục tương đối và PDF nguồn.
- JSON giữ toàn văn từng trang, dòng, tọa độ, điểm nhận dạng, cảnh báo và lỗi.
- Chạy nền, một tác vụ tài liệu tại một thời điểm, hủy, chuyển màn hình giữ trạng thái, xem trước và mở kết quả.
- Chặn ghi đè mặc định; thay thế có rollback khi xuất cả hai định dạng và một file gặp lỗi.
- Model chuẩn bị sẵn cục bộ; inference không cần mạng. SHA-256 model: `0921503a41375a0584268e23ef3d414ea478a8fe8777865c7745d38f2d0bc5db`.

## Kết quả kiểm tra

1. `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1` — PASS.
   - Worker giả riêng chỉ dành cho kiểm thử: đủ trang, UTF-8, TXT-only/JSON-only, thư mục con, tên trùng, file PDF hỏng, lỗi trang, thiếu model, timeout, hủy và dừng process worker.
   - Giữ file cũ khi không cho ghi đè; rollback TXT khi JSON bị khóa; dọn thư mục tạm.
   - Giao diện thực: điều hướng, giữ trạng thái, khóa cấu hình, xem trước kết quả, hủy và mở lại thao tác.
   - Kiểm tra bố cục qua ảnh tại chiều rộng 1050 và 1280 pixel.
   - Artifacts: `tmp/vietocr-tests/run-8211cb77653f4fc9ae8c2687a7488ccd/`.
2. `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1 -RealOcr` — PASS.
   - Detector và VietOCR thật, CPU, PDF tiếng Việt hai trang, 300 DPI; tổng thời gian khoảng 33,5 giây gồm khởi động/nạp model.
   - Cả hai trang có văn bản OCR, TXT và JSON đọc được, xác nhận nội dung ở cả trang đầu và trang thứ hai.
   - Artifacts: `tmp/vietocr-tests/run-7e47422798a94f37831c59fa1e64c0fe/real-output/`.
   - Đây là smoke test vận hành, chưa phải benchmark độ chính xác. Mẫu thực tế vẫn có lỗi dấu và dấu câu; điểm model cao không chứng nhận nội dung đúng.
3. `python tests\test_vietocr_geometry.py` — PASS: thứ tự hai cột với tiêu đề/chân trang kéo dài và ánh xạ ngược tọa độ sau xoay 0/90/180/270 độ.
4. `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-WinFormsNavigationTests.ps1` — PASS: điều hướng/cache form, trạng thái tác vụ, hủy và đóng ứng dụng của các chức năng hiện có.
5. MSBuild (Visual Studio 2022): `CountFile2.sln /t:Build /p:Configuration=Debug /verbosity:quiet /nologo /nr:false` — PASS; bản Release dùng cùng lệnh với `Configuration=Release`. Có cảnh báo cũ trong Form1/frmXuLyOCR.
6. `.vietocr-venv\Scripts\python.exe -m pip check` — PASS, không có dependency bị thiếu/xung đột sau khi cập nhật setuptools 80.9.0.

## Chưa kiểm chứng hoặc chưa triển khai

- Chưa có tập gán nhãn để đo CER, độ phủ detector hay tỷ lệ đúng theo nhóm tài liệu. Cần đối chiếu kết quả với bản gốc.
- CUDA chưa kiểm thử vì máy không có CUDA khả dụng.
- Designer round trip chưa chạy: công cụ phiên này không có quyền điều khiển giao diện Visual Studio Designer. Form mới được dựng bằng code theo mẫu các màn hình dịch vụ hiện có; đã kiểm thử runtime/layout.
- Bảng được đọc thành dòng chữ và tọa độ, chưa khôi phục ô bảng. Chưa có overlay/sửa chữ, tiếp tục giữa PDF hoặc OCR ngữ nghĩa ảnh/chữ ký.

Giữ nguyên target framework, mô hình khởi động, Designer files và các engine OCR hiện có. Hướng dẫn sử dụng/cài đặt: `CountFile2/VietOcr/README.md`.

## Cập nhật 24/10/2026 — Markdown / JSON

- Màn hình chọn **Markdown + JSON**, **Markdown (.md)** hoặc **JSON (.json)**; mặc định xuất cả hai. Không cần thay model hoặc tải thêm thư viện inference.
- Markdown UTF-8 có tên tài liệu, tổng số trang, tiêu đề từng trang, trạng thái và lỗi; giữ ngắt dòng, escape ký tự Markdown/HTML để hiển thị chữ OCR nguyên văn.
- Thêm `WriteMarkdown` và `MarkdownPath`; service vẫn hỗ trợ TXT cho tương thích. Ghi đè/rollback áp dụng cho tất cả file đầu ra được chọn.
- Khung xem trước hiển thị mã Markdown/JSON; JSON-only tự chọn đúng định dạng. Đã kiểm tra layout ở chiều rộng 1050 và 1280 pixel.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1` — PASS; thêm trường hợp Markdown-only, Markdown+JSON, Unicode, phân trang, escape, lỗi trang, không ghi đè, rollback khi JSON bị khóa, chuyển xem Markdown/JSON, JSON-only và hủy.
  - Artifacts: `tmp/vietocr-tests/run-bc0edbe2a0a9488a82cee64bcbf69558/`.
- `python tests\test_vietocr_markdown_render.py tmp\vietocr-tests\run-bc0edbe2a0a9488a82cee64bcbf69558` — PASS; kiểm tra HTML sau render CommonMark: đúng tiêu đề/ngắt dòng, nội dung gốc không tạo thêm heading/link/ảnh/HTML/code block và lỗi trang vẫn được hiển thị. Script QA này dùng markdown-it-py/beautifulsoup4 có trong Python kiểm thử, không phải dependency của ứng dụng.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1 -RealOcr` — PASS; detector và VietOCR thật, CPU, PDF hai trang ở 300 DPI, xuất Markdown/JSON khoảng 37,9 giây gồm nạp model. Đây là kiểm tra vận hành, chưa đo độ chính xác.
  - Markdown mẫu: `tmp/vietocr-tests/run-8693a6b6d3e04b0c9bd306e8bb92bf6a/real-output/văn bản.pdf.ocr.md`.
- MSBuild Release: `CountFile2.sln /t:Build /p:Configuration=Release /verbosity:quiet /nologo /nr:false`.
- Designer round trip chưa chạy vì không có công cụ điều khiển Visual Studio Designer; runtime UI đã kiểm thử. Markdown chưa suy luận cấp tiêu đề hay cấu trúc bảng của PDF.

## Cập nhật 25/10/2026 — Số trang ghi tay tích hợp theo trang

- Bật sẵn trên màn hình Việt OCR, vùng mặc định 20% bên phải × 10% phía trên, chỉnh được 1–100%. Một yêu cầu worker cho mỗi trang vừa OCR toàn văn vừa đọc vùng số, dùng chung ảnh/model. PaddleOCR không bật tính năng này.
- Metadata riêng giữ số 0 đầu, nhãn gốc/ứng viên, tọa độ, điểm và trạng thái. Không suy ra nhãn từ chỉ số PDF hoặc sửa chữ thành số. Vùng trống không gọi bộ nhận dạng.
- Tab kiểm tra theo trang hỗ trợ xem ảnh cắt, sửa/xác nhận, xác nhận không có số, lọc trang cần kiểm tra và chỉ đọc lại nhãn trang đang chọn. Lưu cập nhật Markdown/JSON đồng thời, có rollback và lịch sử.
- Cache JSONL chỉ giữ cho phiên làm việc, cập nhật theo dòng để tránh nạp toàn bộ tài liệu vào RAM, xóa khi dispose kết quả. PDF nguồn được kiểm tra kích thước/thời điểm sửa trước khi đối chiếu/đọc lại.

### Kiểm chứng

- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1` — PASS, artifacts `tmp/vietocr-tests/run-6c4d76334bba47e8a74c54604774e604/`.
  - 7 kiểm thử nhánh số: đúng ROI 20×10, ngoài vùng/chỉnh vùng, giữ `003`, vùng trống/tắt không gọi model, từ chối `O03`/`0 03`/`12/100`, ứng viên bất đồng/điểm thấp, xoay tọa độ và lỗi cục bộ.
  - Service/UI: một yêu cầu trên mỗi trang, nhãn không thay chỉ số PDF, nhánh số lỗi không làm mất toàn văn, sửa giữ nhãn máy gốc, ghi đè/rollback khi JSON bị khóa, xác nhận không có số, đọc lại riêng trang, Markdown-only/JSON-only, xóa cache, tab kiểm tra và xác nhận qua UI.
  - 2 kiểm thử bố cục/tọa độ cũ và các ca xuất file/hủy/timeout/điều hướng tiếp tục qua.
  - Bộ kiểm thử có thay thế cache tạm được chạy ngoài AppContainer: sandbox Windows chặn `File.Replace` trên cache trong thư mục AC/Temp; thao tác ngoài sandbox chạy thành công. Đây là giới hạn của môi trường kiểm thử, không dùng việc bỏ qua ca kiểm tra để ghi nhận PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-VietOcrTests.ps1 -RealOcr` — PASS: model/detector thật, PDF hai trang, toàn văn + metadata số; hai vùng trống trả `not_found`, không bịa nhãn. Khoảng 33,2 giây gồm nạp model. Artifacts `tmp/vietocr-tests/run-3b126f4e3e974c43a80c07d028df0eaf/`.
- `.vietocr-venv\Scripts\python.exe tests\smoke_vietocr_page_number.py tmp\vietocr-number-real-20261025` — PASS: model thật trên số mô phỏng; `003` và `12` được đọc đúng; `1` đọc ra nhưng điểm dưới 0,9 nên giữ `value=null`, trạng thái cần kiểm tra. Thời gian đầy đủ mỗi ảnh khoảng 1,2–2,1 giây sau khi nạp model; đây không phải số đo chi phí tăng thêm của nhánh số trên PDF thực tế.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-WinFormsNavigationTests.ps1` — PASS: điều hướng, trạng thái, hủy và đóng ứng dụng. Artifacts `tmp/winforms-navigation-tests/run-2f25cddb0e8a44468a804dba154afc53/`.
- `python tests\test_paddleocr_adapter.py` — PASS, 3 kiểm thử tương thích adapter.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tests\Run-PaddleOcrSmoke.ps1 -Pdf <PDF hai trang mẫu>` — PASS với model PaddleOCR thật qua service dùng chung; không bật đọc số trang. Artifacts `tmp/paddle-smoke-651337200a9643beaef6ea377ff8ef17/`.
- MSBuild: `CountFile2.sln /t:Build /p:Configuration=Release /verbosity:quiet /nologo /nr:false` — PASS, cảnh báo cũ ở Form1/frmXuLyOCR, không có lỗi biên dịch mới.
- Runtime UI đã kiểm tra bằng ảnh ở chiều rộng 1050/1280 và tab số ghi tay. Designer round trip chưa chạy vì không có công cụ điều khiển Visual Studio Designer trong phiên này.

### Giới hạn chất lượng

Chưa có tập số viết tay thực tế do người dùng cung cấp/gán nhãn để đo tỷ lệ đúng và hiệu chuẩn ngưỡng. Ngưỡng 0,9 cùng hai biến thể đồng thuận là cấu hình ban đầu, không phải bảo đảm độ chính xác. Chưa fine-tune, chưa tự dò chiều xoay trang, chưa mở lại kết quả cũ để kiểm tra trong phiên sau. Không báo hoàn thành bước đánh giá chất lượng dữ liệu thực tế chỉ từ ảnh mô phỏng.
