# Kế hoạch thực hiện Form OCR / OCR Form Implementation Plan

## 1. Thiết kế giao diện / UI Design
### Components cần thêm / Required Components:
- Form title: "Xử Lý OCR"
- Input/Output section:
  - 2 TextBox cho input và output path
  - 2 Button để mở FolderBrowserDialog
  - Label cho mỗi TextBox

### Layout chia thành 3 phần / 3-pane layout:
1. **Pane 1 - File Explorer (Left):**
   - TreeView để hiển thị cấu trúc thư mục
   - ListView để hiển thị danh sách file
   - Size: ~25% của form width

2. **Pane 2 - Document Viewer (Center):**
   - PictureBox/PDF Viewer control
   - Size: ~50% của form width
   - Sử dụng PdfiumViewer cho PDF
   - Sử dụng PictureBox cho ảnh

3. **Pane 3 - Metadata (Right):**
   - Labels và TextBoxes cho:
     - Số Văn Bản
     - Cơ Quan Ban Hành
     - Ngày Ban Hành
     - Loại Văn Bản
     - Trích Yếu
   - Size: ~25% của form width

## 2. Chức năng / Functionality

### Input/Output Path Selection:
1. Thêm FolderBrowserDialog
2. Xử lý sự kiện click button để mở dialog
3. Cập nhật TextBox với đường dẫn đã chọn
4. Validate đường dẫn

### Pane 1 - File Explorer:
1. Load và hiển thị cấu trúc thư mục từ input path
2. Xử lý sự kiện click vào thư mục:
   - Cập nhật input path
   - Reload danh sách file và thư mục
3. Xử lý sự kiện click vào file:
   - Kiểm tra định dạng file (PDF/Image)
   - Load file vào Pane 2 nếu hợp lệ

### Pane 2 - Document Viewer:
1. Implement PDF viewer using PdfiumViewer
2. Implement image viewer using PictureBox
3. Xử lý chuyển đổi giữa hai chế độ xem
4. Optimize hiệu suất hiển thị

### Pane 3 - Metadata:
1. Hiển thị/ẩn panel dựa trên loại file được chọn
2. Clear data khi chuyển file
3. Validate input data
4. Chuẩn bị cho OCR processing

## 3. Thư viện sử dụng / Required Libraries
- PdfiumViewer: Xem file PDF
- System.Drawing: Xử lý ảnh
- Tesseract: OCR processing
- Windows Forms components

## 4. Các bước thực hiện / Implementation Steps
1. Tạo UI cơ bản với layout 3 pane
2. Implement input/output path selection
3. Implement file explorer trong Pane 1
4. Implement document viewer trong Pane 2
5. Implement metadata panel trong Pane 3
6. Thêm xử lý sự kiện cho tất cả components
7. Tối ưu hiệu suất và UX
8. Testing và debug

## 5. Lưu ý / Notes
- Sử dụng try-catch cho file operations
- Validate tất cả input từ user
- Implement loading indicators
- Tối ưu memory usage khi xử lý file lớn
- Thêm tooltips cho better UX
- Implement proper cleanup và disposal của resources 