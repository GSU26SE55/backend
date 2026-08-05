# API Documentation — AuditAggregatorService

> Base URL: `http://localhost:{port}` (qua gateway: `http://localhost:4001`)
> Prefix nhóm endpoint admin: `/api/admin/audit`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`
> **ID fields:** Mọi `id` trong response là `string` (UUID `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Entity C# dùng `Guid`, serialize thành `string` trong JSON.

## Mục đích service

`AuditAggregatorService` là **read-store hợp nhất (materialized view)** gom audit event của TOÀN hệ thống (Auth/Battery/Ticket/File/Notification/Sms…) về 1 nơi (`audit_aggregate`), phục vụ **điều tra forensic, truy vết bảo mật, compliance, GDPR**. Service consume `AuditCreatedEventV1` từ RabbitMQ và expose REST API tra cứu chỉ cho **Admin**.

> **KHÔNG phải nguồn chân lý** — bản gốc nằm ở bảng append-only của từng service; read-store replay lại được từ `{service}_audit_outbox` (xem `POST /api/admin/audit/replay`).

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "",
  "data": { },
  "listErrors": null
}
```

- `listErrors` = `null` khi không có lỗi field-level. Khi có lỗi field-level (vd `400`), `listErrors` là mảng `[{ "field": "...", "detail": "..." }]` và `message` để trống.
- Lỗi không-field (404/500…) ghi ở `message`, `listErrors = null`.

**Pagination response (`data` của `/search`):**
```json
{
  "items": [ /* AuditAggregateDto[] */ ],
  "totalItems": 1234,
  "pageNumber": 1,
  "pageSize": 50,
  "totalPages": 25,
  "hasNextPage": true,
  "hasPreviousPage": false
}
```

> `totalPages`, `hasNextPage`, `hasPreviousPage` là field **tính toán** (computed) — server tự suy từ `totalItems`/`pageNumber`/`pageSize`, FE không gửi lên.

---

## Auth & giới hạn chung (áp dụng MỌI endpoint `/api/admin/audit/*`)

- **Auth:** Bắt buộc, **chỉ role `Admin`** (`[Authorize(Roles = "Admin")]`). Role `SecurityOfficer` đã gộp vào `Admin` cho scope capstone.
- **Rate limit:** nhóm "audit" — **200 req/phút**. Vượt → `429`.
- **Status code chung:**
  - `401` — thiếu/sai/hết hạn token.
  - `403` — đã đăng nhập nhưng không phải role `Admin`.
  - `429` — vượt rate limit.
  - `500` — lỗi server (GlobalExceptionMiddleware).

---

## Enums / tập giá trị cố định

> Các trường audit dùng **string** (không phải int enum). Dưới đây là tập giá trị hợp lệ.
>
> **Validate (giải pháp A+E):** `severity` và `category` được đối chiếu **exact-match (phân biệt hoa-thường)** với danh sách canonical ở server. Sai value hoặc sai hoa-thường → **`400` + `listErrors`** kèm danh sách giá trị đúng (KHÔNG trả `200` rỗng âm thầm). `service` và `action` **không** validate ở server (tên service tự do / 100+ action) → FE render **dropdown/typeahead** từ chính các giá trị tài liệu này (cách A). FE nên dựng dropdown cho mọi field tập-đóng để admin chỉ chọn, không gõ tay.

### `Severity` (mức độ)

| Giá trị | Ý nghĩa |
|---|---|
| `Info` | Hành động bình thường, thành công (vd LoginSucceeded, BatteryCreated) |
| `Warning` | Bất thường nhẹ / thất bại không nghiêm trọng (vd validation fail, EmailDeferred) |
| `Critical` | Sự cố nghiêm trọng (vd SLA breach, anomaly Critical, account locked) — **giữ vĩnh viễn (không bị retention drop)** |
| `Security` | Sự kiện an ninh (vd permission grant, brute-force, GDPR redact) — **giữ vĩnh viễn** |

### `ActionCategory` (9 category cố định, cross-service)

| Giá trị | Ý nghĩa |
|---|---|
| `Authentication` | Đăng nhập/đăng xuất/OTP/2FA/refresh token |
| `Authorization` | Cấp/thu hồi quyền, role, permission |
| `AccountManagement` | Quản lý vòng đời account (create/lock/merge…) |
| `DataModification` | Tạo/sửa/xóa dữ liệu nghiệp vụ (battery/ticket…) |
| `DataAccess` | Truy cập/đọc/tải dữ liệu nhạy cảm (file download, audit export) |
| `Configuration` | Thay đổi cấu hình/threshold/rule |
| `Security` | Sự kiện an ninh/forensic/compliance |
| `Communication` | Gửi thông báo ra ngoài (email/push/sms) |
| `System` | Hành động hệ thống/tự động/AI |

### `TargetType` (loại đối tượng bị tác động)

`Account` · `Role` · `Permission` · `Session` · `RefreshToken` · `Battery` · `BatteryAsset` · `SensorReading` · `ThresholdConfig` · `Alert` · `Ticket` · `MaintenanceLog` · `Comment` · `Attachment` · `File` · `Email` · `Notification` · `Sms` · `Model` · `GatewayRoute` · `AuditRecord`

### `ActionCode` (mã hành động)

String PascalCase thì quá khứ (vd `LoginSucceeded`, `BatteryUpdated`, `StateTransitioned`). Danh sách đầy đủ theo từng service: xem [docs/audit/action-code-registry.md](audit/action-code-registry.md).

> **Cập nhật Sprint 6.2/6.3 — nguồn `NotificationService`:**
>
> - **Sprint 6.2 NOTI-13 (#684):** 7 action của NotificationService (`PushSent`, `PushFailed`,
>   `PushDelivered`, `PushOpened`, `InAppCreated`, `InAppRead`, `InAppDismissed`) **nay mới thật sự
>   được ghi**. Trước đó bảng `notification_audit_logs` + outbox + relay đã có sẵn nhưng **không dòng
>   code nào tạo record** ⇒ Audit Explorer lọc `service=NotificationService` luôn trả **rỗng**.
>   Từ sprint này, dữ liệu bắt đầu chảy về Aggregator — FE nên kỳ vọng thấy nhóm event mới.
> - **Sprint 6.3 NOTI3-12 (#712):** thêm action **`TemplateTestSent`** (category `Communication`,
>   severity **`Warning`**) — ghi mỗi lần Admin bấm "gửi thử" một template email.
>   `metadataJson` gồm `templateId`, `type`, `locale`, `version`, `quotaUsed`, `recipientSource`.
>
> Đặc thù khi lọc/hiển thị các event nguồn NotificationService:
> `targetType` luôn là `Notification`, `targetId` = id record notification; `actorAccountId` là
> **người NHẬN** notification (do hệ thống phát, không có người thao tác) chứ không phải người gây ra
> hành động — đừng hiển thị nhãn "người thực hiện" cho nhóm này. `actorRole` và `actorDisplay` luôn `null`.
> Kênh **Email/Sms không sinh audit** (nằm ngoài 7 action của `#AUDIT-34`).

> **Cập nhật Sprint Chat DoD (2026-07-31) — nguồn `TicketService` thêm 7 action:**
>
> `ChatCreated` · `ChatEdited` · `ChatDeleted` · `ChatPinned` · `ChatUnpinned` · `ChatReacted` ·
> `ChatMentioned` (giá trị enum 22–28). Trước đó module Chat **không ghi audit nào** — mọi thao tác
> trên kênh trao đổi Customer ↔ Staff/Manager đều không có vết forensic, trong khi đây là nơi dễ
> phát sinh tranh chấp nội dung nhất. `TicketService` nay có **28** action code.
>
> Cả 7 đều `severity = Info`, `category = DataModification`. Đặc thù khi hiển thị:
>
> - **`targetId` là ID TICKET, KHÔNG phải ID tin nhắn** (`targetDisplay` = mã ticket). ID tin nhắn
>   nằm trong `metadataJson.chatId`. Đừng render `targetId` với nhãn "tin nhắn".
> - **`metadataJson`** — `ChatCreated`: `chatId`, `isInternal`; `ChatReacted`: `chatId`,
>   `reactionType`; `ChatMentioned`: `chatId`, `mentionedUserIds[]`; bốn action còn lại chỉ có `chatId`.
> - **Một lần gửi tin có tag người sinh 2 bản ghi** (`ChatCreated` + `ChatMentioned`) cùng
>   `correlationId`. Nếu FE đếm "số tin nhắn" theo số bản ghi audit sẽ ra số lớn hơn thực tế —
>   phải đếm riêng `ChatCreated`.
>
> ⚠️ **4 action code của TicketService đã khai báo nhưng CHƯA CÓ handler nào ghi** (rà mã nguồn
> 2026-08-01): `AttachmentUploaded` · `AttachmentDeleted` · `ClosedByUser` · `FalseAlarmMarked`.
> Thực tế chỉ **24/28** mã xuất hiện trong dữ liệu; lọc theo 4 mã đó luôn trả `200` + rỗng.
> Dựng dropdown chọn action thì nên ẩn hoặc gắn nhãn "chưa có dữ liệu".

### `groupBy` (param của `/stats`)

| Giá trị | Ý nghĩa |
|---|---|
| `service` | Gộp theo service phát sinh |
| `action` | Gộp theo mã action |
| `severity` | Gộp theo mức độ (mặc định) |

### `format` (param của `/export`)

| Giá trị | Ý nghĩa |
|---|---|
| `csv` | Xuất file CSV (mặc định) |
| `json` | Xuất file JSON (mảng) |

---

## DTO: `AuditAggregateDto`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | Surrogate id của bản ghi read-store |
| `eventId` | `string` | Không | Idempotency key — định danh duy nhất của audit event |
| `serviceName` | `string` | Không | Service phát sinh (vd `AuthService`) |
| `actionCode` | `string` | Không | Mã hành động (vd `LoginSucceeded`) |
| `actionCategory` | `string` | Không | Category (xem enum `ActionCategory`) |
| `severity` | `string` | Không | Mức độ (xem enum `Severity`) |
| `targetType` | `string?` | Null nếu hành động không gắn đối tượng | Loại đối tượng bị tác động (xem `TargetType`) |
| `targetId` | `string?` | Null nếu không có target | ID đối tượng bị tác động |
| `targetDisplay` | `string?` | Null / `[REDACTED]` sau GDPR | Tên hiển thị đối tượng (PII) |
| `actorAccountId` | `string?` | Null nếu hành động hệ thống/ẩn danh | Account thực hiện hành động |
| `actorRole` | `string?` | Null nếu không xác định | Role của actor lúc thực hiện |
| `actorDisplay` | `string?` | Null / `[REDACTED]` sau GDPR | Tên hiển thị actor (PII) |
| `actorIp` | `string?` | Null / `[REDACTED]` sau GDPR | IP của actor (PII) |
| `isSuccess` | `bool` | Không | Hành động thành công hay thất bại |
| `errorCode` | `string?` | Null nếu thành công | Mã lỗi (khi `isSuccess=false`) |
| `reason` | `string?` | Null nếu không có | Lý do/ghi chú |
| `metadataJson` | `string?` | Null nếu không có | Payload bổ sung dạng JSON string |
| `correlationId` | `string?` | Null nếu không gắn luồng | Id luồng nghiệp vụ (trace xuyên service) |
| `causationId` | `string?` | Null nếu không có nhân-quả | Id event gây ra event này |
| `occurredAt` | `DateTime` | Không | Thời điểm hành động xảy ra (UTC) — partition key |
| `recordedAt` | `DateTime` | Không | Thời điểm ghi vào source DB (UTC) |
| `geoCountry` | `string?` | Null nếu không tra được geo | Mã quốc gia (MaxMind, 2 ký tự) |
| `geoCity` | `string?` | Null nếu không tra được geo | Thành phố (MaxMind) |

## DTO: `AuditStatsItemDto`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `key` | `string` | Không | Giá trị nhóm (tên service/action/severity tùy `groupBy`) |
| `count` | `long` | Không | Số lượng event thuộc nhóm |

---

## Nhóm 1 — Audit Explorer (Admin)

### `GET /api/admin/audit/search`

**Mục đích:** Tra cứu (tìm kiếm) audit event xuyên service có filter nâng cao + phân trang. Màn hình chính của Audit Explorer.

**Tác dụng:** Điều tra sự cố bảo mật, rà soát hoạt động bất thường (vd login fail tăng), tra audit của 1 account/đối tượng, audit định kỳ.

**Auth:** Admin.

**Query params (đều optional — bỏ trống = không lọc theo tiêu chí đó):**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `service` | `string?` | Không | Lọc theo service phát sinh (vd `AuthService`) |
| `action` | `string?` | Không | Lọc theo mã action (vd `LoginSucceeded`) |
| `category` | `string?` | Không | Lọc theo category (xem `ActionCategory`) |
| `severity` | `string?` | Không | Lọc theo mức độ (xem `Severity`) |
| `actorId` | `string?` (UUID) | Không | Lọc theo account thực hiện |
| `targetId` | `string?` (UUID) | Không | Lọc theo đối tượng bị tác động |
| `correlationId` | `string?` (UUID) | Không | Lọc theo luồng nghiệp vụ |
| `isSuccess` | `bool?` | Không | Lọc theo thành công/thất bại |
| `from` | `DateTime?` | Không | Lọc từ thời điểm `occurredAt` (UTC) |
| `to` | `DateTime?` | Không | Lọc đến thời điểm `occurredAt` (UTC) |
| `pageNumber` | `int` | Không (mặc định 1; ≤0 tự về 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 10, trần 100; ≤0 tự về 10) | Số item mỗi trang |

**Request:** `GET /api/admin/audit/search?service=AuthService&severity=Critical&from=2026-06-01T00:00:00Z&pageNumber=1&pageSize=50`

**Response thành công `200`:** `CommonResponse<PaginationResponse<AuditAggregateDto>>` (sắp xếp `occurredAt` giảm dần).

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "items": [
      {
        "id": "9b1f...",
        "eventId": "a2c4...",
        "serviceName": "AuthService",
        "actionCode": "LoginFailedInvalidCredentials",
        "actionCategory": "Authentication",
        "severity": "Warning",
        "targetType": "Account",
        "targetId": "f1e2...",
        "targetDisplay": "user@example.com",
        "actorAccountId": "f1e2...",
        "actorRole": "Customer",
        "actorDisplay": "Nguyen Van A",
        "actorIp": "1.2.3.4",
        "isSuccess": false,
        "errorCode": "INVALID_CREDENTIALS",
        "reason": null,
        "metadataJson": "{\"attempt\":3}",
        "correlationId": "c0rr...",
        "causationId": null,
        "occurredAt": "2026-06-20T08:00:00Z",
        "recordedAt": "2026-06-20T08:00:00Z",
        "geoCountry": "VN",
        "geoCity": "Ho Chi Minh City"
      }
    ],
    "totalItems": 1234,
    "pageNumber": 1,
    "pageSize": 50,
    "totalPages": 25,
    "hasNextPage": true,
    "hasPreviousPage": false
  },
  "listErrors": null
}
```

> Query mẫu truyền `pageSize=50` nên response phản hồi `50`; nếu không truyền, mặc định là `10`.

**Lỗi:**
- `400` — `severity` hoặc `category` không hợp lệ (sai value/sai hoa-thường): `listErrors = [{ "field": "severity", "detail": "Giá trị '...' không hợp lệ. Hợp lệ (phân biệt hoa-thường): Info, Warning, Critical, Security." }]`, `message` để trống.
- `401` / `403` / `429` — như Auth chung.

---

### `GET /api/admin/audit/{eventId}`

**Mục đích:** Lấy chi tiết đầy đủ 1 audit event theo `event_id`.

**Tác dụng:** Xem sâu 1 sự kiện (thường mở từ kết quả search), lấy `correlationId`/`causationId` để trace tiếp.

**Auth:** Admin.

**Path param:** `eventId` — UUID của audit event (lấy từ route).

**Response thành công `200`:** `CommonResponse<AuditAggregateDto>`.

**Lỗi thường gặp:**
- `404` — không tồn tại `event_id` trong read-store (hoặc đã bị retention drop). `message = "Không tìm thấy audit event."`, `listErrors = null`.
- `401` / `403`.

---

### `GET /api/admin/audit/correlation/{correlationId}`

**Mục đích:** Truy vết toàn bộ chuỗi sự kiện xuyên service theo `correlation_id`.

**Tác dụng:** Dựng lại 1 luồng nghiệp vụ end-to-end (vd: anomaly pin → tự tạo ticket → gửi notification đều cùng correlation), điều tra root-cause.

**Auth:** Admin.

**Path param:** `correlationId` — UUID luồng nghiệp vụ.

**Response thành công `200`:** `CommonResponse<List<AuditAggregateDto>>` (sắp xếp `occurredAt` **tăng dần**) — có thể **mảng rỗng** nếu không có.

**Lỗi:** `401` / `403`.

---

### `GET /api/admin/audit/account/{accountId}/timeline`

**Mục đích:** Dựng dòng thời gian hoạt động của 1 account trên toàn hệ thống (làm **actor** HOẶC **target**).

**Tác dụng:** Điều tra tài khoản nghi vấn, hỗ trợ khiếu nại user, audit truy cập GDPR.

**Auth:** Admin.

**Path param:** `accountId` — UUID account (lấy từ route).

**Query param:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `limit` | `int` | Không (mặc định 100, trần 500; ngoài khoảng tự về 100) | Số bản ghi tối đa |

**Response thành công `200`:** `CommonResponse<List<AuditAggregateDto>>` (mới nhất trước) — lọc `actorAccountId = accountId OR targetId = accountId`.

**Lỗi:** `401` / `403`.

---

### `GET /api/admin/audit/stats`

**Mục đích:** Thống kê đếm số event gộp theo nhóm (`service`|`action`|`severity`) — số liệu cho dashboard/biểu đồ.

**Tác dụng:** Vẽ biểu đồ phân bố, phát hiện tăng đột biến (login fail tăng → nghi brute-force), báo cáo định kỳ.

**Auth:** Admin.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `from` | `DateTime?` | Không | Mốc đầu (UTC) |
| `to` | `DateTime?` | Không | Mốc cuối (UTC) |
| `groupBy` | `string` | Không (mặc định `severity`) | Tiêu chí gộp: `service`/`action`/`severity` |

**Response thành công `200`:** `CommonResponse<List<AuditStatsItemDto>>` (sắp giảm dần theo `count`).

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": [
    { "key": "Info", "count": 9000 },
    { "key": "Warning", "count": 420 },
    { "key": "Critical", "count": 12 }
  ],
  "listErrors": null
}
```

**Lỗi:** `401` / `403`.

---

### `GET /api/admin/audit/export`

**Mục đích:** Xuất audit khớp filter ra **file** CSV/JSON theo kiểu streaming (lưu trữ/compliance/phân tích offline).

**Tác dụng:** Nộp hồ sơ audit/compliance, sao lưu trước retention, đưa sang Excel/BI.

**Auth:** Admin.

**Query params:** Giống `/search` (service, action, category, severity, actorId, targetId, correlationId, isSuccess, from, to — **KHÔNG phân trang**, xuất toàn bộ khớp filter) + thêm:

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `format` | `string` | Không (mặc định `csv`) | `csv` hoặc `json` |

**Response thành công `200`:** **File download** (KHÔNG bọc CommonResponse).
- Header: `Content-Type: text/csv` hoặc `application/json`; `Content-Disposition: attachment; filename=audit-export.csv|json`.
- CSV cột: `EventId,ServiceName,ActionCode,Category,Severity,ActorId,ActorDisplay,TargetId,IsSuccess,OccurredAt,GeoCountry`.

**Lỗi:**
- `400` — `severity`/`category` không hợp lệ (validate trước khi stream): trả **`CommonResponse` JSON** (`Content-Type: application/json`, camelCase, `listErrors[{field, detail}]`), KHÔNG phải file.
- `401` / `403` — như Auth chung.

---

### `POST /api/admin/audit/replay`

**Mục đích:** Yêu cầu replay (tái nạp) audit từ source-of-truth về read-store khi `audit_aggregate` hỏng/mất dữ liệu — **xử lý bất đồng bộ**.

**Tác dụng:** Khôi phục read-store sau sự cố. Dữ liệu gốc vẫn ở `{service}_audit_logs` nên re-publish lại được.

**Auth:** Admin.

**Query params (đều optional):**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `service` | `string?` | Không (null = tất cả) | Chỉ replay 1 service |
| `from` | `DateTime?` | Không | Giới hạn từ (UTC) |
| `to` | `DateTime?` | Không | Giới hạn đến (UTC) |

**Request body:** Không có.

**Response thành công `202 Accepted`:** `CommonResponse<object>` — `data` chứa `jobId`, `service`, `from`, `to`, `expectedResponders`, `status`.
`202` chỉ được trả **sau khi job đã ghi vào DB**; nếu ghi hỏng sẽ trả lỗi chứ không trả 202.

**Response `400`:** tham số sai — `service` không thuộc 6 service có audit, `from > to`, hoặc `from` ở tương lai. Khi đó **không** tạo job.

### `GET /api/admin/audit/replay/{jobId}`

**Mục đích:** xem tiến độ job replay.

| Trường | Ý nghĩa |
|---|---|
| `status` | `Requested` / `InProgress` / `Completed` / `CompletedWithErrors` |
| `respondedServices` / `pendingServices` | Đã / chưa phản hồi — dùng khi job treo |
| `republishedCount` | Tổng bản ghi audit đã phát lại |
| `truncated` | `true` ⇒ **dữ liệu chưa đầy đủ** (chạm trần 50.000/lần hoặc payload hỏng) |
| `error` | Lỗi gộp từ các service báo thất bại |

**`404`** nếu không có job.

> **Phạm vi capstone:** endpoint ghi nhận yêu cầu; re-publish per-service hoàn thiện khi onboard từng service.

**Lỗi:** `401` / `403`.

---

### `POST /api/admin/audit/redact`

**Mục đích:** GDPR — ẩn danh (redact) PII của 1 account trong read-store, **KHÔNG xóa dòng** (giữ toàn vẹn audit).

**Tác dụng:** Thực thi "quyền được lãng quên": thay `actorDisplay`/`targetDisplay`/`actorIp` của account thành `[REDACTED]`. Giữ `eventId`+`actionCode`+timestamp. Source tables KHÔNG bị redact (legal hold). Tự ghi meta-audit `AccountDataRedacted` (severity `Security`).

**Auth:** Admin.

**Query param:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `accountId` | `string` (UUID) | **Có** — khác `Guid.Empty` | Account cần ẩn danh PII |

**Request body:** Không có.

**Response thành công `200`:** `CommonResponse<object>` — `data = null`, `message` chứa số dòng bị ảnh hưởng.

**Lỗi thường gặp:**
- `400` — thiếu/không hợp lệ `accountId`. `listErrors = [{ "field": "accountId", "detail": "accountId là bắt buộc." }]`, `message` để trống.
- `401` / `403`.

---

## Nhóm 2 — Health probes (hạ tầng, KHÔNG auth)

> Dùng cho k8s liveness/readiness — không expose qua gateway, không cần token.

### `GET /live`
**Mục đích:** Liveness — process còn sống. **Response `200`:** `{ "status": "alive" }`.

### `GET /ready`
**Mục đích:** Readiness — sẵn sàng nhận traffic. **Response `200`:** `{ "status": "ready" }`.

### `GET /health`
**Mục đích:** Health tổng quát. **Response `200`:** `{ "status": "ok", "service": "AuditAggregatorService" }`.
