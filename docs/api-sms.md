# API Documentation — SmsService

> **Base URL:**
> - Direct HTTP (dev): `http://localhost:5140` (xem `Properties/launchSettings.json`)
> - Direct HTTPS (dev): `https://localhost:7221`
> - Docker container nội bộ: `http://smsservice:8080` (ENV `ASPNETCORE_URLS=http://+:8080` trong `Dockerfile`)
> - Qua ApiGateway (dev): `http://localhost:4001` (route `/api/sms-gateway/*`, `/api/admin/sms-gateway/*`, `/hubs/sms-gateway`)
>
> Content-Type mặc định: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem phần [Cấu trúc Response chung](#cấu-trúc-response-chung)

**Connection string env var:** `ConnectionStrings__SmsDb` (PostgreSQL). Fallback đọc thêm `SmsDb`, `Sms_Db`, `SMS_DB` (xem `Program.cs:30-35`) — thiếu cả 4 → throw `InvalidOperationException` khi startup.

**CORS:** Policy `"AllowAll"` (xem `SharedInfrastructure/.../AddCORS.cs`) — `SetIsOriginAllowed(_ => true) + AllowAnyMethod + AllowAnyHeader + AllowCredentials`. Mọi origin (Flutter, web admin, mobile, Postman) gọi được. **Production NÊN siết** lại whitelist domain.

**Ops endpoints (không bọc CommonResponse, không cần auth):**

| Path | Method | Mục đích |
|---|---|---|
| `/` | GET | Smoke test — trả plaintext `"SMS Gateway Service is Running..."` |
| `/metrics` | GET | Prometheus scrape — request latency, status code distribution, custom metrics (outbox processed, inbox skipped duplicate…) |
| `/hubs/sms-gateway` | WS upgrade | SignalR Hub (xem [Nhóm 3](#nhóm-3--signalr-hub-hubssms-gateway)) |

**Pagination:**
- `GET /api/admin/sms-gateway/devices` **KHÔNG paginate** — trả toàn bộ list (filter `!IsDeleted`). Quy mô < 100 device thì OK; production scale lớn cần thêm pagination
- `GET /api/sms-gateway/messages/pending` có `limit` query (clamp `[1..20]`) — chỉ trả batch nhỏ để device gửi tuần tự
- **Không có endpoint admin list SMS messages** — admin truy vấn trực tiếp DB hoặc qua audit log

**CancellationToken:**
Mọi endpoint nhận `CancellationToken` từ `HttpContext.RequestAborted`. Khi client disconnect (vd Flutter mất mạng giữa POST `/messages/report`), handler có thể bị cancel trước khi `SaveChangesAsync()` → transaction rollback → device sẽ retry report (idempotent guarantee). FE phải implement retry-with-backoff.

---

## Mục đích & người sử dụng

SmsService là **SMS Forwarder Gateway** — không có modem GSM/SMS provider ở backend. Thay vào đó, backend nhận yêu cầu gửi SMS từ các service khác (qua RabbitMQ), queue vào DB, rồi push xuống **Flutter app `sms_fowarder`** chạy trên điện thoại Android có SIM thật. App đó nhận, gửi qua SIM, rồi báo kết quả ngược lại backend.

### Hai nhóm endpoint, hai nhóm người dùng khác nhau

| Nhóm | Base route | Người dùng | Auth scheme |
|---|---|---|---|
| **Gateway (device)** | `/api/sms-gateway/*` | Flutter app `sms_fowarder` cài trên điện thoại Android có SIM | `GatewayApiKey` — header `Authorization: Bearer <api-key-plaintext>` + `X-Device-Code: <code>` |
| **Admin** | `/api/admin/sms-gateway/*` | Admin của hệ thống Solar Battery (qua web portal hoặc Swagger) | JWT Bearer của AuthService — role `Admin` |
| **SignalR Hub** | `/hubs/sms-gateway` | Flutter app (cùng credential `GatewayApiKey`) | Query string `?deviceCode=<code>&access_token=<api-key>` |

### Internal channel (KHÔNG có REST endpoint)

| Loại | Tên | Hướng | Mô tả |
|---|---|---|---|
| Integration event | `SendSmsCommand` | **Inbound** vào SmsService | Service khác (AuthService OTP, BatteryService alert…) publish event này → SmsService consume qua RabbitMQ → tạo row trong `sms_messages` ở state Pending |
| Integration event | `SmsDeliveryReportEvent` | **Outbound** từ SmsService | Khi SMS gửi thành công, SmsService publish → service phát ban đầu nhận callback (vd AuthService đánh dấu `LastOtpSentAt` để rate-limit OTP) |
| Integration event | `SmsFailedEvent` | **Outbound** từ SmsService | Khi SMS đã exhaust retry, publish event (subscriber có thể alert admin) |

→ Service khác **không gọi REST endpoint** của SmsService để gửi SMS — luôn publish `SendSmsCommand` qua RabbitMQ để giữ atomic + có Outbox pattern.

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
| `data` | `T?` | Dữ liệu trả về. Khi error: handler nào trả `string` → set `""` (empty string), handler nào trả `List<T>` → set `[]` (empty array), handler nào trả DTO → set placeholder DTO với Guid.Empty + chuỗi rỗng. **KHÔNG luôn là `null`** — FE phải parse defensive theo type. |
| `listErrors` | `Errors[] \| null` | Field-level validation errors — `null` khi không có lỗi field-level (`ErrorsListJsonConverter` tự convert empty list → JSON `null`) |

**Format quy ước:**
- **Property naming:** camelCase (vd `isSuccess`, `phoneNumber`, `deviceCode`) — System.Text.Json default
- **Timestamps:** ISO 8601 UTC với suffix `Z` (vd `"2026-06-15T08:30:42.123Z"`) — không có timezone offset (mọi field datetime đều là UTC theo convention `DateTime.UtcNow`)
- **Date-only:** ISO `yyyy-MM-dd` (vd `"sentTodayDate": "2026-06-15"`) — `DateOnly` serialize không kèm time
- **Guid:** lowercase với dashes (vd `"11111111-1111-1111-1111-111111111111"`)
- **Enum:** integer khi ở DB / payload internal, **string** khi serialize ra REST response (vd `"data": "Sent"` thay vì `"data": 2`) — handler dùng `.ToString()`

**Quy ước ListErrors vs Message:**
- **Field validation** (request body field thiếu/sai format) → `listErrors` chứa `{field, detail}`, `message = "Dữ liệu không hợp lệ."`
- **Business rule / system error** (vd duplicate DeviceCode, SMS đã Sent không cancel được) → `message` chứa mô tả cụ thể, `listErrors = null`

**ModelState binding error wrapping:**
Khi request body deserialize fail (vd thiếu field bắt buộc, JSON malformed, sai type), ASP.NET `[ApiController]` mặc định trả `ValidationProblemDetails`. Service override qua `AddCommonModelStateResponse` (`SharedInfrastructure`) để **luôn trả `CommonResponse`** với `statusCode: 400`, `listErrors: [{field: "<bindingPath>", detail: "<errorMessage>"}]`, `message: ""`. Client luôn parse được 1 schema duy nhất.

**Status-only response wrapping:**
Mọi response 401/403/404/405/415/429/502/503/504 không có body (vd từ auth handler, JWT middleware, rate limiter) đều bị middleware `UseCommonResponseStatusCodes` (`SharedInfrastructure`) bọc thành `CommonResponse` với message mặc định:

| Status | Default message |
|---|---|
| 401 | `"Chưa xác thực hoặc token không hợp lệ."` |
| 403 | `"Không có quyền truy cập tài nguyên này."` |
| 404 | `"Không tìm thấy tài nguyên hoặc endpoint yêu cầu."` |
| 405 | `"HTTP method không được hỗ trợ cho endpoint này."` |
| 415 | `"Content-Type không được hỗ trợ."` |
| 429 | `"Quá nhiều request, vui lòng thử lại sau."` |
| 502 | `"Upstream service không phản hồi hợp lệ."` |
| 503 | `"Service đang tạm ngừng phục vụ."` |
| 504 | `"Upstream service phản hồi quá thời gian cho phép."` |

→ Nếu handler tự return CommonResponse với custom message (vd `"Device không tồn tại."` cho 404 từ `HeartbeatCommandHandler`), middleware **KHÔNG override** (chỉ wrap khi response body rỗng). Client thấy message handler.

**HTTP status code convention:**
- `200` — Thành công (GET, PATCH idempotent)
- `201` — Tạo resource mới (POST create)
- `400` — Field validation fail → có `listErrors`
- `401` — Token thiếu/hết hạn (JWT admin) hoặc sai API key / sai header `X-Device-Code` (device)
- `403` — Có token nhưng không đủ permission (sai role admin, hoặc device đã revoke, hoặc device không phải owner của SMS)
- `404` — Resource không tồn tại / đã soft-delete
- `409` — Conflict với state hiện tại (vd duplicate DeviceCode, cancel SMS đã Sent)
- `429` — Vượt rate limit 60 req/phút/device (chỉ áp dụng cho `/api/sms-gateway/*`)
- `500` — Lỗi server ngoài dự kiến

---

## Enums

### `SmsStatus`

State machine cho 1 SMS trong gateway.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 0 | Mới queue, chờ device claim |
| `Sending` | 1 | Đã có device claim, đang gửi qua SIM (hoặc đang chờ device báo report) |
| `Sent` | 2 | Device đã báo gửi thành công qua SIM |
| `Failed` | 3 | Đã exhaust retry (`RetryCount >= MaxRetryCount` mặc định 3) — final state |
| `Cancelled` | 4 | Admin huỷ thủ công trước khi gửi |

**State transitions hợp lệ:**
```
Pending  → Sending     (device claim qua GET /messages/pending)
Sending  → Sent        (device report Status="Sent")
Sending  → Pending     (device report Status="Failed", còn retry → quay về Pending, bump RetryCount)
Sending  → Failed      (device report Status="Failed", exhaust retry)
Sending  → Pending     (StaleSmsReaperBackgroundService revert sau 5 phút nếu device chưa report)
Pending  → Cancelled   (admin POST /messages/{id}/cancel)
Sending  → Cancelled   (admin cancel + push BatchRevoked qua SignalR tới device đã claim)
Sent     → (final, không transition; sau 24h Redactor xóa cột message)
Failed   → (final)
Cancelled → (final)
```

### `SmsAuditEvent`

Loại sự kiện ghi vào append-only log `sms_audit_logs`. Mỗi transition state đẻ ra 1 row audit.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Queued` | 0 | SMS vừa được queue (qua `SendSmsCommand`) |
| `Picked` | 1 | Device đã claim SMS (Pending → Sending) |
| `Sent` | 2 | Device báo gửi thành công |
| `Failed` | 3 | Device báo Failed lần cuối (state Failed final) |
| `Retry` | 4 | Device báo Failed nhưng còn retry → SMS quay về Pending |
| `Cancelled` | 5 | Admin huỷ |
| `Reaped` | 6 | StaleSmsReaper revert state Sending → Pending sau 5 phút |
| `Redacted` | 7 | Redactor xóa cột `message` sau 24h (state Sent) |

---

## DTOs

### `PendingSmsDto`

Trả về cho Flutter app khi claim batch SMS Pending.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | Guid của SMS — Flutter dùng làm `smsId` khi gọi `POST /messages/report` |
| `phoneNumber` | `string` | Không | Số điện thoại đích, đã normalize E.164 VN (vd `+84901234567`) |
| `message` | `string` | Không | Nội dung SMS plaintext để app đẩy qua SIM |

### `GatewayDeviceDto`

Trả về cho admin khi list devices. **KHÔNG bao giờ chứa `apiKeyHash`** hay plaintext (security).

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | Guid của device record |
| `deviceName` | `string` | Không | Tên hiển thị admin đặt khi tạo |
| `deviceCode` | `string` | Không | Định danh device — dùng trong header `X-Device-Code` |
| `isActive` | `bool` | Không | `false` nếu admin đã revoke |
| `revokedAt` | `DateTime?` | Có | Thời điểm revoke (null nếu chưa revoke) |
| `dailyLimit` | `int` | Không | Số SMS tối đa device được gửi mỗi ngày |
| `sentToday` | `int` | Không | Counter SMS đã gửi hôm nay (reset 00:00 UTC) |
| `sentTodayDate` | `DateOnly?` | Có | Ngày UTC của counter — null nếu chưa gửi SMS nào |
| `lastSeenAt` | `DateTime?` | Có | Thời điểm device cuối gọi `POST /heartbeat` |
| `lastSeenIp` | `string?` | Có | IP của device khi heartbeat (lưu để forensic) |
| `createdAt` | `DateTime` | Không | Thời điểm admin tạo device |

### `CreateGatewayDeviceResponseDto`

Trả về **1 LẦN DUY NHẤT** khi tạo device. Request `GET /devices` sau này chỉ trả hash.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | Guid của device record |
| `deviceCode` | `string` | Không | DeviceCode admin đã set khi tạo |
| `apiKey` | `string` | Không | API key plaintext (base64 của 32 byte random — 256-bit entropy). **Copy NGAY** — admin không xem lại được; nếu mất phải revoke + tạo device mới |

### Audit fields (background)

Mọi entity (`SmsMessage`, `SmsGatewayDevice`, `OutboxMessage`) đều extend `AuditableEntity` — các field sau auto-set bởi `AuditableEntityInterceptor` ở `SaveChangesAsync()`:

| Field | Type | Khi nào set |
|---|---|---|
| `Id` | `Guid` | Handler tự gán `Guid.NewGuid()` khi tạo entity |
| `CreatedAt` | `DateTime` (UTC) | Auto khi `EntityState.Added` |
| `CreatedBy` | `string?` | Auto từ `ICurrentUserService` (null nếu request không auth, vd consumer) |
| `UpdatedAt` | `DateTime?` (UTC) | Auto khi `EntityState.Modified` |
| `UpdatedBy` | `string?` | Auto từ `ICurrentUserService` |
| `IsDeleted` | `bool` | Default `false`; interceptor convert `Remove()` → set `true` (soft delete) |
| `DeletedAt` | `DateTime?` (UTC) | Auto khi interceptor convert soft delete |

→ Handler **không cần** set thủ công `CreatedAt`/`UpdatedAt`. Nhưng `SmsAuditLog` extend `BaseEntity` (không phải `AuditableEntity`) nên handler PHẢI set `CreatedAt = now` thủ công (xem code các handler).

→ `GatewayDeviceDto.createdAt` đến từ `AuditableEntity.CreatedAt`. `SmsGatewayDevice` không có `UpdatedAt` trong DTO vì admin không quan tâm.

---

## Integration Events (RabbitMQ — không phải REST)

### `SendSmsCommand` (inbound)

Service khác publish event này khi muốn gửi SMS. SmsService consume qua `SendSmsCommandConsumer` → handler `QueueSmsCommandHandler` validate + insert row Pending + push SignalR `NewPendingSms`.

**Routing key (MassTransit):** `urn:message:SharedContracts.Events:SendSmsCommand`

**Payload fields:**

| Field | Type | Bắt buộc | Validation (handler enforce) | Mô tả |
|---|---|---|---|---|
| `phoneNumber` | `string` | Bắt buộc | Phải normalize được về E.164 VN qua `PhoneNumberNormalizer.NormalizeVn` — xem bảng format chấp nhận bên dưới. Sai format → handler trả `400` (consumer log warning, message KHÔNG queue). | Số điện thoại đích |
| `message` | `string` | Bắt buộc | Không rỗng, max **1600 ký tự** (handler hằng số `MaxMessageLength`) | Nội dung SMS |
| `sourceService` | `string` | Bắt buộc | — | "auth" / "battery" / "ticket" / "notification" — audit + per-source filter |
| `correlationId` | `Guid` | Bắt buộc | — | ID để service phát track end-to-end (vd `OtpRequest.Id`) — sẽ echo trong `SmsDeliveryReportEvent` |
| `category` | `string?` | Tuỳ chọn | — | Phân loại nội bộ ("otp" / "alert" / "info") |
| `targetDeviceCode` | `string?` | Tuỳ chọn | — | Nếu set: chỉ device có deviceCode khớp mới claim được SMS này. Null = broadcast tới mọi device (đua claim) |

**Format chấp nhận cho `phoneNumber`** (test cases từ `PhoneNumberNormalizerTests.cs`):

| Input | Output sau normalize | Ghi chú |
|---|---|---|
| `0901234567` | `+84901234567` | Số mobile VN bắt đầu 0 |
| `0 901 234 567` | `+84901234567` | Space giữa các nhóm |
| `(090) 123-4567` | `+84901234567` | Dấu ngoặc + dash |
| `090.123.4567` | `+84901234567` | Dot phân nhóm |
| `84901234567` | `+84901234567` | Đã có country code, thiếu `+` |
| `+84901234567` | `+84901234567` | E.164 hoàn chỉnh — pass thẳng |
| `0281234567` | `+84281234567` | Landline 0xx vẫn được accept |
| `""` / `null` / `"   "` | `null` | Reject |
| `"123"` (< 9 số) | `null` | Quá ngắn cho E.164 |
| `"12345678901234567"` (> 15 số) | `null` | Quá dài cho E.164 |
| `"abcdefghij"` / `"0123abc456"` | `null` | Chứa ký tự không phải số |

→ Quy trình: cleanup regex `[\s\-\(\)\.]` (xóa space/dash/paren/dot) → nếu `0xxxxxxxxx` (10 số) → prepend `+84` → nếu `84xxxxxxxxx` (11 số) → prepend `+` → validate cuối qua regex `^\+?[0-9]{9,15}$`.

→ Publisher service **không cần tự normalize** — handler tự xử lý. Nhưng nên validate input ở UI/form trước khi publish event để tránh waste round-trip.

**Idempotency (Inbox pattern):** Consumer wrap mỗi message qua `ProcessOnceAsync(IInboxStore, consumerName, action)`. Redis key format: `inbox:{consumerName}:{messageId:N}` — TTL default **7 ngày** (`InboxOptions.TtlDays`).

**MessageId resolution** (`IdempotentConsumerExtensions.cs:21`):
- Nếu message implement `IntegrationEvent` → dùng `evt.Id` (Guid) — **chuẩn** cho mọi event qua `SharedContracts.Events`
- Fallback: `context.MessageId` (MassTransit default từ envelope) hoặc `Guid.NewGuid()` (mất idempotency!)

⚠️ **`IntegrationEvent.Id` gotcha:** Base class set `Id = Guid.NewGuid()` trong constructor (xem `SharedContracts/Events/Root/IntegrationEvent.cs`). Mỗi lần `new SendSmsCommand(...)` sẽ sinh Id MỚI → nếu publisher tạo instance mới cho mỗi retry → **MẤT dedup**.

**Cách publish đúng** (publisher phải đảm bảo cùng `Id` cho mọi retry):
1. **Outbox pattern (khuyến nghị):** Insert row `outbox_messages` cùng transaction với business data. `OutboxRelayBackgroundService` deserialize + publish lại cùng instance đã serialize → cùng `Id`
2. **Cache event instance:** Lưu reference event trong DB/Redis với business key, retry chỉ re-publish cùng object đã tạo
3. **Stable Id explicit:** Sau khi `new SendSmsCommand(...)`, override `Id` qua reflection (anti-pattern, không khuyến khích)

→ **TUYỆT ĐỐI KHÔNG** publish trực tiếp `_bus.Publish(new SendSmsCommand(...))` trong handler không có Outbox — mỗi retry sẽ là `Id` mới → user nhận SMS nhiều lần.

**Redis fail behavior:** `InboxOptions.FailOpenWhenRedisDown` default `false` → Redis down → consumer **throw**, MassTransit retry. Nếu set `true`: bypass Inbox check (risk duplicate but no outage). Production khuyến nghị giữ `false` để fail-safe.

**Khi handler validate fail (phone sai format / message rỗng / message > 1600):** Consumer log warning + SMS không vào DB. Service phát ban đầu KHÔNG nhận callback (`SmsDeliveryReportEvent`/`SmsFailedEvent`) vì SMS chưa tồn tại. Nên validate ở phía publisher trước khi publish event.

### `SmsDeliveryReportEvent` (outbound)

SmsService publish khi 1 SMS chuyển state sang `Sent`.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `smsId` | `Guid` | Không | Guid của SMS |
| `correlationId` | `Guid` | Không | Echo từ `SendSmsCommand.correlationId` |
| `phoneNumber` | `string` | Không | Số điện thoại E.164 |
| `sourceService` | `string` | Không | Echo từ `SendSmsCommand.sourceService` |
| `sentAt` | `DateTime` | Không | UTC timestamp device báo Sent |
| `gatewayDeviceCode` | `string` | Không | DeviceCode của device đã gửi SMS này |

### `SmsFailedEvent` (outbound)

SmsService publish khi SMS exhaust retry → state Failed final. **Retry trung gian KHÔNG publish event** (chỉ event ở final failure).

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `smsId` | `Guid` | Không | Guid của SMS |
| `correlationId` | `Guid` | Không | Echo từ `SendSmsCommand.correlationId` |
| `phoneNumber` | `string` | Không | Số điện thoại E.164 |
| `sourceService` | `string` | Không | Echo từ `SendSmsCommand.sourceService` |
| `errorMessage` | `string?` | Có | Lý do failure cuối cùng (device report) |
| `failedAt` | `DateTime` | Không | UTC timestamp final failure |
| `finalFailure` | `bool` | Không | Luôn `true` ở event này (placeholder cho future use) |

### `SendPhoneOtpEvent` (inbound — DEPRECATED, backward-compat)

> ⚠️ **Sẽ xóa sau 1-2 sprint.** AuthService đã migrate sang publish `SendSmsCommand` trực tiếp (Phase 9). Consumer `SendPhoneOtpConsumer` giữ lại tạm để hỗ trợ message tồn đọng trong queue cũ.

SmsService nhận event này (legacy contract) qua `SendPhoneOtpConsumer` → render template `"Ma OTP cua ban la {Otp}. Vui long khong chia se ma nay."` → forward sang `QueueSmsCommand` nội bộ với `SourceService="auth"`, `Category="otp"`.

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Bắt buộc | Dùng làm `CorrelationId` của SMS |
| `phoneNumber` | `string` | Bắt buộc | Số điện thoại đích |
| `otp` | `string` | Bắt buộc | Mã OTP 6 chữ số — render vào template |

**Service mới KHÔNG được publish event này.** Dùng `SendSmsCommand` để control nội dung message + `category` + `targetDeviceCode`.

---

## Nhóm 1 — Gateway (Flutter device)

Base route: `/api/sms-gateway`

**Authentication scheme `GatewayApiKey`:**

Auth handler chấp nhận credential qua **header HOẶC query string** (linh hoạt vì SignalR WS handshake không gửi custom header):

| Loại | Header (ưu tiên) | Query string (fallback) |
|---|---|---|
| API key | `Authorization: Bearer <api-key-plaintext>` | `?access_token=<api-key-plaintext>` |
| Device code | `X-Device-Code: <device-code>` | `?deviceCode=<device-code>` |

→ REST khuyến nghị dùng **header** (chuẩn HTTP), WS bắt buộc dùng **query** (browser hạn chế custom header trong WS upgrade). Cả 4 tổ hợp đều work — vd `GET /api/sms-gateway/heartbeat?access_token=...&deviceCode=...` cũng valid.

⚠️ **Cẩn thận** khi log access log / nginx access.log: query string chứa `access_token` plaintext sẽ vào log. Production nên cấu hình nginx redact query `access_token` hoặc dùng header thay cho REST.

**Authorization:** Auth handler (`GatewayApiKeyAuthenticationHandler`) filter điều kiện device tồn tại + `IsActive=true` + `!IsDeleted` ngay ở bước verify credential. → **Device không tồn tại / đã revoke / sai API key / thiếu `X-Device-Code` đều trả `401`**. Sau khi auth pass, handler nội bộ có thêm defense-in-depth check `IsActive` → trả `403` (chỉ xảy ra trong race condition device bị revoke giữa lúc request đang chạy).

**Claims set sau khi auth pass:**
- `device_code` — DeviceCode của device
- `device_id` — Guid của device
- `ClaimTypes.NameIdentifier` — Guid của device
- `ClaimTypes.Name` — DeviceName

**Rate limit:** **60 request/phút/device** — partition theo header `X-Device-Code`, fallback partition `"anon"` nếu request thiếu header. Vượt → HTTP 429 (auto-wrap thành `CommonResponse` qua `UseCommonResponseStatusCodes` middleware).

> ⚠️ **Middleware order**: `UseRateLimiter()` đặt **trước** `UseAuthentication()` (xem `Program.cs:154-155`). Hậu quả: request không có credential vẫn bị rate-limit (partition `"anon"`) → bad actor spam có thể làm hết quota của partition `anon` mà chưa cần API key. Mỗi device hợp lệ có partition riêng theo `X-Device-Code` nên không bị ảnh hưởng.

---

### `GET /api/sms-gateway/messages/pending`

**Mục đích:** Device claim 1 batch SMS Pending để gửi qua SIM. Chuyển state `Pending → Sending`.

**Auth:** `GatewayApiKey`

**Query params:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `limit` | `int` | Tuỳ chọn (default `5`) | Clamp `[1..20]` trong handler | Số SMS tối đa mỗi batch |

**Hoạt động:**
- `DeviceId` + `DeviceCode` lấy từ JWT-like claim của scheme `GatewayApiKey` (controller gán; KHÔNG bind từ body/query — bảo vệ bởi `[JsonIgnore] + [BindNever]`).
- Pick SMS thoả 1 trong 2 điều kiện:
  - `Status == Pending` AND (`TargetDeviceCode = null` HOẶC khớp device hiện tại), HOẶC
  - `Status == Sending` AND `PickedAt < now - 5 phút` (stale claim từ device khác đã crash)
- Order theo `CreatedAt ASC` (FIFO).
- Áp daily limit (`SmsGatewayDevice.DailyLimit`):
  - Nếu `SentToday >= DailyLimit` → trả `data: []` + `message: "Daily limit reached."`
  - Nếu còn quota: `take = min(limit, DailyLimit - SentToday)` — không bao giờ claim quá quota còn lại
- Concurrency token `xmin` (Postgres) bảo vệ: 2 device cùng claim 1 row → 1 thắng, 1 nhận `data: []` + `message: "Concurrent claim, retry next poll."`.
- Mỗi SMS claim thành công → append audit log `Picked`.

**Response thành công `200` (có data):**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "OK",
  "data": [
    {
      "id": "11111111-1111-1111-1111-111111111111",
      "phoneNumber": "+84901234567",
      "message": "Your OTP is 123456"
    }
  ],
  "listErrors": null
}
```

**Response thành công `200` (rỗng — daily limit):**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Daily limit reached.",
  "data": [],
  "listErrors": null
}
```

**Response thành công `200` (rỗng — concurrent claim):**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Concurrent claim, retry next poll.",
  "data": [],
  "listErrors": null
}
```

**Error responses:**

| Status | Trường hợp |
|---|---|
| `401` | Thiếu/sai API key, thiếu `X-Device-Code`, hoặc device đã bị revoke / không tồn tại |
| `403` | Device được auth nhưng giữa request bị revoke (race condition — defense-in-depth handler check). Message: `"Device không tồn tại hoặc đã bị thu hồi."` |
| `429` | Vượt rate limit 60 req/phút/device |

**Lưu ý:**
- SMS đã claim mà chưa report sau 5 phút sẽ bị `StaleSmsReaperBackgroundService` revert về Pending — device khác có thể pick.
- Chỉ tính vào daily limit (`SentToday`) sau khi device báo `Sent` qua `POST /messages/report` — claim không tính.

---

### `POST /api/sms-gateway/messages/report`

**Mục đích:** Device báo kết quả gửi 1 SMS (`Sent` hoặc `Failed`). **Idempotent** — có thể gọi nhiều lần an toàn.

**Auth:** `GatewayApiKey`

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `smsId` | `Guid` | Bắt buộc | Phải tồn tại trong DB | Guid của SMS đã claim |
| `status` | `string` | Bắt buộc | `"Sent"` hoặc `"Failed"` (case-insensitive) | Kết quả gửi |
| `errorMessage` | `string?` | Tuỳ chọn (chỉ khi `status="Failed"`) | — | Lý do thất bại (timeout, no signal, blocked by carrier…) |

**Hoạt động:**

State machine — chỉ accept khi SMS đang ở state `Sending` AND `GatewayDeviceCode` khớp device hiện tại. Mọi state khác → trả 200 với `message: "Report ignored"` (idempotent no-op).

**Khi `status = "Sent"`:**
- SMS → state `Sent`, `SentAt = now`
- Tăng `SmsGatewayDevice.SentToday` (áp daily limit)
- Publish `SmsDeliveryReportEvent` qua Outbox
- Sau 24h, `SmsMessageRedactorBackgroundService` xóa cột `message` (bảo mật)

**Khi `status = "Failed"`:**
- Nếu `RetryCount + 1 < MaxRetryCount` (default 3): SMS → state `Pending`, bump `RetryCount`, device khác có thể pick lại
- Nếu đã exhaust: SMS → state `Failed` final, publish `SmsFailedEvent`
- `errorMessage` được lưu vào DB

**Atomic guarantee:** Outbox event publish TRƯỚC `SaveChangesAsync()` → cùng transaction với business data (no dual-write).

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "OK",
  "data": "Sent",
  "listErrors": null
}
```

`data` là string đại diện cho `SmsStatus` enum hiện tại sau khi xử lý (`"Sent"` / `"Failed"` / `"Pending"` nếu Failed nhưng còn retry).

**Response idempotent no-op `200` (state đã không phải Sending):**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Report ignored: current status is Sent.",
  "data": "Sent",
  "listErrors": null
}
```

**Response concurrent xmin conflict `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Concurrent report; treated as duplicate.",
  "data": "Sending",
  "listErrors": null
}
```

**Error responses:**

| Status | Message | Trường hợp |
|---|---|---|
| `400` | `"Dữ liệu không hợp lệ."` + `listErrors: [{field:"Status", detail:"..."}]` | `status` không phải "Sent"/"Failed" (case-insensitive) |
| `401` | `"Chưa xác thực hoặc token không hợp lệ."` (wrap từ middleware) | Thiếu/sai API key, thiếu `X-Device-Code`, device không tồn tại hoặc đã revoke |
| `403` | `"Device này không giữ SMS đó."` | Device hiện tại không phải owner của SMS (`GatewayDeviceCode` không khớp — vd SMS đã bị reaper revert, device khác claim lại) |
| `404` | `"SMS không tồn tại."` | `smsId` không tồn tại trong DB hoặc đã soft-delete |
| `429` | `"Quá nhiều request, vui lòng thử lại sau."` (wrap từ middleware) | Vượt rate limit 60 req/phút/device |

---

### `POST /api/sms-gateway/heartbeat`

**Mục đích:** Device báo "alive" định kỳ — cập nhật `LastSeenAt` + `LastSeenIp`.

**Auth:** `GatewayApiKey`

**Request body:** Rỗng (IP lấy từ `HttpContext.Connection.RemoteIpAddress`)

**Tần suất khuyến nghị:** 60 giây/lần (chiếm ~1/60 budget rate limit).

**Hoạt động:**
- `DeviceId` lấy từ JWT-like claim (controller gán, `[JsonIgnore] + [BindNever]`)
- Update `LastSeenAt = now`, `LastSeenIp = remoteIp`
- Server-side dùng để: admin biết device nào còn online (Grafana alert nếu mất heartbeat > 10 phút), forensic IP

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "OK",
  "data": "pong",
  "listErrors": null
}
```

**Error responses:**

| Status | Message | Trường hợp |
|---|---|---|
| `401` | (auth 401 không có body) | Thiếu/sai API key, device đã revoke |
| `404` | `"Device không tồn tại."` | Device record bị hard-delete giữa lúc auth pass + handler run (race condition hiếm) |
| `429` | `"Quá nhiều request, vui lòng thử lại sau."` (wrap từ middleware) | Vượt rate limit 60 req/phút/device |

---

## Nhóm 2 — Admin

Base route: `/api/admin/sms-gateway`

**Authentication:** JWT Bearer của AuthService (`JwtBearerDefaults.AuthenticationScheme`)
**Authorization:** Role `Admin` trong JWT claim. Manager/Staff/Customer **KHÔNG được phép**.

**Workflow điển hình:**
1. Admin tạo device qua `POST /devices` → copy `apiKey` plaintext (chỉ trả 1 lần) → cấu hình vào Flutter app
2. Theo dõi danh sách qua `GET /devices` — biết device nào online, đã gửi bao nhiêu SMS hôm nay
3. Khi device thất lạc/compromise → `DELETE /devices/{id}` revoke ngay
4. Khi cần huỷ SMS trước khi device gửi (vd OTP nhầm) → `POST /messages/{id}/cancel`

---

### `POST /api/admin/sms-gateway/devices`

**Mục đích:** Tạo gateway device mới. Backend sinh API key plaintext 32 byte random base64, trả về **1 LẦN DUY NHẤT**.

**Auth:** JWT Bearer + role `Admin`

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `deviceName` | `string` | Bắt buộc | Không rỗng, max 64 ký tự | Tên hiển thị (vd "iPhone Văn phòng tầng 5") |
| `deviceCode` | `string` | Bắt buộc | Không rỗng, max 64 ký tự, **UNIQUE** trong DB | Định danh device — dùng trong header `X-Device-Code` |
| `dailyLimit` | `int` | Tuỳ chọn (default `100`) | Range `[1..10000]` | Số SMS tối đa device được gửi mỗi ngày |

**Hoạt động:**
- Backend sinh `apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))` (256-bit entropy)
- Hash qua BCrypt workFactor 11 → lưu cột `api_key_hash`
- Trả plaintext **1 lần duy nhất** trong response — `GET /devices` sau này không bao giờ trả plaintext

**Response thành công `201`:**
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Device created. API key plaintext shown ONCE — store it securely.",
  "data": {
    "id": "22222222-2222-2222-2222-222222222222",
    "deviceCode": "android-gateway-001",
    "apiKey": "Yk9HZk1wQzZ5N0Q4d0F2cWxQNGZHc..."
  },
  "listErrors": null
}
```

