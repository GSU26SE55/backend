# API Documentation — NotificationService

> Base URL: `http://localhost:{port}/api`
> Content-Type mặc định: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem phần [Cấu trúc Response chung](#cấu-trúc-response-chung)

NotificationService quản lý notification gửi tới user theo nhiều kênh (Push/Email/SMS/InApp). Flow production chính tạo notification qua **RabbitMQ Consumer** trong `NotificationService.Application/Consumers/`. Tại thời điểm hiện tại có 3 consumer đã wire-up:

- `AlertTicketSagaFailedConsumer` ← `AlertTicketSagaFailedEvent` (Sprint 5B #238)
- `BatteryAlertEscalationRequestedConsumer` ← `BatteryAlertEscalationRequestedEvent` (Sprint 5B #238)
- `IotDeviceWentOfflineConsumer` ← `IotDeviceWentOfflineEvent` (Sprint IoT-1 #249)

Các loại notification khác trong `NotificationTypeEnum` (TicketCreated, SlaWarning, BatteryAnomalyDetected, …) hiện được tạo qua REST API `POST /api/notifications` (admin/internal tooling) hoặc sẽ mở rộng consumer sau.

REST API hiện tại phơi 2 endpoint phục vụ:

- Người dùng cuối: xem danh sách notification của chính mình.
- Admin / công cụ test: tạo notification thủ công (backfill, smoke test).

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": []
}
```

| Field | Type | Mô tả |
|---|---|---|
| `isSuccess` | `bool` | `true` nếu thành công, `false` nếu có lỗi nghiệp vụ |
| `statusCode` | `int` | HTTP status code |
| `message` | `string?` | Thông báo tóm tắt kết quả |
| `data` | `T?` | Dữ liệu trả về, `null` khi thất bại |
| `listErrors` | `Errors[]` | Danh sách lỗi validation — mỗi phần tử có `field` và `detail` |

**Pagination wrapper** (dùng cho `GET /api/notifications`):

```json
{
  "items": [ ... ],
  "totalItems": 42,
  "pageNumber": 1,
  "pageSize": 10
}
```

| Field | Type | Mô tả |
|---|---|---|
| `items` | `T[]` | Danh sách bản ghi của trang hiện tại |
| `totalItems` | `int` | Tổng số bản ghi match filter (không phụ thuộc page) |
| `pageNumber` | `int` | Trang hiện tại (1-based) |
| `pageSize` | `int` | Số bản ghi tối đa mỗi trang |

**Lỗi HTTP chung:**
- `400` — Validation hoặc input không hợp lệ; body theo `CommonResponse<T>` với `listErrors`
- `401` — Token thiếu/hết hạn/không hợp lệ (do JWT middleware trả, body không phải `CommonResponse<T>`)
- `403` — Có token nhưng không đủ quyền (sai role)
- `500` — Lỗi server ngoài dự kiến

---

## Enums

### `NotificationTypeEnum`

Phân loại notification, dùng cho template routing và filter UI. Giá trị int **không liên tục** — `15` đã được skip, `99` reserved cho `System`.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `TicketCreated` | 1 | Ticket mới được tạo |
| `TicketAssigned` | 2 | Ticket được assign cho Staff |
| `TicketStatusChanged` | 3 | Ticket đổi trạng thái trong state machine |
| `TicketResolved` | 4 | Ticket chuyển sang `RESOLVED` |
| `TicketClosed` | 5 | Ticket chuyển sang `CLOSED` |
| `TicketEscalated` | 6 | Ticket bị escalate (P1/P2 breach hoặc Staff request) |
| `SlaWarning` | 7 | Cảnh báo sớm sắp breach SLA |
| `SlaBreached` | 8 | SLA đã breach |
| `BatteryAnomalyDetected` | 9 | AI module phát hiện bất thường ở pin |
| `EnvironmentalIncidentDetected` | 10 | Sự cố môi trường (nhiệt độ, độ ẩm,…) được phát hiện |
| `EnvironmentalIncidentResolved` | 11 | Sự cố môi trường đã giải quyết |
| `AccountActivated` | 12 | Account được kích hoạt thành công |
| `AdminInvite` | 13 | Admin gửi lời mời tạo account cho Staff/Manager |
| `IncidentDeclared` | 14 | Incident chính thức được declare (Major Incident) |
| `BatteryAlertEscalationPending` | 16 | Sprint 5B #238 — Critical Alert chưa được ack > 5 phút (`BatteryAlertEscalationRequestedEvent`) |
| `AlertTicketSagaFailed` | 17 | Sprint 5B #238 — Alert–Ticket Saga vào terminal state `Failed`, admin cần reprocess (`AlertTicketSagaFailedEvent`) |
| `IotDeviceWentOffline` | 18 | Sprint IoT-1 (#249) — IoT edge device mất heartbeat > 5 phút |
| `System` | 99 | Notification hệ thống tổng quát (maintenance, broadcast,…) |

### `NotificationStatusEnum`

Trạng thái lifecycle của notification record.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Đã tạo trong DB, chờ Dispatcher gửi |
| `Sent` | 2 | Đã gửi thành công qua channel (push provider/SMTP/SMS gateway) |
| `Failed` | 3 | Gửi thất bại — đã hết số lần retry hoặc lỗi vĩnh viễn |
| `Read` | 4 | User đã đọc notification (InApp) |

### `NotificationChannelEnum`

Kênh phát notification.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Push` | 1 | Push notification qua Expo / FCM tới Mobile |
| `Email` | 2 | Email qua SMTP |
| `Sms` | 3 | SMS qua gateway |
| `InApp` | 4 | Notification trong app (hiển thị trong list, không cần kênh ngoài) |

### `NotificationFrequencyEnum`

Tần suất gửi notification — đặt ở `NotificationPreference` per-user. Không xuất hiện trên 2 endpoint hiện tại nhưng được khai báo trong domain để hỗ trợ digest (§49 — Notification advanced).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Immediate` | 1 | Gửi ngay khi có sự kiện |
| `Daily` | 2 | Gom thành digest gửi 1 lần/ngày |

### `DevicePlatformEnum`

Platform của device token (dùng cho push notification — domain reference).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Ios` | 1 | iOS device |
| `Android` | 2 | Android device |
| `Web` | 3 | Web push (browser) |

---

## Endpoints

Base route: `/api/notifications`

---

### `GET /api/notifications`

**Mục đích:** Lấy danh sách notification của **user hiện tại** (UserId trích xuất từ JWT claim). Hỗ trợ phân trang và filter theo `Type` / `Channel` / `Status` / `UnreadOnly`. Sắp xếp mặc định theo `CreatedAt` giảm dần.

**Auth:** `[Authorize]` — yêu cầu access token hợp lệ. Mọi role đã đăng nhập đều gọi được; mỗi user chỉ thấy notification của chính mình.

**Query parameters:**

| Param | Type | Bắt buộc | Default | Mô tả |
|---|---|---|---|---|
| `pageNumber` | `int` | Không | `1` | Số trang (1-based) |
| `pageSize` | `int` | Không | `10` | Số bản ghi mỗi trang |
| `type` | `NotificationTypeEnum?` (int) | Không | `null` | Lọc theo loại notification (1=TicketCreated, 2=TicketAssigned,…) |
| `channel` | `NotificationChannelEnum?` (int) | Không | `null` | Lọc theo kênh (1=Push, 2=Email, 3=Sms, 4=InApp) |
| `status` | `NotificationStatusEnum?` (int) | Không | `null` | Lọc theo trạng thái (1=Pending, 2=Sent, 3=Failed, 4=Read) |
| `unreadOnly` | `bool?` | Không | `null` | `true` → chỉ lấy notification có `Status != Read` |

**Ví dụ:**

```
GET /api/notifications?pageNumber=1&pageSize=20&channel=4&unreadOnly=true
Authorization: Bearer eyJ...
```

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "id": "8b9d0f1e-2a3b-4c5d-9e6f-7a8b9c0d1e2f",
        "userId": "1a2b3c4d-5e6f-7890-abcd-ef0123456789",
        "type": 9,
        "channel": 4,
        "status": 2,
        "title": "Phát hiện bất thường ở pin B-007",
        "body": "Voltage giảm bất thường lúc 12:34. Hãy kiểm tra ticket #4521.",
        "payloadJson": "{\"batteryId\":\"...\",\"ticketId\":\"...\"}",
        "entityType": "Ticket",
        "entityId": "4521aaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        "sentAt": "2026-06-13T05:34:10Z",
        "readAt": null,
        "createdAt": "2026-06-13T05:34:09Z"
      }
    ],
    "totalItems": 42,
    "pageNumber": 1,
    "pageSize": 20
  },
  "listErrors": []
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.items[].id` | `Guid` | Không | ID của notification |
| `data.items[].userId` | `Guid` | Không | ID của user nhận notification (luôn bằng user hiện tại) |
| `data.items[].type` | `NotificationTypeEnum` (int) | Không | Loại notification |
| `data.items[].channel` | `NotificationChannelEnum` (int) | Không | Kênh phát |
| `data.items[].status` | `NotificationStatusEnum` (int) | Không | Trạng thái lifecycle |
| `data.items[].title` | `string` | Không | Tiêu đề (đã render template) |
| `data.items[].body` | `string` | Không | Nội dung (đã render template) |
| `data.items[].payloadJson` | `string?` | Có | JSON chứa metadata bổ sung (FE deep-link, parse client-side) |
| `data.items[].entityType` | `string?` | Có | Loại entity liên quan (`Ticket`, `Battery`, `Account`,…) — dùng để deep-link |
| `data.items[].entityId` | `Guid?` | Có | ID entity liên quan |
| `data.items[].sentAt` | `DateTime?` | Có | Thời điểm gửi thành công (`null` khi `Status=Pending`/`Failed`) |
| `data.items[].readAt` | `DateTime?` | Có | Thời điểm user đọc (`null` nếu chưa đọc) |
| `data.items[].createdAt` | `DateTime` | Không | Thời điểm tạo record |
| `data.totalItems` | `int` | Không | Tổng số notification match filter |
| `data.pageNumber` | `int` | Không | Trang hiện tại |
| `data.pageSize` | `int` | Không | Kích thước trang |

**Lưu ý:**
- Mảng `items` có thể rỗng (`[]`) khi user chưa có notification nào hoặc filter quá hẹp — vẫn trả `200` chứ không phải `404`.
- Query đã filter sẵn `IsDeleted = false` (soft delete) và `UserId = current user`. User KHÔNG thể đọc notification của user khác.
- Enum được serialize ra **số nguyên** (giá trị int), không phải string. FE map ngược ra tên qua bảng [Enums](#enums).

**Lỗi thường gặp:**
- `400` — JWT đã pass `[Authorize]` nhưng claim `UserId` (hoặc `sub` / `NameIdentifier`) thiếu hoặc malformed → server không xác định được user. Đây là **client request error** (token shape sai), không phải auth fail. Body theo `CommonResponse<T>`:
  ```json
  {
    "isSuccess": false,
    "statusCode": 400,
    "message": "Không xác định được user."
  }
  ```
- `401` — Token thiếu, hết hạn hoặc chữ ký không hợp lệ (trả bởi JWT middleware, không phải `CommonResponse<T>`).

---

### `POST /api/notifications`

**Mục đích:** Tạo 1 notification record mới một cách thủ công. **Endpoint này phục vụ admin / smoke test / backfill / các integration event chưa có consumer riêng** — flow production cho 3 event đã wire-up (`AlertTicketSagaFailedEvent`, `BatteryAlertEscalationRequestedEvent`, `IotDeviceWentOfflineEvent`) chạy qua RabbitMQ Consumer chứ không gọi REST.

Sau khi tạo, notification ở trạng thái `Pending` (`Status = 1`); Dispatcher sẽ pick lên và chuyển sang `Sent`/`Failed` tùy kết quả gửi.

**Auth:** `[Authorize(Roles = "Admin")]` — chỉ role Admin.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `userId` | `Guid` | Bắt buộc | Khác `Guid.Empty` | ID của user sẽ nhận notification |
| `type` | `NotificationTypeEnum` (int) | Bắt buộc | Phải là giá trị hợp lệ trong `NotificationTypeEnum` | Loại notification |
| `channel` | `NotificationChannelEnum` (int) | Bắt buộc | Phải là giá trị hợp lệ trong `NotificationChannelEnum` | Kênh phát |
| `title` | `string` | Bắt buộc | Không rỗng, tối đa **200 ký tự** | Tiêu đề |
| `body` | `string` | Bắt buộc | Không rỗng, tối đa **2000 ký tự** | Nội dung |
| `payloadJson` | `string?` | Tùy chọn | — | JSON metadata bổ sung (FE deep-link) |
| `entityType` | `string?` | Tùy chọn | Tối đa **100 ký tự** nếu có | Loại entity liên quan (`Ticket`, `Battery`,…) |
| `entityId` | `Guid?` | Tùy chọn | — | ID entity liên quan |

**Ví dụ:**

```json
POST /api/notifications
Authorization: Bearer eyJ... (Admin)
Content-Type: application/json

