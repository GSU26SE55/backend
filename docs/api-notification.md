# API Documentation — NotificationService

> Base URL: `http://localhost:{port}/api`
> Content-Type mặc định: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem phần [Cấu trúc Response chung](#cấu-trúc-response-chung)
>
> **Cập nhật 30/07/2026 — Sprint 6.2 (`#672..#688`) + Sprint 6.3 (`#701..#717`).**
> Đây là hai sprint làm thay đổi lớn nhất từ trước tới nay của service: Sprint 6.2 đưa pipeline từ
> *"ghi DB rồi nằm im"* lên *"thật sự gửi được"*, Sprint 6.3 đưa tiếp lên *"vận hành được"*
> (đo được, chặn spam, biết thư có tới không, feed hết nhân bản).
> Toàn bộ điểm khác so với bản doc trước: xem [Changelog](#changelog).

---

## Mục lục

1. [Kiến trúc pipeline](#kiến-trúc-pipeline) — luồng từ event → record → giao nhận
2. [Cấu trúc Response chung](#cấu-trúc-response-chung)
3. [Enums](#enums) — 8 enum + bảng ánh xạ type → nhóm
3b. [Routing qua ApiGateway](#routing-qua-apigateway) — 5 route mới
4. [Endpoints — Notifications](#endpoints--notifications) (`/api/notifications`)
5. [Endpoints — Device Tokens](#endpoints--device-tokens) (`/api/device-tokens`)
6. [Endpoints — Notification Preferences](#endpoints--notification-preferences) (`/api/notification-preferences`)
7. [Endpoints — Admin Notification Templates](#endpoints--admin-notification-templates) (`/api/admin/notification-templates`)
8. [Endpoints — Unsubscribe một chạm](#endpoints--unsubscribe-một-chạm) (`/api/notification-unsubscribe`)
9. [Realtime — SignalR Hub `/hubs/notifications`](#realtime--signalr-hub-hubsnotifications)
10. [Tầng dispatch — thứ tự cổng chặn](#tầng-dispatch--thứ-tự-cổng-chặn)
11. [Background services](#background-services) — 7 worker
12. [Integration events](#integration-events) — publish / consume
13. [Cấu hình (appsettings / biến môi trường)](#cấu-hình-appsettings--biến-môi-trường)
14. [Database schema](#database-schema) — bảng + migration Sprint 6.2/6.3
15. [Prometheus metrics](#prometheus-metrics)
16. [Giới hạn đã biết](#giới-hạn-đã-biết)
17. [Changelog](#changelog)

---

## Kiến trúc pipeline

```
                    ┌──────────────────────────────────────────────┐
   RabbitMQ event   │ 31 Consumer  (NotificationService.Application/Consumers)
  ────────────────► │   → ghi 1 record Notification / (người nhận × channel)
                    │   → Status = Pending                          │
                    └──────────────────┬───────────────────────────┘
                                       │ (DB: notifications)
                    ┌──────────────────▼───────────────────────────┐
                    │ NotificationDispatchBackgroundService (NOTI-01)
                    │   quét Pending đến hạn, mỗi 5s, lô 100        │
                    │   leader-election Redis → 1 instance xử lý    │
                    └──────────────────┬───────────────────────────┘
                                       │ DispatchPendingAsync
                    ┌──────────────────▼───────────────────────────┐
                    │ NotificationDispatcher — 6 cổng chặn          │
                    │  preference kênh → preference NHÓM → digest   │
                    │  → rate limit → quiet hours → địa chỉ nhận    │
                    └───┬─────────┬─────────┬──────────┬───────────┘
                        │         │         │          │
                     InApp     Expo Push  Email      SMS
                   (SignalR)   (batch)   (bus →     (bus →
                                          EmailSvc)  SmsSvc)
                        │         │
                        │         └─► push_receipts → ExpoReceiptReconcile
                        │                              → Delivered / token chết
                        └─► NotificationHub → client cập nhật feed + badge ngay
```

**Ba điều quan trọng phải nắm trước khi đọc phần endpoint:**

1. **1 sự kiện nghiệp vụ = N record `Notification`**, mỗi kênh một record. Record kênh
   `Push`/`Email`/`Sms` là **bản ghi giao nhận**, KHÔNG phải mục hiển thị. Chỉ record `InApp` mới là
   dòng trong feed người dùng nhìn thấy — đây là lý do `GET /api/notifications` và
   `GET /api/notifications/unread-count` **mặc định lọc `Channel = InApp`** từ Sprint 6.3 (NOTI3-01).
2. **Consumer chỉ ghi "ý định gửi"**, không quyết định có gửi hay không. Việc tôn trọng tuỳ chọn
   người dùng, quiet hours, hạn mức, digest đều nằm ở `NotificationDispatcher` — nên một consumer
   ghi đủ 4 kênh không có nghĩa người dùng sẽ nhận đủ 4 kênh.
3. **`Status = Sent` KHÔNG có nghĩa "đã tới thiết bị"** với kênh Push — nó chỉ nghĩa là Expo đã nhận
   request. Bằng chứng giao hàng thật là `Delivered` (Sprint 6.3 NOTI3-02/NOTI3-14).

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
- `429` — Vượt hạn mức (chỉ endpoint `test-send`)
- `500` — Lỗi server ngoài dự kiến

> **Enum serialize:** NotificationService trả enum dạng **số nguyên** trong DTO nghiệp vụ
> (`type`, `channel`, `status`, `platform`, `category`). Riêng 3 endpoint mới của Sprint 6.3 —
> `GET /api/admin/notification-templates`, `GET /api/notification-preferences/categories`,
> và payload SignalR `NotificationCreated` — trả **chuỗi tên enum** (`"TicketCreated"`, `"Push"`)
> vì chúng phục vụ màn hình quản trị/hiển thị, đọc bằng mắt quan trọng hơn.
> `NotificationCategoryPreferenceDto` trả **cả hai** (`category` số + `categoryName` chuỗi).

---

## Enums

### `NotificationTypeEnum`

Phân loại notification — quyết định template, ma trận kênh (`TypeChannelMatrix`), nhóm tuỳ chọn
(`NotificationCategoryEnum`), và có thuộc diện critical (bypass quiet hours + hạn mức + giữ vĩnh
viễn khi retention dọn) hay không.

**34 giá trị** (1–33 liên tục + `System = 99`). Số hiệu **không liên tục** — `99` reserved cho `System`.

| Giá trị | Int | Nhóm (category) | Critical? | Ý nghĩa |
|---|---|---|---|---|
| `TicketCreated` | 1 | Ticket | | Ticket mới được tạo → notify Manager |
| `TicketAssigned` | 2 | Ticket | | Ticket được assign/reassign Staff → notify **Staff + Customer** (Customer thêm ở Sprint 6.2) |
| `TicketStatusChanged` | 3 | Ticket | | Ticket đổi trạng thái trong state machine → notify Customer. **Sprint 6.2 NOTI-07: từ enum chết (0 producer) thành có producer thật** |
| `TicketResolved` | 4 | Ticket | | Staff báo xử lý xong → notify Manager + **Customer** (Customer thêm ở Sprint 6.2) |
| `TicketClosed` | 5 | Ticket | | Ticket đóng hẳn (Customer đánh giá xong hoặc auto-close). **Sprint 6.2 NOTI-07: từ enum chết thành có producer thật** |
| `TicketEscalated` | 6 | **SLA** | | Ticket bị escalate. Xếp nhóm SLA (không phải Ticket) vì leo thang luôn là hệ quả của rủi ro vỡ cam kết — người tắt "cập nhật ticket" vẫn phải nhận được |
| `SlaWarning` | 7 | SLA | | Cảnh báo sắp breach SLA → Manager **+ Staff phụ trách** (Staff thêm ở Sprint 6.2 NOTI-05) |
| `SlaBreached` | 8 | SLA | ✅ | SLA đã breach. **Sprint 6.2 NOTI-06: phân nhánh theo priority** — xem bảng bên dưới |
| `BatteryAnomalyDetected` | 9 | Battery | | Bất thường pin mức **Critical** → Customer. Sprint 6.2 NOTI-08: mở đủ 4 kênh (trước chỉ InApp+Push) |
| `EnvironmentalIncidentDetected` | 10 | Environmental | ✅ | Sự cố môi trường tại site (nhiệt độ/độ ẩm/ngập) |
| `EnvironmentalIncidentResolved` | 11 | Environmental | | Sự cố môi trường đã giải quyết (clear banner) |
| `AccountActivated` | 12 | Account | | Account được kích hoạt thành công |
| `AdminInvite` | 13 | Account | | Admin gửi lời mời tạo account cho Staff/Manager |
| `IncidentDeclared` | 14 | SLA | ✅ | Major Incident chính thức được declare |
| `CascadeRiskHigh` | 15 | Battery | | Sprint Bonus NS-14 (#658) — cascade risk ≥ 0.7 trên 1 pin → Manager/Admin |
| `BatteryAlertEscalationPending` | 16 | Battery | ✅ | Sprint 5B #238 — Critical Alert chưa ack > 5 phút |
| `AlertTicketSagaFailed` | 17 | SLA | ✅ | Sprint 5B #238 — Alert–Ticket Saga vào terminal state `Failed`, admin cần reprocess |
| `IotDeviceWentOffline` | 18 | Battery | | Sprint IoT-1 (#249) — IoT edge device mất heartbeat > 5 phút |
| `ChatCreated` | 19 | Chat | | Chat public mới trên ticket |
| `ChatMentioned` | 20 | Chat | | User được @mention trong chat |
| `ChatReacted` | 21 | Chat | | Chat của user nhận được reaction |
| `ParticipantAdded` | 22 | Chat | | User được thêm làm participant ticket |
| `ParticipantRemoved` | 23 | Chat | | User bị xoá khỏi participant ticket |
| `ParticipantRoleChanged` | 24 | Chat | | Role/type của participant trên ticket bị đổi |
| `BlogGenerationCompleted` | 25 | Account ⚠️ | | **GH-671 (module Blog)** — AI sinh blog xong (`status=Draft`). ⚠️ **Chưa khai báo trong `NotificationCategoryMap`** → đang fallback nhóm `Account` |
| `BlogGenerationFailed` | 26 | Account ⚠️ | | **GH-671 (module Blog)** — AI sinh blog thất bại. ⚠️ **Chưa khai báo trong `NotificationCategoryMap`** → đang fallback nhóm `Account` |
| `ChatEscalatedToAdmin` | 27 | **SLA** | ✅ | **Sprint 6.2 NOTI-03 (#674)** — saga `ChatEscalationReview` timeout 30' (Manager không ACK) → escalate lên Admin |
| `TicketApproved` | 28 | Ticket | | **Sprint 6.2 NOTI-07 (#678)** — Manager duyệt kết quả, ticket vào `CLOSED_PENDING_RATE` chờ Customer đánh giá |
| `TicketRejected` | 29 | Ticket | | **Sprint 6.2 NOTI-07** — kết quả resolve bị trả lại (→ Staff), HOẶC ticket đóng do ngoài scope (→ Customer) |
| `TicketReopened` | 30 | Ticket | | **Sprint 6.2 NOTI-07** — Customer mở lại ticket → Manager + Staff đang assign |
| `TicketRatingRequested` | 31 | Ticket | | **Sprint 6.2 NOTI-07** — nhắc Customer đánh giá ticket đang treo |
| `BatteryAnomalyWarning` | 32 | Battery | | **Sprint 6.2 NOTI-08 (#679)** — bất thường pin mức **Warning** (spec §3.4 T#12) → InApp + Push |
| `BatteryAnomalyInfo` | 33 | Battery | | **Sprint 6.2 NOTI-08** — bất thường pin mức **Info** (spec §3.4 T#11) → chỉ InApp |
| `System` | 99 | Account | | Notification hệ thống tổng quát (broadcast, maintenance) + **bản tin digest tổng hợp** |

> ⚠️ **FE/Mobile phải mirror đủ 34 giá trị** (1–33 + `System = 99`).
>
> **Sửa 2026-07-31 — số hiệu ĐÃ ĐỔI:** 7 giá trị của Sprint 6.2 là **27–33**, KHÔNG phải 25–31 như
> bản trước ghi. Module Blog (`GH-671`) chiếm mất **25** và **26**, đẩy toàn bộ nhóm Sprint 6.2 lên
> 2 bậc. FE/Mobile nào đã mirror theo bản cũ **phải sửa lại**, nếu không sẽ hiển thị sai loại
> thông báo (ví dụ coi `ChatEscalatedToAdmin` thành `BlogGenerationCompleted`).
>
> **Không có type cho cảnh báo bảo mật** (đăng nhập lạ / refresh-token reuse). Đây là **cố ý**:
> hai luồng đó đi thẳng `AuthService → EmailService` như OTP, không qua NotificationService, nên
> không thêm enum không producer. Xem [Integration events](#integration-events).

**Critical types (mặc định)** — bypass quiet hours, bypass hạn mức, giữ vĩnh viễn khi retention dọn,
và được fallback push→SMS bảo vệ:
`EnvironmentalIncidentDetected` · `IncidentDeclared` · `BatteryAlertEscalationPending` ·
`AlertTicketSagaFailed` · `SlaBreached` · `ChatEscalatedToAdmin`.
Ghi đè bằng `Notification:Dispatch:CriticalTypes` (khai báo là **thay thế hoàn toàn**, không cộng dồn).

---

### `NotificationStatusEnum`

Trạng thái lifecycle của **một record giao nhận**. Sprint 6.3 NOTI3-14 thêm `Delivered` và `Opened`.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Đã tạo bản ghi, chưa giao xuống channel. Worker sẽ pick lên. **Không bao giờ bị retention dọn** |
| `Sent` | 2 | Đã **bàn giao cho channel** (Expo/Mailjet/SMS gateway). ⚠️ CHƯA chắc thiết bị nhận được |
| `Failed` | 3 | Thất bại vĩnh viễn — hết `MaxAttempts` hoặc lỗi không thể phục hồi (`FailureReason` ghi lý do) |
| `Read` | 4 | User đã mark read trên feed in-app |
| `Delivered` | 5 | **Sprint 6.3** — provider **xác nhận** đã đẩy tới thiết bị. Với push: Expo receipt trả `status:"ok"`. Đây mới là bằng chứng giao hàng thật |
| `Opened` | 6 | **Sprint 6.3** — user đã **mở** notification (bấm push / deep link). Mạnh hơn `Read` (Read có thể chỉ do lướt feed) |

**Chuyển trạng thái hợp lệ:**

```
Pending ──dispatch ok──► Sent ──receipt ok──► Delivered ──user bấm──► Opened
   │                       │                      │
   │                       └──user mark read──────┴──────────────────► Read
   │
   ├──hết MaxAttempts / lỗi vĩnh viễn──► Failed
   └──hoãn (quiet hours/digest/rate limit)──► vẫn Pending, đặt NextAttemptAt
```

**Quy tắc không hạ cấp (quan trọng cho FE):**
- Receipt Expo về sau khi user đã đọc → **giữ nguyên** `Read`/`Opened`, không hạ về `Delivered`
  (đọc được nghĩa là chắc chắn đã nhận).
- `PATCH {id}/read` trên record đang `Opened` → **giữ `Opened`**, trả 200 (hạ xuống `Read` là mất thông tin).
- `SmsFailedEvent` về sau khi user đã `Read`/`Opened` → **không** hạ xuống `Failed`.
- "Chưa đọc" = `Status != Read && Status != Opened` (dùng cho `unreadOnly` và badge).

---

### `NotificationChannelEnum`

| Giá trị | Int | Ý nghĩa | Vai trò trong feed |
|---|---|---|---|
| `Push` | 1 | Push qua Expo (batch tối đa 100 message/request, trần payload 4096 byte) | Bản ghi giao nhận — **không** hiện trong feed |
| `Email` | 2 | Email — publish `SendNotificationEmailEvent` → EmailService gửi qua Mailjet | Bản ghi giao nhận |
| `Sms` | 3 | SMS — publish `SendSmsCommand` → SmsService → gateway Android | Bản ghi giao nhận |
| `InApp` | 4 | Mục hiển thị trong app | ✅ **Đây là feed**. `GET /api/notifications` mặc định chỉ trả kênh này |

---

### `NotificationCategoryEnum` *(MỚI — Sprint 6.3 NOTI3-04 / #704)*

Nhóm nghiệp vụ của notification. **Vì sao cần:** trước sprint này người dùng chỉ bật/tắt được *cả
kênh* — tắt Email là mất luôn email SLA sắp vỡ. Không có nhóm thì người dùng buộc phải chọn giữa bị
làm phiền và bỏ lỡ việc quan trọng, và họ luôn chọn tắt hết.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Ticket` | 1 | Vòng đời ticket: tạo / gán / đổi trạng thái / duyệt / từ chối / mở lại / đánh giá |
| `Sla` | 2 | SLA và leo thang: cảnh báo sắp vỡ, đã vỡ, escalate, sự cố nghiêm trọng, saga failed, chat escalate lên Admin |
| `Battery` | 3 | Sức khoẻ pin và thiết bị IoT: bất thường, rủi ro lan truyền, mất heartbeat |
| `Environmental` | 4 | Sự cố môi trường tại site (nhiệt độ / độ ẩm / ngập) |
| `Chat` | 5 | Trao đổi trên ticket: chat, mention, reaction, thay đổi người tham gia |
| `Account` | 6 | Tài khoản và hệ thống: kích hoạt, mời admin, thông báo hệ thống |

**Bảng ánh xạ `NotificationTypeEnum` → nhóm** nằm ở `NotificationCategoryMap` (Domain layer, có test
bao mọi giá trị enum). FE **không nên nhân bản** bảng này — gọi
[`GET /api/notification-preferences/categories`](#get-apinotification-preferencescategories) để lấy.
Type chưa khai báo rơi về nhóm `Account` (không ném lỗi làm chết đường gửi).

---

### `NotificationFrequencyEnum`

Tần suất gửi, đặt ở `NotificationPreference` per-user.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Immediate` | 1 | Gửi ngay khi có sự kiện (mặc định) |
| `Daily` | 2 | Gom thành digest gửi 1 lần/ngày (cửa sổ 24 giờ) |

> **Sprint 6.2 NOTI-12 (#683) — field này nay ĐÃ CÓ TÁC DỤNG THẬT.** Trước đó `Frequency` và
> `DigestWindowMinutes` tồn tại trong entity + nhận được qua `PUT /api/notification-preferences`
> nhưng **không dòng code nào đọc chúng**. Nay `NotificationDispatcher` + `NotificationDigestBackgroundService`
> thực thi digest. Vẫn **chưa expose `frequency` qua REST** — FE điều khiển digest bằng
> `digestWindowMinutes` (xem [PUT preferences](#put-apinotification-preferences)).

---

### `DevicePlatformEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Ios` | 1 | iOS device |
| `Android` | 2 | Android device |
| `Web` | 3 | Web push (browser) |

---

### `PushReceiptStatusEnum` *(MỚI — Sprint 6.3 NOTI3-02 / #702)*

Kết quả đối soát biên nhận Expo. **Nội bộ — chưa expose qua REST endpoint nào**, nhưng ảnh hưởng
trực tiếp tới `Notification.Status` mà FE nhìn thấy.

| Giá trị | Int | Ý nghĩa | Hệ quả |
|---|---|---|---|
| `Pending` | 1 | Đã gửi lên Expo, chưa hỏi được kết quả | Notification giữ `Sent` |
| `Ok` | 2 | Expo xác nhận đã đẩy tới FCM/APNs | Notification → `Delivered` + audit `PushDelivered` |
| `Error` | 3 | Expo báo lỗi — xem `ErrorCode` | Notification → `Failed` **chỉ khi** không còn receipt anh em nào `Ok`/`Pending` |
| `Expired` | 4 | Hết `MaxCheckAttempts` mà Expo vẫn không trả kết quả (cửa sổ 24h của Expo đã đóng) | Notification giữ nguyên trạng thái |

**Mã lỗi Expo được xử lý riêng:**

| `errorCode` | Xử lý |
|---|---|
| `DeviceNotRegistered` | **Tắt device token** (`IsActive = false`) — nguồn rác lớn nhất; không tắt thì mọi lần gửi sau đều thất bại và vẫn tính quota |
| `MessageTooBig` | Log **Error** — lẽ ra guard 4096 byte ở `ExpoPushChannel` đã chặn ở nguồn; lọt tới đây nghĩa là guard hụt |
| `MessageRateExceeded` | Log **Warning** — chạm trần ~600 msg/s của project, không phải lỗi nội dung |
| `MismatchSenderId`, `InvalidCredentials` | Log **Critical** — sai cấu hình FCM/APNs, TOÀN BỘ push đang hỏng |
| khác | Log Warning, vẫn ghi nhận `Error` |

---

### `NotificationAuditActionEnum`

Hành động ghi vào `notification_audit_logs` + `notification_audit_outbox` (Option C — đẩy sang
AuditAggregatorService, không có endpoint đọc cục bộ).

| Giá trị | Int | ActionCode | Severity | Ghi ở đâu |
|---|---|---|---|---|
| `PushSent` | 1 | `PushSent` | Info (thành công) / Warning | `NotificationDispatcher` sau khi giao push |
| `PushFailed` | 2 | `PushFailed` | Warning | `NotificationDispatcher` khi push thất bại vĩnh viễn |
| `PushDelivered` | 3 | `PushDelivered` | Info | `ExpoReceiptReconcileBackgroundService` khi receipt `ok` |
| `PushOpened` | 4 | `PushOpened` | Info | `PATCH /api/notifications/{id}/opened` |
| `InAppCreated` | 5 | `InAppCreated` | Info | `NotificationDispatcher` sau khi giao record InApp |
| `InAppRead` | 6 | `InAppRead` | Info | `PATCH /api/notifications/{id}/read` |
| `InAppDismissed` | 7 | `InAppDismissed` | Info | *(khai báo, chưa có caller)* |
| `TemplateTestSent` | 8 | `TemplateTestSent` | **Warning** | **Sprint 6.3 NOTI3-12** — `POST /api/admin/notification-templates/{id}/test-send` |

> **Sprint 6.2 NOTI-13 (#684) — 7 action gốc nay MỚI THẬT SỰ ĐƯỢC GHI.** Trước đó hạ tầng audit đã
> dựng đủ (bảng 14 cột + outbox + relay leader-election) nhưng **không dòng code nào tạo record**:
> enum chưa từng được dùng và relay poll bảng rỗng 2 giây/lần vĩnh viễn.
>
> `TemplateTestSent` để Severity **Warning** chứ không Info là có chủ đích: gửi email thật từ domain
> của hệ thống là hành động cần nổi lên trong bộ lọc audit, không nên lẫn vào nhiễu Info.
>
> Audit chỉ ghi cho kênh **Push** và **InApp** (7 action của `#AUDIT-34`); Email/Sms bỏ qua.
> Lỗi ghi audit **không bao giờ** làm hỏng luồng gửi (writer nuốt exception).

---

## Routing qua ApiGateway

Sprint 6.3 thêm **5 route mới** vào `services/ApiGateway/src/ApiGateway/appsettings.json`
(cluster `notificationCluster`). Path **giữ nguyên** qua gateway — không rewrite.

| RouteId | Match path | Dùng cho |
|---|---|---|
| `notifications-hub-route` | `/hubs/notifications/{**catch-all}` | WebSocket SignalR (negotiate + các sub-path) |
| `notifications-hub-root-route` | `/hubs/notifications` | WebSocket SignalR (root) |
| `notification-unsubscribe-route` | `/api/notification-unsubscribe` | Huỷ đăng ký một chạm (**public** — Gmail/Yahoo gọi từ ngoài) |
| `admin-notification-templates-route` | `/api/admin/notification-templates/{**catch-all}` | `preview` / `test-send` / `activate` |
| `admin-notification-templates-root-route` | `/api/admin/notification-templates` | Danh sách template |

> Route `/api/notifications`, `/api/device-tokens`, `/api/notification-preferences` đã có từ trước
> (catch-all) nên **không cần thêm** cho 5 endpoint mới thuộc các nhóm đó.
>
> ⚠️ `notification-unsubscribe-route` phải **cho phép ẩn danh** ở gateway — đây là endpoint duy nhất
> của service không yêu cầu JWT.

---

## Endpoints — Notifications

Base route: `/api/notifications`

| Method | Path | Auth | Sprint |
|---|---|---|---|
| `GET` | `/api/notifications` | `[Authorize]` | — (đổi hành vi ở 6.3) |
| `POST` | `/api/notifications` | `[Authorize(Roles = "Admin")]` | — (sửa bug ở 6.3) |
| `PATCH` | `/api/notifications/{id}/read` | `[Authorize]` | — (đổi hành vi ở 6.3) |
| `PATCH` | `/api/notifications/{id}/opened` | `[Authorize]` | **MỚI — 6.3 NOTI3-14** |
| `POST` | `/api/notifications/read-all` | `[Authorize]` | — |
| `GET` | `/api/notifications/unread-count` | `[Authorize]` | — (đổi hành vi ở 6.3) |

---

### `GET /api/notifications`

**Mục đích:** Lấy **feed** notification của user hiện tại (UserId trích từ JWT claim). Phân trang,
filter theo `type`/`channel`/`status`/`unreadOnly`. Sắp xếp `CreatedAt` giảm dần.

**Auth:** `[Authorize]` — mọi role đã đăng nhập; mỗi user chỉ thấy notification của chính mình.

> ### ⚠️ BREAKING (Sprint 6.3 NOTI3-01 / #701) — mặc định chỉ trả `Channel = InApp`
>
> **Trước:** endpoint trả record của **mọi** channel → cùng một thông báo hiện lặp **2–4 lần**
> (chat 2 dòng, Battery Critical 4 dòng, SLA P1 4 dòng).
> **Sau:** không truyền `channel` ⇒ tự động lọc `Channel = InApp` ⇒ **mỗi sự kiện đúng 1 dòng**.
>
> **FE cần làm gì:** *không gì cả* nếu chỉ hiển thị feed — kết quả tự đúng. Nếu FE đang tự
> de-duplicate ở client thì **gỡ đoạn đó đi**, nếu không sẽ lọc nhầm.
> Muốn xem lại hành vi cũ: truyền `includeAllChannels=true`.

**Query parameters:**

| Param | Type | Bắt buộc | Default | Mô tả |
|---|---|---|---|---|
| `pageNumber` | `int` | Không | `1` | Số trang (1-based). Giá trị `≤ 0` tự về `1` |
| `pageSize` | `int` | Không | `10` | Số bản ghi mỗi trang. `≤ 0` tự về `10`; **tối đa `100`** — lớn hơn bị clamp xuống `100` |
| `type` | `NotificationTypeEnum?` (int) | Không | `null` | Lọc theo loại (1=TicketCreated, 2=TicketAssigned, …) |
| `channel` | `NotificationChannelEnum?` (int) | Không | `null` | Lọc theo kênh (1=Push, 2=Email, 3=Sms, 4=InApp). **Truyền tường minh ⇒ bộ lọc feed mặc định KHÔNG áp dụng** — dùng cho màn hình soi riêng 1 kênh giao nhận |
| `status` | `NotificationStatusEnum?` (int) | Không | `null` | Lọc theo trạng thái (1=Pending, 2=Sent, 3=Failed, 4=Read, **5=Delivered, 6=Opened**) |
| `unreadOnly` | `bool?` | Không | `null` | `true` → chỉ lấy record `Status != Read && Status != Opened`. **Sprint 6.3: `Opened` nay cũng bị loại** — trước đó noti đã mở vẫn hiện là chưa đọc |
| `includeAllChannels` | `bool` | Không | `false` | **MỚI — Sprint 6.3.** `true` = bỏ bộ lọc feed, trả record của MỌI channel (màn hình chẩn đoán/admin). Người dùng cuối luôn chỉ nên thấy feed |

> **Thứ tự ưu tiên lọc kênh:** `channel` (tường minh) → `includeAllChannels=true` (bỏ lọc) →
> mặc định `InApp`. Truyền cả `channel=2` lẫn `includeAllChannels=true` thì `channel=2` thắng.

**Ví dụ:**

```
GET /api/notifications?pageNumber=1&pageSize=20&unreadOnly=true
Authorization: Bearer eyJ...
```

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
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
        "payloadJson": "{\"alertId\":\"...\",\"batteryAssetId\":\"...\",\"screen\":\"BatteryDetail\"}",
        "entityType": "Battery",
        "entityId": "4521aaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        "sentAt": "2026-07-30T05:34:10Z",
        "readAt": null,
        "createdAt": "2026-07-30T05:34:09Z"
      }
    ],
    "totalItems": 42,
    "pageNumber": 1,
    "pageSize": 20,
    "totalPages": 3,
    "hasNextPage": true,
    "hasPreviousPage": false
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.items[].id` | `Guid` | Không | ID của notification record |
| `data.items[].userId` | `Guid` | Không | ID user nhận (luôn bằng user hiện tại) |
| `data.items[].type` | `NotificationTypeEnum` (int) | Không | Loại notification |
| `data.items[].channel` | `NotificationChannelEnum` (int) | Không | Kênh. Mặc định luôn `4` (InApp) trừ khi truyền `channel`/`includeAllChannels` |
| `data.items[].status` | `NotificationStatusEnum` (int) | Không | Trạng thái lifecycle (1–6) |
| `data.items[].title` | `string` | Không | Tiêu đề. **Lưu ý:** đây là nội dung **inline** consumer ghi lúc tạo record — bản render từ DB template (nếu có) chỉ áp dụng ở tầng gửi, KHÔNG ghi ngược vào record |
| `data.items[].body` | `string` | Không | Nội dung (xem lưu ý ở `title`) |
| `data.items[].payloadJson` | `string?` | **Có** | Chuỗi JSON metadata (FE tự `JSON.parse`). Thường có `screen` để deep-link. Có thể chứa `bypassQuietHours`, `digest`, `fallbackFrom` — xem bảng khoá payload bên dưới |
| `data.items[].entityType` | `string?` | **Có** | `"Ticket"` / `"Battery"` / `"Chat"` / `"Account"` / `"NotificationDigest"` |
| `data.items[].entityId` | `Guid?` | **Có** | ID entity liên quan. `null` với record digest tổng hợp |
| `data.items[].sentAt` | `DateTime?` | **Có** | Thời điểm giao xuống channel. `null` khi `Pending`/`Failed` |
| `data.items[].readAt` | `DateTime?` | **Có** | Thời điểm đọc. `null` nếu chưa đọc |
| `data.items[].createdAt` | `DateTime` | Không | Thời điểm tạo record (UTC) |

**Khoá đặc biệt trong `payloadJson`** (FE nên biết để render đúng):

| Khoá | Kiểu | Ý nghĩa |
|---|---|---|
| `screen` | `string` | Màn hình deep-link (`TicketDetail`, `TicketRate`, `BatteryDetail`, …) |
| `bypassQuietHours` | `true` | Sprint IoT-2 — record được phép gửi kể cả trong quiet hours |
| `digest` + `count` + `notificationIds` | `bool` / `int` / `string[]` | **Sprint 6.2 NOTI-12** — đây là bản tin **tổng hợp**; `notificationIds` là các record gốc đã được gộp |
| `fallbackFrom` + `fallbackChannel` | `string` (Guid) / `"Push"` | **Sprint 6.3 NOTI3-05** — đây là bản **SMS bù** sinh ra vì push không có receipt. **Báo cáo/thống kê phải loại ra để không đếm trùng** |

**Lưu ý:**
- Mảng `items` có thể rỗng (`[]`) — vẫn trả `200`, không phải `404`.
- Query đã filter sẵn `IsDeleted = false` và `UserId = current user`. Không thể đọc noti của người khác.
- Cột `FailureReason` (lý do khi `Status = Failed`), `DispatchAttemptCount`, `NextAttemptAt` **chỉ lưu
  nội bộ DB — KHÔNG expose** qua DTO.

**Lỗi thường gặp:**
- `400` — JWT đã pass `[Authorize]` nhưng thiếu/sai claim định danh (`UserId` → `NameIdentifier` → `sub`):
  ```json
  { "isSuccess": false, "statusCode": 400, "message": "Không xác định được user." }
  ```
- `401` — Token thiếu, hết hạn hoặc chữ ký sai (JWT middleware trả, không theo `CommonResponse<T>`).

---

### `POST /api/notifications`

**Mục đích:** Tạo 1 notification record thủ công — **admin / smoke test / backfill**. Flow production
chạy qua 31 RabbitMQ consumer, không gọi REST.

Record tạo ra ở `Status = Pending`; `NotificationDispatchBackgroundService` sẽ pick lên trong vòng
`PollIntervalSeconds` (mặc định 5 giây) và giao xuống đúng channel của record.

**Auth:** `[Authorize(Roles = "Admin")]`.

> ### ⚠️ SỬA LỖI 30/07/2026 — `userId` nay là BẮT BUỘC và ĐƯỢC ĐỌC TỪ BODY
>
> **Bug cũ:** `CreateNotificationCommand.UserId` bị đánh `[JsonIgnore]` (sao chép nhầm từ
> `MarkNotificationReadCommand`, nơi UserId lấy từ claim), trong khi controller **không** gán nó từ
> token. Hệ quả: mọi lần gọi endpoint này đều tạo record với `UserId = Guid.Empty` → worker đánh
> `Failed` với lý do `empty_user_id` → **endpoint vô dụng đúng cho mục đích mà chính doc của nó mô tả**.
>
> **Đã sửa:** gỡ `[JsonIgnore]` + validation từ chối `Guid.Empty` ngay tại pipeline (400) thay vì
> tạo một dòng rác rồi để worker đánh Failed.
> Không ảnh hưởng 9 consumer đang dùng command này — chúng gán `UserId` bằng code C#, mà thuộc tính
> này chỉ chi phối việc deserialize JSON.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `userId` | `Guid` | **Bắt buộc** | Khác `Guid.Empty` | ID user nhận. Admin chỉ định qua body |
| `type` | `NotificationTypeEnum` (int) | Bắt buộc | Phải là giá trị hợp lệ trong enum | Loại notification |
| `channel` | `NotificationChannelEnum` (int) | Bắt buộc | Phải là giá trị hợp lệ trong enum | Kênh phát — record chỉ giao xuống **đúng kênh này** |
| `title` | `string` | Bắt buộc | Không rỗng, tối đa **200 ký tự** | Tiêu đề (`.Trim()` trước khi lưu) |
| `body` | `string` | Bắt buộc | Không rỗng, tối đa **2000 ký tự** | Nội dung (`.Trim()` trước khi lưu) |
| `payloadJson` | `string?` | Tuỳ chọn | — | Chuỗi JSON metadata cho deep-link |
| `entityType` | `string?` | Tuỳ chọn | Tối đa **100 ký tự** nếu có | `"Ticket"`, `"Battery"`, … (`.Trim()` nếu có giá trị) |
| `entityId` | `Guid?` | Tuỳ chọn | — | ID entity liên quan |
| `bypassQuietHours` | `bool` | Tuỳ chọn (default `false`) | — | Sprint IoT-2 #IoT2-31 — bỏ qua quiet hours khi gửi |

**Ví dụ:**

```json
POST /api/notifications
Authorization: Bearer eyJ...   (Admin)
Content-Type: application/json

{
  "userId": "1a2b3c4d-5e6f-7890-abcd-ef0123456789",
  "type": 99,
  "channel": 4,
  "title": "Bảo trì hệ thống",
  "body": "Hệ thống sẽ bảo trì lúc 22:00 hôm nay, dự kiến 30 phút.",
  "payloadJson": null,
  "entityType": null,
  "entityId": null,
  "bypassQuietHours": false
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
| `data` | `Guid` | Không | ID notification vừa tạo (`Status = Pending`) |
| `message` | `string` | Không | `"Tạo notification thành công."` |

**Lưu ý:**
- Handler **không** validate `userId` có tồn tại trong AuthService — caller chịu trách nhiệm. Nhưng
  record với user không có trong `account_read_models` sẽ bị worker đánh `Failed` với lý do
  `no_email` / `no_phone` khi giao Email/SMS.
- Endpoint **không** bypass preference của user — record vẫn đi qua đầy đủ 6 cổng chặn của dispatcher
  (preference kênh, preference nhóm, digest, hạn mức, quiet hours, địa chỉ nhận). Muốn chắc chắn tới
  nơi thì chọn `type` thuộc danh sách critical hoặc bật `bypassQuietHours`.
- Khi `bypassQuietHours = true`: handler **merge** khoá `bypassQuietHours: true` vào `payloadJson`.
  Payload là JSON object hợp lệ → thêm khoá; rỗng → tạo `{"bypassQuietHours":true}`; không parse được
  thành object → wrap `{"bypassQuietHours":true,"original":"<payload cũ>"}`. Flag **không** có cột riêng.

**Lỗi thường gặp:**
- `400` — Validation fail (`listErrors` liệt kê **tất cả** lỗi, không fail sớm):

  | Field | Detail |
  |---|---|
  | `UserId` | `"UserId là bắt buộc — notification không có người nhận sẽ không bao giờ gửi được."` |
  | `Type` | `"Type không hợp lệ."` |
  | `Channel` | `"Channel không hợp lệ."` |
  | `Title` | `"Title không được trống."` / `"Title tối đa 200 ký tự."` |
  | `Body` | `"Body không được trống."` / `"Body tối đa 2000 ký tự."` |
  | `EntityType` | `"EntityType tối đa 100 ký tự."` |

  ```json
  {
    "isSuccess": false,
    "statusCode": 400,
    "message": "Dữ liệu đầu vào không hợp lệ.",
    "listErrors": [
      { "field": "UserId", "detail": "UserId là bắt buộc — notification không có người nhận sẽ không bao giờ gửi được." },
      { "field": "Title", "detail": "Title không được trống." }
    ]
  }
  ```
- `401` — Chưa đăng nhập / token không hợp lệ.
- `403` — Token hợp lệ nhưng không phải role `Admin`.

---

### `PATCH /api/notifications/{id}/read`

**Mục đích:** Đánh dấu 1 notification của user hiện tại là **đã đọc**.

**Auth:** `[Authorize]` — chỉ thao tác được trên notification của chính mình.

**Path param:** `id` — `Guid` của notification.

**Request body:** không có.

> ### Sprint 6.3 NOTI3-01 — lan trạng thái sang record ANH EM
>
> Một sự kiện sinh nhiều record (mỗi kênh một). Nếu chỉ đánh dấu record InApp, các record
> Push/Email/Sms còn `Pending` sẽ bị worker gửi đi ở vòng sau ⇒ **user đã đọc trong app rồi vẫn
> lãnh thêm một cú push cho đúng việc đó**.
>
> Nay handler tìm và đánh dấu luôn các record anh em thoả **tất cả**:
> cùng `UserId` × cùng `Type` × cùng `EntityType` × cùng `EntityId` × `CreatedAt` nằm trong
> **±1 phút** so với record được đọc × `Status ∈ {Pending, Sent, Delivered}`.
>
> Record đã `Failed` **giữ nguyên** — đó là dữ liệu chẩn đoán, ghi đè sẽ mất dấu vết lỗi.
> Số record anh em bị lan ghi vào audit metadata `siblingsMarkedRead`.

**Hành vi:**
- Đánh dấu thành công → `Status = Read`, `ReadAt = UtcNow`, các record anh em cũng `Read` + `NextAttemptAt = null` → **200**.
- Notification đã `Read` **hoặc `Opened`** trước đó → **idempotent**, trả **200**, không ghi lại
  (không hạ `Opened` xuống `Read` — hạ cấp là mất thông tin).
- Không tồn tại hoặc thuộc user khác → **404** (không leak existence, tránh IDOR).

**Response `200`:** `CommonResponse<Guid>` — `data` = ID notification.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đánh dấu đã đọc thành công.",
  "data": "8b9d0f1e-2a3b-4c5d-9e6f-7a8b9c0d1e2f",
  "listErrors": null
}
```

`message` khi idempotent: `"Notification đã được đánh dấu đã đọc."`

**Lỗi thường gặp:**
- `400` — Thiếu claim `UserId` → `"Không xác định được user."`; hoặc `id` = `Guid.Empty` → `"Id notification không hợp lệ."`
- `401` — Chưa đăng nhập / token hết hạn.
- `404` — `"Không tìm thấy notification."`
- `500` — Lỗi server (GlobalExceptionMiddleware).

**Tác dụng phụ:** ghi audit `InAppRead` (cùng transaction với việc đổi trạng thái).

---

### `PATCH /api/notifications/{id}/opened`  *(MỚI — Sprint 6.3 NOTI3-14 / #714)*

**Mục đích:** Client báo user đã **mở** notification — bấm vào push, hoặc mở qua deep link.

**Vì sao tách khỏi `/read`:** `Read` chỉ nghĩa là dòng đó đã hiện trên feed và user bấm "đã đọc";
`Opened` là bằng chứng mạnh hơn — user **chủ động mở nội dung**. Tách ra để đo **open rate thật của
kênh push**, thứ mà chỉ `Read` không nói được.

**Auth:** `[Authorize]` — chỉ chủ sở hữu notification.

**Path param:** `id` — `Guid` của notification.

**Request body:** không có.

**Hành vi:**
- `Status = Opened`; `ReadAt ??= UtcNow` (mở tức là đã đọc — ghi mốc đọc nếu trước đó chưa có, để
  badge và feed thống nhất).
- Lan sang record anh em **giống hệt quy tắc của `/read`** (cùng cửa sổ ±1 phút, cùng tập trạng thái)
  — nhưng record anh em được đặt `Read`, không phải `Opened` (user chỉ mở đúng một bản).
- Idempotent: đã `Opened` rồi vẫn trả **200** (client mobile có thể gửi lại khi mạng chập chờn).
- Không tồn tại hoặc thuộc user khác → **404**.

**Response `200`:** `CommonResponse<Guid>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đánh dấu đã mở thành công.",
  "data": "8b9d0f1e-2a3b-4c5d-9e6f-7a8b9c0d1e2f",
  "listErrors": null
}
```

`message` khi idempotent: `"Notification đã được đánh dấu đã mở."`

**Lỗi thường gặp:** `400` (thiếu claim / id rỗng) · `401` · `404` · `500`.

**Tác dụng phụ:** ghi audit `PushOpened` với metadata `channel`, `type`, `siblingsMarkedRead`.

> **Mobile nên gọi khi nào:** trong handler `notificationResponseReceived` của `expo-notifications`
> (user bấm vào banner), và khi mở màn hình chi tiết từ deep link. **Không** gọi khi chỉ nhận được
> push ở background — đó là `Delivered`, và `Delivered` do server tự suy ra từ receipt Expo.

---

### `POST /api/notifications/read-all`

**Mục đích:** Đánh dấu **mọi** notification chưa đọc của user hiện tại thành đã đọc.

**Auth:** `[Authorize]`.

**Request body:** không có.

**Hành vi:** cập nhật mọi record của chính user có `Status ∉ {Read, Opened}` → `Status = Read`,
`ReadAt = UtcNow`. Trả về **số record đã được đánh dấu**.

> ⚠️ **Khác `GET`/`unread-count`: endpoint này KHÔNG lọc `Channel = InApp`** — nó quét mọi channel.
> Chủ ý: "đọc hết" cũng có nghĩa là **dừng luôn các record Push/Email/Sms còn `Pending`** (worker chỉ
> lấy `Status = Pending`), để user bấm "đánh dấu tất cả đã đọc" xong không còn lãnh loạt push tồn đọng.
> Vì vậy `data` trả về **có thể lớn hơn** số badge mà `unread-count` vừa hiển thị.
>
> **Sprint 6.3 NOTI3-14:** record `Opened` được bỏ qua — hạ `Opened` xuống `Read` là mất thông tin
> (`Opened` = user thực sự bấm mở, `Read` = chỉ lướt qua feed).

**Response `200`:** `CommonResponse<int>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đánh dấu tất cả đã đọc thành công.",
  "data": 12,
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data` | `int` | Không | Số notification vừa được mark |
| `message` | `string` | Không | `"Đánh dấu tất cả đã đọc thành công."` — hoặc **`"Không có notification chưa đọc."`** khi `data = 0` (vẫn `isSuccess: true`, `statusCode: 200`) |

**Lỗi thường gặp:** `400` (thiếu claim `UserId` → `"Không xác định được user."`) · `401` · `500`.

---

### `GET /api/notifications/unread-count`

**Mục đích:** Đếm số notification chưa đọc — phục vụ **badge**.

**Auth:** `[Authorize]`.

> ### ⚠️ BREAKING (Sprint 6.3) — chỉ đếm record `InApp`, và loại cả `Opened`
>
> **Trước:** đếm cả record giao nhận Push/Email/Sms ⇒ badge **phồng 2–4 lần** so với số dòng user
> thực sự nhìn thấy trong feed.
> **Sau:** điều kiện đếm là
> `UserId = me && !IsDeleted && Channel = InApp && Status ∉ {Read, Opened}`.
>
> Badge nay **luôn khớp** số dòng chưa đọc mà `GET /api/notifications?unreadOnly=true` trả về.
> FE nếu đang chia số badge cho một hằng số để "bù trùng" thì **phải gỡ**.

**Response `200`:** `CommonResponse<int>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Lấy số notification chưa đọc thành công.",
  "data": 3,
  "listErrors": null
}
```

**Lỗi thường gặp:** `400` (thiếu claim `UserId`) · `401` · `500`.

> **Realtime:** không cần polling endpoint này nếu đã kết nối
> [`/hubs/notifications`](#realtime--signalr-hub-hubsnotifications) — server chủ động đẩy event
> `UnreadCountChanged` mỗi khi có notification InApp mới.

---

## Endpoints — Device Tokens

Base route: `/api/device-tokens`

Quản lý push token thiết bị (Expo) cho Mobile/Web. **Mọi endpoint đều `[Authorize]`** — `UserId` luôn
lấy từ JWT claim, user chỉ thao tác trên token của chính mình (không nhận `userId` từ body).

> **Sprint 6.3 — token nay có thể bị hệ thống TỰ TẮT.** `ExpoReceiptReconcileBackgroundService` đặt
> `IsActive = false` khi Expo trả `DeviceNotRegistered` (user gỡ app / đổi máy). `ExpoPushChannel`
> cũng tắt token ngay khi ticket gửi trả lỗi này. FE nên **đăng ký lại token sau mỗi lần đăng nhập**
> — endpoint `POST` là idempotent và tự reactivate.

### Message push gửi lên Expo — thứ Mobile cần khớp

Mỗi message có dạng:

```json
{
  "to": "ExponentPushToken[xxx]",
  "title": "…",
  "body": "…",
  "data": { /* payloadJson đã parse; null nếu payload không phải JSON hợp lệ */ },
  "sound": "default",
  "priority": "high",
  "channelId": "alerts-critical"
}
```

| Field | Giá trị | Ghi chú |
|---|---|---|
| `priority` | `"high"` nếu notification **critical**, ngược lại `"normal"` | Critical = type thuộc `CriticalTypes` **hoặc** payload có `bypassQuietHours: true` |
| `channelId` | `"alerts-critical"` (critical) / `"alerts-default"` | ⚠️ **Android app PHẢI tạo sẵn 2 notification channel đúng 2 id này** (`expo-notifications` `setNotificationChannelAsync`). Channel chưa tồn tại → Android dùng channel mặc định, mất hẳn ưu tiên/âm báo đã thiết kế |
| `data` | `payloadJson` đã deserialize | Payload không parse được → gửi **không kèm `data`** (log Warning), push vẫn đi |

**Giới hạn kích thước (Sprint 6.3 NOTI3-02):** message bị ép vừa **4096 byte** *trước* khi gửi.
Thứ tự hy sinh: **bỏ `data` trước** (client vẫn mở được app, chỉ mất deep link), rồi mới **cắt `body`**
theo **byte** (không phải ký tự — tiếng Việt có dấu là 2–3 byte/ký tự) và thêm `…` ở cuối.
Vượt trần mà không chặn thì Expo trả `MessageTooBig` ở **receipt**, tức là *sau* khi đã báo 200 OK:
người dùng không nhận được gì mà hệ thống vẫn tưởng đã gửi.

**Batch (Sprint 6.2 NOTI-16):** tất cả token đang active của **một người nhận** được gộp vào **một**
HTTP call (Expo cho tối đa **100 message/request**; nhiều hơn thì chia lô). Trước đó mỗi token là 1
HTTP call — user có 3 thiết bị = 3 request cho cùng một notification.
**Chỉ cần ≥ 1 thiết bị nhận được** thì notification tính là đã bàn giao (`Sent`).

---

### `POST /api/device-tokens`

**Mục đích:** Đăng ký push token của thiết bị hiện tại cho user đang đăng nhập.

**Auth:** `[Authorize]` — mọi user đã đăng nhập.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `token` | `string` | Bắt buộc | Không rỗng, tối đa **500 ký tự** | Expo push token (`ExponentPushToken[xxx]`) |
| `platform` | `DevicePlatformEnum` (int) | Bắt buộc | Phải hợp lệ (1=Ios, 2=Android, 3=Web) | Platform thiết bị |
| `deviceInfo` | `string?` | Tuỳ chọn | Tối đa **500 ký tự** nếu có | Mô tả thiết bị (model, OS) để user nhận diện |

> `userId` **không** nhận từ body — server set từ JWT claim.

**Hành vi (định danh theo chuỗi token — unique global ở DB):**

| Tình huống | HTTP | `message` |
|---|---|---|
| Token mới | **`201`** | `"Đăng ký thiết bị thành công."` |
| Token đã đăng ký và **đang active cho chính user hiện tại** | **`409`** | `"Thiết bị đã được đăng ký."` (`data` = Id record đang tồn tại) |
| Token đã từng huỷ (inactive) **hoặc** thuộc user khác trên cùng thiết bị | **`200`** | `"Đăng ký lại thiết bị thành công."` — reactivate + gán lại cho user hiện tại (flow re-login / đổi tài khoản) |

**Response (`201` / `200` / `409`):** `CommonResponse<Guid>` — `data` = Id device token.

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
- `400` — `"Token không được trống."` / `"Token tối đa 500 ký tự."` / `"Platform không hợp lệ."` /
  `"DeviceInfo tối đa 500 ký tự."` — hoặc thiếu claim `UserId` → `"Không xác định được user."`
- `401` — Chưa đăng nhập / token hết hạn.
- `409` — Thiết bị đã đăng ký và đang active.

---

### `DELETE /api/device-tokens`

**Mục đích:** Huỷ đăng ký push token của thiết bị (logout). Định danh theo chuỗi `token`; chỉ huỷ
được token đang active thuộc về chính mình (set `IsActive = false`, **giữ record** để re-login dễ).

**Auth:** `[Authorize]`.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `token` | `string` | Bắt buộc | Không rỗng, tối đa **500 ký tự** | Push token cần huỷ |

**Response `200`:** `CommonResponse<Guid>` — `data` = Id device token vừa huỷ.

**Lỗi thường gặp:**
- `400` — Validation lỗi hoặc thiếu claim `UserId`.
- `401` — Chưa đăng nhập / token hết hạn.
- `404` — `"Không tìm thấy thiết bị đang đăng ký."`

---

### `GET /api/device-tokens`

**Mục đích:** Liệt kê thiết bị đã đăng ký của user hiện tại. **Không trả raw token string** — chỉ
metadata để user nhận diện. Sắp xếp theo lần dùng gần nhất.

**Auth:** `[Authorize]`.

**Response `200`:** `CommonResponse<DeviceTokenDto[]>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": [
    {
      "id": "3c2b1a09-8d7e-6f5a-4b3c-2d1e0f9a8b7c",
      "platform": 2,
      "deviceInfo": "Pixel 8 — Android 15",
      "isActive": true,
      "lastUsedAt": "2026-07-30T05:34:10Z",
      "createdAt": "2026-07-01T08:00:00Z"
    }
  ],
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data[].id` | `Guid` | Không | ID device token record |
| `data[].platform` | `DevicePlatformEnum` (int) | Không | Platform (1=Ios, 2=Android, 3=Web) |
| `data[].deviceInfo` | `string?` | **Có** | Mô tả thiết bị |
| `data[].isActive` | `bool` | Không | `false` = đã logout **hoặc bị hệ thống tắt** vì Expo báo `DeviceNotRegistered` |
| `data[].lastUsedAt` | `DateTime?` | **Có** | Lần dùng gần nhất |
| `data[].createdAt` | `DateTime` | Không | Thời điểm đăng ký lần đầu |

**Lỗi thường gặp:** `400` (thiếu claim `UserId`) · `401`.

---

## Endpoints — Notification Preferences

Base route: `/api/notification-preferences`

| Method | Path | Auth | Sprint |
|---|---|---|---|
| `GET` | `/api/notification-preferences` | `[Authorize]` | — |
| `PUT` | `/api/notification-preferences` | `[Authorize]` | — |
| `GET` | `/api/notification-preferences/matrix` | `[Authorize]` | **MỚI — 6.3 NOTI3-04** |
| `PUT` | `/api/notification-preferences/matrix` | `[Authorize]` | **MỚI — 6.3 NOTI3-04** |
| `GET` | `/api/notification-preferences/categories` | `[Authorize]` | **MỚI — 6.3 NOTI3-04** |

**Mô hình hai cấp (Sprint 6.3):**

```
        công tắc lớn (kênh toàn cục)        tuỳ chọn theo nhóm × kênh
   NotificationPreference (1 record/user)   NotificationCategoryPreference (0–6 record/user)
                    │                                      │
                    └──────────── VÀ LOGIC ────────────────┘
                                    │
                     kênh phải bật ở CẢ HAI thì mới gửi
```

- **Tắt kênh toàn cục thắng mọi tuỳ chọn nhóm** — đó là điều người dùng mong đợi khi gạt công tắc lớn.
- **Không có record nhóm = chưa tuỳ chỉnh**, không phải "đã tắt hết" ⇒ dispatcher rơi về công tắc kênh
  (hành vi trước Sprint 6.3). Nhờ vậy **dữ liệu cũ và FE cũ vẫn chạy đúng, không cần backfill**.
- Dispatcher **cache cả hai cấp 5 phút** trong Redis (`notif_pref:{userId}`,
  `notif_cat_pref:{userId}:{categoryInt}`). Mọi endpoint ghi đều tự xoá cache tương ứng.

---

### `GET /api/notification-preferences`

**Mục đích:** Lấy cài đặt kênh + quiet hours của user hiện tại. User **chưa từng cấu hình** → trả
**giá trị mặc định** (KHÔNG ghi DB, không tạo record).

**Auth:** `[Authorize]`.

**Response `200`:** `CommonResponse<NotificationPreferenceDto>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": {
    "pushEnabled": true,
    "emailEnabled": true,
    "smsEnabled": false,
    "inAppEnabled": true,
    "quietHoursStart": "22:00",
    "quietHoursEnd": "07:00",
    "timeZone": "Asia/Ho_Chi_Minh",
    "notifyOnChat": true,
    "notifyOnMention": true,
    "notifyOnReaction": false,
    "digestWindowMinutes": null
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `pushEnabled` | `bool` | Không | Bật push (mặc định `true`) |
| `emailEnabled` | `bool` | Không | Bật email (mặc định `true`) |
| `smsEnabled` | `bool` | Không | Bật SMS (mặc định **`false`** — SMS tốn tiền thật) |
| `inAppEnabled` | `bool` | Không | Bật in-app (mặc định `true`). ⚠️ Tắt = **mất luôn feed**, dispatcher đánh record InApp là `Failed` với lý do `channel_disabled` |
| `quietHoursStart` | `string?` | **Có** | `"HH:mm"`; `null` = không quiet hours |
| `quietHoursEnd` | `string?` | **Có** | `"HH:mm"`; `null` = không quiet hours |
| `timeZone` | `string` | Không | Timezone IANA (mặc định `"Asia/Ho_Chi_Minh"`) — dùng để tính quiet hours |
| `notifyOnChat` | `bool` | Không | (#570) Nhận notification chat thường (mặc định `true`) |
| `notifyOnMention` | `bool` | Không | (#570) Nhận notification khi bị @mention (mặc định `true`) |
| `notifyOnReaction` | `bool` | Không | (#570) Nhận notification khi chat được reaction (mặc định **`false`**) |
| `digestWindowMinutes` | `int?` | **Có** | `null` = gửi ngay; `5`/`15`/`30`/… = gom digest theo cửa sổ phút. **Sprint 6.2: field này nay CÓ TÁC DỤNG THẬT** |

**Giá trị mặc định khi user chưa cấu hình:**

```json
{
  "pushEnabled": true,
  "emailEnabled": true,
  "smsEnabled": false,
  "inAppEnabled": true,
  "quietHoursStart": null,
  "quietHoursEnd": null,
  "timeZone": "Asia/Ho_Chi_Minh",
  "notifyOnChat": true,
  "notifyOnMention": true,
  "notifyOnReaction": false,
  "digestWindowMinutes": null
}
```

**Lỗi thường gặp:**
- `400` — Thiếu claim `UserId` → `"Không xác định được UserId từ token."`
- `401` — Chưa đăng nhập / token hết hạn.

---

### `PUT /api/notification-preferences`

**Mục đích:** Upsert cài đặt kênh của user hiện tại. Chưa có record → **tạo mới**; đã có → **ghi đè
toàn bộ** (không phải patch — field không gửi sẽ nhận giá trị mặc định của command).

**Auth:** `[Authorize]`.

**Request body:**

| Field | Type | Bắt buộc | Default | Validation | Mô tả |
|---|---|---|---|---|---|
| `pushEnabled` | `bool` | Không | `true` | — | Bật push |
| `emailEnabled` | `bool` | Không | `true` | — | Bật email |
| `smsEnabled` | `bool` | Không | `false` | — | Bật SMS |
| `inAppEnabled` | `bool` | Không | `true` | — | Bật in-app |
| `quietHoursStart` | `string?` | Tuỳ chọn | `null` | Đúng định dạng `"HH:mm"` nếu có | `null` = xoá quiet hours |
| `quietHoursEnd` | `string?` | Tuỳ chọn | `null` | Đúng định dạng `"HH:mm"` nếu có | `null` = xoá quiet hours |
| `timeZone` | `string` | Không | `"Asia/Ho_Chi_Minh"` | Không rỗng, tối đa **100 ký tự** | Timezone IANA |
| `notifyOnChat` | `bool` | Không | `true` | — | (#570) |
| `notifyOnMention` | `bool` | Không | `true` | — | (#570) |
| `notifyOnReaction` | `bool` | Không | `false` | — | (#570) |
| `digestWindowMinutes` | `int?` | Tuỳ chọn | `null` | — | `null`/`≤ 0` = gửi ngay. `> 0` = gom Email/Push vào digest theo cửa sổ này |

> `userId` **không** nhận từ body — server set từ JWT claim.

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
  "timeZone": "Asia/Ho_Chi_Minh",
  "notifyOnChat": true,
  "notifyOnMention": true,
  "notifyOnReaction": false,
  "digestWindowMinutes": 15
}
```

**Response `200`:** `CommonResponse<NotificationPreferenceDto>` — preference sau khi cập nhật (shape
giống `GET`).

**Hành vi quan trọng:**
- **Quiet hours qua đêm** (vd `22:00`–`07:00`) được hỗ trợ; tính theo `timeZone` của user.
- Trong quiet hours: record **InApp vẫn được ghi và giao bình thường** (im lặng, không làm phiền);
  Push/Email/SMS bị **hoãn tới khi hết quiet hours** (`NextAttemptAt` = mốc kết thúc + 1 phút đệm),
  KHÔNG bị bỏ và KHÔNG tính là một lần thử.
- Notification thuộc [critical types](#notificationtypeenum) hoặc có `bypassQuietHours` **luôn được
  gửi** bất kể quiet hours.
- `digestWindowMinutes` chỉ áp cho **Email/Push**. InApp và SMS luôn gửi ngay — nên lịch sử in-app của
  user không bao giờ thiếu mục nào.
- Sau khi commit, handler **xoá cache Redis** `notif_pref:{userId}` để dispatcher đọc bản mới ngay ở
  lần dispatch kế tiếp.
- Field `Frequency` (`NotificationFrequencyEnum`) **không nhận từ body và không trả ra DTO** — chỉ đổi
  được ở tầng DB. Khi `digestWindowMinutes = null` mà `Frequency = Daily` thì cửa sổ digest = 24 giờ.

**Lỗi thường gặp:**
- `400` — `"Định dạng phải là HH:mm."` (`QuietHoursStart`/`QuietHoursEnd`) ·
  `"TimeZone không được trống."` / `"TimeZone tối đa 100 ký tự."` ·
  `"Không xác định được user."`
- `401` — Chưa đăng nhập / token hết hạn.

---

### `GET /api/notification-preferences/matrix`  *(MỚI — Sprint 6.3 NOTI3-04 / #704)*

**Mục đích:** Lấy **ma trận tuỳ chọn nhóm × kênh** của user hiện tại.

**Auth:** `[Authorize]`.

**Hành vi:** luôn trả **đủ 6 nhóm**, kể cả nhóm user chưa tuỳ chỉnh — khi đó giá trị được **kế thừa**
từ công tắc kênh toàn cục và `isCustomized = false`. Nhờ vậy FE vẽ được trạng thái "kế thừa" mà
không phải tự đoán nhóm nào thiếu.

**Response `200`:** `CommonResponse<NotificationPreferenceMatrixDto>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": {
    "channels": {
      "pushEnabled": true,
      "emailEnabled": true,
      "smsEnabled": false,
      "inAppEnabled": true,
      "quietHoursStart": "22:00",
      "quietHoursEnd": "07:00",
      "timeZone": "Asia/Ho_Chi_Minh",
      "notifyOnChat": true,
      "notifyOnMention": true,
      "notifyOnReaction": false,
      "digestWindowMinutes": null
    },
    "categories": [
      {
        "category": 1,
        "categoryName": "Ticket",
        "pushEnabled": true,
        "emailEnabled": true,
        "smsEnabled": false,
        "inAppEnabled": true,
        "isCustomized": false
      },
      {
        "category": 2,
        "categoryName": "Sla",
        "pushEnabled": true,
        "emailEnabled": true,
        "smsEnabled": true,
        "inAppEnabled": true,
        "isCustomized": true
      },
      {
        "category": 3, "categoryName": "Battery",
        "pushEnabled": true, "emailEnabled": true, "smsEnabled": false, "inAppEnabled": true,
        "isCustomized": false
      },
      {
        "category": 4, "categoryName": "Environmental",
        "pushEnabled": true, "emailEnabled": true, "smsEnabled": false, "inAppEnabled": true,
        "isCustomized": false
      },
      {
        "category": 5, "categoryName": "Chat",
        "pushEnabled": true, "emailEnabled": false, "smsEnabled": false, "inAppEnabled": true,
        "isCustomized": true
      },
      {
        "category": 6, "categoryName": "Account",
        "pushEnabled": true, "emailEnabled": true, "smsEnabled": false, "inAppEnabled": true,
        "isCustomized": false
      }
    ]
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.channels` | `NotificationPreferenceDto` | Không | Công tắc kênh toàn cục — **vẫn thắng mọi dòng nhóm**. Shape giống `GET /api/notification-preferences` |
| `data.categories` | `NotificationCategoryPreferenceDto[]` | Không | **Luôn đúng 6 phần tử**, sắp theo thứ tự giá trị enum (1→6) |
| `data.categories[].category` | `NotificationCategoryEnum` (int) | Không | Mã nhóm — ổn định để FE lưu |
| `data.categories[].categoryName` | `string` | Không | Tên nhóm dạng chuỗi (`"Ticket"`, `"Sla"`, `"Battery"`, `"Environmental"`, `"Chat"`, `"Account"`) — FE hiển thị mà không cần bảng tra cứu riêng |
| `data.categories[].pushEnabled` | `bool` | Không | Bật push cho nhóm này |
| `data.categories[].emailEnabled` | `bool` | Không | Bật email cho nhóm này |
| `data.categories[].smsEnabled` | `bool` | Không | Bật SMS cho nhóm này |
| `data.categories[].inAppEnabled` | `bool` | Không | Bật in-app cho nhóm này |
| `data.categories[].isCustomized` | `bool` | Không | `false` = **chưa tuỳ chỉnh**, 4 giá trị trên là kế thừa từ `channels`. `true` = user đã đặt tường minh (có record trong DB) |

> `GET /api/notification-preferences` (bản cũ) **vẫn giữ nguyên** để FE hiện tại không vỡ.

**Lỗi thường gặp:** `400` (thiếu claim `UserId` → `"Không xác định được UserId từ token."`) · `401`.

---

### `PUT /api/notification-preferences/matrix`  *(MỚI — Sprint 6.3 NOTI3-04 / #704)*

**Mục đích:** Cập nhật ma trận nhóm × kênh.

**Auth:** `[Authorize]`.

**Ghi theo kiểu VÁ TỪNG DÒNG:** chỉ nhóm có trong `items` bị đổi, nhóm còn lại **giữ nguyên**.
Ghi đè toàn bộ sẽ khiến hai tab mở song song xoá thiết lập của nhau.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `items` | `CategoryPreferenceItem[]` | **Bắt buộc** | Ít nhất 1 phần tử; không trùng `category` | Các dòng cần vá |
| `items[].category` | `NotificationCategoryEnum` (int) | Bắt buộc | Phải là giá trị hợp lệ 1–6 | Nhóm cần đổi |
| `items[].pushEnabled` | `bool` | Bắt buộc | — | Không gửi ⇒ nhận `false` (JSON default), **không** giữ giá trị cũ |
| `items[].emailEnabled` | `bool` | Bắt buộc | — | (như trên) |
| `items[].smsEnabled` | `bool` | Bắt buộc | — | (như trên) |
| `items[].inAppEnabled` | `bool` | Bắt buộc | — | (như trên) |

> ⚠️ **Trong một dòng thì phải gửi đủ 4 kênh.** "Vá từng dòng" nghĩa là vá theo **nhóm**, không phải
> theo từng ô kênh — bỏ trống `emailEnabled` sẽ ghi thành `false` chứ không giữ nguyên.
> `userId` **không** nhận từ body — server set từ JWT claim.

**Ví dụ — tắt email cho nhóm Chat, bật SMS cho nhóm SLA:**

```json
PUT /api/notification-preferences/matrix
Authorization: Bearer eyJ...
Content-Type: application/json

{
  "items": [
    { "category": 5, "pushEnabled": true, "emailEnabled": false, "smsEnabled": false, "inAppEnabled": true },
    { "category": 2, "pushEnabled": true, "emailEnabled": true,  "smsEnabled": true,  "inAppEnabled": true }
  ]
}
```

**Response `200`:** `CommonResponse<NotificationPreferenceMatrixDto>` — **ma trận đầy đủ sau khi cập
nhật** (shape giống `GET .../matrix`), `message = "Cập nhật tuỳ chọn theo nhóm thành công."`

**Tác dụng phụ:** xoá cache Redis `notif_cat_pref:{userId}:{category}` của **đúng những nhóm vừa
sửa** — không xoá thì dispatcher còn dùng bản cũ tới 5 phút và người dùng tưởng thiết lập không ăn.

**Lỗi thường gặp:**
- `400` — `listErrors` liệt kê:

  | Field | Detail |
  |---|---|
  | `Items` | `"Cần ít nhất một nhóm để cập nhật."` |
  | `Items.Category` | `"Nhóm '{số}' không hợp lệ."` — giá trị ngoài 1–6 |
  | `Items.Category` | `"Nhóm '{tên}' xuất hiện nhiều lần trong cùng một request."` — gửi trùng thì không biết dòng nào thắng, từ chối thay vì đoán |
  | `UserId` | `"Không xác định được user."` |
- `401` — Chưa đăng nhập / token hết hạn.

---

### `GET /api/notification-preferences/categories`  *(MỚI — Sprint 6.3 NOTI3-04 / #704)*

**Mục đích:** Bảng tra cứu `NotificationTypeEnum` → nhóm. FE dùng để giải thích *"tắt nhóm này thì
mất những thông báo nào"* mà **không phải nhân bản bảng ánh xạ ở client** — nhân bản là chắc chắn sẽ
lệch khi thêm type mới.

**Auth:** `[Authorize]` (kế thừa từ controller — mọi user đã đăng nhập).

**Response `200`:** `CommonResponse<object[]>` — sắp theo `categoryValue` rồi `typeValue`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": [
    { "type": "TicketCreated",  "typeValue": 1,  "category": "Ticket", "categoryValue": 1 },
    { "type": "TicketAssigned", "typeValue": 2,  "category": "Ticket", "categoryValue": 1 },
    { "type": "TicketEscalated","typeValue": 6,  "category": "Sla",    "categoryValue": 2 },
    { "type": "SlaWarning",     "typeValue": 7,  "category": "Sla",    "categoryValue": 2 },
    { "type": "System",         "typeValue": 99, "category": "Account","categoryValue": 6 }
  ],
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data[].type` | `string` | Không | Tên `NotificationTypeEnum` |
| `data[].typeValue` | `int` | Không | Giá trị số của type |
| `data[].category` | `string` | Không | Tên `NotificationCategoryEnum` |
| `data[].categoryValue` | `int` | Không | Giá trị số của nhóm |

**Số phần tử:** đúng bằng số type có trong `NotificationCategoryMap` (hiện **32**).

---

## Endpoints — Admin Notification Templates

Base route: `/api/admin/notification-templates` · **`[Authorize(Roles = "Admin")]` cho toàn controller**
*(MỚI — Sprint 6.3 NOTI3-12 / #712)*

Ba việc trước sprint này không làm được:
- **Xem trước** — sửa template xong chỉ biết đúng/sai khi có sự kiện thật xảy ra.
- **Gửi thử** — kiểm chứng bản dựng thật trong hộp thư, không phải đoán qua HTML.
- **Quay lui** — bản mới sai chính tả gửi cho hàng trăm khách thì phải sửa tay lại.

> ⚠️ **Dùng `[Authorize(Roles = "Admin")]`, KHÔNG dùng `[Authorize(Policy = "AdminOnly")]`.**
> Policy `AdminOnly` trong `SharedInfrastructure/DependencyInjection/Extensions/AddAuthorizationRole.cs`
> **đã bị comment toàn bộ** (code chết), và định nghĩa cũ `RequireClaim("Role","1")` cũng không khớp
> token hiện tại (JWT phát `role = "Admin"` dạng chuỗi). Dùng policy chưa đăng ký ⇒ ASP.NET ném
> `InvalidOperationException` → **HTTP 500 ở mọi request, kể cả của Admin**. Phát hiện khi test E2E 30/07/2026.

**Mô hình phiên bản:** sửa template là **tạo bản mới** với `version` tăng dần, KHÔNG ghi đè bản cũ.
Trong cùng bộ ba `(Type × Channel × Locale)` chỉ được có **đúng một** bản `isActive = true`;
dispatcher luôn lấy bản đó. Có 2 unique index ràng buộc ở DB — xem [Database schema](#database-schema).

---

### `GET /api/admin/notification-templates`

**Mục đích:** Danh sách template, lọc theo type/channel/locale. **Bao gồm cả bản không active** để
thấy lịch sử phiên bản.

**Auth:** Admin.

**Query parameters:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `type` | `NotificationTypeEnum?` | Không | Lọc theo loại notification |
| `channel` | `NotificationChannelEnum?` | Không | Lọc theo kênh |
| `locale` | `string?` | Không | Lọc theo locale (`vi-VN`, `en-US`). Rỗng/whitespace bị bỏ qua |

**Sắp xếp:** `type` → `channel` → `locale` → `version` **giảm dần** (bản mới nhất lên đầu mỗi bộ ba).

**Response `200`:** `CommonResponse<object[]>`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": [
    {
      "id": "b1f5c0aa-1111-2222-3333-444455556666",
      "type": "SlaBreached",
      "channel": "Email",
      "locale": "vi-VN",
      "version": 2,
      "isActive": true,
      "titleTemplate": "🔴 SLA vi phạm — ticket {{ticketCode}}",
      "bodyTemplate": "Ticket {{ticketCode}} đã vượt hạn {{slaDeadline}}. Cần xử lý ngay.",
      "createdAt": "2026-07-30T09:32:20Z",
      "updatedAt": "2026-07-30T10:05:11Z"
    }
  ],
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data[].id` | `Guid` | Không | ID template |
| `data[].type` | `string` | Không | **Tên** `NotificationTypeEnum` (không phải số) |
| `data[].channel` | `string` | Không | **Tên** `NotificationChannelEnum` |
| `data[].locale` | `string` | Không | BCP-47 (`vi-VN` / `en-US`) |
| `data[].version` | `int` | Không | Số phiên bản trong cùng bộ ba (bắt đầu từ `1`) |
| `data[].isActive` | `bool` | Không | Bản đang được dispatcher dùng |
| `data[].titleTemplate` | `string` | Không | Template tiêu đề (cú pháp Handlebars `{{var}}`) |
| `data[].bodyTemplate` | `string` | Không | Template nội dung |
| `data[].createdAt` | `DateTime` | Không | UTC |
| `data[].updatedAt` | `DateTime?` | **Có** | `null` nếu chưa sửa lần nào |

**Seed:** `NotificationTemplateCatalog` seed **đủ 32 type × mọi kênh** trong `DefaultTypeChannelMatrix`
(trước Sprint 6.3 chỉ 5/32 type có template). Các type **hướng Customer** có thêm bản `en-US`:
`TicketCreated`, `TicketStatusChanged`, `TicketResolved`, `TicketClosed`, `TicketApproved`,
`TicketRejected`, `TicketReopened`, `TicketRatingRequested`, `AccountActivated`,
`BatteryAnomalyWarning`, `BatteryAnomalyInfo`, `EnvironmentalIncidentDetected`,
`EnvironmentalIncidentResolved`. Type nội bộ chỉ có `vi-VN` — dịch thứ không ai đọc chỉ tạo thêm thứ
phải bảo trì.

Bản SMS được **biến tấu tự động**: gộp tiêu đề vào thân và cắt còn tối đa **300 ký tự**
(`"[Solar Battery]"` làm tiêu đề) — SMS tính tiền theo đoạn 160 ký tự và không có tiêu đề riêng.

Seeder **idempotent theo bộ ba** `(Type × Channel × Locale)`: đã có bản nào cho bộ ba đó thì KHÔNG
thêm. Cố ý **không ghi đè** — người vận hành có thể đã sửa nội dung trong DB.

---

### `POST /api/admin/notification-templates/{id}/preview`

**Mục đích:** Dựng thử template với dữ liệu mẫu — **KHÔNG gửi đi đâu cả**.

**Auth:** Admin.

**Path param:** `id` — `Guid` của template.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `sampleData` | `object?` | Tuỳ chọn | Cặp khoá–giá trị ứng với placeholder trong template. Không gửi (hoặc gửi `null`, hoặc gửi thứ không phải object) ⇒ render với model rỗng |

Giá trị trong `sampleData` được quy về dictionary phẳng: `string` → chuỗi; `number` → `int64` nếu
nguyên, ngược lại `double`; `true`/`false` → bool; `null` → null; **object/array lồng nhau → giữ
nguyên dạng JSON thô** (chuỗi).

**Ví dụ:**

```json
POST /api/admin/notification-templates/b1f5c0aa-1111-2222-3333-444455556666/preview
Authorization: Bearer eyJ...   (Admin)
Content-Type: application/json

{
  "sampleData": {
    "ticketCode": "TK-001",
    "priority": "P1Critical",
    "slaDeadline": "30/07/2026 18:00"
  }
}
```

**Response `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": null,
  "data": {
    "type": "SlaBreached",
    "channel": "Email",
    "locale": "vi-VN",
    "version": 2,
    "title": "🔴 SLA vi phạm — ticket TK-001",
    "body": "Ticket TK-001 đã vượt hạn 30/07/2026 18:00. Cần xử lý ngay."
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.type` / `data.channel` | `string` | Không | Tên enum |
| `data.locale` | `string` | Không | Locale của template |
| `data.version` | `int` | Không | Phiên bản |
| `data.title` | `string` | Không | Tiêu đề **sau khi render** |
| `data.body` | `string` | Không | Nội dung **sau khi render** |

> **Placeholder không có trong `sampleData` sẽ rỗng** — đó chính là cách phát hiện template gọi tên
> biến sai.

**Lỗi thường gặp:**
- `400` — Template hỏng cú pháp Handlebars → `"Template hỏng cú pháp: {chi tiết}"` (trả 400 thay vì
  ném 500).
- `401` / `403` — Không phải Admin.
- `404` — `"Không tìm thấy template."`

---

### `POST /api/admin/notification-templates/{id}/test-send`

**Mục đích:** Gửi thử template tới **chính admin đang đăng nhập**.

**Auth:** Admin.

> ### 🔒 KHÔNG NHẬN ĐỊA CHỈ TỰ DO (R-46)
>
> Endpoint nhận địa chỉ tuỳ ý sẽ biến hệ thống thành **cổng gửi thư rác có xác thực** — kẻ chiếm được
> một tài khoản admin có thể bắn nội dung tự soạn từ domain có SPF/DKIM hợp lệ của chúng ta.
> Địa chỉ nhận **LUÔN** lấy từ danh tính người gọi, không bao giờ từ body.
>
> **Hai nguồn, theo thứ tự:**
> 1. `account_read_models.email` của chính admin — chuẩn nhất;
> 2. claim `email` trong JWT — **dự phòng**.
>
> Vì sao cần nguồn thứ 2: read-model chỉ được điền từ `AccountActivatedEvent` /
> `AccountProfileUpdatedEvent`. Tài khoản admin **seed thẳng vào `auth_db`** (không đi qua luồng kích
> hoạt) sẽ KHÔNG BAO GIỜ có mặt ở đó — phát hiện khi test E2E 30/07/2026: mọi lần gọi test-send đều
> trả 400 dù người gọi là Admin hợp lệ. Lấy từ claim vẫn an toàn: đó là danh tính đã được JWT xác thực.

**Path param:** `id` — `Guid` của template.

**Request body:** giống `preview` — `{ "sampleData": { ... } }` (tuỳ chọn).

**Ràng buộc:**
- **Chỉ template kênh `Email`.** Kênh khác → `400` (gửi thử SMS tốn tiền thật; push cần device token
  của admin).
- **Rate limit 5 lần/giờ mỗi admin.** Đếm bằng Redis key `tpl_test_send:{adminId:N}:{yyyyMMddHH}`
  (TTL 2 giờ). Vượt → `429`.
- **Ghi audit** `TemplateTestSent` (Severity **Warning**) mỗi lần gửi, metadata gồm `templateId`,
  `type`, `locale`, `version`, `quotaUsed`, `recipientSource` (`"read-model"` | `"jwt-claim"`).

**Hành vi:** render title/body → publish `SendNotificationEmailEvent` với
`Subject = "[GỬI THỬ] {title}"`, `SourceService = "notification-template-test"`,
`UnsubscribeUrl = null`.

> Email gửi thử **KHÔNG có link huỷ**: nó không phải thư gửi hàng loạt, và link huỷ trong bản thử sẽ
> tắt nhầm thông báo thật của chính admin.

**Response `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã gửi thử tới admin@solarbattery.site.",
  "data": { "remainingThisHour": 3 },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `message` | `string` | Không | `"Đã gửi thử tới {email}."` |
| `data.remainingThisHour` | `int` | Không | Số lượt còn lại trong giờ hiện tại (`max(0, 5 - đã dùng)`) |

**Lỗi thường gặp:**

| HTTP | `message` | Nguyên nhân |
|---|---|---|
| `400` | `"Không xác định được UserId từ token."` | Thiếu claim định danh |
| `400` | `"Chỉ gửi thử được template kênh Email."` | Template là Push/Sms/InApp |
| `400` | `"Không xác định được email của admin đang đăng nhập (thiếu cả read-model lẫn claim email)."` | Cả 2 nguồn địa chỉ đều rỗng |
| `400` | `"Template hỏng cú pháp: {chi tiết}"` | Handlebars lỗi |
| `401` / `403` | — | Không phải Admin |
| `404` | `"Không tìm thấy template."` | — |
| `429` | `"Đã dùng hết 5 lượt gửi thử trong giờ này."` | Vượt hạn mức |

---

### `POST /api/admin/notification-templates/{id}/activate`

**Mục đích:** **Quay lui** — kích hoạt lại một phiên bản template cũ.

**Auth:** Admin.

**Path param:** `id` — `Guid` của phiên bản muốn kích hoạt.

**Request body:** không có.

**Hành vi:** trong cùng bộ ba `(Type × Channel × Locale)` chỉ được có đúng một bản active, nên thao
tác này **tắt bản đang dùng rồi bật bản được chọn trong MỘT lần lưu** — để không có khoảnh khắc nào
bộ ba đó không có bản active (khoảnh khắc ấy dispatcher sẽ rơi về chuỗi hardcode trong consumer).
Bản đã đúng trạng thái mong muốn thì bỏ qua (không ghi thừa).

**Response `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã kích hoạt phiên bản 1.",
  "data": null,
  "listErrors": null
}
```

**Lỗi thường gặp:** `401` / `403` · `404` `"Không tìm thấy template."`

> **Chưa có endpoint tạo/sửa template qua REST.** Tạo bản mới hiện làm bằng SQL/seed; endpoint
> `activate` chỉ chuyển giữa các bản đã tồn tại. Ghi nhận là giới hạn có chủ đích của Sprint 6.3.

---

## Endpoints — Unsubscribe một chạm

Base route: `/api/notification-unsubscribe` · **`[AllowAnonymous]`** *(MỚI — Sprint 6.3 NOTI3-15 / #715)*

**Vì sao public:** Gmail/Yahoo gửi `POST` **tự động** khi người dùng bấm nút "Huỷ đăng ký" ngay trong
giao diện hộp thư — không kèm cookie hay JWT. Vì vậy endpoint xác thực bằng **token ký HMAC-SHA256**
trong URL thay vì bằng đăng nhập.

**Vì sao phải có:** từ 2024 Gmail và Yahoo **bắt buộc** người gửi số lượng lớn hỗ trợ huỷ một chạm
(RFC 8058). Không có nút huỷ, người nhận sẽ bấm "báo cáo spam" — tỷ lệ spam vượt **0.3%** là mất
reputation của domain `solarbattery.site` đang trong giai đoạn warm-up.

**Cấu trúc token:** `base64url(userId.categoryInt.expiresAtUnix) + "." + base64url(HMAC-SHA256)`.
- Khoá ký: `Notification:Unsubscribe:Secret`. **Không cấu hình ⇒ không phát hành được token**
  (trả `null`, email vẫn gửi nhưng không có header huỷ) — thay vì rơi về khoá mặc định, vì khoá mặc
  định nghĩa là ai đọc mã nguồn cũng ký được link hợp lệ.
- Hạn dùng: `Notification:Unsubscribe:TokenLifetimeDays` (mặc định **180 ngày**). Email cũ nằm trong
  hộp thư nhiều năm; token vô hạn là một cánh cửa mở mãi.
- So sánh chữ ký **constant-time** (`CryptographicOperations.FixedTimeEquals`).

**Link được gắn ở đâu:** `EmailBusChannel` dựng URL
`{Notification:Unsubscribe:PublicBaseUrl}/api/notification-unsubscribe?token=...` cho **đúng NHÓM**
của notification, rồi truyền qua `SendNotificationEmailEvent.UnsubscribeUrl`. EmailService gắn cặp
header `List-Unsubscribe: <url>` + `List-Unsubscribe-Post: List-Unsubscribe=One-Click`.
**Email giao dịch (OTP, đặt lại mật khẩu, mời admin) đi consumer khác và cố ý KHÔNG có header này** —
người dùng không thể "huỷ đăng ký" khỏi mã xác thực do chính họ yêu cầu.

---

### `POST /api/notification-unsubscribe`

**Mục đích:** Huỷ đăng ký một chạm — điểm mà Gmail/Yahoo gọi tự động.

**Auth:** công khai; xác thực bằng token trong query.

**Query parameters:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `token` | `string` | **Bắt buộc** | Token ký HMAC lấy từ link trong email |

**Request body:** không có (Gmail gửi `List-Unsubscribe=One-Click` dạng form, server không đọc body).

**Hành vi:** tắt kênh **Email** cho **đúng nhóm ghi trong token**, KHÔNG tắt toàn bộ email — người
dùng huỷ vì bị chat làm phiền vẫn phải nhận được cảnh báo SLA.
- Chưa có dòng tuỳ chọn cho nhóm đó → **tạo mới** với `emailEnabled = false`, `pushEnabled = true`,
  `smsEnabled = false`, `inAppEnabled = true` (các kênh khác giữ mặc định).
- Đã có → chỉ đặt `emailEnabled = false`, `updatedAt = now`.
- **Idempotent** — email client có thể gửi lại.
- Xoá cache `notif_cat_pref:{userId}:{category}` ngay; không xoá thì người dùng còn nhận email tới 5
  phút sau khi bấm huỷ, và họ sẽ báo cáo spam.

**Response `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã ngừng gửi email nhóm 'Chat'. Các kênh khác không thay đổi.",
  "data": null,
  "listErrors": null
}
```

**Lỗi thường gặp:**
- `400` — Token thiếu, sai chữ ký, hoặc hết hạn:
  ```json
  { "isSuccess": false, "statusCode": 400, "message": "Liên kết hủy đăng ký không hợp lệ hoặc đã hết hạn." }
  ```
  > **Không nói rõ sai chữ ký hay hết hạn** — tránh giúp người dò thu hẹp phạm vi. IP được ghi log Warning.

---

### `GET /api/notification-unsubscribe`

**Mục đích:** Trang xác nhận khi người dùng bấm thẳng vào liên kết trong nội dung email.

**Auth:** công khai; cùng token với `POST`.

**Query parameters:** `token` (bắt buộc) — giống `POST`.

> ### ⚠️ CHỈ HIỂN THỊ — KHÔNG thay đổi gì
>
> `GET` phải an toàn (RFC 9110): **trình quét link của hộp thư tự mở mọi URL trong email**; nếu `GET`
> cũng huỷ thì người dùng bị huỷ oan mà không hề bấm.

**Response `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Xác nhận để ngừng nhận email nhóm này.",
  "data": { "category": "Chat", "confirmMethod": "POST" },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.category` | `string` | Không | Tên nhóm sẽ bị huỷ |
| `data.confirmMethod` | `string` | Không | Luôn `"POST"` — FE/trang tĩnh phải gọi `POST` cùng token để thực sự huỷ |

**Lỗi thường gặp:** `400` — token thiếu/sai/hết hạn (cùng message với `POST`).

---

## Realtime — SignalR Hub `/hubs/notifications`

*(MỚI — Sprint 6.3 NOTI3-13 / #713)*

**Vấn đề:** feed in-app chỉ cập nhật khi client tự gọi lại API. Người dùng đang mở màn hình thông báo
phải kéo xuống làm mới mới thấy cảnh báo vừa xảy ra — trong khi đây chính là hệ thống giám sát pin,
nơi độ trễ vài chục giây là có ý nghĩa.

| Thuộc tính | Giá trị |
|---|---|
| Endpoint | `/hubs/notifications` (qua ApiGateway: cùng path, route `notifications-hub-route`) |
| Auth | **`[Authorize]` + `.RequireAuthorization()`** — bắt buộc đăng nhập |
| Nhóm | `user:{userId:N}` — **tự ghép ở `OnConnectedAsync`**, client KHÔNG gọi thêm method nào |
| Rời nhóm | Tự động ở `OnDisconnectedAsync` |

> ### 🔒 Nhóm lấy từ CLAIM, không nhận tham số từ client
>
> Id nhóm lấy từ claim trong JWT (`UserId` → `AccountId` → `NameIdentifier`) chứ **không** từ đối số
> client truyền lên. Nếu để client tự khai "tôi muốn nghe nhóm của user X" thì bất kỳ ai cũng đọc
> được thông báo của người khác — kể cả khi đã `[Authorize]`.
>
> Token thiếu claim định danh → kết nối vẫn thành công nhưng **không được ghép nhóm** (log Warning) ⇒
> không nhận được event nào.

**Server → Client events:**

| Event | Payload | Khi nào |
|---|---|---|
| `NotificationCreated` | object (bảng dưới) | `InAppChannel` giao xong một record InApp — tức là **sau khi đã lưu DB**, để client không bao giờ thấy thông báo mà REST chưa trả về |
| `UnreadCountChanged` | `int` | Ngay sau `NotificationCreated`, mang số chưa đọc mới (đếm theo đúng công thức badge: `InApp && Status ∉ {Read, Opened}`) |

**Payload `NotificationCreated`** — ⚠️ enum trả dạng **chuỗi** (khác REST DTO trả số):

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID notification |
| `type` | `string` | Không | Tên `NotificationTypeEnum` (vd `"SlaBreached"`) |
| `channel` | `string` | Không | Luôn `"InApp"` |
| `status` | `string` | Không | Luôn `"Sent"` tại thời điểm đẩy |
| `title` | `string` | Không | Tiêu đề |
| `body` | `string` | Không | Nội dung |
| `payloadJson` | `string?` | **Có** | Chuỗi JSON metadata |
| `entityType` | `string?` | **Có** | `"Ticket"` / `"Battery"` / … |
| `entityId` | `Guid?` | **Có** | ID entity liên quan |
| `createdAt` | `DateTime` | Không | UTC |

**Snippet FE:**

```js
import * as signalR from "@microsoft/signalr";

const conn = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/notifications", { accessTokenFactory: () => accessToken })
  .withAutomaticReconnect()
  .build();

conn.on("NotificationCreated", (n) => prependToFeed(n));
conn.on("UnreadCountChanged", (count) => setBadge(count));

await conn.start();   // không cần gọi thêm method nào — server tự ghép nhóm
```

> **Polling REST vẫn giữ nguyên làm đường dự phòng.** Realtime là lớp tăng tốc, không phải nguồn dữ
> liệu duy nhất — WebSocket rớt thì client vẫn phải lấy được đủ thông báo bằng
> `GET /api/notifications`. Notifier **nuốt mọi lỗi**: mất realtime không được làm hỏng bản ghi thật.

---

## Tầng dispatch — thứ tự cổng chặn

`NotificationDispatcher.DispatchPendingAsync` xử lý **một record `Pending`** qua các cổng dưới đây,
**theo đúng thứ tự này**. Hiểu thứ tự rất quan trọng khi debug "vì sao noti không tới".

| # | Cổng | Không qua được thì sao | `FailureReason` / lý do metric |
|---|---|---|---|
| 0 | Record không còn `Pending` | Bỏ qua (đã xử lý ở vòng trước) | — |
| 0 | Không có channel handler cho `Channel` | → `Failed` | `no_channel_handler` |
| 0 | `UserId == Guid.Empty` | → `Failed` | `empty_user_id` |
| 1 | **Preference kênh toàn cục** + **preference nhóm × kênh** (và logic) | → `Failed` (không bao giờ gửi được, dừng luôn để tránh retry vô hạn) | `channel_disabled` |
| 2 | **Digest** — không-critical, kênh Email/Push, user có `digestWindowMinutes > 0` hoặc `Frequency = Daily`, và record **không phải** bản digest | → **Deferred**: `NextAttemptAt = now + window`, KHÔNG tăng attempt | `digest` |
| 2b | **Hạn mức người dùng** — không-critical, kênh ≠ InApp, record không phải digest | → **Deferred** `now + DeferMinutes` (mặc định 60') để gom vào digest. **Không vứt bỏ** — vứt là mất dữ liệu nghiệp vụ | `rate_limited` (+ label `per_hour` / `per_type`) |
| 3 | **Quiet hours** — không-critical, kênh ≠ InApp | → **Deferred** tới khi hết quiet hours (+1 phút đệm) | `quiet_hours` |
| 4 | **Địa chỉ nhận** — Email cần `account_read_models.email`, SMS cần `phoneNumber` | → `Failed` | `no_email` / `no_phone` |
| 4b | **Device token** — kênh Push cần ≥ 1 token `IsActive` | → `Failed` | `no_device_token` |
| 5 | **Render nội dung** — ưu tiên DB template `(Type × Channel × Locale)` active, `OrderByDescending(Version)`; không có → lùi về `DefaultLocale`; vẫn không có → dùng Title/Body inline. **Template hỏng KHÔNG chặn gửi** | (không chặn) | — |
| 6 | **`channel.SendAsync`** | Lỗi → tăng `DispatchAttemptCount`, đặt `NextAttemptAt` theo backoff, vẫn `Pending`. Chạm `MaxAttempts` → `Failed` | `max_attempts_exceeded` |

**Kết quả (`DispatchOutcome`):**

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Sent` | 1 | Đã giao thành công (`Status = Sent`, `SentAt = now`, `FailureReason = null`, `NextAttemptAt = null`) |
| `Retrying` | 2 | Lỗi tạm thời — đã tăng attempt + hẹn `NextAttemptAt`, vẫn `Pending` |
| `Failed` | 3 | Lỗi vĩnh viễn hoặc hết lượt thử |
| `Deferred` | 4 | Hoãn có chủ đích (quiet hours / digest / rate limit) — vẫn `Pending`, **KHÔNG tính là 1 lần thử** |

**Backoff:** lần thử thứ *n* hoãn `BaseBackoffSeconds × 2^(n−1)`, chặn trần `MaxBackoffSeconds`.
Mặc định: 30s → 60s → 120s → 240s → 480s (trần 900s), tối đa `MaxAttempts = 5` lần.

**Hạn mức (NOTI3-06) — cửa sổ trượt xấp xỉ trên Redis:**

```
count ≈ đếm(cửa sổ trước) × (1 − phần thời gian đã trôi) + đếm(cửa sổ hiện tại)
```

Cách này chỉ cần một `INCR` atomic mỗi lần gửi mà vẫn tránh lỗ hổng kinh điển của cửa sổ cố định
(gửi hết hạn mức cuối giờ rồi lại hết hạn mức đầu giờ sau = 2× trong vài phút). Key:
`noti_rl:{userId:N}:h:{bucket}` và `noti_rl:{userId:N}:t:{typeInt}:{bucket}`, TTL 2 giờ.
**Fail-open có chủ đích:** Redis lỗi ⇒ **cho gửi** — Redis chết mà chặn hết notification thì một sự
cố hạ tầng phụ trợ sẽ làm câm luôn cả cảnh báo an toàn.

**Ma trận Type → Channel** (`DefaultTypeChannelMatrix`, override qua `Notification:Dispatch:TypeChannelMatrix`):

| Type | Kênh mặc định |
|---|---|
| `TicketCreated`, `TicketStatusChanged`, `TicketClosed`, `TicketEscalated`, `SlaWarning`, `EnvironmentalIncidentResolved`, `BatteryAlertEscalationPending`, `AlertTicketSagaFailed`, `IotDeviceWentOffline`, `ChatCreated`, `ChatReacted`, `ParticipantAdded`, `ParticipantRemoved`, `ParticipantRoleChanged`, `TicketReopened`, `TicketRatingRequested`, `BatteryAnomalyWarning` | InApp + Push |
| `TicketAssigned`, `TicketResolved`, `BatteryAnomalyDetected`, `CascadeRiskHigh`, `ChatMentioned`, `ChatEscalatedToAdmin`, `TicketApproved`, `TicketRejected` | InApp + Push + Email |
| `SlaBreached`, `EnvironmentalIncidentDetected`, `IncidentDeclared` | InApp + Push + Email + **SMS** |
| `AccountActivated` | InApp + Email |
| `AdminInvite` | **chỉ Email** |
| `BatteryAnomalyInfo`, `System` | **chỉ InApp** |
| Type không khai báo | fallback **chỉ InApp** |

> **Sprint 6.2 NOTI-14 (#685) bổ sung 14 type còn thiếu** vào ma trận — trước đó chúng rơi vào
> fallback InApp-only, nghĩa là `DispatchAsync` (đường fan-out) không bao giờ gửi push/email cho
> chat, cascade risk, và toàn bộ type mới của Sprint 6.2.

**`SlaBreached` — phân nhánh theo priority (Sprint 6.2 NOTI-06 / #677):**

| Priority | Người nhận | Kênh | Nội dung |
|---|---|---|---|
| **P1** (`Priority` bắt đầu bằng `"P1"`) | Manager **+ Admin** | InApp + Push + Email + **SMS** | `"🔴 SLA P1 bị vi phạm — cần xử lý ngay"` — reassign Senior (Tier 3) + báo Admin |
| **P2** (`"P2"…`) | Manager | InApp + Push + Email (**không SMS**) | `"🟠 SLA P2 bị vi phạm"` — cân nhắc reassign Tier 2/3 |
| **P3** / không đọc được | Manager | **chỉ InApp** | `"🟡 SLA P3 bị vi phạm"` — Manager review khi có thể |

Nhận diện theo **tiền tố chuỗi** để không phụ thuộc `TicketService.Domain`; giá trị lạ → coi như P3
(mức ồn ào thấp nhất, tránh bắn SMS vì dữ liệu lạ). Payload trả thêm `priorityTier` (`"P1"`/`"P2"`/`"P3"`).
**Priority KHÔNG bị đổi khi breach** (Priority Policy) — breach chỉ thêm nhân lực/kênh báo.

---

## Background services

**7 worker thường trú** — nhiều nhất hệ thống. Vì vậy `mem_limit` của container nâng **384m → 512m**
ngày 30/07/2026 (đo thật lúc rảnh: 329 MiB = 86% của 384m, chỉ còn ~55 MiB đệm).

| Worker | Sprint | Chu kỳ | Leader-election | Tắt bằng |
|---|---|---|---|---|
| `NotificationAuditOutboxRelayBackgroundService` | #AUDIT-34 | 2s | ✅ | — |
| `NotificationDispatchBackgroundService` | **6.2 NOTI-01** | `PollIntervalSeconds` (5s) | ✅ | `Notification:Dispatch:Enabled=false` |
| `NotificationDigestBackgroundService` | **6.2 NOTI-12** | `PollIntervalMinutes` (5') | ✅ | `Notification:Digest:Enabled=false` |
| `NotificationDlqMonitorBackgroundService` | **6.3 NOTI3-08** | `IntervalSeconds` (60s, sàn 10s) | ❌ (chỉ đọc) | `MessageBus:DlqMonitor:Enabled=false` |
| `ExpoReceiptReconcileBackgroundService` | **6.3 NOTI3-02** | `PollIntervalSeconds` (300s, sàn 10s) | ❌ | `Notification:ExpoReceipt:Enabled=false` |
| `NotificationFallbackBackgroundService` | **6.3 NOTI3-05** | `PollIntervalSeconds` (120s) | ✅ | `Notification:Fallback:Enabled=false` |
| `NotificationRetentionBackgroundService` | **6.3 NOTI3-11** | tick 15', chạy 1 lần/ngày lúc `RunAtUtcHour` | ✅ | `Notification:Retention:Enabled=false` |

**Leader-election** dùng Redis key riêng cho từng worker (`notification_dispatch_leader`,
`notification_digest_leader`, `notification_fallback_leader`, `notification_retention_leader`) với
lease TTL 30s–10'. **Redis lỗi thì xử lý khác nhau theo worker:**
- Dispatch / Fallback: **vẫn chạy** (thà gửi trùng còn hơn không ai gửi — cảnh báo critical đến hai
  lần vẫn hơn là không đến).
- Retention: **bỏ qua lượt dọn** (ở đây "làm trùng" nghĩa là hai instance cùng xoá — thà hoãn tới đêm
  sau còn hơn dọn nhầm khi không chắc).

---

### `NotificationDispatchBackgroundService` — mắt xích còn thiếu (6.2 NOTI-01 / #672)

Trước Sprint 6.2, `NotificationDispatcher` đã viết xong và đăng ký DI nhưng **không có caller nào**;
22 consumer chỉ ghi record `Pending` rồi thôi → **Push/Email/SMS không bao giờ được gửi ở runtime**,
user chỉ thấy notification khi tự poll REST.

**Điều kiện lấy batch:**
```
Status = Pending  AND  !IsDeleted
AND DispatchAttemptCount < MaxAttempts
AND (NextAttemptAt IS NULL OR NextAttemptAt <= now)
ORDER BY CreatedAt  LIMIT BatchSize
```
Gauge `notification_pending_total` được đo **trước** khi lấy batch (phản ánh tồn đọng thật) và **luôn
ghi kể cả khi = 0** — chỉ ghi khi > 0 thì gauge sẽ kẹt ở giá trị cũ.
Một record hỏng **không chặn cả batch** (bắt exception per-record).

---

### `NotificationDigestBackgroundService` (6.2 NOTI-12 / #683)

Quét record đã bị dispatcher hoãn và **nay tới hạn**, gom theo `(user × channel)` thành **1 record
tổng hợp mới**, rồi đánh dấu record gốc là `Sent` (chúng đã được giao — dưới dạng một mục trong digest).

- Record tổng hợp: `Type = System`, `EntityType = "NotificationDigest"`, `EntityId = null`,
  `Title = "Bạn có N thông báo mới"` (hoặc chính title nếu chỉ có 1 mục),
  `Body` liệt kê tối đa `MaxItemsInBody` (mặc định 10) dòng dạng `• {Title}: {Body}` rồi
  `"… và N thông báo khác."`
- `PayloadJson`: `{ digest: true, count, from, to, notificationIds: [...] }`
- Chỉ gom cho user **thực sự bật digest** — record hoãn vì lý do khác (backoff lỗi, quiet hours) phải
  để nguyên cho dispatch worker thử lại.
- Bản digest mang `EntityType = "NotificationDigest"` để dispatcher **không gom nó vào digest lần nữa**
  (nếu không sẽ tự hoãn chính nó mãi mãi).
- **Chỉ áp cho Email/Push.** InApp và SMS gửi ngay.

---

### `ExpoReceiptReconcileBackgroundService` (6.3 NOTI3-02 / #702)

Lấy receipt `Pending` đã đủ "chín" (`CreatedAt <= now - MinAgeMinutes`, mặc định 15' — hỏi sớm hơn
thì Expo hầu như trả rỗng), hỏi `POST https://exp.host/--/api/v2/push/getReceipts` theo lô
`BatchSize` (Expo cho tối đa **1000** id/request; worker tự clamp), rồi:

| Kết quả | Xử lý |
|---|---|
| `ok` | Receipt → `Ok`; Notification → `Delivered` + audit `PushDelivered`; metric `expo_push_receipt_total{status="ok"}` |
| lỗi | Receipt → `Error` + `ErrorCode`/`ErrorMessage`; xử lý riêng theo mã (xem [PushReceiptStatusEnum](#pushreceiptstatusenum-mới--sprint-63-noti3-02--702)) |
| Expo không trả ticket đó | Chỉ **tăng `CheckAttemptCount`**, KHÔNG đánh lỗi (đánh lỗi ở đây sẽ biến "chưa biết" thành "thất bại" — sai lệch số liệu) |
| Chạm `MaxCheckAttempts` | Receipt → `Expired` (cửa sổ 24h của Expo đã đóng, hỏi thêm cũng vô nghĩa) |

**Notification chỉ chuyển `Failed` khi KHÔNG còn thiết bị nào nhận được** — user có 3 máy, 1 máy gỡ
app thì đó không phải thất bại giao hàng.

---

### `NotificationFallbackBackgroundService` (6.3 NOTI3-05 / #705)

Push **critical** đã `Sent` quá `PushReceiptTimeoutMinutes` mà **không** có receipt `Ok` nào ⇒ sinh
thêm **một bản SMS bù** cho cùng người nhận.

- Có receipt `Ok` → nâng luôn notification lên `Delivered`, bỏ qua.
- Đã bù rồi (tồn tại record SMS cùng `UserId × Type × EntityId` có `PayloadJson` chứa id push gốc) →
  bỏ qua, chống gửi SMS lặp mỗi vòng quét.
- Bản bù mang `PayloadJson.fallbackFrom = {id push gốc}` + `fallbackChannel = "Push"` để **báo cáo
  không đếm trùng**; payload gốc giữ nguyên nên deep link vẫn dùng được.
- Metric `notification_fallback_total{from_channel="push", to_channel="sms"}`.

> ⚠️ **`PushReceiptTimeoutMinutes` KHÔNG độc lập.** Nó phải lớn hơn thời điểm sớm nhất mà worker đối
> soát có thể biết kết quả:
> ```
> ngưỡng an toàn tối thiểu = ExpoReceipt:MinAgeMinutes        (15')
>                          + ExpoReceipt:PollIntervalSeconds/60 (5')
>                          + biên dự phòng độ trễ HTTP         (~5')
>                          = 25 phút
> ```
> Mặc định **30**. Worker tự kiểm tra lúc khởi động và **ghi cảnh báo** nếu cấu hình vi phạm ràng buộc
> này — đặt thấp hơn thì fallback bắn SMS trước khi đối soát kịp chạy, tức gửi thừa cho **mọi** push critical.

> 📌 **Giới hạn có chủ đích (R-44):** nhánh B chốt 30/07/2026 — **KHÔNG mua provider thứ hai**.
> Chuỗi này chỉ cứu ca *push hỏng*, **KHÔNG cứu được ca SMS hỏng** (gateway là một chiếc điện thoại
> Android — hết pin hoặc mất mạng là cả tầng SMS chết). Bù bằng: alert Grafana khi gateway mất
> heartbeat, và tách sẵn interface `IEmailProvider` / `ISmsProvider` để cắm provider thứ hai sau này
> không phải sửa business logic.

---

### `NotificationRetentionBackgroundService` (6.3 NOTI3-11 / #711)

Chạy hằng đêm lúc `RunAtUtcHour` (mặc định **18h UTC = 01h sáng giờ Việt Nam**).

**Quy tắc dọn:**
- Chỉ động vào notification `CreatedAt < now - Days` (mặc định **90 ngày**) và **không** ở `Pending`
  — bản `Pending` tuyệt đối giữ lại, xoá là mất thông báo chưa từng được gửi.
- Notification thuộc `CriticalTypes` **giữ VĨNH VIỄN** khi `KeepCriticalForever = true` — đó là bằng
  chứng "đã cảnh báo", cần cho điều tra sự cố và đối chiếu SLA.
- **Xoá mềm** (`IsDeleted = true`, `DeletedAt = now`) chứ không `DELETE` thật — dữ liệu vẫn phục hồi
  được nếu ngưỡng cấu hình sai, và tránh khoá bảng lâu.
- Batch `BatchSize` (mặc định 500, clamp 1–5000), tối đa `MaxBatchesPerRun` vòng (mặc định 20, clamp
  1–1000). Chạm trần → log Warning "còn tồn đọng, sẽ dọn tiếp đêm sau".

---

### `NotificationDlqMonitorBackgroundService` (6.3 NOTI3-08 / #708)

MassTransit tự chuyển message vào queue `<tên-queue>_error` khi consumer thất bại hết lượt retry.
Trước sprint này **không ai theo dõi** các queue đó: message chết nằm im, không log, không cảnh báo.

Không có API nào của MassTransit đọc được độ sâu queue, nên worker hỏi **RabbitMQ Management API**
(`GET /api/queues`, cổng 15672, Basic auth từ `RabbitMQ:Username`/`Password`), lọc queue có hậu tố
`_error`, đẩy vào gauge `notification_dlq_size{queue}`.
**Ghi cả queue rỗng** — chỉ ghi khi > 0 thì gauge kẹt ở giá trị cũ sau khi đã dọn xong DLQ.
**Chỉ giám sát, KHÔNG tự xử lý message** — quyết định replay hay bỏ là việc của con người.
Management API không sẵn sàng **không** làm chết service, chỉ mất khả năng đo.

---

## Integration events

### Events CONSUMED — 31 consumer class / 32 loại event

**Sprint 6.2 thêm 9 consumer class** (đánh dấu 🆕).
`AccountActivatedEvent` có **2** consumer (một tạo notification, một đồng bộ read-model);
`ParticipantChangeConsumer` là **1 class** xử lý **3** loại event.

| Consumer | Event | Người nhận | Kênh |
|---|---|---|---|
| `TicketCreatedConsumer` | `TicketCreatedEvent` | Manager | InApp + Push |
| `TicketAssignedConsumer` | `TicketAssignedEvent` | Staff **+ Customer** | InApp + Push + Email (mỗi bên 1 bộ, nội dung khác nhau) |
| `TicketResolvedConsumer` | `TicketResolvedEvent` | Manager **+ Customer** | InApp + Push + Email |
| `TicketEscalatedConsumer` | `TicketEscalatedEvent` | Manager/Admin | InApp + Push |
| `SlaWarningConsumer` | `SlaWarningEvent` | Manager **+ Staff phụ trách** | InApp + Push |
| `SlaBreachedConsumer` | `SlaBreachedEvent` | theo priority (bảng trên) | theo priority |
| `IncidentDeclaredConsumer` | `IncidentDeclaredEvent` | Manager/Admin | InApp + Push |
| `BatteryAnomalyDetectedConsumer` | `BatteryAnomalyDetectedEvent` | Customer sở hữu pin | **đủ 4 kênh** (trước 6.2 chỉ InApp+Push) |
| 🆕 `BatteryAnomalyWarningConsumer` | `BatteryAnomalyWarningDetectedEvent` | Customer sở hữu pin | Warning → InApp+Push · Info → **chỉ InApp** |
| `BatteryAlertEscalationRequestedConsumer` | `BatteryAlertEscalationRequestedEvent` | Manager/Admin | InApp + Push + **Email** (bản Email dùng HTML đã render sẵn từ template) |
| `BatteryCascadeRiskHighConsumer` | `BatteryCascadeRiskHighEvent` | Manager/Admin | InApp + Push + Email |
| `AlertTicketSagaFailedConsumer` | `AlertTicketSagaFailedEvent` | Admin | InApp + Push + **Email** (bản Email dùng HTML đã render sẵn) |
| `IotDeviceWentOfflineConsumer` | `IotDeviceWentOfflineEvent` | Manager/Admin | InApp + Push |
| `EnvironmentalIncidentDetectedConsumer` | `EnvironmentalIncidentDetectedEvent` | theo site | InApp + Push + Email + SMS, `BypassQuietHours = true` |
| `EnvironmentalIncidentResolvedConsumer` | `EnvironmentalIncidentResolvedEvent` | theo site | chỉ InApp (clear banner) |
| `ChatCreatedConsumer` | `ChatCreatedEvent` | Customer ↔ Staff | **InApp + Push** (Sprint 6.2 NOTI-10 thêm InApp) |
| `ChatMentionConsumer` | `ChatMentionedEvent` | người được mention | InApp + Push + Email |
| `ChatReactionConsumer` | `ChatReactedEvent` | tác giả chat | InApp + Push |
| 🆕 `ChatEscalatedToAdminConsumer` | `ChatEscalatedToAdminEvent` | **toàn bộ Admin** | InApp + Push + Email |
| `ParticipantChangeConsumer` | `ParticipantAddedEvent`, `ParticipantRemovedEvent`, `ParticipantRoleChangedEvent` | participant liên quan | InApp + Push |
| 🆕 `TicketStatusChangedConsumer` | `TicketStatusChangedEvent` | Customer | InApp + Push |
| 🆕 `TicketApprovedConsumer` | `TicketApprovedEvent` | Customer | InApp + Push + Email |
| 🆕 `TicketRejectedConsumer` | `TicketRejectedEvent` | Staff **hoặc** Customer (theo `IsClosedRejected`) | InApp + Push + Email |
| 🆕 `TicketClosedConsumer` | `TicketClosedEvent` | Customer + Manager | InApp + Push |
| 🆕 `TicketReopenedConsumer` | `TicketReopenedEvent` | Manager + Staff đang assign | InApp + Push |
| 🆕 `TicketRatingRequestedConsumer` | `TicketRatingRequestedEvent` | Customer | InApp + Push |
| 🆕 `SmsFailedConsumer` | `SmsFailedEvent` | *(không tạo noti)* | cập nhật record SMS → `Failed` |
| `AccountActivatedConsumer` | `AccountActivatedEvent` | user mới | **chỉ InApp** (email chào mừng đi đường AuthService → EmailService riêng) |
| `AccountActivatedSyncConsumer` | `AccountActivatedEvent` | *(read-model)* | ghi `account_read_models` |
| `AccountProfileUpdatedSyncConsumer` | `AccountProfileUpdatedEvent` | *(read-model)* | cập nhật read-model |
| `AccountDeletedSyncConsumer` | `AccountDeletedEvent` | *(read-model)* | xoá read-model |

**`SmsFailedConsumer` (6.2 NOTI-11 / #682) — vòng phản hồi cho SMS:**
`SmsBusChannel` publish `SendSmsCommand` với `CorrelationId = Notification.Id` rồi đánh dấu record là
`Sent` ngay khi đẩy được lên bus. Nếu SmsService gửi thất bại thật (đã hết retry) nó publish
`SmsFailedEvent` nhưng trước đây **không ai consume** → record kẹt ở `Sent` dù SMS không bao giờ tới.
Consumer chỉ xử lý event có `SourceService == "notification"` (SMS của service khác không có record
tương ứng), đối chiếu `CorrelationId` về đúng record kênh `Sms`, rồi đặt
`Status = Failed`, `SentAt = null`, `FailureReason = "SMS gateway thất bại ({phone}): {lỗi}"`,
`NextAttemptAt = null`. **Không hạ cấp** nếu record đã `Read`/`Opened`.

**Idempotency (Sprint 6.3 NOTI3-09 / #709):** `NotificationDebounce` nay dùng **`SET key val NX EX ttl`
atomic** (`ICacheService.TrySetIfNotExistsAsync`) thay cho cặp `Get` rồi `Set` — hai lời gọi tách rời
tạo cửa sổ tranh chấp, 2 event trùng đến gần như đồng thời cùng đọc thấy "chưa có" nên cùng được xử lý.
- `notif_debounce:{alertId}` — TTL **5 phút** (debounce nghiệp vụ theo AlertId)
- `notif_msg:{messageId}` — TTL **30 phút** (idempotency khi MassTransit retry)

> ⚠️ **Phải sửa TRƯỚC NOTI3-08:** bật retry ở tầng bus trên consumer chưa idempotent thật sự sẽ
> **nhân bản** notification thay vì chỉ gửi lại (rủi ro R-41).

---

### Events PUBLISHED

| Event | Publish bởi | Consumer | Ghi chú |
|---|---|---|---|
| `SendNotificationEmailEvent` | `EmailBusChannel`, `AdminNotificationTemplatesController.TestSend` | **`SendNotificationEmailConsumer` (EmailService)** — 🆕 Sprint 6.2 NOTI-02 | Trước 6.2 event này **0 consumer**: RabbitMQ drop message không có binding, không lỗi, không log, **email biến mất**. Kể cả sau khi bật dispatcher thì thiếu bước này email vẫn không tới hộp thư |
| `SendSmsCommand` | `SmsBusChannel` | `SendSmsCommandConsumer` (SmsService) | `CorrelationId = Notification.Id` để đối chiếu ngược qua `SmsFailedEvent` |
| `AuditCreatedEventV1` | `NotificationAuditWriter` → outbox → relay | AuditAggregatorService | Option C |

**`SendNotificationEmailEvent` — payload:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `notificationId` | `Guid` | Không | ID record notification (hoặc Guid mới cho test-send) |
| `toEmail` | `string` | Không | Địa chỉ nhận |
| `subject` | `string` | Không | Tiêu đề đã render |
| `body` | `string` | Không | Nội dung. HTML dựng sẵn → gửi nguyên; text thuần → EmailService bọc vào `NotificationGeneric.html` (placeholder được HTML-encode) |
| `sourceService` | `string` | Không | `"notification"` \| `"notification-template-test"` |
| `unsubscribeUrl` | `string?` | **Có** | **Sprint 6.3 NOTI3-15.** Có giá trị ⇒ EmailService gắn `List-Unsubscribe` + `List-Unsubscribe-Post: List-Unsubscribe=One-Click`. `null` ⇒ email giao dịch, **không** được gắn |

---

### Cảnh báo bảo mật KHÔNG đi qua NotificationService

**Sprint 6.2 NOTI-04 (#675)** thêm 2 consumer ở **EmailService**, không phải ở đây:

| Event | Consumer (EmailService) | Template | Vì sao đi thẳng |
|---|---|---|---|
| `SuspiciousLoginDetectedEvent` | `SuspiciousLoginDetectedConsumer` | `SuspiciousLogin.html` | Email bảo mật **phải tới ngay**, không phụ thuộc preference / quiet hours / digest của user |
| `RefreshTokenReuseDetectedEvent` | `RefreshTokenReuseDetectedConsumer` | `RefreshTokenReuse.html` | (như trên) |

Logic detect ở AuthService đã chạy từ trước nhưng **không service nào consume** → công detect bỏ đi,
nạn nhân chỉ thấy mình "bị logout" không rõ lý do và mất cơ hội đổi mật khẩu kịp thời.
`RefreshTokenReuseDetectedEvent` được **thêm field `Email`** ở Sprint 6.2 vì EmailService không có DB
account nên không tra ngược được từ `AccountId`.

Đây cũng là lý do **cố ý không thêm `NotificationTypeEnum` cho 2 loại này** — tránh đẻ thêm enum không
producer, đúng lỗi mà review đã nêu.

---

### Retry / DLQ ở tầng bus (Sprint 6.3 NOTI3-08 / #708)

Sửa ở `SharedInfrastructure/Bus/MassTransitExtensions.cs` ⇒ **ảnh hưởng ĐỦ 8 service**
(Auth · Battery · Ticket · Notification · Email · Sms · FileStorage · AuditAggregator).

| Tầng | Mặc định | Cấu hình | Ghi chú |
|---|---|---|---|
| `UseMessageRetry` (retry **trong tiến trình**, lỗi thoáng qua ms→giây) | ✅ **BẬT** — Exponential, 3 lần, 200ms → 5000ms | `MessageBus:Retry:Limit` / `:InitialIntervalMs` / `:MaxIntervalMs` | Không phụ thuộc hạ tầng nên bật mặc định. `Limit = 0` để tắt |
| `UseDelayedRedelivery` (trả về broker, giao lại sau nhiều phút — lỗi kéo dài) | ❌ **TẮT** | `MessageBus:Redelivery:Enabled` + `:IntervalsMinutes` (mặc định `[5,15,60]`) | ⚠️ Trên RabbitMQ cần plugin `rabbitmq_delayed_message_exchange`. Image `rabbitmq:3-management-alpine` **KHÔNG có** plugin này — bật lên mà thiếu plugin thì khai báo exchange **thất bại ngay lúc bus khởi động** ⇒ **chết TẤT CẢ service dùng bus** |

Thứ tự khai báo: redelivery bọc **ngoài** retry — chuỗi retry ngắn chạy hết rồi mới tính tới lần giao
lại có độ trễ.

**Sửa kèm 30/07/2026 — `cfg.UsePublishMessageScheduler()`:** saga TicketService (`AlertTicketSaga`,
`ChatEscalationReview`) dùng `.Schedule(...)` để hẹn timeout; MassTransit lấy scheduler từ **payload**
của pipe context, mà payload đó chỉ tồn tại khi bus factory gọi `UsePublishMessageScheduler()`.
Trước bản sửa chỉ có nửa đầu (`AddPublishMessageScheduler()` phía DI), nên mọi lần saga cần hẹn giờ
đều ném `MassTransit.PayloadNotFoundException: MassTransit.MessageSchedulerContext` → retry → rơi
`_error`. Đo được lúc test E2E 30/07/2026: **1662 message** kẹt ở `AlertTicketSagaState_error`,
`qrtz_triggers` = 0 dòng, 2 saga treo vĩnh viễn.

---

## Cấu hình (appsettings / biến môi trường)

Định dạng biến môi trường: thay `:` bằng `__` (vd `Notification__Dispatch__Enabled`).
Nguồn: `env.prod.example`, `deploy/helm/solar-battery/values.yaml`,
`deploy/helm/solar-battery/templates/shared/configmap.yaml` (PublicBaseUrl), `.../secret.yaml` (Secret).

### `Notification:Dispatch` — worker giao nhận (6.2 NOTI-01 + NOTI-14)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Enabled` | `bool` | `true` | `false` = quay lại hành vi cũ: chỉ ghi DB, **KHÔNG gửi gì** (log Warning lúc khởi động) |
| `PollIntervalSeconds` | `int` | `5` | Chu kỳ quét (sàn 1s) |
| `BatchSize` | `int` | `100` | Số record mỗi vòng |
| `MaxAttempts` | `int` | `5` | Số lần thử tối đa trước khi `Failed` vĩnh viễn |
| `BaseBackoffSeconds` | `int` | `30` | Backoff cơ sở; lần thứ *n* hoãn `Base × 2^(n−1)` |
| `MaxBackoffSeconds` | `int` | `900` | Trần backoff |
| `UseDbTemplates` | `bool` | `true` | Ưu tiên render từ bảng `notification_templates`; không có template thì dùng Title/Body inline |
| `DefaultLocale` | `string` | `"vi-VN"` | Locale lùi về khi người nhận không có `PreferredLocale` hoặc locale đó chưa có template |
| `TypeChannelMatrix` | `object` | `{}` | Override ma trận Type → Channel. Key/value là **tên enum**, không phân biệt hoa-thường. **Merge lên default theo từng key** |
| `CriticalTypes` | `string[]` | `[]` | Override danh sách critical. Khai báo ⇒ **thay thế hoàn toàn** default (không cộng dồn); khai báo toàn giá trị không parse được ⇒ rơi về default |

```jsonc
"Notification": {
  "Dispatch": {
    "PollIntervalSeconds": 5,
    "BatchSize": 100,
    "MaxAttempts": 5,
    "TypeChannelMatrix": { "SlaBreached": [ "InApp", "Push", "Email", "Sms" ] },
    "CriticalTypes": [ "SlaBreached", "IncidentDeclared" ]
  }
}
```

### `Notification:Digest` (6.2 NOTI-12)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Bật worker gom digest |
| `PollIntervalMinutes` | `int` | `5` | Chu kỳ quét (sàn 1') |
| `BatchSize` | `int` | `50` | Số **nhóm (user × channel)** xử lý mỗi vòng |
| `MaxItemsInBody` | `int` | `10` | Số dòng tối đa liệt kê trong thân digest |

### `Notification:ExpoReceipt` (6.3 NOTI3-02)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Tắt = **mất khả năng biết push có tới nơi hay không** |
| `PollIntervalSeconds` | `int` | `300` | Chu kỳ quét (sàn 10s) |
| `MinAgeMinutes` | `int` | `15` | Chỉ hỏi receipt của message đã gửi tối thiểu bấy nhiêu phút — Expo khuyến nghị ~15' |
| `BatchSize` | `int` | `300` | Số ticket id/request (Expo tối đa 1000, worker clamp) |
| `MaxCheckAttempts` | `int` | `5` | Hỏi quá số lần này mà vẫn rỗng ⇒ `Expired` |

### `Notification:Fallback` (6.3 NOTI3-05)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Bật chuỗi dự phòng push → SMS |
| `PushReceiptTimeoutMinutes` | `int` | `30` | ⚠️ **Phải ≥ 25** (xem ràng buộc ở trên) |
| `PollIntervalSeconds` | `int` | `120` | Chu kỳ quét |
| `BatchSize` | `int` | `100` | Số notification xử lý mỗi vòng |

### `Notification:RateLimit` (6.3 NOTI3-06)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Bật hạn mức |
| `MaxPerUserPerHour` | `int` | `20` | Trần tổng notification **chủ động** (Push/Email/SMS) mỗi người mỗi giờ. `≤ 0` = không giới hạn |
| `MaxPerUserPerType` | `int` | `8` | Trần riêng cho từng type — chặn một loại sự kiện lặp lại chiếm hết hạn mức chung |
| `DeferMinutes` | `int` | `60` | Thời gian hoãn khi chạm trần (record chờ rồi được gộp vào digest) |

### `Notification:Retention` (6.3 NOTI3-11)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Enabled` | `bool` | `true` | Bật worker dọn |
| `Days` | `int` | `90` | Giữ notification bấy nhiêu ngày kể từ `CreatedAt` |
| `BatchSize` | `int` | `500` | Số dòng xoá mềm mỗi vòng (clamp 1–5000) |
| `MaxBatchesPerRun` | `int` | `20` | Số vòng tối đa mỗi lượt (clamp 1–1000) |
| `RunAtUtcHour` | `int` | `18` | Giờ UTC chạy hằng đêm (18h UTC = 01h VN) |
| `KeepCriticalForever` | `bool` | `true` | Giữ vĩnh viễn notification thuộc `CriticalTypes` |

### `Notification:Unsubscribe` (6.3 NOTI3-15)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Secret` | `string` | *(không có)* | Khoá ký HMAC. **Không cấu hình ⇒ không phát hành token** ⇒ email gửi **không có** `List-Unsubscribe` (log Warning). Sinh bằng `openssl rand -base64 48` |
| `PublicBaseUrl` | `string` | *(không có)* | Địa chỉ gốc API công khai để dựng URL tuyệt đối (vd `https://api.solarbattery.site`). Fallback đọc khoá `PublicBaseUrl` |
| `TokenLifetimeDays` | `int` | `180` | Hạn dùng token (giá trị `≤ 0` hoặc không parse được → 180) |

### `MessageBus` (6.3 NOTI3-08 — áp cho cả 8 service)

| Khoá | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Retry:Limit` | `int` | `3` | Số lần retry trong tiến trình. `0` = tắt |
| `Retry:InitialIntervalMs` | `int` | `200` | Khoảng chờ đầu |
| `Retry:MaxIntervalMs` | `int` | `5000` | Trần khoảng chờ |
| `Redelivery:Enabled` | `bool` | **`false`** | ⚠️ Bật cần plugin RabbitMQ `rabbitmq_delayed_message_exchange` |
| `Redelivery:IntervalsMinutes` | `int[]` | `[5,15,60]` | Các mốc giao lại |
| `DlqMonitor:Enabled` | `bool` | `true` | Bật worker đo DLQ |
| `DlqMonitor:IntervalSeconds` | `int` | `60` | Chu kỳ (sàn 10s) |
| `DlqMonitor:ManagementUrl` | `string?` | suy từ `RabbitMQ:Host` + cổng `15672` | Base URL RabbitMQ Management API |

---

## Database schema

DB: `notification_db`. **4 migration mới** ở Sprint 6.2/6.3.

### `notifications` — cột thêm (migration `20260729161154_AddNotificationDispatchRetryColumns`)

| Cột | Kiểu | Null | Default | Ý nghĩa |
|---|---|---|---|---|
| `dispatch_attempt_count` | `integer` | Không | `0` | **Sprint 6.2 NOTI-01** — số lần worker đã thử gửi. Chạm `MaxAttempts` → `Failed` (dừng retry vô hạn) |
| `next_attempt_at` | `timestamptz` | **Có** | `null` | **Sprint 6.2 NOTI-01** — thời điểm sớm nhất được thử lại (UTC). `null` = gửi ngay được. Dùng cho backoff **và** hoãn quiet hours / digest / rate limit |

> **Vì sao `next_attempt_at` cần cho cả trường hợp hoãn:** nếu để record hoãn ở `Pending` không mốc
> thời gian thì batch (order by `created_at`) sẽ bị chúng chiếm chỗ, **chặn record mới phía sau**.

**Index mới:** `ix_notifications_dispatch_queue` trên `(status, next_attempt_at, created_at)` — đúng
hình dạng câu truy vấn của dispatch worker.

**Không expose qua API:** cả 2 cột + `failure_reason` chỉ dùng nội bộ.

---

### `push_receipts` — bảng MỚI (migration `20260730084126_AddPushReceiptAndDeliveryStatus`)

Một `Notification` push tới người dùng có N thiết bị sẽ sinh **N bản ghi receipt**.

| Cột | Kiểu | Null | Ý nghĩa |
|---|---|---|---|
| `id` | `uuid` | Không | PK |
| `notification_id` | `uuid` | Không | Notification đã sinh ra message push này |
| `user_id` | `uuid` | Không | Nhân bản để đối soát không phải join sang `notifications` |
| `ticket_id` | `varchar(200)` | Không | Ticket id Expo trả về (`data[i].id`) — **UNIQUE**, cùng một ticket id không được đối soát hai lần |
| `device_token` | `varchar(500)` | Không | Token đã gửi tới — cần để tắt đúng thiết bị khi `DeviceNotRegistered` |
| `status` | `integer` | Không | `PushReceiptStatusEnum` (1=Pending, 2=Ok, 3=Error, 4=Expired) |
| `error_code` | `varchar(100)` | **Có** | Mã lỗi Expo |
| `error_message` | `varchar(1000)` | **Có** | Thông điệp lỗi đầy đủ |
| `checked_at` | `timestamptz` | **Có** | Lần đối soát gần nhất. `null` = chưa hỏi Expo lần nào |
| `check_attempt_count` | `integer` | Không (default `0`) | Số lần đã hỏi mà chưa có kết quả |
| + 5 cột `AuditableEntity` | | | `created_at`, `created_by`, `updated_at`, `is_deleted`, `deleted_at` |

**Index:** `ix_push_receipts_notification_id` · `ix_push_receipts_status_created (status, created_at)` ·
`ux_push_receipts_ticket_id` (**unique**).

---

### `notification_category_preferences` — bảng MỚI (migration `20260730091039_AddNotificationCategoryPreference`)

| Cột | Kiểu | Null | Default | Ý nghĩa |
|---|---|---|---|---|
| `id` | `uuid` | Không | — | PK |
| `user_id` | `uuid` | Không | — | Chủ sở hữu |
| `category` | `integer` | Không | — | `NotificationCategoryEnum` (1–6) |
| `push_enabled` | `boolean` | Không | `true` | |
| `email_enabled` | `boolean` | Không | `true` | |
| `sms_enabled` | `boolean` | Không | `false` | |
| `in_app_enabled` | `boolean` | Không | `true` | |
| + 5 cột `AuditableEntity` | | | | |

**Index:** `ux_notification_category_preferences_user_category` trên `(user_id, category)` — **unique**
⇒ mỗi user tối đa **6 record**.

---

### `notification_templates` + `account_read_models` (migration `20260730093220_AddTemplateVersioningAndAccountLocale`)

| Bảng | Cột | Kiểu | Null | Default | Ý nghĩa |
|---|---|---|---|---|---|
| `notification_templates` | `version` | `integer` | Không | `1` | **Sprint 6.3 NOTI3-12** — số phiên bản trong cùng bộ ba |
| `account_read_models` | `preferred_locale` | `varchar(16)` | **Có** | `null` | **Sprint 6.3 NOTI3-12** — locale BCP-47 ưa dùng của người nhận |

**Index đổi:**
- **DROP** `IX_notification_templates_type_channel_locale` (unique cũ trên bộ ba — chặn versioning).
- **ADD** `ux_notification_templates_active_per_key` unique trên `(type, channel, locale)`
  **có filter `is_active = true AND is_deleted = false`** ⇒ DB tự bảo đảm mỗi bộ ba chỉ có đúng 1 bản active.
- **ADD** `ux_notification_templates_type_channel_locale_version` unique trên `(type, channel, locale, version)`.

> `preferred_locale` **UserService chưa publish** ⇒ hiện luôn `null` và rơi về
> `Notification:Dispatch:DefaultLocale`. Cột có sẵn để khi UserService bổ sung thì chỉ cần map, không
> phải đổi migration.

---

## Prometheus metrics

Trước Sprint 6.3, **toàn service chỉ có đúng 1 metric** (`AuditOutboxPending`) — Sprint 6.2 vừa bật
tầng gửi mà không có cách nào biết nó đang hỏng.

| Metric | Kiểu | Labels | Ý nghĩa |
|---|---|---|---|
| `notification_sent_total` | Counter | `channel`, `type` | Đã giao thành công xuống channel. **Delivery-rate tách theo channel là metric số 1** — mỗi kênh có kiểu hỏng khác nhau nên KHÔNG được gộp |
| `notification_failed_total` | Counter | `channel`, `reason` | Thất bại **vĩnh viễn**. `reason` là nhóm ngắn: `channel_disabled`, `no_device_token`, `no_email`, `no_phone`, `no_channel_handler`, `empty_user_id`, `max_attempts_exceeded` |
| `notification_retry_total` | Counter | `channel` | Lỗi tạm thời, sẽ retry |
| `notification_deferred_total` | Counter | `channel`, `reason` | Hoãn có chủ đích: `quiet_hours` \| `digest` \| `rate_limited` |
| `notification_delivery_latency_seconds` | Histogram | `channel` | Độ trễ end-to-end `CreatedAt → SentAt`. Bucket `1, 5, 15, 30, 60, 300, 900, 3600` (kéo dài tới 1 giờ vì record có thể bị hoãn) |
| `notification_pending_total` | Gauge | — | Số record đang chờ giao (tới hạn). **Tín hiệu queue lag — đây là metric quyết định khi nào phải bỏ leader-election** |
| `notification_dlq_size` | Gauge | `queue` | Số message trong queue `*_error`. **Khoẻ mạnh = 0** |
| `notification_rate_limited_total` | Counter | `channel`, `reason` | Bị chặn bởi hạn mức. `reason` = `per_hour` \| `per_type` |
| `notification_fallback_total` | Counter | `from_channel`, `to_channel` | Bản bù sinh ra khi kênh chính không có receipt |
| `expo_push_receipt_total` | Counter | `status`, `error_code` | Receipt đã đối soát. `status` = `ok` \| `error` \| `expired` |
| `expo_push_token_deactivated_total` | Counter | — | Token bị tắt sau khi Expo báo không dùng được |

> ⚠️ **Cardinality:** tuyệt đối không nhét message lỗi thô vào label `reason` — mỗi giá trị khác nhau
> tạo một time-series mới.

**Dashboard:** `monitoring/grafana/dashboards/notification-ops.json` (bản Helm:
`deploy/helm/solar-battery/dashboards/notification-ops.json`).

**Alert rules** — nhóm `notification-delivery` trong `monitoring/prometheus/alert-rules.yml`
(label `domain: notification`):

| Alert | Điều kiện | Ý nghĩa |
|---|---|---|
| `NotificationFailureRateHigh` | tỷ lệ `failed / (sent + failed)` theo channel > 5% trong 5' | Kiểm tra provider của channel đó (Expo / Mailjet / SMS gateway) và cột `failure_reason` |
| `NotificationDlqNotEmpty` | `sum(notification_dlq_size) > 0` | Có message chết — trạng thái khoẻ mạnh là 0 |
| `NotificationQueueBacklog` | `notification_pending_total > 1000` | Tín hiệu chạm trần thông lượng ⇒ cân nhắc bỏ leader-election (xem [Giới hạn §1](#1-trần-thông-lượng--20-notificationgiây-r-45--chốt-nhánh-b-30072026)) |
| `NotificationDispatchStalled` | `rate(notification_sent_total[15m]) == 0` **và** `notification_pending_total > 0` | Worker chết / Redis leader key bị instance chết giữ / `Notification:Dispatch:Enabled=false` |
| `ExpoTokenDeactivationSpike` | tăng đột biến `expo_push_token_deactivated_total` | Nhiều token chết bất thường — nghi cấu hình FCM/APNs hỏng |

---

## Giới hạn đã biết

### 1. Trần thông lượng ≈ 20 notification/giây (R-45 — chốt nhánh B, 30/07/2026)

`NotificationDispatchBackgroundService` dùng **leader-election** ⇒ **chỉ MỘT instance xử lý** tại mỗi
thời điểm, dù chạy bao nhiêu replica.

```
tối đa ≈ BatchSize / PollIntervalSeconds = 100 / 5 = 20 notification/giây
```

Con số này thừa sức cho quy mô đồ án. Nó chỉ thành vấn đề khi một sự kiện fan-out ra hàng nghìn người
nhận cùng lúc — ví dụ sự cố toàn site khiến mọi khách hàng đều phải được báo.

**Dấu hiệu phải nâng cấp:** gauge `notification_pending_total` tăng đều mà không tự tiêu (alert
`NotificationQueueBacklog`).
**Cách nâng cấp:** bỏ leader-election, chia việc theo phân vùng (`Channel` hoặc `hash(UserId) % N`)
kết hợp `SELECT … FOR UPDATE SKIP LOCKED`. Ước tính ~1.5 ngày công — **hoãn tới khi metric cho thấy
queue lag tăng thật**, vì tối ưu trước khi có số liệu là đoán mò.

### 2. Mô hình dữ liệu feed vẫn là bản vá (NOTI3-01 nhánh A)

Lọc `Channel = InApp` chữa được triệu chứng (feed nhân bản), nhưng mô hình đúng chuẩn Knock/Courier là
**tách bảng** `Notification` (1 dòng feed) khỏi `NotificationDelivery` (1 bản ghi/channel). Thêm kênh
mới về sau vẫn đẻ row thừa. Nhánh B (~4 ngày) **hoãn sang sprint sau, không huỷ**.

⚠️ Hệ quả bắt buộc phải giữ (R-40): **mọi notification hướng user phải có ít nhất 1 row `InApp`** —
type nào chỉ ghi `Push` sẽ **biến mất hoàn toàn** khỏi feed sau khi bật lọc. Có test chặn
(`EveryConsumerWritesInAppTests`) bao mọi `NotificationTypeEnum` hướng user.

### 3. Một provider mỗi kênh (R-44)

Mailjet · Expo · gateway SMS Android tự dựng — **đều là single point of failure**, chấp nhận có chủ
đích vì provider thứ hai ngoài ngân sách đồ án. Đã tách sẵn `IEmailProvider` / `ISmsProvider` để cắm
provider thứ hai sau này không phải sửa business logic.

### 4. Không có suppression list email (NOTI3-03 đã huỷ 30/07/2026)

Task đã code hoàn chỉnh (entity + `EmailDbContext` + webhook Mailjet + guard + 2 endpoint + migration
+ 23 unit test, tất cả pass) rồi **gỡ bỏ toàn bộ**. Lý do: cái giá là biến EmailService từ **service
thuần tiêu thụ message** thành **service có database** (thêm `email_db`, migration,
`depends_on: postgres`, và 2 cơ chế token tự dựng vì EmailService không có tầng JWT), trong khi ở quy
mô đồ án (vài chục email test tới địa chỉ thật của nhóm) bảng suppression sẽ **rỗng vĩnh viễn**.

**Rủi ro chấp nhận:** gửi lặp vào địa chỉ chết không bị chặn ⇒ về lâu dài có thể ảnh hưởng reputation
domain. **Bù đắp:** NOTI3-15 (`List-Unsubscribe` một chạm) vẫn giữ — đó mới là thứ Gmail/Yahoo **bắt
buộc** từ 2024.

### 5. Chưa có endpoint tạo/sửa template qua REST

`/api/admin/notification-templates` chỉ có `list` / `preview` / `test-send` / `activate`. Tạo bản mới
vẫn phải qua SQL hoặc seeder.

---

## Changelog

### 2026-07-30 — Sprint 6.3: Notification production-hardening (`#701..#717`, 16/17 task)

**Endpoint mới (9):**
- `PATCH /api/notifications/{id}/opened` — NOTI3-14
- `GET` / `PUT /api/notification-preferences/matrix`, `GET /api/notification-preferences/categories` — NOTI3-04
- `GET` / `POST /api/admin/notification-templates`, `.../{id}/preview`, `.../{id}/test-send`, `.../{id}/activate` — NOTI3-12
- `GET` / `POST /api/notification-unsubscribe` (public, token HMAC) — NOTI3-15
- SignalR Hub `/hubs/notifications` — NOTI3-13

**Breaking cho FE:**
- `GET /api/notifications` **mặc định chỉ trả `Channel = InApp`** ⇒ mỗi sự kiện đúng 1 dòng (trước
  lặp 2–4 lần). Tham số mới `includeAllChannels`. **Gỡ mọi logic de-dup ở client.**
- `GET /api/notifications/unread-count` **chỉ đếm `InApp`** và loại cả `Opened` ⇒ badge giảm 2–4 lần,
  nay khớp đúng số dòng feed chưa đọc.
- `unreadOnly=true` nay loại cả `Status = Opened`.
- `NotificationStatusEnum` thêm **`Delivered = 5`**, **`Opened = 6`** — FE/Mobile phải mirror.
- Enum mới `NotificationCategoryEnum` (6 giá trị) + `PushReceiptStatusEnum` (4 giá trị).
- `NotificationAuditActionEnum` thêm `TemplateTestSent = 8`.

**Hành vi mới:**
- `PATCH .../read` và `.../opened` **lan trạng thái sang record anh em** cùng sự kiện (±1 phút) ⇒ đọc
  trong app xong không còn lãnh thêm push cho đúng việc đó.
- Đối soát receipt Expo (`push_receipts`) ⇒ `Sent` → `Delivered` thật; token chết tự tắt.
- Fallback push → SMS cho notification critical sau 30' không có receipt.
- Rate limit 20/giờ/user + 8/giờ/type (critical bypass); vượt trần **hoãn vào digest, không vứt**.
- Retention 90 ngày (xoá mềm, giữ critical vĩnh viễn, `Pending` không bao giờ bị dọn).
- Retry 3 lần + DLQ monitor ở tầng bus — **ảnh hưởng cả 8 service**.
- Dedup chuyển sang `SET NX EX` atomic.
- Template: seed đủ **32 type × mọi kênh** (trước 5/32), có `version` + rollback, locale `en-US` cho
  13 type hướng Customer.
- **XSS:** bỏ `NoEscape = true` ở `HandlebarsTemplateRenderer`, thay bằng `HtmlOnlyTextEncoder` —
  escape đúng 5 ký tự HTML, **giữ nguyên tiếng Việt** ở mọi kênh (encoder mặc định của Handlebars.NET
  mã hoá cả ký tự ngoài ASCII: "Nguyễn" → `Nguy&#7877;n`, đúng cho email HTML nhưng ra chuỗi rác ở
  push/SMS). Cần HTML thật thì dùng `{{{ba-ngoặc}}}` có kiểm soát.
- Template file đổi đuôi `.hbs` → `.html` (mở bằng trình duyệt/IDE xem trước được; nội dung vẫn là cú
  pháp Handlebars).
- 11 metric Prometheus + dashboard "Notification Ops" + 3 alert rule.
- `mem_limit` container 384m → **512m** (7 worker thường trú).

**Huỷ:** NOTI3-03 (suppression list email) — đã implement xong rồi gỡ bỏ hoàn toàn, xem
[Giới hạn đã biết §4](#4-không-có-suppression-list-email-noti3-03-đã-huỷ-30072026).

**Sửa lỗi phát hiện khi test E2E 30/07/2026:**
- `POST /api/notifications`: gỡ `[JsonIgnore]` khỏi `userId` — trước đó endpoint **luôn** tạo record
  `UserId = Guid.Empty` rồi bị worker đánh `Failed`, tức vô dụng đúng cho mục đích của chính nó.
  Kèm validation từ chối `Guid.Empty` ngay ở pipeline.
- `AdminNotificationTemplatesController` dùng `[Authorize(Roles = "Admin")]` chứ **không** phải
  `[Authorize(Policy = "AdminOnly")]` — policy đó là code chết, dùng sẽ **500 ở mọi request**.
- `test-send` lấy email từ claim JWT khi read-model không có (tài khoản admin seed thẳng vào `auth_db`
  không bao giờ xuất hiện trong `account_read_models`).
- `MassTransitExtensions`: thêm `cfg.UsePublishMessageScheduler()` — thiếu nó saga TicketService ném
  `PayloadNotFoundException` mỗi lần hẹn giờ (1662 message kẹt ở `AlertTicketSagaState_error`).

---

### 2026-07-29 — Sprint 6.2: Notification pipeline completion (`#672..#688`, 17 task)

**Vấn đề gốc:** `NotificationDispatcher` là **dead code (0 caller)** → toàn bộ tầng giao nhận
Push/Email/SMS **không bao giờ gửi** ở runtime; notification chỉ ghi DB `Status = Pending` rồi nằm im,
user chỉ thấy khi tự poll REST. Cộng nhiều **orphan event** (publish nhưng 0 consumer).

**THÊM:**
- `NotificationDispatchBackgroundService` (NOTI-01) — **mắt xích còn thiếu**; leader-election, batch,
  retry backoff, 2 cột DB mới `dispatch_attempt_count` / `next_attempt_at`.
- `SendNotificationEmailConsumer` ở **EmailService** (NOTI-02) + template `NotificationGeneric.html` —
  trước đó `SendNotificationEmailEvent` có 0 consumer, RabbitMQ **drop message không log**, email biến mất.
- `ChatEscalatedToAdminConsumer` (NOTI-03) — saga escalation chat chạy đúng, chỉ thiếu đầu nhận.
- 2 consumer cảnh báo bảo mật ở **EmailService** (NOTI-04): `SuspiciousLoginDetectedConsumer`,
  `RefreshTokenReuseDetectedConsumer` + 2 template. `RefreshTokenReuseDetectedEvent` thêm field `Email`.
- 6 event vòng đời ticket + 6 consumer (NOTI-07): `TicketStatusChangedEvent`, `TicketApprovedEvent`,
  `TicketRejectedEvent`, `TicketClosedEvent`, `TicketReopenedEvent`, `TicketRatingRequestedEvent`.
  ⇒ 2 enum chết `TicketStatusChanged(3)` / `TicketClosed(5)` **nay có producer thật**.
- `BatteryAnomalyWarningDetectedEvent` + `BatteryAnomalyWarningConsumer` (NOTI-08) — event **riêng**,
  chỉ NotificationService consume nên **không đẻ ticket**; dedup 60' ở publisher.
- `SmsFailedConsumer` (NOTI-11) — vòng phản hồi, record SMS hết kẹt ở `Sent`.
- Digest thật sự hoạt động (NOTI-12) + `NotificationDigestBackgroundService`.
- `INotificationAuditWriter` (NOTI-13) — 7 action audit **nay mới thật sự được ghi**; relay hết poll
  bảng rỗng vô ích.
- 2 template email thiếu (NOTI-09): `OtpPasswordReset.html`, `OtpEmailChange.html` — trước đó fallback
  về `OtpRegister.html` khiến user reset mật khẩu nhận email nội dung "đăng ký tài khoản".

**Enum mới (7)** — *số hiệu đã sửa 2026-07-31, xem cảnh báo ở bảng enum trên:*
`ChatEscalatedToAdmin = 27`, `TicketApproved = 28`, `TicketRejected = 29`,
`TicketReopened = 30`, `TicketRatingRequested = 31`, `BatteryAnomalyWarning = 32`,
`BatteryAnomalyInfo = 33`. (25 và 26 thuộc module Blog `GH-671`, không phải Sprint 6.2.)

**SỬA:**
- `TicketAssignedEvent` / `TicketResolvedEvent` thêm `CustomerId`; `TicketCreatedEvent` thêm
  `CustomerId` + `Priority`; `SlaWarningEvent` thêm `StaffId` (NOTI-05) ⇒ **Customer và Staff nay
  được notify** (trước bị bỏ với comment "deferred").
- `SlaBreachedConsumer` phân nhánh P1/P2/P3 (NOTI-06).
- `BatteryAnomalyDetectedConsumer` mở đủ 4 kênh (NOTI-08).
- `ChatCreatedConsumer` ghi thêm row `InApp` song song `Push` (NOTI-10).
- `TypeChannelMatrix` + `CriticalTypes` đưa ra config, bổ sung **14 type còn thiếu**; chốt 1 pattern
  template: **DB thắng, inline là fallback** (NOTI-14).
- `ExpoPushChannel` gửi **batch 100 message/request** thay vì 1 HTTP call mỗi token (NOTI-16).

**XOÁ (NOTI-15):** `SendPhoneOtpConsumer` stub ở EmailService + consumer legacy ở SmsService.

---

## Tham khảo

**Notifications:**
- Controller: `services/NotificationService/src/NotificationService.Api/Controllers/NotificationsController.cs`
- Query handlers: `.../Application/CQRS/Handler/Notification/GetNotificationsQueryHandler.cs`, `GetUnreadCountQueryHandler.cs`
- Command handlers: `.../Handler/Notification/{Create,MarkNotificationRead,MarkNotificationOpened,MarkAllNotificationsRead}CommandHandler.cs`
- DTO: `.../DTOs/Response/Notification/NotificationDto.cs`

**Device Tokens:** `.../Api/Controllers/DeviceTokensController.cs` · `.../Handler/DeviceToken/` ·
`.../Domain/Entities/DeviceToken.cs`

**Preferences:** `.../Api/Controllers/PreferencesController.cs` · `.../Handler/Preference/` ·
`.../DTOs/Response/Preference/NotificationCategoryPreferenceDto.cs` ·
`.../Domain/Entities/{NotificationPreference,NotificationCategoryPreference}.cs`

**Admin Templates:** `.../Api/Controllers/AdminNotificationTemplatesController.cs` ·
`.../Infrastructure/Persistence/Seeders/NotificationTemplateCatalog.cs`

**Unsubscribe:** `.../Api/Controllers/NotificationUnsubscribeController.cs` ·
`.../Application/Services/UnsubscribeTokenService.cs`

**Realtime:** `.../Infrastructure/Realtime/NotificationHub.cs` · `SignalRNotificationNotifier.cs`

**Dispatch:** `.../Infrastructure/Services/NotificationDispatcher.cs` ·
`.../Infrastructure/BackgroundJobs/` (7 worker) · `.../Application/Services/*Options.cs`

**Channels:** `.../Infrastructure/Channels/{InApp,ExpoPush,EmailBus,SmsBus}Channel.cs`

**Consumers:** `.../Application/Consumers/` (31 class) · `NotificationWriter.cs` · `NotificationDebounce.cs`

**Chung:**
- Domain enums: `.../NotificationService.Domain/Enums/` (gồm `NotificationCategoryMap.cs`)
- `PaginationResponse` / `PaginationRequest` (clamp `pageSize` ≤ 100): `shared/src/SharedContracts/Common/`
- Metrics: `shared/src/SharedInfrastructure/Metrics/AppMetrics.cs`
- Bus retry/DLQ: `shared/src/SharedInfrastructure/Bus/MassTransitExtensions.cs`
- Spec: `overall.md` §3.3–§3.6 (entity + routing matrix + giới hạn), §17 Sprint 6.2 / Sprint 6.3
  (§17.6.3.1–§17.6.3.5 gồm decision log 4 fork + lý do huỷ NOTI3-03)
- Review nguồn: `reviewnotification.md`