**Error responses:**

| Status | Message | Trường hợp |
|---|---|---|
| `400` | `"Dữ liệu không hợp lệ."` + `listErrors: [{field, detail}]` | `deviceName`/`deviceCode` rỗng hoặc > 64, `dailyLimit` ngoài `[1..10000]` |
| `401` | `"Chưa xác thực hoặc token không hợp lệ."` (wrap từ middleware) | Chưa đăng nhập / JWT hết hạn / sai signature |
| `403` | `"Không có quyền truy cập tài nguyên này."` (wrap từ middleware) | JWT hợp lệ nhưng không có role `Admin` |
| `409` | `"DeviceCode đã tồn tại."` | `deviceCode` đã tồn tại trong DB (lọc theo `!IsDeleted`) — business conflict, `listErrors: null` |

**Lưu ý bảo mật:**
- **QUAN TRỌNG:** Nếu admin mất API key plaintext → BUỘC phải revoke device cũ + tạo device mới (không có cách reset)
- Endpoint không log API key plaintext vào file (chỉ trả qua HTTPS response)
- Recommend dùng HSM/Vault để lưu apiKey production thay vì .env

---

### `GET /api/admin/sms-gateway/devices`

**Mục đích:** List toàn bộ gateway device — kèm trạng thái, counter daily, last seen. Dùng cho dashboard admin monitoring.