{
  "userId": "1a2b3c4d-5e6f-7890-abcd-ef0123456789",
  "type": 99,
  "channel": 4,
  "title": "Bảo trì hệ thống",
  "body": "Hệ thống sẽ bảo trì lúc 22:00 hôm nay, dự kiến 30 phút.",
  "payloadJson": null,
  "entityType": null,
  "entityId": null
}
```

**Response thành công `201`:**

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Tạo notification thành công.",
  "data": "9f8e7d6c-5b4a-3210-fedc-ba9876543210",
  "listErrors": []
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data` | `Guid` | Không | ID của notification vừa tạo. Notification ở `Status = Pending (1)` — Dispatcher sẽ xử lý sau |
| `message` | `string` | Không | `"Tạo notification thành công."` |

**Lưu ý:**
- Handler không validate `userId` có thực sự tồn tại trong UserService — caller chịu trách nhiệm. Notification với `userId` không tồn tại vẫn được tạo nhưng sẽ không ai nhận được (FE filter theo user khi GET).
- `title` và `body` được `.Trim()` trước khi lưu; `entityType` cũng được `.Trim()` nếu có giá trị.
- Endpoint **không** kiểm tra preference của user (Frequency / channel opt-out) — vì đây là endpoint admin/test bypass logic Dispatcher.

