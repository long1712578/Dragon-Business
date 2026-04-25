# 🐉 Dragon Business API

Dịch vụ lõi xử lý Thanh toán (Payments) và Quản lý nhân sự (Staff) cho hệ sinh thái Dragon. Tối ưu hóa cho .NET 9 Native AOT và chạy trên hạ tầng K3s.

## 🛠️ Công nghệ sử dụng
- **Runtime:** .NET 9
- **Deployment:** Native AOT (Ahead-of-Time)
- **Database:** SQLite (In-process)
- **Message Broker:** RedisFlow (Redis Streams)
- **Documentation:** Scalar API Reference (`/scalar/v1`)

---

## 🚀 Hướng dẫn Test Business Logic (Step-by-Step)

Mở trình duyệt truy cập: `http://localhost:5077/scalar/v1`

### Bước 0: Kiểm tra hệ thống
- **GET /**
- Kỳ vọng: Trả về JSON `{ "Message": "Dragon Business API is running!" }`

### Bước 1: Tạo đơn hàng thanh toán
- **Endpoint:** `POST /api/payments/create`
- **Body:**
```json
{
  "amount": 120000,
  "desc": "Hoa don ca phe",
  "staffId": "1"
}
```
- **Lưu ý:** Copy `orderId` từ response (ví dụ: `6db1beb03b`).

### Bước 2: Lấy mã chữ ký (MAC) để giả lập Webhook
- **Endpoint:** `POST /api/dev/webhook/sign`
- **Body:**
```json
{
  "orderId": "6db1beb03b",
  "result": "paid"
}
```
- **Kỳ vọng:** Trả về mã `mac`. Copy mã này cho Bước 3.

### Bước 3: Gửi Webhook xác nhận thanh toán (Simulate ZaloPay)
- **Endpoint:** `POST /api/payments/webhook/zalopay`
- **Header:** `x-zalopay-signature: <MA_MAC_O_BUOC_2>`
- **Body:**
```json
{
  "jsonContent": "{\"orderId\":\"6db1beb03b\",\"result\":\"paid\"}",
  "orderId": "6db1beb03b"
}
```
- **Kỳ vọng:** Trả về `return_code: 1` (success).

### Bước 4: Kiểm tra trạng thái cuối cùng
- **Endpoint:** `GET /api/payments/{orderId}`
- **Kỳ vọng:** Trạng thái chuyển từ `Created` sang `Paid` và có giá trị `paidAt`.

---

## 👥 Module Nhân sự (Staff)
- **GET /api/staff**: Lấy danh sách nhân viên và tổng tiền thưởng nhận được.
- **POST /api/staff**: Thêm nhân viên mới.
- **GET /api/staff/{id}**: Chi tiết nhân viên và lịch sử giao dịch liên quan.

---
*Author: Antigravity AI — TechLead*
------------------------------
Chào bạn, đây là một câu hỏi rất hay để làm rõ bức tranh tổng thể (Big Picture). Dưới góc độ Solution Architect, tôi sẽ giải thích cho bạn theo cách dễ hiểu nhất, kết hợp giữa thực tế kinh doanh và kỹ thuật.

1. Mối quan hệ giữa Staff và Payment
Trong hệ thống Dragon PayHub, mối quan hệ này là "Gắn kết để ghi nhận":

Về mặt dữ liệu: Một bản ghi Payment sẽ có một trường StaffId. Điều này có nghĩa là mỗi khi khách hàng trả tiền, chúng ta biết chính xác nhân viên nào đang phục vụ giao dịch đó.
Mục đích:
Tính thưởng (Tip): Quán cafe có thể dùng cái này để chia tiền tip cho nhân viên vào cuối tháng.
Quản lý (Accountability): Nếu có khiếu nại về đơn hàng, chủ quán biết nhân viên nào đã xử lý đơn đó.
KPI: Biết được ai là nhân viên năng nổ nhất, mang về nhiều doanh thu nhất.
2. MAC là gì? (Tại sao nó quan trọng?)
MAC (Message Authentication Code) là "Dấu vân tay" của dữ liệu.

Hãy tưởng tượng ZaloPay gửi cho bạn một tin nhắn: "Đơn hàng 6db1 đã trả 120.000đ thành công".

Vấn đề: Làm sao bạn biết tin nhắn này là từ ZaloPay thật, hay là một "hacker" nào đó giả mạo gửi tin nhắn để lấy cafe miễn phí?
Giải pháp (MAC):
ZaloPay và Bạn cùng giữ một "Mật mã bí mật" (Key2).
ZaloPay lấy nội dung tin nhắn + Mật mã bí mật -> Chạy qua một thuật toán toán học (HMAC-SHA256) -> Ra được mã MAC.
Khi bạn nhận tin nhắn, bạn cũng lấy nội dung đó + Mật mã bí mật -> Tự tính lại mã MAC.
Nếu MAC của bạn == MAC của ZaloPay, chứng tỏ tin nhắn là THẬT và KHÔNG BỊ SỬA ĐỔI.
3. Workflow làm việc của quán Cafe (Scenario thực tế)
Đây là cách hệ thống của bạn sẽ vận hành trong thực tế:

Bước 1: Khách gọi món & Tạo mã QR
Nhân viên (tên là An) nhận order 1 ly Cafe ($50k).
An mở App Dragon PayHub, nhập số tiền 50.000đ và chọn tên mình là người phục vụ.
Hệ thống tạo đơn hàng (Status: Created) và hiện mã QR ZaloPay lên màn hình máy tính hoặc tablet của quán.
Bước 2: Khách thanh toán
Khách dùng app ZaloPay quét mã QR và xác nhận thanh toán trên điện thoại của khách.
Bước 3: Webhook & RedisFlow (Xử lý ngầm)
ZaloPay báo về máy chủ của bạn qua Webhook. Máy chủ kiểm tra mã MAC để xác nhận tiền đã vào tài khoản chủ quán thật.
Trạng thái đơn hàng chuyển thành Paid.
Một sự kiện PaymentSuccessEvent được bắn vào RedisFlow.
Bước 4: Thông báo & Ghi nhận
Hệ thống thông báo (Notification Hub) đang nghe RedisFlow sẽ ngay lập tức:
Bắn một tin nhắn Telegram cho Chủ quán: "Nhân viên An vừa nhận thanh toán 50.000đ".
Cập nhật vào bảng thống kê của An: Tổng tiền phục vụ trong ngày tăng thêm 50.000đ.
💡 Lợi ích cho Shop/Cafe nhỏ:
Chủ quán không cần phải có mặt ở quán 24/7. Chỉ cần ngồi ở nhà, mỗi khi nhân viên thanh toán xong, Telegram sẽ báo "Ting Ting" về điện thoại. Cuối tháng chỉ cần mở GET /api/staff là thấy ngay bảng lương/thưởng cho từng người.


----------flow cũ
sequenceDiagram
    participant Z as ZaloPay / Mock
    participant P as PaymentService
    participant S as SignalR
    participant D as Dashboard (Browser)

    Z->>P: Webhook / Simulate
    Note over P: Cập nhật Database
    P->>S: Gửi trực tiếp thông báo
    S-->>D: Hiển thị lên Dashboard
    P->>Z: Trả về kết quả (OK)
Vấn đề: Nếu SignalR gặp sự cố hoặc Dashboard quá tải, nó có thể làm chậm hoặc gây lỗi cho quá trình xử lý thanh toán.

----------flow mới
ssequenceDiagram
    participant Z as ZaloPay / Mock
    participant P as PaymentService
    participant R as Redis Stream
    participant C as Notification Consumer
    participant S as SignalR
    participant D as Dashboard (Browser)
    Z->>P: Webhook / Simulate
    Note over P: Cập nhật Database
    P->>R: Bắn Event "PaymentSuccess"
    P->>Z: Trả về kết quả ngay lập tức (Cực nhanh)
    
    Note over R: Event nằm an toàn trong Redis
    
    R->>C: Consumer nhặt tin nhắn
    C->>S: Yêu cầu gửi thông báo
    S-->>D: Hiển thị lên Dashboard

Bạn hãy nói với Team 4 điểm "vàng" này để họ thấy độ "vịn" của kiến trúc mới:

Độ tin cậy tuyệt đối (Reliability):

Nếu Dashboard bị sập hoặc SignalR bị lỗi, đơn hàng của khách hàng vẫn được xử lý thành công.
Tin nhắn được lưu an toàn trong Redis. Khi Dashboard hoặc Consumer online trở lại, nó sẽ tự động xử lý các tin nhắn còn tồn đọng. Không bao giờ mất thông báo!
Tốc độ phản hồi cực nhanh (Performance):

PaymentService không cần đợi SignalR gửi xong thông báo mới trả về kết quả cho ZaloPay. Nó chỉ cần "ném" tin nhắn vào Redis (mất < 1ms) rồi trả về luôn. Điều này giúp giảm thiểu rủi ro bị timeout Webhook.
Dễ dàng mở rộng (Extensibility):

Giả sử ngày mai Team muốn: "Khi thanh toán xong thì gửi Email cho sếp và in hóa đơn".
Cũ: Phải vào sửa nát file PaymentService.cs.
Mới: Chỉ cần tạo thêm 1 Consumer mới (vd: EmailConsumer) lắng nghe cùng stream Redis đó. Không cần động vào một dòng code nào của logic thanh toán cũ.
Sẵn sàng cho Microservices (Scale):

Sau này nếu hệ thống lớn lên, bạn có thể tách phần gửi Notification sang một Server riêng (Service riêng) để chạy. PaymentService chỉ việc bắn event, còn Service khác lo việc gửi. Hệ thống của bạn đã sẵn sàng cho việc mở rộng quy mô lớn (Scaling).
Tóm lại: Chúng ta đang chuyển từ một hệ thống "mọi thứ dính chùm vào nhau" sang một hệ thống Event-Driven chuyên nghiệp, giúp ứng dụng của chúng ta chạy nhanh hơn, an toàn hơn và cực kỳ dễ bảo trì. 🐉🚀🔥