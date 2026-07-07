# API Documentation — NotificationService

> Base URL: `http://localhost:{port}/api`
> Content-Type mặc định: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem phần [Cấu trúc Response chung](#cấu-trúc-response-chung)

NotificationService quản lý notification gửi tới user theo nhiều kênh (Push/Email/SMS/InApp). Flow production chính tạo notification qua **RabbitMQ Consumer** trong `NotificationService.Application/Consumers/`. Tại thời điểm hiện tại có các consumer đã wire-up:

- `AlertTicketSagaFailedConsumer` ← `AlertTicketSagaFailedEvent` (Sprint 5B #238)
- `BatteryAlertEscalationRequestedConsumer` ← `BatteryAlertEscalationRequestedEvent` (Sprint 5B #238)
- `IotDeviceWentOfflineConsumer` ← `IotDeviceWentOfflineEvent` (Sprint IoT-1 #249)
- `EnvironmentalIncidentDetectedConsumer` ← `EnvironmentalIncidentDetectedEvent` (Sprint IoT-2 #IoT2-31) — Push + Email + SMS, `BypassQuietHours = true`
- `EnvironmentalIncidentResolvedConsumer` ← `EnvironmentalIncidentResolvedEvent` (Sprint IoT-2 #IoT2-31) — chỉ InApp (clear banner)

> Lưu ý: `EnvironmentalIncidentDetectedConsumer` và `EnvironmentalIncidentResolvedConsumer` được khai báo trong cùng file `EnvironmentalIncidentDetectedConsumer.cs`.

Các loại notification khác trong `NotificationTypeEnum` (TicketCreated, SlaWarning, BatteryAnomalyDetected, …) hiện được tạo qua REST API `POST /api/notifications` (admin/internal tooling) hoặc sẽ mở rộng consumer sau.

REST API hiện tại phơi 3 nhóm endpoint:

- **`/api/notifications`** — Người dùng cuối xem danh sách notification của chính mình; Admin / công cụ test tạo notification thủ công (backfill, smoke test).
- **`/api/device-tokens`** — Người dùng cuối đăng ký / hủy / liệt kê push token thiết bị (Expo/FCM) cho Mobile/Web.
- **`/api/notification-preferences`** — Người dùng cuối xem / cập nhật cài đặt kênh gửi (push/email/sms/in-app) và quiet hours của chính mình.

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": null
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
  "pageSize": 10,
  "totalPages": 5,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

| Field | Type | Mô tả |
|---|---|---|
| `items` | `T[]` | Danh sách bản ghi của trang hiện tại |
| `totalItems` | `int` | Tổng số bản ghi match filter (không phụ thuộc page) |
| `pageNumber` | `int` | Trang hiện tại (1-based) |
| `pageSize` | `int` | Số bản ghi tối đa mỗi trang |
| `totalPages` | `int` | Tổng số trang — `ceil(totalItems / pageSize)` (computed) |
| `hasNextPage` | `bool` | `true` nếu còn trang sau (`pageNumber < totalPages`) (computed) |
| `hasPreviousPage` | `bool` | `true` nếu có trang trước (`pageNumber > 1`) (computed) |

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

Tần suất gửi notification — đặt ở `NotificationPreference` per-user (default `Immediate`). Hiện **chưa expose** qua endpoint `/api/notification-preferences` (request/response của PUT/GET không có field `frequency`) — chỉ tồn tại ở tầng entity để hỗ trợ digest sau (§49 — Notification advanced).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Immediate` | 1 | Gửi ngay khi có sự kiện |
| `Daily` | 2 | Gom thành digest gửi 1 lần/ngày |

### `DevicePlatformEnum`

Platform của device token — dùng trong request/response của các endpoint `/api/device-tokens` (xem [Endpoints — Device Tokens](#endpoints--device-tokens)).

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
| `pageNumber` | `int` | Không | `1` | Số trang (1-based). Giá trị `≤ 0` tự về `1`. |
| `pageSize` | `int` | Không | `10` | Số bản ghi mỗi trang. Giá trị `≤ 0` tự về `10`; **tối đa `100`** — gửi lớn hơn sẽ bị clamp xuống `100`. |
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
  "listErrors": null
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
- Entity `Notification` còn có cột `FailureReason` (lý do khi `Status = Failed`) **chỉ lưu nội bộ DB — KHÔNG expose** qua DTO/API.

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
| `bypassQuietHours` | `bool` | Tùy chọn | — | Sprint IoT-2 #IoT2-31 — bypass quiet hours khi gửi (chỉ dùng cho Critical channel, vd Environmental Incident). Khi `true`, handler merge key `bypassQuietHours: true` vào `payloadJson`; Dispatcher (Sprint 6+) đọc flag này để SKIP `NotificationPreference.QuietHoursStart/End`. Mặc định `false`. |

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
  "listErrors": null
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
- Khi `bypassQuietHours = true`: handler **merge** key `bypassQuietHours: true` vào `payloadJson`. Nếu `payloadJson` là JSON object hợp lệ → thêm key vào object đó; nếu rỗng → tạo `{"bypassQuietHours":true}`; nếu không parse được thành object → wrap thành `{"bypassQuietHours":true,"original":"<payload cũ>"}`. Flag **không** được lưu thành cột riêng — chỉ tồn tại trong `payloadJson`.

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

## Endpoints — Device Tokens

Base route: `/api/device-tokens`

Quản lý push token thiết bị (Expo/FCM) cho Mobile/Web app. **Mọi endpoint đều `[Authorize]`** — `UserId` luôn lấy từ JWT claim, user chỉ thao tác trên token của chính mình (không nhận `userId` từ body).

---

### `POST /api/device-tokens`

**Mục đích:** Đăng ký push token của thiết bị hiện tại cho user đang đăng nhập.

**Auth:** `[Authorize]` — mọi user đã đăng nhập.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `token` | `string` | Bắt buộc | Không rỗng, tối đa **500 ký tự** | Push token (Expo/FCM) |
| `platform` | `DevicePlatformEnum` (int) | Bắt buộc | Phải hợp lệ (1=Ios, 2=Android, 3=Web) | Platform thiết bị |
| `deviceInfo` | `string?` | Tùy chọn | Tối đa **500 ký tự** nếu có | Mô tả thiết bị (model, OS,…) để user nhận diện |

> `userId` **không** nhận từ body — server set từ JWT claim.

**Hành vi (theo token string — unique global ở DB):**
- Token mới → tạo record, trả **`201`** `"Đăng ký thiết bị thành công."`
- Token đã đăng ký và **đang active cho chính user hiện tại** → **`409`** `"Thiết bị đã được đăng ký."` (`data` = Id record đang tồn tại)
- Token đã từng hủy (inactive) **hoặc** thuộc user khác trên cùng thiết bị → reactivate + gán lại cho user hiện tại, trả **`200`** `"Đăng ký lại thiết bị thành công."` (flow re-login / đổi tài khoản)

**Response (`201` / `200` / `409`):** `CommonResponse<Guid>` — `data` = Id của device token.

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Đăng ký thiết bị thành công.",
  "data": "3c2b1a09-8d7e-6f5a-4b3c-2d1e0f9a8b7c",
  "listErrors": null
}
```

**Lỗi thường gặp:**
- `400` — Validation fail (`Token` rỗng/`> 500 ký tự` → `"Token không được trống."` / `"Token tối đa 500 ký tự."`; `Platform` không hợp lệ → `"Platform không hợp lệ."`; `DeviceInfo > 500 ký tự` → `"DeviceInfo tối đa 500 ký tự."`) **hoặc** thiếu claim `UserId` → `"Không xác định được user."`
- `401` — Chưa đăng nhập / token hết hạn.
- `409` — Thiết bị đã được đăng ký và đang active.

---

### `DELETE /api/device-tokens`

**Mục đích:** Hủy đăng ký push token của thiết bị (logout). Định danh theo `token` string; chỉ hủy được token đang active thuộc về chính mình (set `IsActive = false`, **giữ record** để re-login dễ).

**Auth:** `[Authorize]` — mọi user đã đăng nhập.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `token` | `string` | Bắt buộc | Không rỗng, tối đa **500 ký tự** | Push token cần hủy |

**Response `200`:** `CommonResponse<Guid>` — `data` = Id của device token vừa hủy.

**Lỗi thường gặp:**
- `400` — Validation lỗi hoặc thiếu claim `UserId`.
- `401` — Chưa đăng nhập / token hết hạn.
- `404` — Không tìm thấy token đang đăng ký (active) của user hiện tại → `"Không tìm thấy thiết bị đang đăng ký."`

---

### `GET /api/device-tokens`

**Mục đích:** Liệt kê các thiết bị đã đăng ký của user hiện tại. **Không trả raw token string** — chỉ metadata để user nhận diện thiết bị. Sắp xếp theo lần dùng gần nhất.

**Auth:** `[Authorize]` — mọi user đã đăng nhập.

**Response `200`:** `CommonResponse<DeviceTokenDto[]>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": [
    {
      "id": "3c2b1a09-8d7e-6f5a-4b3c-2d1e0f9a8b7c",
      "platform": 2,
      "deviceInfo": "Pixel 8 — Android 15",
      "isActive": true,
      "lastUsedAt": "2026-06-13T05:34:10Z",
      "createdAt": "2026-06-01T08:00:00Z"
    }
  ],
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data[].id` | `Guid` | Không | ID của device token record |
| `data[].platform` | `DevicePlatformEnum` (int) | Không | Platform (1=Ios, 2=Android, 3=Web) |
| `data[].deviceInfo` | `string?` | Có | Mô tả thiết bị |
| `data[].isActive` | `bool` | Không | `true` nếu token còn active |
| `data[].lastUsedAt` | `DateTime?` | Có | Lần dùng gần nhất |
| `data[].createdAt` | `DateTime` | Không | Thời điểm đăng ký lần đầu |

**Lỗi thường gặp:**
- `400` — Thiếu claim `UserId` → `"Không xác định được user."`
- `401` — Chưa đăng nhập / token hết hạn.

---

## Endpoints — Notification Preferences

Base route: `/api/notification-preferences`

Quản lý cài đặt thông báo per-user: bật/tắt từng kênh (Push/Email/SMS/InApp) và quiet hours. **Mọi endpoint đều `[Authorize]`** — `UserId` luôn lấy từ JWT claim, user chỉ thao tác trên preference của chính mình (không nhận `userId` từ body). Mỗi user có **1 record duy nhất** (1-1 với Account).

> Dispatcher (Sprint 6+) đọc preference này để quyết định gửi qua kênh nào và có tôn trọng quiet hours không (trừ notification có flag `bypassQuietHours` — xem `POST /api/notifications`).

---

### `GET /api/notification-preferences`

**Mục đích:** Lấy cài đặt thông báo của **user hiện tại**. Nếu user **chưa từng cấu hình**, trả về **giá trị mặc định** (KHÔNG ghi DB — không tạo record).

**Auth:** `[Authorize]` — mọi user đã đăng nhập.

**Response `200`:** `CommonResponse<NotificationPreferenceDto>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "pushEnabled": true,
    "emailEnabled": true,
    "smsEnabled": false,
    "inAppEnabled": true,
    "quietHoursStart": "22:00",
    "quietHoursEnd": "07:00",
    "timeZone": "Asia/Ho_Chi_Minh"
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.pushEnabled` | `bool` | Không | Bật push notification |
| `data.emailEnabled` | `bool` | Không | Bật email |
| `data.smsEnabled` | `bool` | Không | Bật SMS |
| `data.inAppEnabled` | `bool` | Không | Bật in-app |
| `data.quietHoursStart` | `string?` | Có | Giờ bắt đầu quiet hours dạng `"HH:mm"`; `null` = không quiet hours |
| `data.quietHoursEnd` | `string?` | Có | Giờ kết thúc quiet hours dạng `"HH:mm"`; `null` = không quiet hours |
| `data.timeZone` | `string` | Không | Timezone IANA (vd `"Asia/Ho_Chi_Minh"`) |

**Giá trị mặc định (user chưa cấu hình):**

```json
{
  "pushEnabled": true,
  "emailEnabled": true,
  "smsEnabled": false,
  "inAppEnabled": true,
  "quietHoursStart": null,
  "quietHoursEnd": null,
  "timeZone": "Asia/Ho_Chi_Minh"
}
```

**Lỗi thường gặp:**
- `400` — Thiếu claim `UserId` → `"Không xác định được UserId từ token."` (body theo `CommonResponse<object>`).
- `401` — Chưa đăng nhập / token hết hạn.

---

### `PUT /api/notification-preferences`

**Mục đích:** Cập nhật (upsert) cài đặt thông báo của user hiện tại. Nếu chưa có record → **tạo mới**; nếu đã có → **ghi đè**.

**Auth:** `[Authorize]` — mọi user đã đăng nhập.

**Request body:**

| Field | Type | Bắt buộc | Default | Validation | Mô tả |
|---|---|---|---|---|---|
| `pushEnabled` | `bool` | Không | `true` | — | Bật push |
| `emailEnabled` | `bool` | Không | `true` | — | Bật email |
| `smsEnabled` | `bool` | Không | `false` | — | Bật SMS |
| `inAppEnabled` | `bool` | Không | `true` | — | Bật in-app |
| `quietHoursStart` | `string?` | Tùy chọn | `null` | Phải đúng định dạng `"HH:mm"` nếu có | Giờ bắt đầu quiet hours; `null` = xóa quiet hours |
| `quietHoursEnd` | `string?` | Tùy chọn | `null` | Phải đúng định dạng `"HH:mm"` nếu có | Giờ kết thúc quiet hours; `null` = xóa quiet hours |
| `timeZone` | `string` | Không | `"Asia/Ho_Chi_Minh"` | Không rỗng, tối đa **100 ký tự** | Timezone IANA |

> `userId` **không** nhận từ body — server set từ JWT claim.
> Quiet hours qua đêm (vd `22:00`–`07:00`) được hỗ trợ. Critical notifications (EnvironmentalIncident, SlaBreached, IncidentDeclared,…) vẫn được gửi bất kể quiet hours.

**Ví dụ:**

```json
PUT /api/notification-preferences
Authorization: Bearer eyJ...
Content-Type: application/json

{
  "pushEnabled": true,
  "emailEnabled": false,
  "smsEnabled": false,
  "inAppEnabled": true,
  "quietHoursStart": "22:00",
  "quietHoursEnd": "07:00",
  "timeZone": "Asia/Ho_Chi_Minh"
}
```

**Response thành công `200`:** `CommonResponse<NotificationPreferenceDto>` — trả về preference sau khi cập nhật (shape giống `GET`).

**Lưu ý:**
- Sau khi commit, handler **xóa cache Redis** key `notif_pref:{userId}` để Dispatcher đọc preference mới ở lần dispatch kế tiếp.
- Entity `NotificationPreference` còn có field `Frequency` (`NotificationFrequencyEnum`, default `Immediate`) **không** nhận từ body và **không** trả ra DTO — hiện chưa expose.

**Lỗi thường gặp:**
- `400` — Validation fail. Body theo `CommonResponse<NotificationPreferenceDto>` với `listErrors`. Các trường hợp:
  - `QuietHoursStart` / `QuietHoursEnd` sai định dạng → `"Định dạng phải là HH:mm."`
  - `TimeZone` rỗng → `"TimeZone không được trống."` / `> 100 ký tự` → `"TimeZone tối đa 100 ký tự."`
  - Thiếu claim `UserId` → `"Không xác định được UserId từ token."`
- `401` — Chưa đăng nhập / token hết hạn.

---

## Tham khảo

**Notifications:**
- Controller: `services/NotificationService/src/NotificationService.Api/Controllers/NotificationsController.cs`
- Query handler: `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Notification/GetNotificationsQueryHandler.cs`
- Command handler: `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Notification/CreateNotificationCommandHandler.cs`
- DTO: `services/NotificationService/src/NotificationService.Application/DTOs/Response/Notification/NotificationDto.cs`

**Device Tokens:**
- Controller: `services/NotificationService/src/NotificationService.Api/Controllers/DeviceTokensController.cs`
- Handlers: `services/NotificationService/src/NotificationService.Application/CQRS/Handler/DeviceToken/`
- DTO: `services/NotificationService/src/NotificationService.Application/DTOs/Response/DeviceToken/DeviceTokenDto.cs`
- Entity: `services/NotificationService/src/NotificationService.Domain/Entities/DeviceToken.cs`

**Notification Preferences:**
- Controller: `services/NotificationService/src/NotificationService.Api/Controllers/PreferencesController.cs`
- Handlers: `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Preference/`
- DTO: `services/NotificationService/src/NotificationService.Application/DTOs/Response/Preference/NotificationPreferenceDto.cs`
- Entity: `services/NotificationService/src/NotificationService.Domain/Entities/NotificationPreference.cs`

**Chung:**
- Domain enums: `services/NotificationService/src/NotificationService.Domain/Enums/`
- Pagination wrapper (computed fields `totalPages`/`hasNextPage`/`hasPreviousPage`): `shared/src/SharedContracts/Common/Responses/PaginationResponse.cs`
- Pagination request (clamp `pageSize` max 100): `shared/src/SharedContracts/Common/Requests/PaginationRequest.cs`
- Flow event-driven (RabbitMQ Consumer) — xem `overall.md §3.3` và các consumer trong `NotificationService.Application/Consumers/` (`AlertTicketSagaFailedConsumer`, `BatteryAlertEscalationRequestedConsumer`, `IotDeviceWentOfflineConsumer`, `EnvironmentalIncidentDetectedConsumer`, `EnvironmentalIncidentResolvedConsumer`).