**Auth:** JWT Bearer + role `Admin`

**Query params:**

| Field | Type | Bắt buộc | Mặc định | Mô tả |
|---|---|---|---|---|
| `includeRevoked` | `bool` | Tuỳ chọn | `true` | `true` = include cả device đã revoke (xem lịch sử). `false` = chỉ list device còn active |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "OK",
  "data": [
    {
      "id": "22222222-2222-2222-2222-222222222222",
      "deviceName": "Flutter Sim Office",
      "deviceCode": "android-gateway-001",
      "isActive": true,
      "revokedAt": null,
      "dailyLimit": 100,
      "sentToday": 12,
      "sentTodayDate": "2026-06-15",
      "lastSeenAt": "2026-06-15T08:30:42Z",
      "lastSeenIp": "192.168.1.50",
      "createdAt": "2026-06-10T03:15:20Z"
    }
  ],
  "listErrors": null
}
```

Sắp xếp theo `CreatedAt DESC`. Lọc `!IsDeleted` luôn áp dụng. Response **KHÔNG bao giờ** chứa `apiKeyHash` hay plaintext.

**Error responses:**

| Status | Trường hợp |
|---|---|
| `401` | Chưa đăng nhập / JWT hết hạn |
| `403` | JWT không có role `Admin` |

---

### `DELETE /api/admin/sms-gateway/devices/{id}`

**Mục đích:** Thu hồi (revoke) 1 gateway device — set `IsActive=false`, **idempotent**.

**Auth:** JWT Bearer + role `Admin`

**Route params:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | Guid của device cần revoke. **Chỉ lấy từ route**, `[JsonIgnore]` chặn override qua body |

**Hoạt động:**
- Set `IsActive = false`, `RevokedAt = now`
- **KHÔNG xoá record** — giữ lại để audit
- Sau khi revoke, mọi request từ device đó → 403 (auth handler check `IsActive && !IsDeleted`)
- SMS đang ở state `Sending` mà device đã claim → tự revert về `Pending` sau 5 phút (qua `StaleSmsReaperBackgroundService`)
- **Idempotent:** gọi DELETE 2 lần trên cùng device đã revoke vẫn trả 200

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Device revoked.",
  "data": "android-gateway-001",
  "listErrors": null
}
```