**Lỗi thường gặp:**
- `400` — Validation fail. Body theo `CommonResponse<T>` với `listErrors`. Các trường hợp:
  - `UserId` rỗng (`Guid.Empty`) → `"UserId không được rỗng."`
  - `Type` không nằm trong enum → `"Type không hợp lệ."`
  - `Channel` không nằm trong enum → `"Channel không hợp lệ."`
  - `Title` rỗng hoặc > 200 ký tự → `"Title không được trống."` / `"Title tối đa 200 ký tự."`
  - `Body` rỗng hoặc > 2000 ký tự → `"Body không được trống."` / `"Body tối đa 2000 ký tự."`
  - `EntityType` > 100 ký tự → `"EntityType tối đa 100 ký tự."`

  Ví dụ body lỗi:
  ```json
  {
    "isSuccess": false,
    "statusCode": 400,
    "message": "Dữ liệu đầu vào không hợp lệ.",
    "listErrors": [
      { "field": "Title", "detail": "Title không được trống." },
      { "field": "Body", "detail": "Body tối đa 2000 ký tự." }
    ]
  }
  ```
- `401` — Chưa đăng nhập / token không hợp lệ (middleware).
- `403` — Token hợp lệ nhưng không phải role `Admin`.

---

## Tham khảo

- Controller: `services/NotificationService/src/NotificationService.Api/Controllers/NotificationsController.cs`
- Query handler: `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Notification/GetNotificationsQueryHandler.cs`
- Command handler: `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Notification/CreateNotificationCommandHandler.cs`
- DTO: `services/NotificationService/src/NotificationService.Application/DTOs/Response/Notification/NotificationDto.cs`
- Domain enums: `services/NotificationService/src/NotificationService.Domain/Enums/`
- Flow event-driven (RabbitMQ Consumer) — xem `overall.md §3.3` và các consumer trong `NotificationService.Application/Consumers/`.
