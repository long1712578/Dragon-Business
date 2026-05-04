# 🐉 Dragon Business API

**Dragon Business** là dịch vụ lõi (Core Service) xử lý nghiệp vụ Cửa hàng (Cafe/Nhà hàng), Quản lý Nhân sự (Staff), và Tích hợp Thanh toán (Payments) cho hệ sinh thái Dragon.

Hệ thống được thiết kế theo kiến trúc **Vertical Slice Architecture**, tối ưu hóa tuyệt đối cho **.NET 9 Native AOT** và môi trường container giới hạn tài nguyên (như K3s 2GB RAM).

---

## 🛠️ Công nghệ cốt lõi
- **Runtime:** .NET 9 (C# 13)
- **Kiến trúc:** Vertical Slice / Feature-based Modules
- **Hiệu năng:** Native AOT (Ahead-of-Time Compilation)
- **Cơ sở dữ liệu:** SQLite (In-process, Tối ưu hóa với Raw SQL cho AOT)
- **Event Streaming:** RedisFlow (Kiến trúc Event-Driven qua Redis Streams)
- **Tài liệu API:** Scalar API Reference (`/scalar/v1`)
- **Real-time:** SignalR

---

## 🏗️ Kiến trúc Hệ thống (Vertical Slice Architecture)

Để đảm bảo hiệu suất tốt nhất cho Native AOT (loại bỏ Reflection) và dễ dàng bảo trì, hệ thống từ bỏ Clean Architecture đa tầng rườm rà để chuyển sang **Kiến trúc phân chia theo tính năng (Feature-based)** trong phạm vi 1 Project duy nhất.

Mỗi thư mục `Module` chứa trọn vẹn nghiệp vụ từ API Endpoints, Services đến Data Access:

```text
Dragon.Business/
├── Configuration/            # Cấu hình lõi của ứng dụng
│   └── AppJsonContext.cs     # Nơi đăng ký DTOs & Serialization cho Native AOT
├── Infrastructure/           # Tương tác với thế giới bên ngoài (DB, Hubs)
│   └── Data/
│       └── DatabaseInitializer.cs # Chứa logic Seed Database thay vì EF Migration
├── Modules/                  # Các Business Features (Vertical Slices)
│   ├── Orders/               # Quản lý Đơn hàng & Menu Cafe
│   │   ├── OrderEndpoints.cs
│   │   └── CafeOrderService.cs
│   ├── Payments/             # Xử lý Thanh toán (ZaloPay, MoMo, Mock)
│   │   ├── PaymentEndpoints.cs
│   │   └── PaymentService.cs
│   ├── Staff/                # Quản lý Nhân viên & Thống kê
│   │   ├── StaffEndpoints.cs
│   │   └── StaffService.cs
│   └── Notifications/        # Lắng nghe sự kiện RedisFlow và đẩy qua SignalR
└── Program.cs                # Entry point siêu gọn gàng (~130 dòng)
```

### 💎 Điểm nổi bật của kiến trúc này:
1. **Cô lập nghiệp vụ (High Cohesion):** Mọi logic liên quan đến Thanh toán đều nằm trong `Modules/Payments`. Giảm rủi ro side-effect khi bảo trì.
2. **Native AOT Compatible:** Toàn bộ DTOs được khai báo trong `AppJsonContext.cs` với `[JsonSerializable]`, đảm bảo ứng dụng không bị crash khi biên dịch Ahead-of-Time.
3. **Event-Driven Workflow:** Khi một đơn hàng được thanh toán, `PaymentService` không gọi trực tiếp SignalR mà **bắn sự kiện vào Redis Stream**. `NotificationConsumer` (Background) sẽ nhặt sự kiện và đẩy cho Client. Điều này đảm bảo tốc độ phản hồi Webhook cho ZaloPay cực nhanh (<1ms).

---

## 🚀 Hướng dẫn Test Business Logic (Step-by-Step)

Mở trình duyệt truy cập tài liệu API: `http://localhost:5077/scalar/v1`

### Kịch bản: Nhân viên lên đơn và khách thanh toán
1. **Lên đơn hàng (Order):**
   - **Endpoint:** `POST /api/orders`
   - Tạo đơn hàng với các món cafe, gán `staffId` của nhân viên phục vụ. Hệ thống trả về `OrderId`.
2. **Yêu cầu Thanh toán (Checkout):**
   - **Endpoint:** `POST /api/orders/{id}/checkout`
   - Gọi API này để tạo link thanh toán (hoặc lấy mã QR ảo).
3. **Giả lập Khách quét mã thanh toán:**
   - **Endpoint:** `POST /api/payments/mock/{paymentOrderId}/simulate-paid`
   - Bấm thực thi để giả lập ZaloPay gọi Webhook báo thành công.
4. **Kết quả hệ thống tự động xử lý:**
   - Bảng `Payments` được cập nhật thành `Paid`.
   - Bảng `CafeOrders` tự động chuyển status sang `Completed`.
   - Sự kiện `PaymentSuccessEvent` được bắn lên RedisFlow.
   - Nhân sự được cộng dồn doanh thu KPI.

---

## 👥 Quản lý Nhân sự (Staff API)
- **GET `/api/staff`**: Lấy danh sách nhân viên và tổng tiền thưởng/doanh thu họ mang lại.
- **POST `/api/staff`**: Thêm nhân viên mới (Yêu cầu quyền Manager).
- **DELETE `/api/staff/{id}`**: Xóa nhân sự (Yêu cầu quyền Manager).

---
*Developed with ☕ & 🐉 by The Dragon Setup Team.*