`data` = `deviceCode` của device đã revoke.

**Error responses:**

| Status | Message | Trường hợp |
|---|---|---|
| `401` | `"Chưa xác thực hoặc token không hợp lệ."` (wrap từ middleware) | Chưa đăng nhập / JWT hết hạn / sai signature |
| `403` | `"Không có quyền truy cập tài nguyên này."` (wrap từ middleware) | JWT hợp lệ nhưng không có role `Admin` |
| `404` | `"Device không tồn tại."` | `id` không tồn tại hoặc đã soft-delete |

**Lưu ý:** Hiện **không có endpoint un-revoke**. Để khôi phục, phải tạo device mới + cấu hình apiKey mới vào Flutter app.

---

### `POST /api/admin/sms-gateway/messages/{id}/cancel`

**Mục đích:** Huỷ 1 SMS đang ở queue trước khi device gửi. Chỉ valid khi state còn `Pending` hoặc `Sending`.

**Auth:** JWT Bearer + role `Admin`

**Route params:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | Guid của SMS cần huỷ. **Chỉ lấy từ route**, `[JsonIgnore]` chặn override |

**Request body:** Rỗng

**Hoạt động:**

| State trước | Sau cancel | Behavior |
|---|---|---|
| `Pending` | `Cancelled` | OK, append audit log |
| `Sending` | `Cancelled` | OK + push `BatchRevoked` qua SignalR tới device đã claim (best-effort) |
| `Sent` | — | 409 — không thể recall SMS đã gửi qua SIM |
| `Failed` | — | 409 — đã exhaust retry, cancel vô nghĩa |
| `Cancelled` | — | 409 — đã cancel từ trước |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Cancelled.",
  "data": "Cancelled",
  "listErrors": null
}
```

`data` = string state hiện tại của SMS (`"Cancelled"`).

**Error responses:**

| Status | Message | Trường hợp |
|---|---|---|
| `401` | `"Chưa xác thực hoặc token không hợp lệ."` (wrap từ middleware) | Chưa đăng nhập / JWT hết hạn / sai signature |
| `403` | `"Không có quyền truy cập tài nguyên này."` (wrap từ middleware) | JWT hợp lệ nhưng không có role `Admin` |
| `404` | `"SMS không tồn tại."` | `id` không tồn tại hoặc đã soft-delete |
| `409` | `"SMS đang ở trạng thái {Status}, không thể huỷ."` | SMS đang ở state `Sent` / `Failed` / `Cancelled` (terminal). `{Status}` thay bằng tên enum thực tế. |

**Lưu ý:** SignalR `BatchRevoked` là best-effort — nếu device offline, nó sẽ KHÔNG nhận lại event này khi reconnect (no persistent buffer). Tuy nhiên state đã ghi `Cancelled` trong DB nên lần claim kế tiếp Flutter sẽ không thấy SMS này nữa.

---

## Nhóm 3 — SignalR Hub `/hubs/sms-gateway`

**Mục đích:** Push realtime tới Flutter app khi có SMS mới hoặc admin cancel SMS — giảm latency vs polling (< 1s thay vì poll 10s).

**Endpoint:** `/hubs/sms-gateway`
**Transport:** WebSockets (qua YARP reverse proxy, app gateway có `app.UseWebSockets()`)
**Auth:** Cùng scheme `GatewayApiKey`, nhưng truyền qua query string thay vì header (do trình duyệt/SignalR client không gửi custom header trong WS handshake):

```
wss://api.solarbattery.com/hubs/sms-gateway?deviceCode=android-001&access_token=<api-key-plaintext>
```

| Query param | Bắt buộc | Mô tả |
|---|---|---|
| `deviceCode` | Bắt buộc | DeviceCode (giống header `X-Device-Code` của REST) |
| `access_token` | Bắt buộc | API key plaintext (giống `Authorization: Bearer <key>` của REST) |

### Server → Client events

#### `NewPendingSms`

Push khi 1 SMS mới được queue (qua `SendSmsCommand` → consumer tạo row Pending → notifier push). Flutter app nhận → trigger claim ngay.

**Payload:**
```json
{
  "smsId": "11111111-1111-1111-1111-111111111111",
  "phoneNumber": "+84901234567",
  "ts": "2026-06-15T08:30:42.123+00:00"
}
```

| Field | Type | Mô tả |
|---|---|---|
| `smsId` | `Guid` | Guid SMS |
| `phoneNumber` | `string` | Số điện thoại E.164 |
| `ts` | `DateTimeOffset` | Server timestamp |

**Group routing (xem `SmsGatewayHub.cs`):**
- Group naming:
  - `gateway:all` — mọi device đã connect
  - `device:<deviceCode>` — chỉ 1 device (vd `device:android-gateway-001`)
- Khi `SendSmsCommand.TargetDeviceCode = null` → push tới group `gateway:all` (broadcast — mọi device đua claim)
- Khi `TargetDeviceCode` được set → push tới group `device:<deviceCode>` (chỉ device đó nhận)

#### `BatchRevoked`

Push khi admin cancel SMS đang ở state `Sending` — báo device remove khỏi local queue.

**Payload:**
```json
{
  "smsIds": ["11111111-1111-1111-1111-111111111111"],
  "ts": "2026-06-15T08:31:00.456+00:00"
}
```

| Field | Type | Mô tả |
|---|---|---|
| `smsIds` | `Guid[]` | Danh sách SMS đã bị cancel |
| `ts` | `DateTimeOffset` | Server timestamp |

### Client → Server methods

#### `Ping()` → returns `"pong"`

Healthcheck client-side — Flutter có thể invoke `connection.invoke("Ping")` để verify Hub còn alive (vd UI hiển thị chip "REALTIME").

**Return:** string `"pong"`.

Không có method nào khác — Hub chủ yếu làm push channel server→client.

### Connection lifecycle

- Khi device connect Hub thành công:
  - Auth handler `GatewayApiKeyAuthenticationHandler` verify `deviceCode` + `access_token` (query string)
  - Hub `OnConnectedAsync` đọc claim `device_code` + `device_id`. Nếu thiếu hoặc `device_id` không parse được Guid → `Context.Abort()` đóng connection ngay (race condition rare — auth đã pass nhưng claim missing)
  - Auto join 2 group: `device:<deviceCode>` + `gateway:all`
  - Hub auto cập nhật `LastSeenAt` + `LastSeenIp` của device record trong DB (giống `POST /heartbeat`) — Flutter app khi SignalR connect ổn định KHÔNG cần gọi `/heartbeat` REST liên tục
  - Log: `[SmsGateway] Connected <deviceCode> (<connectionId>)`
- Khi disconnect:
  - Tự leave 2 group khi connection close
  - Log: `[SmsGateway] Disconnected <deviceCode> (<connId>) reason=...`
- SignalR client tự reconnect khi mạng chập (Flutter dùng `withAutomaticReconnect()`)
- Nếu device offline khi event push → event **mất** (no persistent buffer). Fallback bằng polling `GET /messages/pending`
- Nếu auth fail (sai apiKey / sai deviceCode / device revoked) → handshake bị abort, client nhận lỗi 401

> ⚠️ **Hub KHÔNG bị rate-limit** (60 req/phút/device chỉ áp cho `SmsGatewayController` qua `[EnableRateLimiting("gateway")]`). Hub có cơ chế riêng: `KeepAliveInterval=15s` server ping, `ClientTimeoutInterval=60s` server kill connection nếu client im lặng. SignalR client invoke spam vẫn được accept — chỉ giới hạn ở mức TCP/WebSocket connection.

### Hub options (server-side)

| Option | Giá trị | Mục đích |
|---|---|---|
| `KeepAliveInterval` | 15 giây | Server ping client mỗi 15s để giữ kết nối WS sống |
| `ClientTimeoutInterval` | 60 giây | Server đánh dấu disconnect nếu không nhận message nào từ client trong 60s |
| `EnableDetailedErrors` | `true` (Development), `false` (Production) | Chi tiết exception trả về client |

---

## Database schema (Postgres `sms_db`)

4 bảng + 1 đặc trưng:

| Bảng | Entity | Mục đích | xmin? |
|---|---|---|---|
| `sms_messages` | `SmsMessage` | 1 row = 1 SMS (queue + state machine) | ✅ Concurrency token |
| `sms_gateway_devices` | `SmsGatewayDevice` | 1 row = 1 Flutter device đã onboard | ✅ Concurrency token |
| `sms_audit_logs` | `SmsAuditLog` | Append-only log (8 event type — xem `SmsAuditEvent`) | ❌ Insert-only |
| `outbox_messages` | `OutboxMessage` | Outbox pattern cho event publish | ❌ Update-only theo `Id` |

**Indexes quan trọng:**

| Index | Bảng | Mục đích |
|---|---|---|
| `ix_sms_messages_status_created_at` | `sms_messages` | Hỗ trợ claim query (`WHERE status=Pending ORDER BY created_at`) |
| `ix_sms_messages_phone_number` | `sms_messages` | Lookup theo số điện thoại |
| `ix_sms_messages_correlation_id` | `sms_messages` | Service phát track lại SMS theo correlationId |
| `ix_sms_messages_status_sent_at` | `sms_messages` | Hỗ trợ redactor scan SMS state Sent đã cũ |
| `ix_sms_messages_target_device_code_status_created_at` | `sms_messages` | Hỗ trợ claim với `TargetDeviceCode` filter |
| `ux_sms_gateway_devices_device_code` | `sms_gateway_devices` | **UNIQUE** — enforce DeviceCode không trùng |
| `ix_sms_audit_logs_sms_message_id` | `sms_audit_logs` | Query audit theo SMS |
| `ix_sms_audit_logs_sms_message_id_created_at` | `sms_audit_logs` | Query audit theo SMS + thời gian |
| `ix_outbox_messages_processed_at_occurred_at` | `outbox_messages` | Hỗ trợ relay poll (`WHERE processed_at IS NULL ORDER BY occurred_at`) |

**xmin (Postgres-specific):**
- 2 bảng `sms_messages` + `sms_gateway_devices` config `xmin` (system column) làm optimistic concurrency token (`HasColumnType("xid").IsConcurrencyToken()`)
- 2 device cùng claim 1 row `sms_messages` → 1 thắng, 1 nhận `DbUpdateConcurrencyException` → handler bắt → trả response `data: []`, `message: "Concurrent claim, retry next poll."`
- 2 request cùng device update `SentToday` đồng thời → tương tự
- **Không global query filter** cho `IsDeleted` — handler PHẢI thêm `.Where(x => !x.IsDeleted)` thủ công ở mọi query

**Soft delete:**
- Mọi `Remove()` trên entity extend `AuditableEntity` bị `AuditableEntityInterceptor` convert thành `IsDeleted=true` + `DeletedAt=UtcNow`
- Hiện chưa có endpoint admin để hard-delete hay restore device — chỉ revoke (`IsActive=false`)
- `SmsAuditLog` extend `BaseEntity` (không phải `AuditableEntity`) → không có soft delete

**Bảng `outbox_messages` chi tiết** (cho devops/debug):

| Cột | Type | Mục đích |
|---|---|---|
| `id` | `Guid` PK | Identifier (`AuditableEntity.Id`) |
| `event_type` | `string` | Full type name của event (vd `SharedContracts.Events.SmsDeliveryReportEvent`) |
| `payload` | `string` (JSON) | Serialized event payload — relay deserialize + publish |
| `occurred_at` | `DateTime` UTC | Khi event được create (handler set) |
| `processed_at` | `DateTime?` UTC | Set khi relay publish thành công ra RabbitMQ. `NULL` = chưa publish |
| `retry_count` | `int` | Số lần publish fail. Cap `MaxRetries=10` (`OutboxOptions`) — quá cap → bỏ qua (poison) |
| `last_error` | `string?` | Exception message lần fail gần nhất (debug) |
| `created_at` / `updated_at` / `is_deleted` / `deleted_at` | từ `AuditableEntity` | Audit chuẩn |

**Query debug thường dùng:**
```sql
-- Outbox còn pending publish (relay chưa pickup)
SELECT id, event_type, occurred_at, retry_count, last_error
FROM outbox_messages
WHERE processed_at IS NULL AND retry_count < 10
ORDER BY occurred_at LIMIT 50;

-- Poison messages (đã exhaust retry)
SELECT id, event_type, retry_count, last_error
FROM outbox_messages
WHERE processed_at IS NULL AND retry_count >= 10;

-- SMS distribution theo status
SELECT status, COUNT(*) FROM sms_messages WHERE NOT is_deleted GROUP BY status;

-- Audit trail của 1 SMS
SELECT event, device_code, detail, created_at
FROM sms_audit_logs WHERE sms_message_id = '...'
ORDER BY created_at;
```

---

## Prometheus metrics (cho Grafana dashboard / alert)

Endpoint: `GET /metrics` (không cần auth). Ngoài metric default của `prometheus-net` (HTTP request duration, status code distribution, GC, thread pool), SmsService expose **6 custom metric** từ `SharedInfrastructure.Metrics.AppMetrics`:

### Outbox (publisher-side)

| Metric | Type | Labels | Mục đích |
|---|---|---|---|
| `outbox_messages_processed_total` | Counter | `event_type` (vd `SmsDeliveryReportEvent`) | Đếm event publish thành công ra RabbitMQ |
| `outbox_messages_failures_total` | Counter | `reason` (`type_not_found` / `deserialize_null` / tên Exception type vd `RabbitMqConnectionException`) | Đếm publish fail — chia theo nguyên nhân để alert riêng |
| `outbox_messages_skipped_total` | Counter | — | Đếm poison messages (`RetryCount >= MaxRetries`) — bỏ qua vĩnh viễn |
| `outbox_messages_pending_count` | Gauge | — | **Current backlog** — đếm row `processed_at IS NULL` tại mỗi tick. Alert nếu > 100 (relay không kịp / RabbitMQ down) |

### Inbox (consumer-side)

| Metric | Type | Labels | Mục đích |
|---|---|---|---|
| `inbox_messages_processed_total` | Counter | `consumer` (`SendSmsCommandConsumer` / `SendPhoneOtpConsumer`) | Đếm event được consume lần đầu (tạo SMS row) |
| `inbox_messages_skipped_duplicate_total` | Counter | `consumer` | Đếm duplicate skip — MassTransit retry hoặc service publisher gửi cùng `MessageId` 2 lần |

### Suggest alert rules (PromQL)

```promql
# RabbitMQ down — outbox không publish được
rate(outbox_messages_failures_total[5m]) > 0.5

# Outbox backlog tăng — alert urgent
outbox_messages_pending_count > 100

# Poison messages tích lũy
increase(outbox_messages_skipped_total[1h]) > 0

# Duplicate ratio cao bất thường (publisher có bug?)
rate(inbox_messages_skipped_duplicate_total[5m])
  / rate(inbox_messages_processed_total[5m] + inbox_messages_skipped_duplicate_total[5m]) > 0.3
```

### Suggest dashboard panels

1. **SMS throughput** — `rate(inbox_messages_processed_total{consumer="SendSmsCommandConsumer"}[1m])` × 60 = SMS/min queue rate
2. **Delivery success rate** — `rate(outbox_messages_processed_total{event_type="SmsDeliveryReportEvent"}[5m])` / SMS throughput
3. **Outbox health** — line chart `outbox_messages_pending_count` + bar `outbox_messages_failures_total` theo reason
4. **HTTP latency** — sẵn từ `prometheus-net`: `histogram_quantile(0.95, http_request_duration_seconds_bucket{controller="SmsGatewayController"})`

---

## Background services (worker chạy trong SmsService)

3 hosted services chạy nền liên tục:

| Service | Tick interval | Threshold | Mục đích |
|---|---|---|---|
| `OutboxRelayBackgroundService` | `OutboxOptions.PollIntervalSeconds` (default **2 giây**) | `BatchSize=50` rows/tick, `MaxRetries=10` cap | Poll bảng `outbox_messages` → publish event ra RabbitMQ → mark `ProcessedAt`. Atomic guarantee cho `SmsDeliveryReportEvent`, `SmsFailedEvent`. Sau khi `RetryCount >= MaxRetries`: row bị bỏ qua (poison message), log warning |
| `StaleSmsReaperBackgroundService` | **1 phút** (hardcode) | **5 phút** stale threshold | Quét SMS state `Sending` với `PickedAt < now - 5 phút` → revert về `Pending` (gọi `ReapStaleClaim`), append audit `Reaped`. KHÔNG bump `RetryCount` (không tính là attempt thất bại từ device) |
| `SmsMessageRedactorBackgroundService` | **15 phút** (hardcode) | **24 giờ** retain | Quét SMS state `Sent` với `SentAt < now - 24h` → set `Message = null`, `RedactedAt = now`, append audit `Redacted`. Bảo mật nội dung sau gửi |

**Config đường dẫn** (đọc từ `appsettings.json` / env var):
```jsonc
{
  "Sms": {
    "DefaultDailyLimit": 100   // SmsOptions.DefaultDailyLimit — fallback khi admin tạo device không truyền DailyLimit
  },
  "Outbox": {
    "BatchSize": 50,
    "PollIntervalSeconds": 2,
    "MaxRetries": 10
  }
}
```

User/admin không tương tác trực tiếp các service này — chúng tự chạy ngầm.

---

## Lưu ý quan trọng

### Bảo mật

- **API key plaintext chỉ hiển thị 1 lần** khi `POST /devices` — admin phải copy ngay. Mất → revoke + tạo device mới
- BCrypt workFactor 11 để hash key (verify ~100ms — chậm cố ý chống brute force)
- `DeviceId`/`DeviceCode` trong command body luôn có `[JsonIgnore] + [BindNever]` — chống device A spoof identity device B
- Route params (`{id:guid}` cho cancel/revoke) cũng `[JsonIgnore]` — chống override qua body

### Thứ tự khi tích hợp service mới muốn gửi SMS

1. Service publish `SendSmsCommand` qua RabbitMQ (**KHÔNG** gọi REST endpoint SmsService)
2. SmsService consume + queue row Pending + push SignalR
3. Flutter app claim + gửi qua SIM + report
4. SmsService publish `SmsDeliveryReportEvent` qua Outbox
5. Service phát đăng ký `IConsumer<SmsDeliveryReportEvent>` để nhận callback (vd update `LastOtpSentAt`)

### Cross-reference

- Mã nguồn handler: `services/SmsService/src/SmsService.Application/CQRS/Handler/`
- Integration events: `shared/src/SharedContracts/Events/SendSmsCommand.cs`, `SmsDeliveryReportEvent.cs`, `SmsFailedEvent.cs`
- Auth handler: `services/SmsService/src/SmsService.Infrastructure/Security/GatewayApiKeyAuthenticationHandler.cs`
- SignalR Hub: `services/SmsService/src/SmsService.Infrastructure/Realtime/SmsGatewayHub.cs`
- DbContext + EF Configurations: `services/SmsService/src/SmsService.Infrastructure/Persistence/`
- Background services: `services/SmsService/src/SmsService.Infrastructure/BackgroundJobs/`
- Program.cs (DI + middleware order): `services/SmsService/src/SmsService.Api/Program.cs`
- Swagger setup (2 auth scheme): `services/SmsService/src/SmsService.Api/Swagger/SmsGatewaySwaggerExtensions.cs`
- Test guide: xem hướng dẫn test SmsService trong conversation log hoặc `services/SmsService/README.md`
- Spec đầy đủ: `overall.md` Phần IX §68 (3428 dòng)
- Sprint SMS task list: `overall.md` Sprint SMS section

### Liên hệ docs khác

- `docs/api-auth.md` — JWT issue + role claim (Admin endpoints SmsService dùng JWT này)
- `docs/api-notification.md` — service đăng ký `IConsumer<SmsDeliveryReportEvent>` để gửi email follow-up
- `docs/api-battery.md` — service publish `SendSmsCommand` cho alert pin (planned)
- `services/SmsService/docs/flutter-datasource-patch.md` — patch Flutter `sms_fowarder` để dùng các endpoint Sprint SMS
