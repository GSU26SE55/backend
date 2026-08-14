# API Documentation — TicketService

## Active ticket lifecycle (GH-1176)

> **Canonical override — 2026-08-11:** This block supersedes all older lifecycle descriptions in this document. The current `TicketStatusEnum` values are `Open=1`, `Pending=2`, `InProgress=3`, `Request=4`, `ReAssign=5`, `Completed=6`, `Closed=7`, and `ClosedRejected=8`.

| Status | Value | Active meaning | SLA |
|---|---:|---|---|
| `Open` | 1 | Manager queue; not scheduled for work | Not running |
| `Pending` | 2 | Future scheduled work or held active work; inspect `pendingContext` (`Scheduled` / `Held`) | Not running; held cycle remains paused |
| `InProgress` | 3 | Primary Staff is actively working | Running only for `P3Normal`, `P2High`, or `P1Critical`; never for `Urgent` |
| `Request` | 4 | Staff escalation awaiting Manager decision | Paused |
| `ReAssign` | 5 | Manager must confirm or replace qualified Primary Staff and schedule work | Not running |
| `Completed` | 6 | Staff finished; Manager reviews result | Met/stopped |
| `Closed` | 7 | Manager-approved terminal result; eligible Customer may rate or reopen within seven days | Not running |
| `ClosedRejected` | 8 | Manager rejected an `Open` ticket | Not running |

`TicketPriorityEnum`: `P1Critical=1`, `P2High=2`, `P3Normal=3`, `Urgent=4`. `PendingContextEnum`: `Scheduled=1`, `Held=2`.

| Operation | Active endpoint and result |
|---|---|
| Assign / Reassign | `POST /api/admin/tickets/{id}/assign` or `.../reassign`; schedule within `[nowUtc-5m, nowUtc]` → `InProgress`, future schedule → `Pending/Scheduled` |
| Reschedule | `POST /api/admin/tickets/{id}/schedule`; increments `scheduleVersion` |
| Hold / early resume | `POST /api/staff/tickets/{id}/hold` → `Pending/Held`; only current Primary Staff can resume it early, with a reason |
| Escalation | Staff: `InProgress → Request`; Manager reject: `Request → InProgress`; Manager approve: `Request → ReAssign` and priority rises one level |
| Completion | `POST /api/staff/tickets/{id}/resolve` → `Completed`; Manager approve → `Closed`, reject → `InProgress` |
| Reject open ticket | `POST /api/admin/tickets/{id}/triage-reject`; `Open → ClosedRejected` |

Removed APIs/states: `POST .../triage`, `POST .../start`, force escalation, auto-close, `New`, `Assigned`, waiting variants, `Resolved`, `Escalated`, `ClosedPendingRate`, and lifecycle `Incident`.

> Source of truth: `.gemini/kb/modules/ticket-service.md`, `TicketStatusEnum.cs`, `TicketPriorityEnum.cs`, and `PendingContextEnum.cs`.

### Obsolete GH-1176 draft (retained below only for diff history)

This section is authoritative for ticket lifecycle APIs. Lifecycle text farther below that uses
removed statuses or endpoints is retained only as historical documentation and must not be used by
new clients.

| Status | Value | Active meaning |
|---|---:|---|
| `Open` | 2 | Awaiting assignment or reassignment setup |
| `InProgress` | 4 | Primary Staff is actively working |
| `Pending` | 5 | Scheduled work or held work; inspect `pendingContext` |
| `Completed` | 8 | Staff submitted completion for Manager decision |
| `ReAssign` | 9 | Awaiting qualified Primary Staff and schedule |
| `Closed` | 11 | Manager-approved terminal result; eligible tickets may be rated/reopened for seven days |
| `ClosedRejected` | 12 | Manager-rejected terminal ticket |
| `Request` | 14 | Staff escalation request awaiting Manager decision |

`TicketPriorityEnum` is `P1Critical=1`, `P2High=2`, `P3Normal=3`, `Urgent=4`.
SLA runs only while status is `InProgress` and priority is not `Urgent`.

| Operation | Active endpoint and result |
|---|---|
| Assign | `POST /api/admin/tickets/{id}/assign`; current window → `InProgress`, future → `Pending/Scheduled` |
| Reassign | `POST /api/admin/tickets/{id}/reassign`; same current/future schedule rule |
| Reschedule | `POST /api/admin/tickets/{id}/schedule`; increments `scheduleVersion` |
| Hold | `POST /api/staff/tickets/{id}/hold`; requires reason, note, and future appointment → `Pending/Held` |
| Early resume | `POST /api/staff/tickets/{id}/resume`; only current Primary Staff and only `Pending/Held` |
| Completion | `POST /api/staff/tickets/{id}/resolve` → `Completed`; Manager approve → `Closed`, reject → correction `InProgress` |
| Reject open ticket | `POST /api/admin/tickets/{id}/triage-reject`; `Open → ClosedRejected` |

Removed APIs: `POST .../triage`, `POST .../start`, and force escalation. Auto-close is removed;
rating reminders preserve `Closed` status.

> **GH-1176 lifecycle override (2026-08-11):** The canonical statuses are
> `Open=1`, `Pending=2`, `InProgress=3`, `Request=4`, `ReAssign=5`, `Completed=6`,
> `Closed=7`, and `ClosedRejected=8`. Priority adds `Urgent=4`.
> Any older examples below that mention `New`, `Assigned`, waiting variants, `Resolved`,
> `Escalated`, `ClosedPendingRate`, lifecycle `Incident`, `/triage`, or `/start` are superseded.
> Assignment/reassignment requires `priority`, `primaryHandlerStaffId`, and offset-aware
> `scheduledStartAt`: `[nowUtc-5m, nowUtc]` starts directly in `InProgress`; future work enters
> `Pending/Scheduled`. SLA runs only for non-Urgent `InProgress`. Hold produces
> `Pending/Held`; only its active Primary Staff may resume early with a reason. Completion is
> `InProgress → Completed → Closed`; eligible unrated non-merged `Closed` tickets may be rated
> or reopened within seven days. See `logs/GH-1176/plan.md` and `.gemini/kb/modules/ticket-service.md`.

> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`
> **ID fields:** Tất cả `id` trong response đều là `string` (UUID dạng `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Entity C# dùng `Guid` nhưng serialize thành `string` trong JSON — TypeScript dùng `string` cho mọi id field.
>
> **Phạm vi:** toàn bộ TicketService — Ticket, Chat, Participants, Maintenance Log, Saga, Reports, **Knowledge Base** (Nhóm 8–11) và **Blog** (Nhóm 13–16). File `api-ticket-kb-blog.md` đã được **gộp vào đây và xoá** (2026-08-02) — mọi link tới file đó cần trỏ về tài liệu này.

## Mục lục nhóm endpoint

| Nhóm | Phạm vi | Base path |
|---|---|---|
| 1 | Chung (mọi role) | `/api/tickets` |
| — | Ticket Chats · Admin Chats · Chats Utilities · Participants | `/api/tickets/{id}/chats`, `/api/chats`, `.../participants` |
| 2 | Customer | `/api/customer/tickets` |
| 3 | Staff | `/api/staff/tickets` |
| 4 | Admin/Manager | `/api/admin/tickets` |
| 5 | Maintenance Logs | `/api/tickets/{id}/maintenance-logs` |
| 6 | Alert–Ticket Saga | `/api/admin/sagas/alert-ticket` |
| 7 | Health & Diagnostics | `/api/ticket/health` |
| **8** | **Knowledge Base — tra cứu** (Staff/Manager/Admin) | `/api/knowledge-base` |
| **9** | **Knowledge Base — Internal** (Staff/Manager/Admin) | `/api/internal/knowledge-base` |
| **10** | **Knowledge Base — Admin/Manager workflow** | `/api/admin/knowledge-base` |
| **10bis** | **KB Templates** (Admin only) | `/api/admin/knowledge-base/templates` |
| **11** | **Ticket–KB References** | `/api/knowledge-base/references` |
| 12 | Reports | `/api/reports` |
| **13** | **Blog — Public** (mọi role đã đăng nhập) | `/api/blog` |
| **14** | **Blog — Internal** (Staff/Manager/Admin) | `/api/internal/blog` |
| **15** | **Blog — Admin/Manager workflow** | `/api/admin/blog` |
| **16** | **Blog Templates** (Admin only — ghi) | `/api/admin/blog/templates` |
| — | Audit Logs nội bộ (Admin) | `/api/admin/ticket/audit-logs` |

> ⚠️ **Đổi số nhóm khi gộp:** Blog trước đây là Nhóm 12–15 trong `api-ticket-kb-blog.md`, nay là **13–16** vì Nhóm 12 của tài liệu này đã là **Reports**.

---

## Server-side Sort (`SortBy` + `SortDir`) — cập nhật đợt này

**Mục đích/Tác dụng:** sort **toàn dataset** ở server (order **trước** phân trang, kèm tie-breaker `Id ASC`), thay client-side sort chỉ sort được 1 page. Không truyền `SortBy` → giữ default cũ. **Response shape KHÔNG đổi** — chỉ đổi thứ tự `items`.

**Request — 2 query param mới (PascalCase, đều optional):**

| Param | Type | Nullable | Default | Mô tả |
|---|---|---|---|---|
| `SortBy` | string | ✓ | field mặc định | Whitelist per-endpoint; ngoài whitelist → field mặc định |
| `SortDir` | string | ✓ | `desc` | `asc` \| `desc`; lạ → `desc` |

### `GET /api/admin/tickets`

Ví dụ: `GET /api/admin/tickets?PageNumber=1&PageSize=20&SortBy=priority&SortDir=asc`

> Giữ param cũ `IsDescending` (bool, tương thích ngược) — nếu có `SortDir` thì **`SortDir` thắng**.

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `code` | mã ticket | string | Không |
| `title` | tiêu đề | string | Không |
| `category` | phân loại | enum `TicketCategoryEnum` | Không |
| `status` | trạng thái | enum `TicketStatusEnum` | Không |
| `priority` | độ ưu tiên | enum `TicketPriorityEnum` | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

> **Historical documentation notice:** The enum definitions in the sections below this notice predate GH-1176 and are not active API contracts. Use the canonical table at the top of this document.
**`TicketCategoryEnum`:** `Charging=1` · `Overheat=2` · `NoPower=3` · `Performance=4` · `Other=5` · `Repair=6`.

> Sort theo enum = sort theo **giá trị số** (`New=1` → `Incident=13`, P1→P3…), không theo tên hiển thị.

### `GET /api/knowledge-base`

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `code` | mã bài | string | Không |
| `title` | tiêu đề | string | Không |
| `category` | phân loại | enum `TicketCategoryEnum` (như trên) | Không |
| `status` | trạng thái | enum `KbArticleStatusEnum` | Không |
| `viewCount` | lượt xem | int | Không |
| `helpfulCount` | lượt đánh giá hữu ích | int | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`KbArticleStatusEnum`:** `Draft=1` · `PendingReview=2` · `Published=3` · `Archived=4`.

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
| `listErrors` | `Errors[] \| null` | Lỗi validation field-level — mỗi phần tử có `field` và `detail`. **`null`** khi thành công hoặc khi lỗi không thuộc field-level (lỗi nghiệp vụ/quyền chỉ có `message`) — BE serialize list rỗng thành `null` (`ErrorsListJsonConverter`), không bao giờ trả `[]` |

**Lỗi HTTP chung:**
- `400` — Validation hoặc input không hợp lệ
- `401` — Token thiếu/hết hạn/không hợp lệ
- `403` — Có token nhưng không đủ quyền hoặc resource không thuộc user hiện tại / sai trạng thái ticket
- `404` — Không tìm thấy resource
- `500` — Lỗi server ngoài dự kiến

### `TicketActionResponse`

Wrapper **riêng biệt** (không phải `CommonResponse<T>`) dành cho các hành động thay đổi trạng thái ticket (POST mutations). `data` trỏ tới `TicketActionDTO`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": {
    "id": "guid",
    "code": "TKT-2606-0001",
    "status": "InProgress"
  },
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data.id` | `string` | ID của ticket vừa thao tác. **Ngoại lệ:** với `POST .../maintenance-logs`, đây là ID của **maintenance log**, không phải ticket |
| `data.ticketId` | `string?` | Chỉ xuất hiện ở `POST .../maintenance-logs` (ID ticket). Các endpoint khác **bỏ field này** (`JsonIgnore` khi null) |
| `data.code` | `string` | Mã hiển thị của ticket |
| `data.status` | `TicketStatusEnum` | Trạng thái mới của ticket sau hành động |
| `data.warnings` | `string[]?` | Cảnh báo không chặn hành động (vd re-prioritize khiến SLA breach ngay, Staff không đủ tier). **Bỏ field khi null** (`JsonIgnore`) — FE phải check tồn tại trước khi map |

**TypeScript types:**
```ts
interface TicketActionDTO {
  id: string;
  ticketId?: string; // chỉ có ở response của POST maintenance-logs
  code: string;
  status: TicketStatusEnum;
  warnings?: string[]; // chỉ xuất hiện khi có cảnh báo
}

interface TicketActionResponse {
  isSuccess: boolean;
  statusCode: number;
  message: string | null;
  data: TicketActionDTO | null;
  listErrors: Array<{ field: string | null; detail: string | null }> | null;
}
```

### `PaginationResponse<T>`

Dữ liệu nằm trong field `data` của `CommonResponse` khi truy vấn danh sách.

| Field | Type | Mô tả |
|---|---|---|
| `items` | `T[]` | Danh sách dữ liệu trang hiện tại |
| `totalItems` | `int` | Tổng số bản ghi thỏa mãn bộ lọc |
| `pageNumber` | `int` | Chỉ số trang hiện tại |
| `pageSize` | `int` | Số lượng bản ghi trên một trang |
| `totalPages` | `int` | Tổng số trang |
| `hasNextPage` | `bool` | Còn trang tiếp theo không |
| `hasPreviousPage` | `bool` | Có trang trước không |

---

## Enums

### `TicketStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `New` | 1 | Vừa tạo, **chờ Manager triage** — đây là state của Manager queue |
| `Open` | 2 | **Đã triage** (đã có Impact/Urgency/Priority), chờ Manager gán Staff |
| `Assigned` | 3 | Đã gán Staff, chờ Staff xác nhận và bắt đầu |
| `InProgress` | 4 | Staff đang xử lý |
| `WaitingCustomer` | 5 | Tạm dừng — chờ khách hàng phản hồi |
| `WaitingParts` | 6 | Tạm dừng — chờ linh kiện |
| `WaitingOnsiteSchedule` | 7 | Tạm dừng — chờ lịch hẹn tại chỗ |
| `Resolved` | 8 | Staff báo đã xong, chờ Manager kiểm tra |
| `Escalated` | 9 | Đã được chuyển cấp xử lý (SLA breach hoặc Staff/Manager request) |
| `ClosedPendingRate` | 10 | Manager đã phê duyệt kết quả, chờ Customer đánh giá |
| `Closed` | 11 | Đã đóng chính thức (sau khi Customer rate) |
| `ClosedRejected` | 12 | Manager từ chối ticket tại **triage** (`New → ClosedRejected`) hoặc reject ticket đã `Escalated`. ⚠️ KHÔNG dùng cho reject kết quả resolve — luồng đó chuyển thẳng `Resolved → InProgress` |
| `Incident` | 13 | Sự cố nghiêm trọng được Admin/Manager đánh dấu |

> ⚠️ **KHÔNG có `Approved = 14`.** `TicketStatusEnum` chỉ có **13 giá trị** (`New=1` … `Incident=13`). Doc trước đây mô tả một state `Approved` nằm giữa `Open` và `Assigned` — state đó **chưa từng tồn tại trong code**. Vai trò "đã duyệt tính hợp lệ, chờ gán Staff" do chính state **`Open`** đảm nhiệm. FE/Mobile đang mirror `Approved: 14` phải **xóa** giá trị này.

**State machine chính** (nguồn: `TransitionRuleProvider.GetRules()`):
```
New → Open → Assigned → InProgress → Resolved → ClosedPendingRate → Closed
                             ↕ (hold/resume)
                    WaitingCustomer / WaitingParts / WaitingOnsiteSchedule

New        → Open (Manager/Admin/System triage) | → Escalated | → ClosedRejected (triage-reject)
Open       → Assigned (Manager/Admin gán Staff) | → Escalated
Assigned   → Assigned (Manager reassign) | → InProgress (Staff được gán / Admin) | → Escalated (System/Admin: SLA breach)
InProgress → WaitingCustomer/WaitingParts/WaitingOnsiteSchedule (hold) | → Resolved | → Escalated
Waiting*   → InProgress (resume — Staff được gán / System / Admin)
Resolved   → ClosedPendingRate (Manager approve) | → InProgress (Manager reject — KHÔNG qua ClosedRejected)
Escalated  → Assigned (reassign) | → Incident | → ClosedRejected
Incident   → Assigned
ClosedPendingRate → Closed (Customer rate / System auto-close / Admin) | → Open (Customer reopen ≤ 7 ngày; Admin bypass)
```

> **Lưu ý quan trọng (đã verify với `TransitionRuleProvider`):**
> - **Triage** (`POST /api/admin/tickets/{id}/triage`) chuyển `New → **Open**`, **KHÔNG phải `Open → Approved`**. Manager queue liệt kê ticket ở state **`New`**.
> - **Assign** chuyển `Open → **Assigned**` (không phải `Approved → Assigned`).
> - **Reopen** chuyển `ClosedPendingRate → **Open**` (chờ Manager gán lại), **KHÔNG phải `InProgress`**.
> - **Manager reject kết quả** (`reject`) chuyển `Resolved → **InProgress**` trực tiếp; `ClosedRejected` chỉ dùng cho **triage-reject** (`New → ClosedRejected`) và reject ticket `Escalated`.
> - **Từ `Open` KHÔNG transition thẳng sang `ClosedRejected`** — triage-reject xuất phát từ state `New`.
> - **Auto-escalate khi reopen**: kích hoạt từ **lần reopen thứ 2** (`ReopenCount >= 2`, count được tăng trước khi check).
> - State `Closed` là terminal — mọi transition tiếp theo bị chặn ngay tại `TicketStateMachine.CanTransition`.

### `TicketPriorityEnum`

| Giá trị | Int | SLA | Ý nghĩa |
|---|---|---|---|
| `P1Critical` | 1 | 4h | Nghiêm trọng — mất điện/nguy cơ an toàn/diện rộng |
| `P2High` | 2 | 24h | Cao — degradation đáng kể |
| `P3Normal` | 3 | 72h | Bình thường — bất thường nhẹ/bảo trì định kỳ |

**Lưu ý:** Priority được tính tự động từ `ImpactScope × UrgencyLevel` matrix tại bước triage. **Không thay đổi** trong toàn bộ vòng đời ticket. Override thủ công chỉ khi có lý do an toàn (`priorityOverrideReason`).

### `TicketCategoryEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Charging` | 1 | Lỗi sạc |
| `Overheat` | 2 | Quá nhiệt |
| `NoPower` | 3 | Không có điện |
| `Performance` | 4 | Hiệu suất kém |
| `Other` | 5 | Khác |
| `Repair` | 6 | Yêu cầu sửa chữa |

### `TicketOriginEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `ManualByCustomer` | 1 | Customer tự tạo qua app/web |
| `AutoFromAlert` | 2 | Tự động tạo từ `BatteryAnomalyDetectedEvent` |
| `CreatedByStaff` | 3 | Staff tạo thay cho Customer |
| `System` | 4 | **Hệ thống tự tạo** (không từ 1 alert cụ thể) — Sprint Bonus NS-13/NS-22. Vd: cascade risk High mà pin chưa có ticket active (#657), hoặc sự cố môi trường Critical (#662). ⚠️ Wire value cross-service — FE cần mirror giá trị 4 |

### `ImpactScopeEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `SingleAsset` | 1 | Một thiết bị |
| `Site` | 2 | Một khu vực/trạm |
| `MultiSite` | 3 | Nhiều khu vực |

### `UrgencyLevelEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Low` | 1 | Thấp |
| `Medium` | 2 | Trung bình |
| `High` | 3 | Cao |

### `EscalationReasonEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `SkillGap` | 1 | Vượt quá năng lực kỹ thuật của Staff hiện tại |
| `PartsRequired` | 2 | Cần linh kiện không có sẵn |
| `SafetyConcern` | 3 | Lo ngại về an toàn |
| `SlaBreach` | 4 | SLA đã vi phạm |
| `CustomerComplaint` | 5 | Khiếu nại của khách hàng |

### `PauseReasonEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `WaitingCustomer` | 1 | Chờ khách hàng cung cấp thêm thông tin |
| `WaitingParts` | 2 | Chờ linh kiện về |
| `WaitingOnsiteSchedule` | 3 | Chờ lịch hẹn đến tận nơi |
| `AwaitingCustomerChat` | **4** | Chờ khách hàng trả lời trong luồng chat |

> ⚠️ `AwaitingCustomerChat = 4` **không** map sang một `TicketStatusEnum` riêng (chỉ có 3 state `Waiting*`) — FE cần mirror giá trị 4 nhưng không giả định có state tương ứng.

### `SlaTimerStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Running` | 1 | Đang đếm ngược |
| `Paused` | 2 | Đang tạm dừng (hold) |
| `Met` | 3 | Đã giải quyết đúng hạn |
| `Breached` | 4 | Đã vi phạm SLA |
| `Stopped` | **5** | Timer bị dừng hẳn (vd ticket bị gộp/đóng ngoài luồng) — không tính met cũng không tính breached |

### `MaintenanceLogTypeEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `RemoteSupport` | 1 | Hỗ trợ từ xa |
| `OnSite` | 2 | Đến tại chỗ |
| `PartReplacement` | 3 | Thay linh kiện |
| `Inspection` | 4 | Kiểm tra định kỳ |

### `ActivityActionEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Created` | 1 | Ticket được tạo |
| `StatusChanged` | 2 | Trạng thái thay đổi |
| `PriorityAssigned` | 3 | Priority được gán (tại triage) |
| `StaffAssigned` | 4 | Staff được gán |
| `StaffReassigned` | 5 | Staff được điều chuyển |
| `Chatted` | 6 | Có tin nhắn chat mới (tên cũ trong doc là `Commented` — đã đổi từ Sprint Chat) |
| `MaintenanceLogged` | 7 | Nhật ký bảo trì được thêm |
| `AttachmentAdded` | 8 | File đính kèm được thêm |
| `SlaPaused` | 9 | SLA bị tạm dừng |
| `SlaResumed` | 10 | SLA tiếp tục |
| `SlaWarning` | 11 | Cảnh báo sắp vi phạm SLA |
| `SlaBreached` | 12 | SLA đã bị vi phạm |
| `EscalationRequested` | 13 | Staff yêu cầu chuyển cấp |
| `Escalated` | 14 | Ticket đã được chuyển cấp |
| `IncidentDeclared` | 15 | Ticket được đánh dấu là sự cố |
| `Resolved` | 16 | Staff báo đã xử lý xong |
| `Approved` | 17 | Manager phê duyệt kết quả |
| `Rejected` | 18 | Manager từ chối kết quả |
| `Rated` | 19 | Customer đã đánh giá |
| `Reopened` | 20 | Customer yêu cầu mở lại |
| `AutoClosed` | 22 | Tự động đóng (hệ thống) — `AutoCloseBackgroundService` sau 7 ngày ở `CLOSED_PENDING_RATE` |
| `ResolvedByEscalatedStaff` | 23 | Được giải quyết bởi Staff cấp cao sau escalation |
| `TriageApproved` | 24 | Manager phê duyệt tính hợp lệ tại bước triage |
| `Closed` | 25 | Ticket đã đóng chính thức |
| `ChatEdited` | 26 | Đã chỉnh sửa tin nhắn chat |
| `ChatDeleted` | 27 | Đã xoá tin nhắn chat |
| `ChatRestored` | 28 | Đã khôi phục tin nhắn chat |
| `ChatReplied` | 29 | Đã trả lời tin nhắn chat |
| `ChatPinned` | 30 | Đã pin tin nhắn chat |
| `ChatUnpinned` | 31 | Đã unpin tin nhắn chat |
| `ChatFlagged` | 32 | Chat bị flag bởi filter spam/profanity/PII (audit trail — không chặn post trừ spam) |
| `RatingRequested` | **33** | **Sprint 6.2 NOTI-07 (#678)** — hệ thống đã gửi nhắc Customer đánh giá ticket đang treo ở `CLOSED_PENDING_RATE`. Đồng thời là **cờ idempotent**: `RatingRequestBackgroundService` chỉ nhắc **1 lần / ticket** bằng cách kiểm tra sự tồn tại của activity này (không cần cột mới / migration) |
| `ParticipantAdded` | **34** | Thêm participant vào ticket |
| `ParticipantRemoved` | **35** | Gỡ participant khỏi ticket |
| `ParticipantRoleChanged` | **36** | Đổi `participantType`/quyền của participant |

> ⚠️ **Giá trị `21` không tồn tại** (bị bỏ trống trong enum). FE không được giả định enum liên tục.
> **`34`–`36` (nhóm Participant) là bổ sung mới — FE/Mobile phải mirror** cùng với `RatingRequested = 33`.

### `ActorRoleEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Admin` | 1 | Quản trị viên |
| `Manager` | 2 | Quản lý |
| `Staff` | 3 | Nhân viên kỹ thuật |
| `Customer` | 4 | Khách hàng |
| `System` | 5 | Hành động tự động của hệ thống |

### `KbArticleStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Draft` | 1 | Nháp |
| `PendingReview` | 2 | Chờ phê duyệt |
| `Published` | 3 | Đã xuất bản (Customer xem được) |
| `Archived` | 4 | Đã lưu trữ (ẩn) |

### `KbVersionStatusEnum`

Trạng thái của một bản ghi lịch sử (`KbArticleVersion`) — khác với `KbArticleStatusEnum` của bài viết chính.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Chờ duyệt |
| `Approved` | 2 | Đã duyệt |
| `Rejected` | 3 | Bị từ chối |
| `Archived` | 4 | Bản sao lưu (snapshot) |

### `KbReferenceTypeEnum`

Dùng khi gán bài viết Knowledge Base vào Ticket (Nhóm 11).

| Giá trị | Int | Ý nghĩa | Ràng buộc |
|---|---|---|---|
| `ConsultedDuringResolve` | 1 | Tham khảo khi xử lý | Chỉ gán được **trước** khi ticket `Resolved` |
| `ProvidedToCustomer` | 2 | Cung cấp cho khách hàng | Gán được đến hết state `Resolved`; **không** gán được bài `isInternalOnly` (→ `422`) |
| `GeneratedAfterResolve` | 3 | Tạo ra sau khi xử lý xong | Gán được đến hết state `Resolved` |

> Từ `ClosedPendingRate` trở đi, **mọi type** đều bị chặn (`409`). Chi tiết bảng quy tắc: xem `POST /api/knowledge-base/references` (Nhóm 11).

### `TicketVerifyStatusEnum`

Trạng thái AI verify tính hợp lệ của ticket (`TicketDTO.aiVerifyStatus`).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Chờ AI kiểm tra |
| `Legitimate` | 2 | AI đánh giá hợp lệ |
| `Suspicious` | 3 | AI nghi ngờ (spam/trùng lặp) — Manager cần review |
| `Skipped` | 4 | Bỏ qua verify (vd ticket auto từ alert) |

### `TicketCloseReasonEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `MergedDuplicate` | 1 | Ticket bị đóng do được gộp vào ticket khác (`mergedIntoTicketId`) |

### `VoiceTranscriptionStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Đã xếp hàng, chưa xử lý |
| `Processing` | 2 | Đang transcribe |
| `Completed` | 3 | Xong — `body` chứa nội dung transcribed |
| `Failed` | 4 | Lỗi — xem `voiceTranscriptionError`, có thể retry |

### `AssignmentRoleEnum`

Vai trò trong `TicketDTO.assignments` (danh sách người xử lý).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `PrimaryHandler` | 1 | Người xử lý chính |
| `Supporter` | 2 | Người hỗ trợ |
| `PreviousPrimaryHandler` | 3 | Người xử lý chính trước đó (sau reassign/escalate) |

### `StaffSkillTierEnum`

Dùng khi gán Staff — priority cao yêu cầu tier cao (`POST .../assign` trả `403` nếu không đủ tier).

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Generalist` | 1 | Tier 1 |
| `ModuleSpecialist` | 2 | Tier 2 |
| `SeniorSpecialist` | 3 | Tier 3 |

### `BlogPostStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Generating` | 1 | AI đang tạo nội dung (async) |
| `GenerationFailed` | 2 | AI tạo thất bại — có thể chỉnh sửa thủ công |
| `Draft` | 3 | Nháp — chờ Manager/Admin publish |
| `Published` | 4 | Đã xuất bản (mọi user đăng nhập xem được) |
| `Archived` | 5 | Đã lưu trữ (ẩn) |

### `BlogPostOriginEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Manual` | 1 | Tạo thủ công bởi Staff/Manager/Admin |
| `AiGeneratedFromKb` | 2 | AI tạo từ bài viết KB (qua `generate-from-kb`) |

---

### `ChatBodyFormatEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `PlainText` | 1 | Văn bản thường |
| `Markdown` | 2 | Markdown — BE tự render sang HTML lưu vào `bodyHtml` |

### `ReactionTypeEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `ThumbsUp` | 1 | 👍 |
| `Acknowledged` | 2 | ✅ Đã biết |
| `Resolved` | 3 | 🔧 Đã giải quyết |
| `NeedMoreInfo` | 4 | ❓ Cần thêm thông tin |
| `Disagree` | 5 | ❌ Không đồng ý |

### `ChatAiIntentEnum`

Phong cách gợi ý AI cho endpoint `POST /chats/suggest`.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `RequestInfo` | 1 | Yêu cầu thêm thông tin từ Customer |
| `TechnicalAnswer` | 2 | Trả lời kỹ thuật (mặc định) |
| `Resolution` | 3 | Đề xuất giải pháp xử lý |
| `FollowUp` | 4 | Theo dõi tiến độ |

---

## DTOs

### `TicketDTO` (item trong danh sách)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID ticket |
| `code` | `string` | Không | Mã hiển thị (e.g. `TKT-2606-0001`) |
| `batteryAssetId` | `string` | Không (default `""`) | **Legacy — 1 pin đầu tiên.** BE trả **chuỗi rỗng `""`** (không phải `null`, không phải GUID toàn số 0) khi ticket không gắn pin cụ thể (vd ticket site-level, `origin = System`). Ticket nay hỗ trợ **nhiều pin** — dùng `batteryAssetIds` |
| `batteryAssetIds` | `string[]` | Không (default `[]`) | **Danh sách đầy đủ ID pin** của ticket — nguồn đúng kể từ khi `POST /api/customer/tickets` nhận `batteryAssetIds` (≥1 phần tử) |
| `customerId` | `string` | Không | ID khách hàng tạo ticket |
| `assignments` | `TicketAssignmentDTO[]` | Không (default `[]`) | **Thay cho `assignedStaffId`.** Danh sách người xử lý kèm `AssignmentRoleEnum` (`PrimaryHandler`/`Supporter`/`PreviousPrimaryHandler`) — xem bảng dưới. ⚠️ `TicketDTO` **không có** field `assignedStaffId` |
| `title` | `string` | Không | Tiêu đề ticket |
| `category` | `TicketCategoryEnum` | Không | Phân loại lỗi |
| `priority` | `TicketPriorityEnum?` | **Null khi chưa triage** | Mức độ ưu tiên (auto từ matrix tại bước triage) — `null` ở các state `New`/`Open` |
| `impactScope` | `ImpactScopeEnum?` | **Null khi chưa triage** | Phạm vi ảnh hưởng (gán tại triage) |
| `urgencyLevel` | `UrgencyLevelEnum?` | **Null khi chưa triage** | Độ khẩn cấp (gán tại triage) |
| `status` | `TicketStatusEnum` | Không | Trạng thái hiện tại |
| `origin` | `TicketOriginEnum` | Không | Nguồn tạo ticket |
| `reopenCount` | `int` | Không | Số lần Customer mở lại |
| `isIncident` | `bool` | Không | Có được đánh dấu là Incident không |
| `createdAt` | `string` | Không | Thời điểm tạo (ISO 8601 UTC) |
| `updatedAt` | `string?` | Null nếu chưa cập nhật | Thời điểm cập nhật gần nhất |
| `slaTimer` | `SlaTimerDTO?` | **Null khi chưa có SLA timer** | Thông tin SLA timer hiện tại. Timer được tạo khi ticket chuyển sang **`Assigned`** (Sprint Bonus NS-12 #656 — trước đó timer không được tạo ở runtime); ticket auto-tạo P1 (cascade NS-13 / env incident NS-22) có timer **ngay khi tạo**. `null` ở các state trước khi có timer (`New`/`Open`) |
| `hasUnreadChat` | `bool` | Không | Ticket có chat chưa đọc với **user hiện tại** không — dùng để chấm badge trên danh sách |
| `detectedAt` | `string?` | Null | Thời điểm Customer khai báo phát hiện sự cố (`incidentDetectedAt` lúc tạo) |
| `batterySerialNumber` | `string?` | Null nếu lookup fail | Serial pin snapshot lúc tạo ticket |
| `aiVerifyStatus` | `TicketVerifyStatusEnum` | Không | Trạng thái AI verify tính hợp lệ (`Pending`/`Legitimate`/`Suspicious`/`Skipped`) |
| `aiVerifyScore` | `number?` | Null khi chưa verify | Điểm hợp lệ `[0..1]` từ AI |
| `aiVerifyReason` | `string?` | Null | Lý do AI đưa ra verdict |
| `suspectedDuplicateOfTicketId` | `string?` | Null nếu không nghi | Ticket bị nghi là trùng với ticket này |
| `duplicateReason` | `string?` | Null | Lý do nghi trùng |
| `mergedIntoTicketId` | `string?` | Null nếu chưa gộp | Ticket đích nếu ticket này **đã bị gộp** (`POST .../merge`). Khác `null` ⇒ ticket đã đóng và ẩn khỏi queue |
| `closeReason` | `TicketCloseReasonEnum?` | Null | Lý do đóng đặc biệt — hiện chỉ có `MergedDuplicate` |

**`TicketAssignmentDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `staffId` | `string` | ID Staff |
| `role` | `AssignmentRoleEnum` | Vai trò (chuỗi): `PrimaryHandler` · `Supporter` · `PreviousPrimaryHandler` |

> **Migration note cho FE:** code cũ đọc `ticket.assignedStaffId` phải đổi sang
> `ticket.assignments.find(a => a.role === 'PrimaryHandler')?.staffId`.

### `TicketDetailDTO` (chi tiết một ticket — extend `TicketDTO`)

Bao gồm tất cả field của `TicketDTO`, cộng thêm:

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `description` | `string` | Không (default `""`) | Mô tả chi tiết vấn đề |
| `resolutionSummary` | `string?` | Null khi chưa resolve | Tóm tắt cách giải quyết |
| `resolvedAt` | `string?` | Null | Thời điểm Staff báo xong |
| `resolvedByStaffId` | `string?` | Null | Staff thực hiện resolve |
| `approvedAt` | `string?` | Null | Thời điểm Manager phê duyệt kết quả |
| `approvedByManagerId` | `string?` | Null | Manager phê duyệt |
| `rejectionReason` | `string?` | Null | Lý do Manager từ chối |
| `closedAt` | `string?` | Null | Thời điểm đóng ticket chính thức |
| `rating` | `int?` (BE: `short?`) | Null khi chưa rate | Điểm đánh giá 1–5 sao. `ratedAt` được set **đồng thời** với `closedAt` khi Customer rate |
| `ratingComment` | `string?` | Null | Nhận xét của Customer |
| `ratedAt` | `string?` | Null | Thời điểm Customer đánh giá (= `closedAt`) |
| `escalatedAt` | `string?` | Null | Thời điểm chuyển cấp |
| `escalationReason` | `EscalationReasonEnum?` | **Nullable** — trả về `null` khi ticket chưa escalate (KHÔNG phải `0`). | Lý do chuyển cấp |
| `originAlertId` | `string?` | Null nếu không từ alert | ID cảnh báo nguồn (khi `origin = AutoFromAlert`) |
| `activities` | `TicketActivityDTO[]` | Không (default `[]`) | Lịch sử hành động (timeline) |
| `chats` | `TicketChatDTO[]` | Không (default `[]`) | Danh sách chat — thay thế `comments` từ v2. Xem mục `TicketChatDTO` bên dưới |
| `maintenanceLogs` | `MaintenanceLogDTO[]` | Không (default `[]`) | Nhật ký bảo trì |
| `attachmentFileIds` | `string[]` | Không (default `[]`) | **Mảng FileId (string)** của file đính kèm — KHÔNG phải mảng `TicketAttachmentDTO`. Field thực tế tên `attachmentFileIds` |

### `SlaTimerDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID SLA timer |
| `priority` | `TicketPriorityEnum` | Không | Priority của ticket khi SLA được tạo |
| `startedAt` | `string` | Không | Thời điểm bắt đầu tính SLA (UTC) |
| `dueAt` | `string` | Không | Thời điểm deadline hiện tại (đã trừ thời gian pause) |
| `originalDueAt` | `string` | Không | Deadline gốc khi chưa tính pause |
| `totalPausedMinutes` | `int` | Không | Tổng số phút đã tạm dừng (SLA không tính) |
| `warningSentAt` | `string?` | Null | Thời điểm gửi cảnh báo sắp breach |
| `breachAt` | `string?` | Null nếu chưa breach | Thời điểm SLA bị vi phạm |
| `status` | `SlaTimerStatusEnum` | Không | Trạng thái SLA timer |
| `remainingPercent` | `number` | Không | Phần trăm thời gian còn lại (0–100) |

### `TicketActivityDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID activity |
| `ticketId` | `string` | Không | ID ticket |
| `sourceTicketId` | `string?` | Null bình thường | ID ticket **nguồn** khi activity được kéo sang do merge — timeline của ticket đích hiển thị cả hoạt động của ticket đã bị gộp |
| `actorUserId` | `string?` | Null nếu System | ID người thực hiện |
| `actorRole` | `ActorRoleEnum` | Không | Role của người thực hiện |
| `actorDisplayName` | `string?` | Null | Tên hiển thị |
| `action` | `ActivityActionEnum` | Không | Loại hành động |
| `oldValue` | `string?` | Null | Giá trị cũ (dạng string) |
| `newValue` | `string?` | Null | Giá trị mới (dạng string) |
| `reason` | `string?` | Null | Lý do thực hiện |
| `createdAt` | `string` | Không | Thời điểm ghi nhận (UTC) |

### `TicketCommentDTO` ~~(Deprecated)~~

> **⚠️ DEPRECATED — đã thay thế bởi `TicketChatDTO`.** Controller `TicketCommentsController` đã bị xóa. Endpoint `GET/POST /api/tickets/{ticketId}/comments` không còn tồn tại. Dùng `GET/POST /api/tickets/{ticketId}/chats` thay thế. `TicketDetailDTO.chats` trả về `TicketChatDTO[]` (không còn `comments`).

### `TicketChatDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID chat |
| `ticketId` | `string` | Không | ID ticket |
| `authorUserId` | `string` | Không (default `""`) | ID người viết |
| `authorRole` | `ActorRoleEnum` | Không | Role của người viết |
| `authorDisplayName` | `string?` | Null | Tên hiển thị |
| `body` | `string` | Không | Nội dung chat |
| `isInternal` | `bool` | Không | `true` = chỉ Staff/Manager/Admin xem được, ẩn với Customer |
| `attachmentFileIds` | `string[]` | Không (default `[]`) | FileId đính kèm — luôn là mảng, không bao giờ `null` |
| `createdAt` | `string` | Không | Thời điểm tạo (UTC) |
| `editedAt` | `string?` | Null nếu chưa sửa | Thời điểm sửa gần nhất |
| `editCount` | `int` | Không | Số lần đã sửa |
| `lastEditedByUserId` | `string?` | Null | ID người sửa gần nhất |
| `bodyFormat` | `ChatBodyFormatEnum` | Không | Định dạng body (`PlainText` hoặc `Markdown`) |
| `bodyHtml` | `string?` | Null khi PlainText | HTML đã render (chỉ khi `bodyFormat=Markdown`) |
| `parentChatId` | `string?` | Null nếu không phải reply | ID chat cha (thread) |
| `threadRootId` | `string?` | Null nếu không thuộc thread | ID chat gốc của thread |
| `replyCount` | `int` | Không | Số reply của chat này |
| `isPinned` | `bool` | Không | Đang được pin không |
| `pinnedAt` | `string?` | Null | Thời điểm pin |
| `pinnedByUserId` | `string?` | Null | ID người pin |
| `attachments` | `TicketAttachmentDTO[]?` | Null trong GetList, có trong GetById | Danh sách attachment đầy đủ (chỉ khi GetById) |
| `mentions` | `TicketChatMentionDTO[]` | Không (default `[]`) | Danh sách mention trong chat này |
| `reactions` | `TicketChatReactionsAggregateDTO` | Không | Tổng hợp reaction theo loại |
| `activeTranslation` | `ChatTranslateDTO?` | Null nếu user hiện tại chưa dịch chat này | Bản dịch **user hiện tại** đã yêu cầu — shape giống response của `POST .../translate` |
| `isDeleted` | `bool` | Không | Chat đã soft-delete. Admin có thể khôi phục qua `PATCH /api/admin/tickets/{ticketId}/chats/{id}/restore` |
| `voiceTranscriptionStatus` | `VoiceTranscriptionStatusEnum?` | **Null với chat thường** | Chỉ có giá trị với chat tạo từ `POST .../chats/voice`: `Pending`/`Processing`/`Completed`/`Failed` |
| `voiceTranscriptionError` | `string?` | Null | Thông báo lỗi khi `voiceTranscriptionStatus = Failed` |
| `transcribedAt` | `string?` | Null | Thời điểm transcribe xong (UTC) |

**`TicketChatReactionsAggregateDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `thumbsUp` | `ChatReactionGroupDTO` | 👍 |
| `acknowledged` | `ChatReactionGroupDTO` | ✅ Đã biết |
| `resolved` | `ChatReactionGroupDTO` | 🔧 Đã giải quyết |
| `needMoreInfo` | `ChatReactionGroupDTO` | ❓ Cần thêm thông tin |
| `disagree` | `ChatReactionGroupDTO` | ❌ Không đồng ý |

**`ChatReactionGroupDTO`:** `count: int`, `users: [{ userId: string, role: ActorRoleEnum }]`

**`TicketChatMentionDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID mention |
| `chatId` | `string` | ID chat chứa mention |
| `ticketId` | `string?` | ID ticket |
| `mentionedUserId` | `string` | ID người được mention |
| `mentionedUserRole` | `ActorRoleEnum` | Role người được mention — serialize dạng **chuỗi** (vd `"Staff"`) |
| `mentionedDisplayName` | `string?` | Tên hiển thị |
| `isInternal` | `bool` | Mention nằm trong chat nội bộ. Dùng để chọn view public/internal và hiển thị chỉ báo — **không phải** authz check |
| `createdAt` | `string` | Thời điểm tạo |

**`ChatEditHistoryDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bản ghi lịch sử sửa |
| `chatId` | `string` | ID chat |
| `oldBody` | `string` | Nội dung cũ |
| `newBody` | `string` | Nội dung mới |
| `editedAt` | `string` | Thời điểm sửa |
| `editedByUserId` | `string` | ID người sửa |
| `editedByRole` | `ActorRoleEnum` | Role người sửa |
| `editReason` | `string?` | Lý do sửa (bắt buộc khi Manager/Admin sửa chat của người khác) |

### `MaintenanceLogDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID nhật ký |
| `staffId` | `string` | Không | ID Staff tạo nhật ký |
| `logType` | `MaintenanceLogTypeEnum` | Không | Loại nhật ký |
| `summary` | `string` | Không (default `""`) | Tóm tắt công việc |
| `diagnosisDetails` | `string?` | Null | Chi tiết chẩn đoán |
| `actionsTaken` | `string?` | Null | Các hành động đã thực hiện |
| `durationMinutes` | `int` | Không | Thời lượng thực hiện (phút) |
| `resolutionNote` | `string?` | Null | Ghi chú kết quả |
| `startedAt` | `string` | Không | Thời điểm bắt đầu (UTC) |
| `completedAt` | `string?` | Null | Thời điểm hoàn thành (UTC) |
| `attachmentFileIds` | `string[]` | Không (default `[]`) | File đính kèm chung — luôn là mảng (rỗng nếu không có), không `null` |
| `beforePhotosFileIds` | `string[]` | Không (default `[]`) | Ảnh trước khi sửa — luôn là mảng, không `null` |
| `afterPhotosFileIds` | `string[]` | Không (default `[]`) | Ảnh sau khi sửa — luôn là mảng, không `null` |
| `relatedKbArticleIds` | `string[]` | Không (default `[]`) | ID bài viết KB liên quan — luôn là mảng, không `null` |
| `createdAt` | `string` | Không | Thời điểm tạo (UTC) |

### `StaffMaintenanceLogGroupDTO`

Dữ liệu trả về khi Staff xem lịch sử nhật ký cá nhân (đã gom nhóm theo Ticket).

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `ticketId` | `string` | Không | ID Ticket |
| `ticketCode` | `string` | Không | Mã hiển thị Ticket |
| `ticketTitle` | `string` | Không | Tiêu đề Ticket |
| `logs` | `MaintenanceLogDTO[]` | Không | Danh sách các phiên bảo trì thuộc Ticket này |

> **Lưu ý:** Field `partsUsed` chỉ tồn tại trong request body khi tạo log — **không có** trong response DTO.

### `TicketAttachmentDTO`

> ⚠️ **Sửa 2026-08-02:** doc cũ ghi DTO này "không xuất hiện trong bất kỳ response nào" và liệt kê thiếu 7 field — **SAI**. DTO này **được trả về thật** ở:
> - `POST /api/tickets/{ticketId}/chats/{id}/attachments` → `CommonResponse<TicketAttachmentDTO>`
> - `POST .../attachments/batch` → `CommonResponse<TicketAttachmentDTO[]>`
> - `GET .../chats/{id}/attachments` · `GET .../chats/files`
> - `TicketChatDTO.attachments` (chỉ khi GetById)
>
> Riêng `TicketDetailDTO` vẫn chỉ trả `attachmentFileIds: string[]` (mảng FileId), không phải object đầy đủ.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID attachment |
| `ticketId` | `string` | Không | ID ticket chứa attachment |
| `chatId` | `string?` | Null nếu attachment không gắn chat | ID chat chứa attachment |
| `uploadedByUserId` | `string` | Không (default `""`) | ID người upload |
| `fileId` | `string` | Không | FileId từ FileStorageService |
| `fileName` | `string` | Không | Tên file gốc |
| `contentType` | `string` | Không | MIME type |
| `sizeBytes` | `int64` | Không | Kích thước file (bytes) |
| `source` | `AttachmentSourceEnum` | Không | Nguồn attachment (chuỗi) — xem enum bên dưới |
| `thumbnailUrl` | `string?` | Null nếu không có thumbnail | URL ảnh thu nhỏ |
| `url` | `string?` | Null | URL truy cập file |
| `isInline` | `bool` | Không | Hiển thị inline trong nội dung chat hay là file đính kèm rời |
| `downloadCount` | `int` | Không | Số lượt tải |
| `virusScanStatus` | `VirusScanStatusEnum` | Không | Trạng thái quét virus (chuỗi) — quyết định `GET .../download` trả `200`/`202`/`451`/`503` |
| `createdAt` | `string` | Không | Thời điểm upload (UTC) |

**`AttachmentSourceEnum`:**

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `CustomerSubmission` | 1 | Customer gửi lên khi tạo ticket / chat |
| `StaffWork` | 2 | Staff đính kèm trong quá trình xử lý |
| `MaintenanceLog` | 3 | Đính kèm thuộc nhật ký bảo trì |

**`VirusScanStatusEnum`:**

| Giá trị | Int | Ý nghĩa | Ảnh hưởng tới `GET .../download` |
|---|---|---|---|
| `Pending` | 1 | Chờ tới lượt quét | `202` — thử lại sau |
| `Clean` | 2 | Sạch | `200` — trả URL download |
| `Infected` | 3 | Nhiễm virus | `451` — chặn tải |
| `Failed` | 4 | Hỏng hẳn: đã thử đủ số lần cho phép mà vẫn không quét được | `503` — cần quản trị viên xem |
| `Scanning` | 5 | **GH-790** — đang quét (đã chiếm để xử lý) | `202` — thử lại sau |

> **GH-790 — hai thay đổi FE cần biết:**
> 1. Thêm giá trị `Scanning` (5). Client nào so khớp đủ nhánh (`switch`/`match`) phải bổ sung, nếu
>    không sẽ rơi vào nhánh mặc định.
> 2. `Failed` KHÔNG còn trả `202` mà trả **`503`**. Trước đây mọi trạng thái ngoài `Clean`/`Infected`
>    đều là `202` "đang quét, thử lại sau" — với một lượt quét đã hỏng hẳn thì đó là lời nói dối:
>    client hỏi lại mãi mãi và không bao giờ nhận được file.

> ⚠️ Nếu cấu hình `Features.EnableVirusScan = false`, handler **bỏ qua toàn bộ bảng trên** và luôn trả `200` + URL download bất kể `virusScanStatus`.

---

## Nhóm 1 — Chung (Mọi role đã đăng nhập)

Base path: `/api/tickets`
**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}`

---

### `GET /api/tickets/{id}`

**Mục đích:** Lấy thông tin chi tiết của một ticket.

**Auth:** Bắt buộc (mọi role)

**Quyền hạn:**
- Customer: Chỉ xem ticket của chính mình.
- Staff: Chỉ xem ticket được gán cho mình.
- Manager/Admin: Xem toàn bộ.

**Path param:** `id` — UUID của ticket.

**Response thành công `200`:** `CommonResponse<TicketDetailDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "code": "TKT-2606-0001",
    "title": "Pin không sạc được",
    "description": "...",
    "status": "InProgress",
    "priority": "P2High",
    "category": "Charging",
    "origin": "ManualByCustomer",
    "isIncident": false,
    "reopenCount": 0,
    "batteryAssetIds": ["guid"],
    "assignments": [{ "staffId": "guid", "role": "PrimaryHandler" }],
    "aiVerifyStatus": "Legitimate",
    "hasUnreadChat": false,
    "createdAt": "2026-06-05T08:00:00Z",
    "slaTimer": { ... },
    "activities": [...],
    "chats": [...],
    "maintenanceLogs": [...],
    "attachmentFileIds": []
  }
}
```

> ⚠️ Ví dụ cũ ghi `"comments": [...]` — field đó **không còn**, thay bằng `chats`.

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy hoặc không có quyền xem

---

### `GET /api/tickets/{id}/activities`

**Mục đích:** Lấy timeline hoạt động của một ticket (danh sách thay đổi trạng thái, người thực hiện, lý do). Sắp xếp từ mới nhất đến cũ nhất.

**Auth:** Bắt buộc (mọi role)

**Path param:** `id` — UUID của ticket.

**Response thành công `200`:** `CommonResponse<TicketActivityDTO[]>`

> **Lưu ý:** Trả về toàn bộ array — **không pagination**.

---

### ~~`GET /api/tickets/{ticketId}/comments`~~ ~~`POST /api/tickets/{ticketId}/comments`~~ (Removed)

> **⚠️ REMOVED — Controller `TicketCommentsController` đã bị xóa.** Không còn endpoint `GET/POST /api/tickets/{ticketId}/comments`. Dùng `GET/POST /api/tickets/{ticketId}/chats` thay thế (xem mục **Nhóm — Ticket Chats** bên dưới).

---

### `POST /api/tickets/{ticketId}/maintenance-logs`

**Mục đích:** Staff ghi nhật ký bảo trì cho ticket — quá trình sửa chữa, thời gian, linh kiện, ảnh chụp, tọa độ check-in.

**Auth:** Bắt buộc (Staff, Manager hoặc Admin)

**Path param:** `ticketId` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `logType` | `MaintenanceLogTypeEnum` | Không | Loại nhật ký. ⚠️ KHÔNG có default — nếu gửi thiếu, giá trị sẽ là `0` (không hợp lệ, ngoài range enum 1–4). FE nên gửi tường minh, vd `RemoteSupport=1` |
| `summary` | `string` | Bắt buộc | Tóm tắt công việc |
| `diagnosisDetails` | `string?` | Không | Chi tiết chẩn đoán |
| `actionsTaken` | `string?` | Không | Các hành động đã thực hiện |
| `durationMinutes` | `int` | Không | Thời lượng (phút) |
| `resolutionNote` | `string?` | Không | Ghi chú kết quả |
| `startedAt` | `DateTime` | Không | Thời điểm bắt đầu (UTC) |
| `completedAt` | `DateTime?` | Không | Thời điểm hoàn thành (UTC) |
| `partsUsed` | `string?` | Không | Linh kiện đã dùng (mô tả dạng text) |
| `attachments` | `MaintenanceAttachmentInput[]?` | Không | File đính kèm chung |
| `beforePhotos` | `MaintenanceAttachmentInput[]?` | Không | Ảnh trước khi sửa |
| `afterPhotos` | `MaintenanceAttachmentInput[]?` | Không | Ảnh sau khi sửa |
| `relatedKbArticleIds` | `Guid[]?` | Không | ID bài viết KB liên quan |
| `checkInLatitude` | `double?` | Không | Vĩ độ điểm check-in |
| `checkInLongitude` | `double?` | Không | Kinh độ điểm check-in |
| `checkInAt` | `DateTime?` | Không | Thời điểm check-in (UTC) |

**`MaintenanceAttachmentInput`:** Cùng shape với `CommentAttachmentInput` — `fileId` (string UUID), `fileName`, `contentType`, `sizeBytes`.

**Response thành công `201`:** `TicketActionResponse` với semantics **khác** các endpoint ticket:
- `data.id` = **ID của maintenance log** vừa tạo (KHÔNG phải ticketId).
- `data.ticketId` = ID của ticket (chỉ endpoint này mới populate field này).
- `data.status` = trạng thái hiện tại của ticket.

**Business rule — Một log đang mở tại một thời điểm:**
Một ticket chỉ được có **1 log đang mở** (`CompletedAt = null`) tại 1 thời điểm. Phải đóng log cũ (set `CompletedAt`) trước khi mở log mới. Nếu request gửi lên có `CompletedAt = null` (đang bắt đầu log mới) mà ticket đã có một log khác chưa đóng → trả về `409`.

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | Validation field lỗi (ví dụ: `Summary` rỗng, `LogType` không hợp lệ) |
| `401` | Chưa đăng nhập |
| `403` | Ticket đã ở trạng thái không cho phép thêm log (`Resolved` / `ClosedPendingRate` / `Closed`) — Staff đã đánh dấu giải quyết hoặc ticket đã đóng |
| `404` | Không tìm thấy ticket |
| `409` | Đang có một maintenance log khác chưa hoàn thành (`CompletedAt = null`) cho ticket này — phải đóng log cũ trước khi mở log mới |

---

## Realtime — SignalR Hub `/hubs/ticket-chats`

**Mục đích:** Push realtime chat mới (`ChatAdded`) + typing indicator (`UserTyping`) cho ticket detail screen — thay thế polling `GET /api/tickets/{ticketId}/chats`.

**Endpoint:** `/hubs/ticket-chats` (gắn ở root, **KHÔNG** có prefix `/api` — vd `http://localhost:{port}/hubs/ticket-chats`)
**Transport:** WebSockets (SignalR tự fallback sang ServerSentEvents/LongPolling nếu WS bị chặn)
**Auth:** Cùng JWT access token với REST (`[Authorize]` trên Hub), nhưng SignalR JS client **không gửi header** lúc bắt tay WS — phải truyền qua query string `access_token`:

```
ws://localhost:{port}/hubs/ticket-chats?access_token=<accessToken>
```

> Server tự nhận diện: `Program.cs` override `JwtBearerEvents.OnMessageReceived` — nếu path bắt đầu `/hubs/ticket-chats` và có query `access_token`, server lấy token từ query string thay vì header `Authorization`. Dùng `accessTokenFactory` của `@microsoft/signalr` thì KHÔNG cần tự ghép query string — client lib tự làm điều này.

### Client → Server methods

#### `JoinTicket(ticketId: string)`

Bắt buộc gọi **sau khi connect** để nhận event của 1 ticket cụ thể — Hub không tự join group nào khi connect.

- Validate `ticketId` là Guid hợp lệ — sai format → `HubException("Invalid ticket ID format.")`
- Check quyền truy cập ticket — **cùng rule với REST** (`TicketQueryHelper.CanAccessTicket`: Admin/Manager luôn được; Customer phải là chủ ticket; Staff phải đang `AssignedStaffId`) — không có quyền → `HubException("Forbidden: No access to this ticket.")`
- Join group `ticket:{ticketId}:public` (mọi role hợp lệ)
- Nếu role ∈ `Admin`/`Manager`/`Staff` → join thêm group `ticket:{ticketId}:internal` (nhận cả chat `isInternal=true`)

#### `LeaveTicket(ticketId: string)`

Rời cả 2 group của ticket đó. `ticketId` sai format → no-op (không throw).

#### `Typing(ticketId: string)`

Broadcast cho người khác đang xem ticket biết mình đang gõ bình luận. Không có quyền truy cập ticket → no-op (không throw, không broadcast).

### Server → Client events

#### `ChatAdded`

Push khi `POST /api/tickets/{ticketId}/chats` tạo chat thành công (xem Nhóm — Ticket Chats).

**Payload:** `TicketChatDTO` (xem bảng field ở mục DTOs đầu tài liệu) — y nguyên DTO trả về từ REST, camelCase, enum dạng chuỗi.

**Group routing (xem `SignalRTicketChatNotifier.cs`):**
- `chat.isInternal == true` → chỉ push tới group `ticket:{ticketId}:internal` (Staff/Manager/Admin đã join)
- `chat.isInternal == false` → push tới group `ticket:{ticketId}:public` (mọi role đã join, bao gồm Customer)

#### `ChatEdited`

Push khi `PUT /api/tickets/{ticketId}/chats/{id}` sửa chat thành công.

**Payload:** `TicketChatDTO` (bản sau khi sửa) — group routing **giống `ChatAdded`** (theo `chat.isInternal`).

#### `ChatDeleted`

Push khi chat bị soft-delete.

**Payload:** object 2 field — **không phải** `TicketChatDTO`:

```json
{ "chatId": "guid", "byUserDisplayName": "Nguyễn Văn A" }
```

Group routing theo `isInternal` của chat bị xoá.

#### `ReactionChanged`

Push khi thêm/gỡ reaction — client dùng để cập nhật cụm reaction mà không cần refetch.

**Payload:**

```json
{ "chatId": "guid", "reactions": { "thumbsUp": { "count": 2, "users": [...] }, "acknowledged": {...}, "resolved": {...}, "needMoreInfo": {...}, "disagree": {...} } }
```

`reactions` là **`TicketChatReactionsAggregateDTO`** (object gộp theo 5 loại), không phải mảng.

#### `MentionReceived`

Push khi user được tag trong một chat mới.

**Payload:** `TicketChatDTO`.

> ⚠️ **Khác mọi event trên:** gửi bằng `Clients.User(mentionedUserId)` — tới **đích danh user được mention trên mọi connection của họ**, KHÔNG qua group ticket. Nghĩa là user **không cần `JoinTicket`** vẫn nhận được. Cần SignalR resolve được user-id từ JWT (`NameIdentifier`).

#### `UserTyping(ticketId: string, userId: string, displayName: string)`

Push tới **người khác** (không phải chính người gõ) trong group `ticket:{ticketId}:public` khi có client gọi `Typing`. Truyền **3 tham số rời**, không bọc object.

**Lưu ý:** chỉ broadcast qua group public — Staff/Manager/Admin xem chat nội bộ vẫn nhận được (họ cũng ở group public), nhưng không có channel typing riêng cho nhóm internal.

> ⚠️ **Sửa 2026-08-02:** doc cũ chỉ liệt kê `ChatAdded` + `UserTyping`. Hub thật phát **6 event**
> (`SignalRTicketChatNotifier.cs` + `TicketChatHub.cs`). FE chỉ handle 2 event sẽ bỏ lỡ cập nhật
> sửa/xoá/reaction realtime và phải refetch thủ công.

### Connection lifecycle

- Hub **không tự join group** khi connect (`OnConnectedAsync` dùng default — không override) — client **phải** gọi `JoinTicket` sau mỗi lần connect/reconnect (kể cả auto-reconnect) trước khi nhận event.
- Khi connection đóng (tab close, mất mạng…), SignalR framework tự gỡ connection khỏi mọi group — không cần gọi `LeaveTicket` trước khi unload, nhưng nên gọi khi user rời màn hình ticket detail trong khi tab vẫn mở (chuyển sang ticket khác) để tránh nhận nhầm event của ticket cũ.
- Token hết hạn giữa session: Hub không tự refresh — connection bị đóng khi token expire, FE cần `accessTokenFactory` trả token mới nhất mỗi lần reconnect và tự gọi lại `JoinTicket`.

### Hub options (server-side)

| Option | Giá trị | Mục đích |
|---|---|---|
| `KeepAliveInterval` | 15 giây | Server ping client để giữ WS sống |
| `ClientTimeoutInterval` | 60 giây | Server đánh dấu disconnect nếu im lặng quá 60s |
| `EnableDetailedErrors` | `true` (Development), `false` (Production) | Chi tiết exception trả client khi `HubException` |
| JSON protocol | camelCase + `JsonStringEnumConverter` | Khớp định dạng JSON với REST response (`TicketChatDTO`) |
| Redis backplane | Bật khi có `ConnectionStrings:Redis` | Scale-out nhiều instance — channel prefix `TicketChat` |

> **Redis backplane (scale-out):** nếu `ConnectionStrings:Redis` có giá trị, hub dùng `AddStackExchangeRedis` để broadcast xuyên instance. **Không cấu hình Redis → hub vẫn chạy bình thường nhưng chỉ trong 1 instance** — khi deploy nhiều replica mà thiếu Redis, client nối vào instance A sẽ **không nhận** được `ChatAdded` phát từ instance B. Đây là fallback im lặng, không có log lỗi.

### Test nhanh (FE / tay)

Cách nhanh nhất — dán đoạn dưới vào DevTools Console của bất kỳ trang nào (hoặc 1 file `.html` mở trực tiếp), không cần cài project:

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
<script>
  const accessToken = "<dán accessToken lấy từ POST /api/auth/login>";
  const ticketId = "<dán 1 ticketId có thật>";

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:{port}/hubs/ticket-chats", {
      accessTokenFactory: () => accessToken,
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Information)
    .build();

  connection.on("ChatAdded", (chat) => console.log("[ChatAdded]", chat));
  connection.on("UserTyping", (tId, userId, displayName) => console.log("[UserTyping]", displayName));

  connection
    .start()
    .then(() => connection.invoke("JoinTicket", ticketId))
    .then(() => console.log("Joined ticket", ticketId))
    .catch((err) => console.error(err));

  // Test: mở tab khác / Postman gọi POST /api/tickets/{ticketId}/chats
  // → console phải in ra "[ChatAdded]" gần như ngay lập tức.
</script>
```

> Thay `{port}` bằng port thật của TicketService (`http://localhost:{port}`, không qua API Gateway nếu gateway chưa proxy WebSocket). `accessToken` lấy từ response `POST /api/auth/login` (AuthService, xem `docs/api-auth.md`) — dùng đúng tài khoản có quyền xem `ticketId` đó (Customer chủ ticket, Staff được assign, hoặc Admin/Manager).
> **ApiGateway route:** `/hubs/ticket-chats` đã được proxy trong YARP config (`ticket-chats-hub-route`, `ticket-chats-hub-root-route`) → ticketCluster.

---

## Nhóm — Ticket Chats

Base path: `/api/tickets/{ticketId}/chats`
**Auth:** Bắt buộc — mọi role đã đăng nhập (`[Authorize]` trên controller)
**Hub:** `/hubs/ticket-chats` — xem mục **Realtime** bên trên

> Thay thế toàn bộ hệ thống Comments cũ.

### Rate limit — `ChatWritePolicy`

Áp dụng cho **8 endpoint ghi** (không chỉ POST/PUT như doc cũ ghi):
`POST /chats` · `PUT /chats/{id}` · `DELETE /chats/{id}` · `POST /chats/{id}/pin` · `DELETE /chats/{id}/pin` · `DELETE /chats/bulk` · `POST /chats/voice` · `POST /chats/{id}/voice/retry`.

Fixed window **1 phút**, `QueueLimit = 0` (vượt hạn → **`429` ngay**, không xếp hàng):

| Role | Hạn mức | Phân vùng đếm |
|---|---|---|
| `Admin` | **Không giới hạn** | — |
| `Customer` | **30 / phút** | **theo từng ticket** (`customer:{ticketId}:{userId}`) — mỗi ticket có quota riêng |
| `Staff` | **60 / phút** | toàn cục theo user (`staff:{userId}`) |
| `Manager` | **90 / phút** | toàn cục theo user (`manager:{userId}`) |

> Role không nhận diện được → rơi vào nhánh mặc định **Staff (60/phút)**.
>
> ⚠️ **Doc-comment trong `ChatRateLimitingExtensions.cs` ghi "Customer 10, Staff 30, Manager 60" — số đó đã cũ, KHÔNG khớp code.** Giá trị thực thi là bảng trên (30/60/90). Lấy số từ `PermitLimit` chứ đừng tin comment.
>
> `429` do rate limit là **HTTP status trần** (`RejectionStatusCode`), không bọc trong `CommonResponse` — khác với `429` của Gemini (được wrap thành `isSuccess: false`).

### Tóm tắt endpoints

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `GET` | `/api/tickets/{ticketId}/chats` | Mọi role | Danh sách chat (phân trang, offset) |
| `GET` | `/api/tickets/{ticketId}/chats/cursor` | Mọi role | Danh sách chat (cursor — infinite scroll) |
| `POST` | `/api/tickets/{ticketId}/chats` | Mọi role | Thêm chat mới |
| `GET` | `/api/tickets/{ticketId}/chats/{id}` | Mọi role | Lấy chi tiết 1 chat |
| `PUT` | `/api/tickets/{ticketId}/chats/{id}` | Mọi role | Sửa chat |
| `DELETE` | `/api/tickets/{ticketId}/chats/{id}` | Mọi role | Xóa (soft-delete) chat |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/history` | Mọi role | Lịch sử sửa chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/replies` | Mọi role | Trả lời chat (thread, tối đa 1 cấp) |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/replies` | Mọi role | Danh sách reply |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/attachments` | Mọi role | Thêm attachment vào chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/attachments/batch` | Mọi role | Thêm **nhiều** attachment cùng lúc |
| `DELETE` | `/api/tickets/{ticketId}/chats/{id}/attachments/{attachmentId}` | Mọi role | Xóa attachment |
| `GET` | `/api/tickets/{ticketId}/chats/files` | Mọi role | Danh sách **toàn bộ file** đính kèm trong mọi chat của ticket |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/attachments` | Mọi role | Danh sách attachment của chat |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/attachments/{attachmentId}/download` | Mọi role | URL download attachment |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/pin` | Staff/Manager/Admin | Pin chat (tối đa 3/ticket) |
| `DELETE` | `/api/tickets/{ticketId}/chats/{id}/pin` | Staff/Manager/Admin | Unpin chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/reactions` | Mọi role | Thêm reaction |
| `DELETE` | `/api/tickets/{ticketId}/chats/{id}/reactions` | Mọi role | Xóa reaction |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/reactions` | Mọi role | Lấy reaction aggregate |
| `POST` | `/api/tickets/{ticketId}/chats/mark-read` | Mọi role | Mark-read nhiều chat (bulk) |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/readers` | Staff/Manager/Admin | Danh sách user đã đọc chat |
| `GET` | `/api/tickets/{ticketId}/chats/unread-count` | Mọi role | Số chat chưa đọc |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/attach-kb` | Staff/Manager/Admin | Gắn KB article vào chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/to-kb-draft` | Staff/Manager/Admin | Chuyển chat thành KB Draft |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/kb-suggestions` | Staff/Manager/Admin | Gợi ý KB articles |
| `POST` | `/api/tickets/{ticketId}/chats/suggest` | Staff/Manager/Admin | AI gợi ý nội dung chat |
| `POST` | `/api/tickets/{ticketId}/chats/summarize` | Staff/Manager/Admin | AI tóm tắt thread |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/translate` | Mọi role | Dịch nội dung chat |
| `POST` | `/api/tickets/{ticketId}/chats/voice` | Mọi role | Upload audio → transcribe → tạo chat |
| `POST` | `/api/tickets/{ticketId}/chats/{chatId}/voice/retry` | Mọi role | Retry transcribe chat voice đang `Failed` |
| `DELETE` | `/api/tickets/{ticketId}/chats/bulk` | Mọi role | Xóa nhiều chat cùng lúc (bulk soft-delete) |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/escalation-review/ack` | Manager/Admin | ACK escalation review |

> Ngoài ra còn `GET /api/chats/unread-count` (`ChatsController`) — tổng số chat chưa đọc của user hiện tại **trên toàn bộ ticket**, khác với `/api/tickets/{ticketId}/chats/unread-count` (theo 1 ticket).

---

### `GET /api/tickets/{ticketId}/chats`

**Mục đích:** Danh sách chat của ticket (offset pagination, sort ASC theo `CreatedAt`). Auto mark-read trang hiện tại.

**Query params:**

| Param | Type | Mặc định | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | 1 | Số trang |
| `pageSize` | `int` | 10 | Số item/trang |
| `search` | `string?` | — | Tìm full-text trong `body` |
| `authorUserId` | `Guid?` | — | Lọc theo tác giả |
| `authorRole` | `ActorRoleEnum?` | — | Lọc theo role tác giả |
| `isInternal` | `bool?` | — | Lọc chat nội bộ/công khai |
| `isPinned` | `bool?` | — | Chỉ chat đã pin |
| `hasAttachments` | `bool?` | — | Chỉ chat có file đính kèm |
| `mentionedMe` | `bool?` | — | Chỉ chat mention user hiện tại |
| `dateFrom` | `DateTime?` | — | Lọc từ ngày (UTC) |
| `dateTo` | `DateTime?` | — | Lọc đến ngày (UTC) |

**Quyền hạn:**
- Customer: Chỉ thấy chat `isInternal=false`
- Staff/Manager/Admin: Thấy tất cả bao gồm `isInternal=true`

**Response `200`:** `CommonResponse<PaginationResponse<TicketChatDTO>>`

---

### `GET /api/tickets/{ticketId}/chats/cursor`

**Mục đích:** Cursor-based pagination — phù hợp load-more / infinite scroll. Không dùng offset.

**Query params:**

| Param | Type | Mặc định | Mô tả |
|---|---|---|---|
| `cursor` | `string?` | — | Opaque cursor từ `nextCursor` trang trước. Bỏ trống = trang đầu |
| `limit` | `int` | 20 | Số chat/trang (tối đa 100) |

**Response `200`:** `CommonResponse<CursorPaginationResponse<TicketChatDTO>>`

```json
{
  "isSuccess": true,
  "data": {
    "items": [...],
    "nextCursor": "opaque-string",
    "hasMore": true
  }
}
```

---

### `POST /api/tickets/{ticketId}/chats`

**Mục đích:** Thêm chat vào ticket. Áp dụng cho mọi role. Rate-limited.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `body` | `string` | **Bắt buộc** | Nội dung chat. Tối đa **10 000** ký tự (`ChatOptions.MaxBodyLengthDefault`). **Không được chỉ chứa khoảng trắng hoặc emoji** |
| `isInternal` | `bool` | Không (mặc định `false`) | `true` = ẩn với Customer |
| `bodyFormat` | `ChatBodyFormatEnum` | Không (mặc định `PlainText`) | Định dạng body |
| `attachments` | `ChatAttachmentInput[]?` | Không | Danh sách file đính kèm |
| `mentions` | `ChatMentionInput[]?` | Không | **Tag từng người cụ thể** — sinh bản ghi `TicketChatMentionDTO` + notification `ChatMentioned` |
| `groupMentions` | `GroupMentionInput[]?` | Không | **Tag cả nhóm** (theo role hoặc theo tier team) — xem bảng giá trị hợp lệ bên dưới |
| `requestCustomerInfo` | `bool` | Không (mặc định `false`) | Đánh dấu chat là **yêu cầu Customer bổ sung thông tin** |

**`ChatAttachmentInput`:** `fileId` (Guid, **bắt buộc**) · `fileName` (string, **bắt buộc**) · `contentType` (string, **bắt buộc**) · `sizeBytes` (int64) · `url` (string?, tùy chọn)

**`ChatMentionInput`:** `userId` (Guid, **bắt buộc**) · `displayName` (string, **bắt buộc**)

**`GroupMentionInput`:** `groupType` + `groupIdentifier` — **whitelist cứng**, sai giá trị → `400`:

| `groupType` | `groupIdentifier` hợp lệ |
|---|---|
| `"role"` | `manager` · `staff` · `admin` · `customer` |
| `"team"` | `tier1-staff` · `tier2-staff` · `tier3-staff` |

> So khớp **không phân biệt hoa thường**, nhưng `groupType` thì **phân biệt** — phải đúng chữ thường `"role"`/`"team"`.

**Response `201`:** `TicketActionResponse`

**Realtime:** Phát event `ChatAdded` qua SignalR hub `/hubs/ticket-chats` — routing theo `isInternal` (internal group / public group).

**Lỗi:**
- `400` — `body` rỗng / vượt 10 000 ký tự / chỉ có whitespace–emoji · attachment thiếu `fileId`/`fileName`/`contentType` · `mentions[i]` thiếu `userId`/`displayName` · `groupMentions[i]` sai `groupType`/`groupIdentifier`. `listErrors[].field` dùng dạng chỉ số: `Attachments[0].FileId`, `Mentions[1].UserId`, `GroupMentions[0].GroupType`
- `400` + `CHAT_DUPLICATE_MESSAGE_LIMIT` — chat thứ **3** trùng nội dung trong 5 phút
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy ticket
- `409` + `CHAT_SPAM_CHECK_IN_PROGRESS` — spam check đang chạy đồng thời, client retry với backoff ngắn

> ⚠️ **Giới hạn 10 000 ký tự lấy từ hằng số, không phải config.** `ValidateAsync()` không inject được `IOptions<ChatOptions>` nên nếu `appsettings.json` set `Chat:MaxBodyLength` khác, **validate vẫn chặn theo 10 000** — override không có tác dụng ở tầng validate.

---

### `PUT /api/tickets/{ticketId}/chats/{id}`

**Mục đích:** Sửa nội dung chat.

**Quyền (theo `ChatAuthorizationService.CanEditChat`):**
- **Chỉ tác giả** sửa được, và **chỉ trong `Chat:EditWindowMinutes`** (mặc định **15 phút**) kể từ `createdAt`.
- **Không role nào** (kể cả Manager/Admin) sửa được chat của người khác → `403`.
- Blocked khi ticket `Closed` (`400`, tuỳ `Chat:BlockEditOnClosed`).

> ⚠️ **Sửa 2026-08-02:** doc cũ ghi "Manager/Admin: Sửa bất cứ lúc nào nhưng phải có `editReason`" — **SAI**. `CanEditChat` chỉ có nhánh `chat.AuthorUserId == actorUserId`, còn lại trả `Forbidden`; hàm **không đọc `actorPermissions`**. Quá window thì **tác giả cũng bị chặn** (`EditWindowExpired`).
>
> Đường duy nhất để Admin sửa chat người khác: `PUT /api/admin/tickets/{ticketId}/chats/{id}/closed-override` — **chỉ cho ticket đã Closed**, bắt buộc `overrideReason`.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `body` | `string` | **Bắt buộc** | Nội dung mới |
| `editReason` | `string?` | Không | Lý do sửa. ⚠️ Handler hiện **luôn ghi `EditReason = null`** — field này thực tế **không có tác dụng** ở endpoint này |

**Response `200`:** `TicketActionResponse`

**Lỗi:**
- `400` — quá edit window (`"Đã quá thời gian cho phép chỉnh sửa (15 phút)."`) hoặc ticket đã Closed
- `403` — không phải tác giả (`"Không có quyền sửa bình luận này."`)
- `404` — không tìm thấy ticket/chat

---

### `DELETE /api/tickets/{ticketId}/chats/{id}`

**Mục đích:** Soft-delete chat **của chính mình**; với chat của người khác thì **ẩn-cho-riêng-mình**.

**Quyền (theo `ChatAuthorizationService.CanDeleteChat`):**

| Người gọi | Kết quả |
|---|---|
| **Tác giả** của chat | Soft-delete thật (`isDeleted = true`) — mọi người không thấy nữa. Không giới hạn thời gian |
| **Bất kỳ ai khác** (kể cả **Manager/Admin**) | **KHÔNG xoá.** BE ghi bản ghi `TicketChatHide` ⇒ chat chỉ ẩn với **riêng người gọi**; người khác vẫn thấy bình thường |

- Blocked khi ticket `Closed` (`400`, tuỳ `Chat:BlockEditOnClosed`).

> ⚠️ **Sửa 2026-08-02 — điểm dễ hiểu nhầm nhất của module Chat.** Doc cũ ghi "Manager/Admin: Xóa của ai cũng được, phải có `deleteReason`" — **SAI**. `CanDeleteChat` chỉ so `AuthorUserId == actorUserId`, **không đọc `actorPermissions`** (tham số có nhận nhưng không dùng). Không role nào xoá được chat của người khác qua endpoint này.
>
> **Quan trọng cho FE:** gọi xoá chat người khác **không trả `403`** mà trả **`200`** với
> `message: "Đã ẩn bình luận."`. Nếu FE hiển thị "Đã xoá" dựa trên `isSuccess === true` thì user sẽ
> tưởng đã xoá cho mọi người — thực tế chỉ ẩn với chính họ. Nên phân biệt bằng `message`, hoặc chỉ
> hiện nút Xoá khi `authorUserId === currentUserId`.
>
> Muốn Admin thật sự xoá chat người khác: dùng **Admin override**
> `DELETE /api/admin/tickets/{ticketId}/chats/{id}/closed-override` — nhưng **chỉ áp dụng cho ticket đã Closed**.

**Request body (tùy chọn):** `{ "deleteReason": "string?" }` — hiện **không được dùng** ở nhánh nào (giữ lại cho tương thích).

**Response `200`:** `TicketActionResponse` — cả 2 nhánh (xoá thật và ẩn-cho-mình) đều trả `200`.

---

### `POST /api/tickets/{ticketId}/chats/{id}/pin` / `DELETE .../pin`

**Mục đích:** Pin / Unpin chat. Tối đa 3 chat pin/ticket.
**Auth:** Staff/Manager/Admin

**Response `200`:** `TicketActionResponse`

**Lỗi `400`:** Pin: đã pin hoặc đạt giới hạn 3. Unpin: chưa pin.

---

### `POST /api/tickets/{ticketId}/chats/{id}/reactions`

**Mục đích:** Thêm reaction — idempotent nếu đã react cùng loại.

**Request body:** `{ "reactionType": "ThumbsUp" }` (xem `ReactionTypeEnum`)

**Response `200`:** `CommonResponse<TicketChatReactionsAggregateDTO>`

---

### `DELETE /api/tickets/{ticketId}/chats/{id}/reactions`

**Mục đích:** Xóa reaction — no-op nếu chưa react loại này.

**Query param:** `type` — `ReactionTypeEnum` (vd `?type=ThumbsUp`)

**Response `200`:** `CommonResponse<TicketChatReactionsAggregateDTO>`

---

### `GET /api/tickets/{ticketId}/chats/{id}/attachments/{attachmentId}/download`

**Mục đích:** Lấy URL download sau khi kiểm tra virus scan.

**Response:**
- `200` — URL download
- `202` — File đang scan, thử lại sau
- `451` — File bị nhiễm virus, không thể tải

---

### `POST /api/tickets/{ticketId}/chats/suggest` (AI)

**Mục đích:** AI gợi ý 3 nội dung chat theo intent — dành cho Staff. PII được mask trước khi gửi Gemini.
**Auth:** Staff/Manager/Admin

**Request body:**

| Field | Type | Mặc định | Mô tả |
|---|---|---|---|
| `intent` | `ChatAiIntentEnum` | `TechnicalAnswer` | Phong cách gợi ý |

**Response `200`:** `CommonResponse<ChatSuggestDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "suggestionId": "guid",
    "suggestions": ["Gợi ý 1", "Gợi ý 2", "Gợi ý 3"]
  }
}
```

**Rate limit (Gemini):** Trả `isSuccess: false, message: "AI service đang bận, vui lòng thử lại sau ít giây."` khi Gemini 429.

---

### `POST /api/tickets/{ticketId}/chats/summarize` (AI)

**Mục đích:** Tóm tắt toàn bộ thread thành 5 dòng bullet — cho Staff mới tiếp nhận ticket sau escalation.
**Auth:** Staff/Manager/Admin

**Response `200`:** `CommonResponse<ChatSummarizeDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "summary": "- Vấn đề: pin không sạc được\n- Đã kiểm tra cáp: bình thường\n- ..."
  }
}
```

---

### `POST /api/tickets/{ticketId}/chats/{id}/translate` (AI)

**Mục đích:** Dịch nội dung chat sang ngôn ngữ đích. Cache 2 lớp: Redis (30 ngày) → DB → Gemini AI.
**Auth:** Mọi role

**Query param:** `to` — mã ISO 639-1 (vd `en`, `vi`, `fr`, `ja`)

**Response `200`:** `CommonResponse<ChatTranslateDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "translatedBody": "Battery cannot charge",
    "targetLanguage": "en",
    "originalLanguage": "vi",
    "provider": "GeminiAi",
    "fromCache": false
  }
}
```

**Validation:** `to` bắt buộc, tối đa 5 ký tự. Rate limit Gemini → `isSuccess: false`.

---

### `POST /api/tickets/{ticketId}/chats/voice` (AI)

**Mục đích:** Upload file audio → Gemini transcribe → tạo chat với nội dung transcribed + đính kèm audio trong `ticket_attachments`.
**Auth:** Mọi role
**Content-Type:** `multipart/form-data`

**Form field:** `audioFile` — file âm thanh

**MIME types hỗ trợ** (từ `Chat:Voice:AllowedAudioMimeTypes`):
`audio/mpeg` · `audio/wav` · `audio/ogg` · `audio/webm` · `audio/mp4` · `audio/flac`

**Giới hạn:** 20MB / file

**Response `201`:** `TicketActionResponse`

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Voice transcription thành công.",
  "data": {
    "id": "guid-of-new-chat",
    "ticketId": "guid",
    "code": "TKT-2606-0001",
    "status": "InProgress"
  }
}
```

**Lỗi:**
- `400` — MIME không hỗ trợ hoặc file quá lớn
- `404` — Không tìm thấy ticket
- `422` — Transcription trả kết quả rỗng
- `429` (wrap trong `isSuccess:false`) — Gemini rate limit

---

> **Cập nhật Voice API (2026-07-31 — thay thế mô tả phía trên):** `POST /api/tickets/{ticketId}/chats/voice` không còn nhận `multipart/form-data`, không upload audio và không chờ Gemini. FE upload audio lên FileStorage trước, rồi gửi JSON `{ "fileId", "fileName", "contentType", "sizeBytes", "url" }`. API trả `202 Accepted`, tạo chat placeholder với `voiceTranscriptionStatus = "Pending"`; FE refresh/poll danh sách chat để hiển thị kết quả hoặc lỗi. MIME hợp lệ: `audio/mpeg`, `audio/mp3`, `audio/wav`, `audio/x-wav`, `audio/wave`, `audio/ogg`, `audio/webm`, `video/webm`, `audio/mp4`, `audio/m4a`, `audio/x-m4a`, `audio/aac`, `audio/flac`, `audio/x-flac`; giới hạn 20 MB.

#### FE copy-paste — Voice transcription flow

> **Use this contract.** The older multipart/`201` description above is obsolete.

```ts
export type VoiceTranscriptionStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed';

export interface QueueVoiceTranscriptionRequest {
  fileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  url: string;
}

export interface TicketActionResponse {
  isSuccess: boolean;
  statusCode: number;
  message?: string;
  data?: { id: string; ticketId: string; code?: string; status?: string };
  listErrors?: Array<{ field: string; detail: string }>;
}

export interface TicketChat {
  id: string;
  ticketId: string;
  body: string;
  attachmentFileIds: string[];
  voiceTranscriptionStatus?: VoiceTranscriptionStatus;
  voiceTranscriptionError?: string | null;
  transcribedAt?: string | null;
}
```

1. Upload audio using the existing FileStorage upload API. Do not send the browser `File` again to TicketService.
2. Validate client-side: max `20 * 1024 * 1024` bytes; use one of the MIME values listed above.
3. Call TicketService with the FileStorage upload result:

```ts
const response = await api.post<TicketActionResponse>(
  `/api/tickets/${ticketId}/chats/voice`,
  {
    fileId: upload.fileId,
    fileName: upload.fileName,
    contentType: upload.contentType,
    sizeBytes: upload.sizeBytes,
    url: upload.url,
  },
);

// Expect HTTP 202. Store response.data.id as the placeholder chat id.
```

4. Refresh the normal ticket-chat list every 3 seconds while any chat has `voiceTranscriptionStatus` equal to `Pending` or `Processing`; stop polling when every voice chat is `Completed` or `Failed`.
5. Render states exactly as follows: `Pending` = “Đang xếp hàng”, `Processing` = “Đang chuyển giọng nói thành văn bản”, `Completed` = render `body`, `Failed` = render `voiceTranscriptionError` plus Retry button.
6. Retry only when `voiceTranscriptionStatus === 'Failed'`; disable the Retry button while the retry request is in flight.

### `POST /api/tickets/{ticketId}/chats/{chatId}/voice/retry`

**Auth:** user có quyền chat trên ticket. Không body. Chỉ retry chat có `voiceTranscriptionStatus = "Failed"`; response `202 Accepted`. `404` nếu chat không thuộc ticket; `409` nếu chat chưa failed hoặc không có audio attachment.

### `POST /api/admin/tickets/{ticketId}/re-prioritize`

#### FE copy-paste — Re-prioritize ticket

```ts
export type TicketPriority = 'P1Critical' | 'P2High' | 'P3Normal';

export interface ReprioritizeTicketRequest {
  priority: TicketPriority;
  reason: string;
}

export async function reprioritizeTicket(
  ticketId: string,
  request: ReprioritizeTicketRequest,
) {
  return api.post<TicketActionResponse>(
    `/api/admin/tickets/${ticketId}/re-prioritize`,
    request,
  );
}
```

**UI rules:** show this action only to Manager; require a non-empty reason (maximum 1000 characters); do not put `managerId`, `managerName`, current priority, SLA time, or ticket id in the JSON body. The server takes manager identity and display name from JWT and ticket id from the URL.

**After `200`:** replace the ticket priority/status from `response.data`, refetch ticket detail to get SLA countdown/timer data, and refetch the activity timeline. Do not calculate the new SLA deadline in FE. A re-prioritize can automatically breach SLA or escalate an insufficiently skilled primary handler.

**Error UI:** `400` show field errors; `401/403` hide/disable action and refresh authorization; `404` return to ticket list; `409` show `message` and refetch ticket because its state changed concurrently; `409`/concurrency response should never be retried automatically.

**Auth:** `Manager` only. FE gửi `{ "priority": "P1Critical", "reason": "..." }`; không gửi `managerId`/`managerName` vì server lấy identity và display name từ JWT. Response `200`: cập nhật priority/SLA nhưng SLA không reset. Nếu deadline mới đã quá hạn, server breach SLA trong transaction. Lỗi: `400` (priority/reason; reason tối đa 1000 ký tự), `404` ticket không tồn tại, `409` trạng thái không cho phép (New/Resolved/Closed/Merged).

### `GET /api/tickets/{ticketId}/chats/unread-count`

**Mục đích:** Số chat chưa đọc của user hiện tại trên ticket này.

**Response `200`:** `CommonResponse<int>` — `data` là **số thuần**, KHÔNG bọc object.
`TicketUnreadCountResponse : CommonResponse<int>`. Đọc `data.unreadCount` sẽ luôn ra `undefined`.

```json
{ "isSuccess": true, "data": 3 }
```

---

### `GET /api/tickets/{ticketId}/chats/{id}/readers`

**Mục đích:** Danh sách user đã đọc 1 chat.
**Auth:** **Staff/Manager/Admin only** — Customer gọi nhận `403`. (Khác `mark-read` và
`unread-count`: 2 endpoint đó mọi role đều gọi được.)

**Response `200`:** `CommonResponse<ChatReaderDTO[]>` — sắp xếp theo `readAt` tăng dần.

**`ChatReaderDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `chatId` | `string` | ID chat |
| `userId` | `string` | ID người đọc |
| `displayName` | `string` | Tên hiển thị — resolve từ `CustomerAccounts`/`StaffAccounts` theo `role`; fallback về `userId` nếu không tìm thấy |
| `role` | `ActorRoleEnum` | Role người đọc (chuỗi, vd `"Staff"`) |
| `readAt` | `string` | Thời điểm đọc (ISO-8601 UTC) |

```json
{
  "isSuccess": true,
  "data": [
    { "chatId": "…", "userId": "…", "displayName": "Nguyễn Văn A", "role": "Staff", "readAt": "2026-08-02T13:24:00Z" }
  ]
}
```

**Lỗi:** `403` không có quyền truy cập ticket · `404` ticket/chat không tồn tại, hoặc chat nội bộ mà actor không được xem.

---

### `POST /api/tickets/{ticketId}/chats/mark-read`

**Mục đích:** Mark-read nhiều chat (bulk) — cũng được gọi tự động khi `GET /chats`.

**Request body:** `{ "chatIds": ["guid", "guid"] }`

**Response `200`:** `CommonResponse<int>` — `data` là **số chat vừa được mark-read**, dạng số thuần.

```json
{ "isSuccess": true, "statusCode": 200, "data": 5, "listErrors": null }
```

> ⚠️ **Sửa 2026-08-02:** doc cũ ghi `CommonResponse<{ markedCount: int }>` — sai. `ChatMarkAsReadResponse : CommonResponse<int>`, handler set `Data = <số int>`. Đọc `data.markedCount` sẽ ra `undefined`.
>
> `chatIds` rỗng hoặc không có chat hợp lệ → vẫn trả `200` với `data: 0` (kèm message giải thích), **không** phải lỗi.

> ⚠️ **Ghi read-receipt là BẤT ĐỒNG BỘ.** Handler chỉ lọc chatId hợp lệ rồi **enqueue** vào `IChatReadReceiptQueue`; `ChatReadReceiptBulkWriter` mới ghi DB theo batch **100 record hoặc mỗi 1 giây** (cái nào đến trước). Hệ quả cho FE:
> - `data` trả về là **số chat được nhận vào hàng đợi**, không phải số đã commit xuống DB.
> - Gọi `GET .../unread-count` hoặc `GET .../readers` **ngay sau** mark-read có thể vẫn thấy giá trị cũ trong ~1 giây. Đừng dùng nó để assert kết quả — hãy cập nhật state ở client thay vì refetch ngay.

---

### `DELETE /api/tickets/{ticketId}/chats/bulk`

**Mục đích:** Soft-delete **nhiều chat cùng lúc**. Không all-or-nothing — chat nào không đủ quyền/không hợp lệ thì bị **bỏ qua** và liệt kê trong `skippedIds`.

**Auth:** Mọi role đã đăng nhập (quyền xoá từng chat vẫn được kiểm riêng như `DELETE .../chats/{id}`).

**Request body:**

| Field | Type | Bắt buộc | Validation |
|---|---|---|---|
| `chatIds` | `Guid[]` | ✅ | Không được rỗng · **tối đa 50** id mỗi lần |

**Response `200`:** `CommonResponse<ChatBulkDeleteResultDTO>`

| Field | Type | Mô tả |
|---|---|---|
| `deleted` | `int` | Số chat đã xoá thành công |
| `skipped` | `int` | Số chat bị bỏ qua |
| `skippedIds` | `string[]` | Danh sách id bị bỏ qua (luôn là mảng, rỗng nếu không có) |

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": { "deleted": 3, "skipped": 1, "skippedIds": ["guid-4"] },
  "listErrors": null
}
```

**Lỗi:** `400` — `chatIds` rỗng (`listErrors[].field = "ChatIds"`) hoặc **vượt 50 phần tử**.

---

## Nhóm — Admin Ticket Chats

Base path: `/api/admin/tickets/{ticketId}/chats`
**Auth:** Bắt buộc — **Admin only** (`[Authorize(Roles = "Admin")]`)

> Cho phép Admin thêm/sửa/xóa chat trên ticket đã **Closed** — vượt qua giới hạn bình thường. Bắt buộc kèm `overrideReason` mọi thao tác.

### Tóm tắt endpoints

| Method | Path | Mô tả |
|---|---|---|
| `POST` | `/api/admin/tickets/{ticketId}/chats/closed-override` | Thêm chat vào ticket đã closed |
| `PUT` | `/api/admin/tickets/{ticketId}/chats/{id}/closed-override` | Sửa chat trên ticket đã closed |
| `DELETE` | `/api/admin/tickets/{ticketId}/chats/{id}/closed-override` | Xóa chat trên ticket đã closed |
| `PATCH` | `/api/admin/tickets/{ticketId}/chats/{id}/restore` | Khôi phục chat đã soft-delete |

---

### `POST /api/admin/tickets/{ticketId}/chats/closed-override`

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `body` | `string` | **Bắt buộc** | Nội dung chat |
| `isInternal` | `bool` | Không (mặc định `false`) | Chat nội bộ |
| `bodyFormat` | `ChatBodyFormatEnum` | Không (mặc định `PlainText`) | Định dạng |
| `overrideReason` | `string` | **Bắt buộc** | Lý do override |

**Response `201`:** `TicketActionResponse`

---

### `PUT /api/admin/tickets/{ticketId}/chats/{id}/closed-override`

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `body` | `string` | **Bắt buộc** | Nội dung mới |
| `overrideReason` | `string` | **Bắt buộc** | Lý do override |

**Response `200`:** `TicketActionResponse`

---

### `DELETE /api/admin/tickets/{ticketId}/chats/{id}/closed-override`

**Request body:** `{ "overrideReason": "string" }` (bắt buộc)

**Response `200`:** `TicketActionResponse`

---

### `PATCH /api/admin/tickets/{ticketId}/chats/{id}/restore`

**Mục đích:** Khôi phục chat đã bị soft-delete — `IsDeleted=false`.

**Request body:** Không cần

**Response `200`:** `TicketActionResponse`

---

## Nhóm — Chats Utilities

Các endpoint cross-ticket — không gắn với 1 ticket cụ thể.

---

### `GET /api/chats/me`

**Mục đích:** Lấy tất cả chat của user hiện tại trên toàn bộ tickets (phân trang).
**Auth:** Mọi role

**Response `200`:** `CommonResponse<PaginationResponse<TicketChatDTO>>`

---

### `POST /api/chats/erase-my-data`

**Mục đích:** GDPR — xóa nội dung toàn bộ chat của user hiện tại (`body` được overwrite bằng `[ERASED]`).
**Auth:** Mọi role

**Response `200`:** `CommonResponse<object>` — **`data` luôn `null`**. Số lượng đã xoá chỉ nằm trong `message`:

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã xóa dữ liệu GDPR: 12 tin nhắn chat đã được ẩn danh hóa.",
  "data": null,
  "listErrors": null
}
```

> ⚠️ **Sửa 2026-08-02:** doc cũ ghi `CommonResponse<{ erasedCount: int }>` — **field `erasedCount` không tồn tại**. Handler không set `Data` bao giờ. FE muốn hiện số lượng phải parse từ `message` (hoặc chỉ hiện thông báo thành công).

---

### `GET /api/chats/search`

**Mục đích:** Tìm kiếm chat toàn hệ thống (Admin/Manager).
**Auth:** Manager hoặc Admin

**Query params:** `q` (full-text), `ticketId`, `authorUserId`, `from`, `to`, `page`, `pageSize`

**Response `200`:** `CommonResponse<PaginationResponse<TicketChatDTO>>`

---

### `GET /api/chats/mentions/me`

**Mục đích:** Danh sách mention của user hiện tại trên mọi ticket.
**Auth:** Mọi role

**Query params:** `page`, `pageSize`

**Response `200`:** `CommonResponse<PaginationResponse<TicketChatMentionDTO>>`

---

## Nhóm — Ticket Participants

Base path: `/api/tickets/{ticketId}/participants`
**Auth:** Bắt buộc đăng nhập (quyền chi tiết theo từng endpoint bên dưới)

### Tóm tắt endpoints

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `GET` | `/api/tickets/{ticketId}/participants` | Bất kỳ ai access được ticket | Danh sách participant **active** |
| `GET` | `/api/tickets/{ticketId}/participants/history` | Staff/Manager/Admin | Toàn bộ lịch sử (gồm đã bị xóa) |
| `POST` | `/api/tickets/{ticketId}/participants` | Manager/Admin hoặc PrimaryAssignee | Thêm 1 participant |
| `POST` | `/api/tickets/{ticketId}/participants/bulk` | Manager/Admin | Thêm nhiều (all-or-nothing) |
| `PATCH` | `/api/tickets/{ticketId}/participants/{userId}` | Manager/Admin | Đổi `participantType`/`canPost`/`canViewInternal` |
| `DELETE` | `/api/tickets/{ticketId}/participants/{userId}` | Manager/Admin | Xóa participant |
| `POST` | `/api/tickets/{ticketId}/participants/leave` | Chính chủ | Tự rời ticket |

### `ParticipantTypeEnum`

Serialize dạng **chuỗi** (như mọi enum khác của TicketService).

| Value | Mô tả |
|---|---|
| `Owner` | Customer sở hữu ticket |
| `PrimaryAssignee` | Staff xử lý chính |
| `Collaborator` | Cùng xử lý |
| `Watcher` | Chỉ theo dõi |
| `Delegate` | Được ủy quyền |
| `PreviousAssignee` | Người xử lý trước đó |

### `TicketParticipantDTO`

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bản ghi participant |
| `ticketId` | `string` | ID ticket |
| `userId` | `string` | ID user |
| `displayName` | `string` | Tên hiển thị — BE resolve từ `CustomerAccounts`/`StaffAccounts` theo `userRole`; fallback về `userId` nếu không tìm thấy |
| `userRole` | `ActorRoleEnum` | Role của user (chuỗi, vd `"Staff"`) |
| `participantType` | `ParticipantTypeEnum` | Vai trò tham gia (chuỗi) |
| `canPost` | `bool` | Được gửi chat trên ticket |
| `canViewInternal` | `bool` | Được xem/nhận chat nội bộ |
| `addedByUserId` | `string` | Ai thêm vào |
| `addedAt` | `string` | Thời điểm thêm (ISO-8601 UTC) |

### `GET /api/tickets/{ticketId}/participants`

**Mục đích:** Danh sách participant **đang active** của ticket. Đây là nguồn đúng để đổ dropdown @-mention trong composer chat — **không** dùng danh sách tác giả đã chat, vì người mới được add vào ticket nhưng chưa nhắn gì vẫn phải tag được.

**Auth:** Bất kỳ ai access được ticket (Customer chủ ticket, PrimaryHandler, participant, Manager/Admin). BE tự check và trả `403` nếu không có quyền.

**Response `200`:** `CommonResponse<TicketParticipantDTO[]>` — chỉ gồm bản ghi `removedAt == null && !isDeleted`, sắp xếp theo `addedAt` tăng dần.

```json
{
  "isSuccess": true,
  "data": [
    {
      "id": "…",
      "ticketId": "…",
      "userId": "…",
      "displayName": "Nguyễn Văn A",
      "userRole": "Staff",
      "participantType": "PrimaryAssignee",
      "canPost": true,
      "canViewInternal": true,
      "addedByUserId": "…",
      "addedAt": "2026-08-02T13:24:00Z"
    }
  ]
}
```

**Gợi ý FE (dropdown mention):** lọc bỏ chính mình, bỏ ai `canPost == false`, và khi đang soạn chat **nội bộ** thì chỉ giữ ai `canViewInternal == true`.

**Lỗi:** `401` chưa đăng nhập · `403` không có quyền truy cập ticket · `404` ticket không tồn tại.

---

### `GET /api/tickets/{ticketId}/participants/history`

**Mục đích:** Toàn bộ lịch sử participant, **bao gồm** người đã bị xóa khỏi ticket.
**Auth:** Staff/Manager/Admin

**Response `200`:** `CommonResponse<ParticipantHistoryDTO[]>`

**`ParticipantHistoryDTO`** — mọi field của `TicketParticipantDTO` **trừ `displayName`**, cộng thêm:

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `removedAt` | `string?` | Null nếu còn active | Thời điểm bị gỡ khỏi ticket (UTC) |
| `removedByUserId` | `string?` | Null | Ai gỡ |
| `removeReason` | `string?` | Null | Lý do gỡ |

---

### `POST /api/tickets/{ticketId}/participants`

**Mục đích:** Thêm 1 participant vào ticket.
**Auth:** Manager/Admin hoặc PrimaryAssignee của ticket

**Request body:** `{ userId, userRole, participantType, canPost, canViewInternal }` — `canPost` default `true`, `canViewInternal` default `false`. Không gửi `actorUserId`/`actorName`, server lấy từ JWT.

---

### `POST /api/tickets/{ticketId}/participants/bulk`

**Mục đích:** Thêm nhiều participant cùng lúc, **all-or-nothing** (1 item lỗi → rollback toàn bộ).
**Auth:** Manager/Admin

**Request body:** `{ "participants": [ { userId, userRole, participantType, canPost, canViewInternal }, … ] }`

---

### `PATCH /api/tickets/{ticketId}/participants/{userId}`

**Mục đích:** Đổi vai trò/quyền của 1 participant.
**Auth:** Manager/Admin

**Request body:** `{ participantType, canPost?, canViewInternal? }` — `canPost`/`canViewInternal` nullable, bỏ trống thì giữ nguyên.

---

### `DELETE /api/tickets/{ticketId}/participants/{userId}`

**Mục đích:** Xóa participant khỏi ticket (soft — set `removedAt`).
**Auth:** Manager/Admin. Xóa `Owner` **chỉ Admin** và **bắt buộc** `removeReason`.

**Request body:** `{ "removeReason": "…" }`

---

### `POST /api/tickets/{ticketId}/participants/leave`

**Mục đích:** Participant tự rời ticket.
**Auth:** Chính chủ (server lấy identity từ JWT)

**Request body:** `{ "leaveReason": "…" }`

---

## Nhóm 2 — Customer

Base path: `/api/customer/tickets`
**Auth:** Bắt buộc — Customer role

---

### `GET /api/customer/tickets/me`

**Mục đích:** Customer lấy danh sách ticket của chính mình. Hệ thống tự động lọc theo `customerId` từ JWT.

**Auth:** Bắt buộc (Customer)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Status` | `TicketStatusEnum?` | Lọc theo trạng thái |
| `PageNumber` | `int` | Trang (mặc định 1; giá trị ≤ 0 → tự về 1) |
| `PageSize` | `int` | Số item/trang (mặc định 10; max 100; ≤ 0 → về 10) |

> **Internal param:** BE expose thêm `ActorCustomerId` (uuid) dùng nội bộ để BE đọc từ JWT. FE **không gửi** param này.

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketDTO>>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập

---

### `POST /api/customer/tickets`

**Mục đích:** Customer tạo yêu cầu hỗ trợ mới.

**Auth:** Bắt buộc (Customer)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `title` | `string` | Bắt buộc | Không rỗng/whitespace (`400` nếu thiếu) | Tiêu đề ngắn gọn |
| `description` | `string` | Bắt buộc | Không rỗng/whitespace (`400` nếu thiếu) | Mô tả chi tiết vấn đề |
| `category` | `TicketCategoryEnum` | Bắt buộc | — | Loại lỗi |
| `batteryAssetIds` | `Guid[]` | **Bắt buộc** | ≥ 1 phần tử, không chứa Guid rỗng, **không trùng lặp** | Danh sách ID pin gặp lỗi |
| `incidentDetectedAt` | `DateTime` | **Bắt buộc** | UTC, **không được ở tương lai** | Thời điểm Customer phát hiện sự cố |
| `attachments` | `TicketAttachmentInput[]` | Không (default `[]`) | Xem bảng dưới | File đính kèm (metadata do client gửi sau khi upload lên FileStorage) |

> ⚠️ **Sửa so với doc cũ:** field `batteryAssetId` (`Guid?`, optional) **không còn tồn tại** — đã thay bằng **`batteryAssetIds` (mảng, bắt buộc ≥1)**. Ngoài ra `incidentDetectedAt` là field **bắt buộc** mới.

**`TicketAttachmentInput`** — mọi field đều bắt buộc (trừ `sizeBytes` chỉ cần ≥ 0):

| Field | Type | Validation |
|---|---|---|
| `fileId` | `Guid` | Không được rỗng |
| `fileName` | `string` | Không rỗng, ≤ 256 ký tự |
| `contentType` | `string` | Không rỗng, ≤ 100 ký tự |
| `sizeBytes` | `int64` | Không được âm |
| `url` | `string` | Không rỗng, ≤ 2000 ký tự |

> **Lưu ý:** `TicketCreateCommand.ValidateAsync()` chỉ check **không rỗng/whitespace** cho `title`/`description` — **KHÔNG** enforce giới hạn độ dài (max 200/2000). FE nên tự giới hạn input để tránh dữ liệu quá dài.
> **Ownership của file cố ý không được TicketService kiểm tra.**

**Response thành công `201`:** `TicketActionResponse`

**Lỗi thường gặp:**
- `400` — Dữ liệu không hợp lệ (title/description rỗng, category không hợp lệ)

---

### `POST /api/customer/tickets/{id}/reopen`

**Mục đích:** Customer yêu cầu mở lại ticket khi chưa hài lòng với kết quả.

**Auth:** Bắt buộc (Customer)

**Điều kiện:**
- Ticket phải ở trạng thái `ClosedPendingRate`.
- Trong vòng 7 ngày kể từ khi Manager approve.
- Lần mở lại thứ 2 trở đi → tự động escalate.

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reopenReason` | `string` | **Bắt buộc** | Lý do mở lại — không được rỗng/whitespace (`400` nếu thiếu) |

**Response thành công `200`:** `TicketActionResponse` — ticket chuyển sang `Open` (chờ Manager gán lại Staff). Lần reopen thứ 2 trở đi (`ReopenCount >= 2`) → tự động `Escalated`.

> Rule 7 ngày nằm ngay trong transition rule `ClosedPendingRate → Open`: `Admin` bypass hoàn toàn; Customer phải là chủ ticket **và** `(UtcNow − ApprovedAt) ≤ 7 ngày` (`ApprovedAt` phải khác null).

**Lỗi thường gặp:**
- `400` — Thiếu `reopenReason`
- `403` — Quá hạn 7 ngày (tính từ `approvedAt` — thời điểm Manager approve kết quả resolve) hoặc sai trạng thái ticket. Admin bypass được rule 7 ngày.

---

### `POST /api/customer/tickets/{id}/rate`

**Mục đích:** Customer đánh giá chất lượng xử lý và đóng ticket chính thức.

**Auth:** Bắt buộc (Customer)

**Điều kiện:** Ticket phải ở trạng thái `ClosedPendingRate`.

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `rating` | `int` | Bắt buộc | 1–5 | Số sao đánh giá |
| `ratingComment` | `string?` | Không | — | Nhận xét chi tiết |

**Response thành công `200`:** `TicketActionResponse` — ticket chuyển sang `Closed`.

**Lỗi thường gặp:**
- `403` — Sai trạng thái ticket

---

## Nhóm 3 — Staff

Base path: `/api/staff/tickets`
**Auth:** Bắt buộc — Staff role

---

### `GET /api/staff/tickets/me`

**Mục đích:** Staff lấy danh sách ticket được giao cho mình. Hệ thống tự động lọc theo `assignedStaffId` từ JWT.

**Auth:** Bắt buộc (Staff)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Status` | `TicketStatusEnum?` | Lọc theo trạng thái |
| `SlaOpen` | `bool?` | `true` = chỉ lấy ticket **đang trong vòng theo dõi SLA**: status ∈ {`Assigned`, `InProgress`, `WaitingCustomer`, `WaitingParts`, `WaitingOnsiteSchedule`, `Escalated`} **và** có `slaTimer != null`. Dùng cho bảng **SLA Monitor** — filter server-side, không bị cap theo pageSize. Bỏ trống/`false` → không filter |
| `SortBy` | `string?` | `"slaRemaining"` (không phân biệt hoa thường) = sort theo hạn SLA (`slaTimer.dueAt`) **tăng dần** — ticket gần breach lên đầu; ticket không có timer xếp cuối; 2 ticket cùng hạn thì tie-break theo `Priority` tăng dần. Giá trị khác/bỏ trống → sort mặc định: `Priority` tăng dần (P1 trước) rồi `CreatedAt` giảm dần |
| `PageNumber` | `int` | Trang (mặc định 1; giá trị ≤ 0 → tự về 1) |
| `PageSize` | `int` | Số item/trang (mặc định 10; max 100; ≤ 0 → về 10) |

> **Internal param:** BE expose thêm `ActorStaffId` (uuid) dùng nội bộ để BE đọc từ JWT. FE **không gửi** param này.

**Ví dụ — bảng SLA Monitor (thay cho việc FE tự lọc client-side trên 1 trang):**
```
GET /api/staff/tickets/me?slaOpen=true&sortBy=slaRemaining&pageNumber=1&pageSize=20
```

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketDTO>>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập

---

### `GET /api/staff/tickets/dashboard/stats`

**Mục đích:** Snapshot KPI dashboard cho **chính Staff đang đăng nhập** — thay cho việc FE tự đếm trên 1 trang list (bị cap 100 → sai số khi vượt pageSize). Phục vụ Staff Dashboard (KPI đang phụ trách / sắp breach / quá hạn / đã xử lý, gauge SLA, donut trạng thái, donut rủi ro SLA, chart 7 ngày) và các KPI của trang SLA Monitor.

**Auth:** Bắt buộc (Staff). Scope tự động theo `assignedStaffId` từ JWT.

**Query params:** Không có — endpoint snapshot, **không nhận from/to** (báo cáo time-series dùng `GET /api/reports/*`). FE nên cache ~1 phút (staleTime).

**Response thành công `200`:** `CommonResponse<StaffTicketDashboardStatsDto>`

```json
{
  "isSuccess": true, "statusCode": 200, "message": "",
  "data": {
    "openCount": 5,
    "resolvedCount": 12,
    "nearBreachCount": 1,
    "breachedCount": 1,
    "pausedCount": 1,
    "slaMonitoredCount": 5,
    "sla": { "met": 10, "breached": 2, "running": 4, "paused": 1, "compliancePercent": 83.33 },
    "countByStatus": {
      "New": 0, "Open": 0, "Assigned": 2, "InProgress": 2,
      "WaitingCustomer": 0, "WaitingParts": 1, "WaitingOnsiteSchedule": 0,
      "Resolved": 3, "Escalated": 0, "ClosedPendingRate": 2, "Closed": 7,
      "ClosedRejected": 1, "Incident": 0
    },
    "slaRisk": { "healthy": 3, "near": 1, "breached": 1 },
    "createdTrend7Days": [
      { "date": "2026-07-01", "count": 0 },
      { "date": "2026-07-02", "count": 1 },
      { "date": "2026-07-03", "count": 0 },
      { "date": "2026-07-04", "count": 2 },
      { "date": "2026-07-05", "count": 0 },
      { "date": "2026-07-06", "count": 1 },
      { "date": "2026-07-07", "count": 2 }
    ]
  },
  "listErrors": null
}
```

**`StaffTicketDashboardStatsDto`** — mọi field đều **không null** (list/dict/object luôn được khởi tạo; `data` chỉ null khi lỗi):

| Field | Type | Mô tả |
|---|---|---|
| `openCount` | `int` | Số ticket đang phụ trách — status **không** thuộc {`Resolved`, `ClosedPendingRate`, `Closed`, `ClosedRejected`} |
| `resolvedCount` | `int` | Số ticket đã xử lý — status ∈ {`Resolved`, `ClosedPendingRate`, `Closed`} (**không** gồm `ClosedRejected` — bị từ chối, không phải hoàn tất) |
| `nearBreachCount` | `int` | Trong nhóm monitored: timer `Running` còn **≤ 25%** thời gian (`remainingPercent ≤ 25`) |
| `breachedCount` | `int` | Trong nhóm monitored: timer đã `Breached` |
| `pausedCount` | `int` | Trong nhóm monitored: timer đang `Paused` |
| `slaMonitoredCount` | `int` | Tổng ticket "đang theo dõi SLA" — status ∈ {`Assigned`, `InProgress`, `WaitingCustomer`, `WaitingParts`, `WaitingOnsiteSchedule`, `Escalated`} **và** có SLA timer (đúng bộ lọc `slaOpen=true` của `GET /me`) |
| `sla` | `SlaSummaryDto` | Tổng hợp **toàn bộ** SLA timer của ticket được gán cho staff (kể cả ticket đã đóng) — xem bảng dưới |
| `countByStatus` | `Dictionary<string,int>` | Số ticket theo từng status — zero-fill từ `Enum.GetValues<TicketStatusEnum>()` nên **luôn đủ 13 key** (key PascalCase), status không có ticket = `0`. ⚠️ **13, không phải 14** — không có key `Approved` |
| `slaRisk` | `SlaRiskDto` | Phân bố rủi ro trên nhóm monitored: `healthy` + `near` + `breached` = `slaMonitoredCount`. `healthy` = còn > 25% thời gian **hoặc đang Paused** |
| `createdTrend7Days` | `DailyCountPointDto[]` | Số ticket (được gán cho staff) tạo mới theo ngày — **luôn đúng 7 phần tử** (6 ngày trước + hôm nay), bucket theo **UTC**, ngày trống = `0` |

**`SlaSummaryDto`:**

| Field | Type | Mô tả |
|---|---|---|
| `met` | `int` | Số timer `Met` (hoàn thành đúng hạn) |
| `breached` | `int` | Số timer `Breached` |
| `running` | `int` | Số timer `Running` |
| `paused` | `int` | Số timer `Paused` |
| `compliancePercent` | `number` | `met / (met + breached) × 100`, làm tròn 2 chữ số. **= 100 khi chưa có timer nào kết thúc** (met + breached = 0) — tránh dashboard hiện 0% khi chưa có data |

**`SlaRiskDto`:** `healthy` (`int`) · `near` (`int`) · `breached` (`int`) — như mô tả ở bảng trên.

**`DailyCountPointDto`:** `date` (`string`, dạng `"2026-07-07"` — ngày UTC) · `count` (`int`).

> ⚠️ **Timezone:** trend bucket theo **ngày UTC** — FE vẽ chart theo giờ VN (UTC+7) cần lưu ý ranh giới ngày.

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập / không đọc được userId từ token
- `403` — Không phải role Staff

---

### `POST /api/staff/tickets/{id}/start`

**Mục đích:** Staff xác nhận bắt đầu xử lý ticket đã được giao.

**Hành động phụ:** Hệ thống tự động tạo một Maintenance Log mới với `startedAt` là thời điểm hiện tại.

**Auth:** Bắt buộc (Staff)

**Path param:** `id` — UUID của ticket.

**Request body:** **Không có.**

> ⚠️ **Sửa so với doc cũ:** action method `Start(Guid id, CancellationToken ct)` **không nhận `[FromBody]`** — controller tự dựng `TicketStartCommand` từ route param + JWT. Field `logType` tồn tại trên command nhưng **client không có cách nào gửi**; log tự động luôn dùng giá trị mặc định. Gửi body cũng bị bỏ qua.
>
> Không có field tọa độ check-in ở endpoint này — nếu cần ghi tọa độ, dùng `checkInLatitude`/`checkInLongitude`/`checkInAt` khi tạo maintenance log qua `POST /api/tickets/{ticketId}/maintenance-logs`.

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/staff/tickets/{id}/hold`

**Mục đích:** Staff tạm dừng xử lý ticket vì lý do khách quan. SLA bị tạm dừng.

**Auth:** Bắt buộc (Staff)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reason` | `PauseReasonEnum` | Bắt buộc | Lý do tạm dừng |
| `note` | `string?` | Không | Ghi chú chi tiết |

**Response thành công `200`:** `TicketActionResponse` — trạng thái chuyển sang `WaitingCustomer` / `WaitingParts` / `WaitingOnsiteSchedule` tương ứng.

---

### `POST /api/staff/tickets/{id}/resume`

**Mục đích:** Staff tiếp tục xử lý ticket từ trạng thái tạm dừng. Trạng thái quay lại `InProgress`, SLA tiếp tục đếm.

**Auth:** Bắt buộc (Staff)

**Path param:** `id` — UUID của ticket.

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/staff/tickets/{id}/resolve`

**Mục đích:** Staff báo cáo đã hoàn thành giải quyết. Chuyển trạng thái `InProgress → Resolved`, chờ Manager phê duyệt.

**Auth:** Bắt buộc (Staff)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `resolutionSummary` | `string` | **Bắt buộc** | Tóm tắt cách giải quyết — không được rỗng/whitespace (`400` nếu thiếu) |

**Hành động phụ:** Khi resolve, hệ thống tự động đóng (`completedAt = now`) **tất cả** maintenance log đang mở của ticket; log nào còn `summary` rỗng / `"Đang thực hiện..."` sẽ được điền bằng `resolutionSummary`.

**Response thành công `200`:** `TicketActionResponse`

**Lỗi thường gặp:**
- `400` — Thiếu `resolutionSummary`
- `403` — Với ticket đã `Escalated`:
  - Chỉ Staff **đang được assign sau escalation** mới resolve được (Staff cũ → `403`).
  - Nếu escalate vì `SkillGap`: Staff phải có `SkillTier >= 2` (Tier 2 trở lên), nếu không → `403`.

---

### `POST /api/staff/tickets/{id}/escalate-request`

**Mục đích:** Staff chủ động yêu cầu chuyển cấp xử lý khi vượt quá khả năng. Chờ Manager điều phối.

**Auth:** Bắt buộc (Staff)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reason` | `EscalationReasonEnum` | Bắt buộc | Lý do yêu cầu — `SkillGap`, `PartsRequired`, `SafetyConcern`, v.v. |
| `note` | `string?` | Không | Ghi chú bổ sung |

**Response thành công `200`:** `TicketActionResponse`

---

## Nhóm 4 — Admin/Manager

Base path: `/api/admin/tickets`
**Auth:** Bắt buộc — Manager hoặc Admin role

---

### `GET /api/admin/tickets`

**Mục đích:** Admin/Manager lấy danh sách toàn bộ ticket với bộ lọc nâng cao.

**Auth:** Bắt buộc (Admin hoặc Manager)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Keyword` | `string?` | Tìm theo mã hoặc tiêu đề |
| `Status` | `TicketStatusEnum?` | Lọc theo trạng thái |
| `Priority` | `TicketPriorityEnum?` | Lọc theo priority |
| `Category` | `TicketCategoryEnum?` | Lọc theo loại lỗi |
| `BatteryAssetId` | `Guid?` | Lọc theo thiết bị |
| `IsDescending` | `bool` | (legacy) Đảo chiều theo `createdAt`, mặc định `true`. **Nếu có `SortDir` thì `SortDir` thắng** |
| `SortBy` | `string?` | Cột sort server-side. Whitelist: `code`, `title`, `category`, `status`, `priority`, `createdAt`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | `asc` \| `desc` (mặc định `desc`; giá trị lạ → `desc`) |
| `PageNumber` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang |

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). Sort theo `status`/`priority`/`category` = theo **giá trị số** enum. Chi tiết enum + nullable: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketDTO>>`

---

### `GET /api/admin/tickets/queue`

**Mục đích:** Manager xem queue ticket đang chờ triage — các ticket ở trạng thái **`New`**, chưa xóa, chưa bị gộp (`mergedIntoTicketId == null`), sắp xếp theo Priority tăng dần (P1 trước) rồi `CreatedAt` **tăng dần** (ticket cũ lên trước).

> ⚠️ **Sửa so với doc cũ:** queue lọc `Status == New` (**không phải `Open`**). Sau khi triage, ticket rời queue này.

**Auth:** Bắt buộc (Manager hoặc Admin)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Priority` | `TicketPriorityEnum?` | Lọc theo priority |
| `Category` | `TicketCategoryEnum?` | Lọc theo loại lỗi |
| `PageNumber` | `int` | Trang |
| `PageSize` | `int` | Số item/trang |

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketDTO>>`

---

### `POST /api/admin/tickets/{id}/triage`

**Mục đích:** Manager phê duyệt tính hợp lệ của ticket và xác định mức độ ưu tiên. Chuyển trạng thái **`New → Open`**. Priority được tính tự động từ `impact × urgency`.

> ⚠️ **Sửa so với doc cũ:** transition thật là `New → Open`, **không phải `Open → Approved`** (state `Approved` không tồn tại). Ticket chờ triage nằm ở state `New`; sau triage sang `Open` để chờ gán Staff.

**Auth:** Bắt buộc (Manager hoặc Admin — controller `[Authorize(Roles = "Manager,Admin")]`)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `impact` | `ImpactScopeEnum` | Bắt buộc | Phạm vi ảnh hưởng |
| `urgency` | `UrgencyLevelEnum` | Bắt buộc | Độ khẩn cấp |
| `manualPriority` | `TicketPriorityEnum?` | Không | Gán priority thủ công (override matrix — cần kèm `priorityOverrideReason`) |
| `priorityOverrideReason` | `string?` | Không | Lý do override priority (bắt buộc nếu `manualPriority` khác giá trị matrix) |
| `managerComment` | `string?` | Không | Nhận xét của Manager |

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/admin/tickets/{id}/triage-reject`

**Mục đích:** Manager/Admin từ chối ticket ngay từ bước phân loại (Triage) khi ticket không hợp lệ (spam, trùng lặp, ngoài scope dịch vụ). Chuyển trạng thái **`New → ClosedRejected`**. Lưu activity `Rejected` kèm `reason` vào timeline.

> ⚠️ **Sửa so với doc cũ:** transition là `New → ClosedRejected`. State `Open` **không** có rule sang `ClosedRejected` — gọi triage-reject trên ticket đã triage sẽ trả `403`.

**Auth:** Bắt buộc (Manager hoặc Admin)

**Path param:** `id` — UUID của ticket.

**Request body:**

```json
{
  "reason": "Ticket trùng lặp với #TKT-2606-0001 — đã xử lý ở ticket gốc."
}
```

**Field rules:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `reason` | `string` | ✅ | Không rỗng/whitespace | Lý do từ chối — bắt buộc, lưu audit trail |

> `ticketId` được bind từ route param (`[JsonIgnore]`), không gửi trong body. `managerId` + `managerName` được resolve từ JWT.

**Response thành công `200`:** `TicketActionResponse`

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Triage rejected.",
  "data": {
    "id": "guid",
    "code": "TKT-2606-0001",
    "status": "ClosedRejected"
  }
}
```

**Lỗi thường gặp:**
- `400` — Thiếu `reason` (`listErrors[].field = "Reason"`) hoặc `ticketId` rỗng
- `401` — Chưa đăng nhập
- `403` — Không có role Manager/Admin, hoặc ticket không ở trạng thái `New`
- `404` — Không tìm thấy ticket

---

### `POST /api/admin/tickets/{id}/assign`

**Mục đích:** Manager gán nhân viên xử lý cho ticket đã triage. Chuyển trạng thái **`Open → Assigned`**.

**Auth:** Bắt buộc (Manager hoặc Admin)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `primaryHandlerStaffId` | `Guid` | **Bắt buộc** | Staff xử lý chính — **phải đủ tier theo priority** của ticket |
| `supporterStaffIds` | `Guid[]` | Không (default `[]`) | Staff hỗ trợ — **không** giới hạn tier. Không được chứa `primaryHandlerStaffId`, không được trùng lặp |
| `notes` | `string?` | Không | Ghi chú khi gán |

> ⚠️ **Sửa so với doc cũ:** field là **`primaryHandlerStaffId`**, KHÔNG phải `staffId`. Đồng thời có thêm `supporterStaffIds` (gán nhiều Staff hỗ trợ trong cùng 1 request).

**Response thành công `200`:** `TicketActionResponse`

**Lỗi thường gặp:**

| Status | Trường hợp |
|---|---|
| `400` | `primaryHandlerStaffId` rỗng · `supporterStaffIds` chứa chính PrimaryHandler · `supporterStaffIds` trùng lặp |
| `403` | Tài khoản Staff bị khóa/vô hiệu hóa (`status != Active`) · Staff `isAvailable = false` · **Staff không đủ `SkillTier`** theo priority ticket · ticket không ở trạng thái `Open` |
| `404` | Không tìm thấy ticket · không tìm thấy Staff PrimaryHandler |

---

### `POST /api/admin/tickets/{id}/reassign`

**Mục đích:** Manager điều chuyển ticket sang cho nhân viên khác. Lưu lịch sử thay đổi Staff.

**Auth:** Bắt buộc (Manager hoặc Admin — controller `[Authorize(Roles = "Manager,Admin")]`)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `newPrimaryHandlerStaffId` | `Guid` | **Bắt buộc** | ID Staff mới làm PrimaryHandler |
| `reason` | `string` | **Bắt buộc** | Lý do điều chuyển — không được rỗng/whitespace (`400` nếu thiếu) |

> ⚠️ **Sửa so với doc cũ:** field là **`newPrimaryHandlerStaffId`**, KHÔNG phải `newStaffId`. Staff cũ được chuyển sang role `PreviousPrimaryHandler` trong `assignments`.

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/admin/tickets/{id}/approve`

**Mục đích:** Manager phê duyệt kết quả giải quyết của Staff. Chuyển trạng thái `Resolved → ClosedPendingRate`, kích hoạt yêu cầu đánh giá cho Customer.

**Auth:** Bắt buộc (Manager hoặc Admin — controller `[Authorize(Roles = "Manager,Admin")]`)

**Path param:** `id` — UUID của ticket.

**Query param:** _(không phải request body)_

| Param | Type | Mô tả |
|---|---|---|
| `comment` | `string?` | Nhận xét của Manager (optional) |

> **Lưu ý FE:** `comment` truyền qua query string, không phải body. Dùng `axios.post(url, null, { params: { comment } })`.

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/admin/tickets/{id}/reject`

**Mục đích:** Manager từ chối kết quả giải quyết của Staff (kết quả chưa đạt). Trạng thái chuyển thẳng `Resolved → InProgress` để Staff tiếp tục xử lý (**không** đi qua `ClosedRejected`).

**Auth:** Bắt buộc (Manager hoặc Admin — controller `[Authorize(Roles = "Manager,Admin")]`)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reason` | `string` | **Bắt buộc** | Lý do từ chối — không được rỗng/whitespace (`400` nếu thiếu). Được lưu vào `ticket.reason` + activity `Rejected` |

**Response thành công `200`:** `TicketActionResponse`

**Lỗi thường gặp:**
- `400` — Thiếu `reason`
- `403` — Ticket không ở trạng thái `Resolved`

---

### `POST /api/admin/tickets/{id}/escalate`

**Mục đích:** Manager ép buộc chuyển cấp xử lý trong trường hợp khẩn cấp hoặc điều phối lại nguồn lực.

**Auth:** Bắt buộc (Manager hoặc Admin — controller `[Authorize(Roles = "Manager,Admin")]`)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reason` | `EscalationReasonEnum` | Bắt buộc | Lý do ép chuyển cấp |
| `note` | `string?` | Không | Ghi chú bổ sung |

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/admin/tickets/{id}/declare-incident`

**Mục đích:** Manager/Admin đánh dấu ticket là sự cố nghiêm trọng (Incident) để có quy trình xử lý ưu tiên. Hệ thống đồng thời ghi một bản ghi `TicketActivity` để lưu vết.

**Auth:** Bắt buộc (Admin hoặc Manager)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `incidentDescription` | `string` | Bắt buộc | Mô tả ngắn lý do declare incident (lưu vào `TicketActivity`). Không được rỗng/whitespace |

```json
{
  "incidentDescription": "Site mất điện diện rộng — ảnh hưởng 3 cluster pin"
}
```

> Hai field `ticketId` và `userId` cũng có trong command nhưng được gắn `[JsonIgnore]` — server tự bind từ path param và JWT claim, client không cần (và không thể) gửi trong body.

**Response thành công `200`:** `TicketActionResponse` — `isIncident = true`.

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | Validation: `incidentDescription` rỗng/whitespace (đi qua `ValidateAsync` của command) |
| `401` | Chưa đăng nhập |
| `403` | Không có role Manager/Admin |
| `404` | Không tìm thấy ticket |
| `409` | Ticket đã được đánh dấu là incident từ trước (idempotency — không thể declare lại) |

> **Lưu ý:** `409` là state conflict (vi phạm idempotency), không phải input error — phía client không nên retry bằng cách gửi lại request.

---

### `POST /api/admin/tickets/{id}/merge`

**Mục đích:** Manager gộp ticket **B** (nghi trùng, `id` trên route) vào ticket **A** (`targetTicketId` trong body). B được đóng (`Closed`), set `closeReason = MergedDuplicate` và `mergedIntoTicketId = A`, đồng thời **ẩn khỏi Manager queue**. Human-in-the-loop: dùng khi Manager xác nhận cờ `suspectedDuplicateOfTicketId` là trùng thật.

**Auth:** Bắt buộc — **chỉ role `Manager`** (`[Authorize(Roles = "Manager")]`).

**Path param:** `id` — UUID của ticket **bị gộp** (B).

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `targetTicketId` | `Guid` | ✅ | Ticket đích (A) — ticket được giữ lại |

> `ticketId` bind từ route (`[JsonIgnore]`); `managerId`/`managerName` lấy từ JWT.

**Response thành công `200`:** `TicketActionResponse`

**Lỗi thường gặp:**
- `400` — `targetTicketId` rỗng, hoặc **gộp ticket vào chính nó** (`ticketId == targetTicketId`)
- `404` — Không tìm thấy ticket nguồn hoặc đích
- `409` — Một trong hai ticket **đã được gộp trước đó** (`mergedIntoTicketId != null`)

> **Tác dụng phụ:** activity của ticket B xuất hiện trong timeline ticket A với `sourceTicketId = B`. Mọi mutation trên ticket đã merge bị chặn bởi `ClosedTicketMutationBehavior`.

---

### `POST /api/admin/tickets/{id}/re-verify`

**Mục đích:** Kích hoạt AI kiểm tra lại tính hợp lệ của 1 ticket — phát hiện ticket spam / trùng lặp.
**Chỉ áp dụng cho ticket do Customer tự tạo** (`origin = ManualByCustomer` = `1`) đang ở
`aiVerifyStatus ∈ {Pending, Skipped}`.

**Cách chạy:** handler reset `aiVerifyStatus` về `Pending`, xoá kết quả cũ
(`aiVerifyScore`, `aiVerifyReason`, `suspectedDuplicateOfTicketId`, `duplicateReason`), rồi gọi AI
**đồng bộ ngay trong request** — không qua event, vì ticket đã tồn tại nên không có race.

**Auth:** Bắt buộc — **chỉ role `Manager`**.

**Path param:** `id` — UUID của ticket.

**Request body:** Không có.

**Response thành công `200`:** `TicketActionResponse` (`data`: `id`, `ticketId`, `code`, `status`),
`message` = `"Đã kiểm tra lại bằng AI."` — sau đó đọc lại
`aiVerifyStatus` / `aiVerifyScore` / `aiVerifyReason` trên ticket detail.

**Lỗi thường gặp** (✅ = đã kiểm chứng bằng request thật trên môi trường Docker, 2026-08-06):

| Mã | Khi nào | `message` | |
|---|---|---|---|
| `401` | Chưa đăng nhập | — | |
| `403` | Không phải Manager | — | |
| `404` | Không tìm thấy ticket hoặc đã soft-delete | `"Không tìm thấy ticket."` | |
| `400` | `origin ≠ ManualByCustomer` — ticket auto từ alert / Staff tạo / hệ thống tạo | `"Chỉ ticket do khách hàng tạo mới có AI kiểm tra."` | ✅ |
| `400` | `aiVerifyStatus` đã là `Legitimate` hoặc `Suspicious` | `"Ticket đã được AI đánh giá — không cần kiểm tra lại."` | ✅ |
| `409` | Ticket đã đóng — chặn bởi `ClosedTicketMutationBehavior` **trước khi** vào handler | `"Ticket đã đóng nên không thể thay đổi."` | ✅ |

> ⚠️ Hai nhánh `400` ở trên **không** được ghi trong tài liệu cũ. Client chỉ bắt `404` sẽ hiển thị sai
> nguyên nhân cho Manager: nút "kiểm tra lại" trông như hỏng, trong khi thực ra ticket không đủ điều kiện.

> ℹ️ Muốn biết ticket nào gọi được: `origin = 1` **và** `aiVerifyStatus ∈ {1, 4}` **và** ticket chưa đóng.

---

### `GET /api/tickets/dashboard/stats`

> **Lưu ý route:** endpoint này nằm ở `/api/tickets/dashboard/stats` (KHÔNG phải `/api/admin/tickets/...`) nhưng thuộc nhóm Admin/Manager về mặt quyền — cùng pattern với `GET /api/battery/dashboard/stats` bên BatteryService.

**Mục đích:** Snapshot KPI ticket **toàn hệ thống** cho Dashboard Admin/Manager — thay cho việc FE tự đếm trên 1 trang list (cap 200 → sai số khi vượt pageSize). Phục vụ: KPI tickets mở/tổng, gauge tuân thủ SLA, pipeline 6 giai đoạn, chart "Ticket mới · 7 ngày", widget "Tải nhân sự" (workload theo staff).

**Auth:** Bắt buộc (Manager hoặc Admin).

**Query params:** Không có — endpoint snapshot, **không nhận from/to** (báo cáo time-series dùng `GET /api/reports/*`). FE nên cache ~1 phút (staleTime).

**Response thành công `200`:** `CommonResponse<TicketDashboardStatsDto>`

```json
{
  "isSuccess": true, "statusCode": 200, "message": "",
  "data": {
    "total": 128,
    "openCount": 38,
    "sla": { "met": 70, "breached": 12, "running": 30, "paused": 4, "compliancePercent": 85.37 },
    "countByStatus": {
      "New": 3, "Open": 8, "Assigned": 6, "InProgress": 12,
      "WaitingCustomer": 2, "WaitingParts": 3, "WaitingOnsiteSchedule": 1,
      "Resolved": 9, "Escalated": 2, "ClosedPendingRate": 5, "Closed": 70,
      "ClosedRejected": 6, "Incident": 0
    },
    "countByPriority": { "P1Critical": 9, "P2High": 34, "P3Normal": 77 },
    "createdTrend7Days": [
      { "date": "2026-07-01", "count": 4 },
      { "date": "2026-07-02", "count": 6 },
      { "date": "2026-07-03", "count": 2 },
      { "date": "2026-07-04", "count": 0 },
      { "date": "2026-07-05", "count": 5 },
      { "date": "2026-07-06", "count": 3 },
      { "date": "2026-07-07", "count": 7 }
    ],
    "openCountByStaff": [
      { "staffId": "3f2a4f35-...", "activeCount": 8 },
      { "staffId": "7a3bbc97-...", "activeCount": 5 }
    ]
  },
  "listErrors": null
}
```

**`TicketDashboardStatsDto`** — mọi field đều **không null** (list/dict/object luôn được khởi tạo; `data` chỉ null khi lỗi):

| Field | Type | Mô tả |
|---|---|---|
| `total` | `int` | Tổng số ticket toàn hệ thống (không tính đã xóa mềm) |
| `openCount` | `int` | Số ticket mở — status **không** thuộc {`Resolved`, `ClosedPendingRate`, `Closed`, `ClosedRejected`} |
| `sla` | `SlaSummaryDto` | Tổng hợp SLA timer toàn hệ thống (met/breached/running/paused + `compliancePercent`) — cấu trúc giống endpoint staff, xem `GET /api/staff/tickets/dashboard/stats` |
| `countByStatus` | `Dictionary<string,int>` | Số ticket theo từng status — zero-fill từ `Enum.GetValues<TicketStatusEnum>()` nên **luôn đủ 13 key** (key PascalCase), status không có = `0`. ⚠️ **13, không phải 14** — không có key `Approved`. FE tự nhóm pipeline — **không gộp `ClosedRejected` vào "Hoàn tất"** (bị từ chối ≠ hoàn tất) |
| `countByPriority` | `Dictionary<string,int>` | Số ticket theo priority — luôn đủ 3 key `P1Critical`/`P2High`/`P3Normal`. **Ticket chưa triage (priority null) không được tính** — vì vậy tổng 3 key có thể nhỏ hơn `total` |
| `createdTrend7Days` | `DailyCountPointDto[]` | Số ticket tạo mới theo ngày — **luôn đúng 7 phần tử** (6 ngày trước + hôm nay), bucket theo **ngày UTC**, ngày trống = `0` |
| `openCountByStaff` | `StaffOpenCountDto[]` | Số ticket mở theo từng staff, **sort giảm dần theo `activeCount`** — chỉ tính ticket mở có `assignedStaffId != null`. Staff không có ticket mở sẽ **không xuất hiện** trong list (FE join với danh sách staff để hiện 0) |

**`StaffOpenCountDto`:** `staffId` (`string` — Guid dạng chuỗi) · `activeCount` (`int`).

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `403` — Không có role Manager/Admin

---

## Nhóm 5 — Maintenance Logs (Nhật ký bảo trì)

Cơ chế quản lý nhật ký bảo trì được tích hợp chặt chẽ với vòng đời của Ticket.

### Quy trình tự động (Automated Workflow)

1.  **Tự động tạo Log:** Khi Staff gọi API `POST /api/staff/tickets/{id}/start`, hệ thống sẽ tự động tạo một Maintenance Log mới với `startedAt` là thời điểm hiện tại và `Summary = "Đang thực hiện..."`.
2.  **Tự động đóng Log:** Khi Staff gọi API `POST /api/staff/tickets/{id}/resolve`, hệ thống sẽ tìm tất cả các nhật ký chưa hoàn thành của Ticket đó và tự động gán `completedAt` bằng thời điểm hiện tại.

---

### `GET /api/staff/tickets/maintenance-logs/me`

**Mục đích:** Staff xem lịch sử bảo trì cá nhân, gom nhóm theo từng Ticket.

**Auth:** Bắt buộc (Staff)

**Response thành công `200`:** `CommonResponse<StaffMaintenanceLogGroupDTO[]>`

---

### `GET /api/tickets/{ticketId}/maintenance-logs`

**Mục đích:** Manager/Admin xem toàn bộ nhật ký bảo trì của một Ticket cụ thể.

**Auth:** Bắt buộc (Manager hoặc Admin)

**Path param:** `ticketId` — UUID của ticket.

**Response thành công `200`:** `CommonResponse<MaintenanceLogDTO[]>`

---

### `POST /api/tickets/{ticketId}/maintenance-logs`

**Mục đích:** Nhập thủ công một nhật ký bảo trì (thường dùng cho trường hợp nhập bù).

**Auth:** Bắt buộc (Staff, Manager **hoặc Admin**)

**Điều kiện:** Một ticket chỉ được có 1 log đang mở (`completedAt = null`) tại 1 thời điểm. Phải đóng log cũ trước khi mở log mới — nếu vi phạm, hệ thống trả `409 Conflict`.

**Request body:** (Xem bảng `MaintenanceLogDTO` để biết các field, lưu ý `startedAt` là bắt buộc).

> Xem chi tiết status codes & business rule ở mục `POST /api/tickets/{ticketId}/maintenance-logs` thuộc Nhóm 1.

---

### `PATCH /api/tickets/{ticketId}/maintenance-logs/{logId}`

**Mục đích:** Cập nhật từng phần (Partial Update) cho nhật ký bảo trì. Chỉ nên gửi lên các trường cần thay đổi.

**Auth:** Bắt buộc (Chủ sở hữu log hoặc Manager/Admin)

**Điều kiện:** Không được chỉnh sửa nếu Ticket đã ở trạng thái `Resolved`, `Closed` hoặc `ClosedPendingRate`.

**Request body:** (Mọi field đều là tùy chọn, ngoại trừ `logId` trong path).

| Field | Type | Mô tả |
|---|---|---|
| `logType` | `MaintenanceLogTypeEnum?` | Loại nhật ký |
| `summary` | `string?` | Tóm tắt (Không được để trống nếu gửi) |
| `diagnosisDetails` | `string?` | Chẩn đoán |
| `actionsTaken` | `string?` | Hành động xử lý |
| `durationMinutes` | `int?` | Thời lượng (`< 0` → `400`) |
| `resolutionNote` | `string?` | Ghi chú kết quả |
| `partsUsed` | `string?` | Linh kiện đã dùng (mô tả text) |
| `attachments` | `Input[]?` | File đính kèm mới |
| `beforePhotos` | `Input[]?` | Ảnh trước khi sửa |
| `afterPhotos` | `Input[]?` | Ảnh sau khi sửa |
| `relatedKbArticleIds` | `Guid[]?` | ID bài viết KB liên quan |

---

## Nhóm 6 — Alert–Ticket Saga (Admin/Manager)

Base path: `/api/admin/sagas/alert-ticket`
**Auth:** Bắt buộc — Admin hoặc Manager role

Quản lý trạng thái Saga điều phối tự động tạo Ticket từ Alert (Sprint 5B #239). Dùng cho debug & vận hành khi pipeline event-driven gặp sự cố.

---

### `GET /api/admin/sagas/alert-ticket`

**Mục đích:** Liệt kê trạng thái Alert–Ticket Saga (filter + phân trang) — dùng cho trang Admin debug & vận hành khi pipeline event-driven gặp sự cố. Sort mặc định `StartedAt DESC`.

**Auth:** Bắt buộc — role `Admin` hoặc `Manager` (permission `ticket.saga.view`).

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `State` | `string?` | ❌ | State cụ thể (vd `"TicketRequested"`, `"Failed"`, `"Completed"`, `"Compensated"`) |
| `AlertId` | `Guid?` | ❌ | Tìm Saga của 1 alert cụ thể |
| `BatteryAssetId` | `Guid?` | ❌ | Lọc Saga theo asset |
| `CustomerId` | `Guid?` | ❌ | Lọc Saga theo customer |
| `StartedFrom` | `DateTime?` | ❌ | UTC, lọc `StartedAt >= from` |
| `StartedTo` | `DateTime?` | ❌ | UTC, lọc `StartedAt <= to` |
| `IsFailed` | `bool?` | ❌ | `true` = chỉ Saga có `FailedAt != null` |
| `PageNumber` | `int` | ❌ (mặc định 1) | Trang |
| `PageSize` | `int` | ❌ (mặc định 50) | Số item/trang |
| `IsDescending` | `bool` | ❌ (mặc định `true`) | Sort theo `StartedAt` |

**Response thành công `200`:** `AlertTicketSagaListResponse` = `CommonResponse<PaginationResponse<AlertTicketSagaDTO>>`

```json
{
  "isSuccess": true,
  "data": {
    "items": [
      {
        "correlationId": "guid",
        "currentState": "TicketRequested",
        "alertId": "guid",
        "batteryAssetId": "guid",
        "customerId": "guid",
        "assetSerialNumber": "BAT-001",
        "anomalyType": 1,
        "severity": 3,
        "ticketId": null,
        "ticketCode": null,
        "ticketIsReused": false,
        "failedAtStage": null,
        "failureReason": null,
        "failureErrorCode": null,
        "failedAt": null,
        "retryCount": 0,
        "startedAt": "2026-06-12T08:00:00Z",
        "completedAt": null
      }
    ],
    "totalItems": 1, "pageNumber": 1, "pageSize": 50,
    "totalPages": 1, "hasNextPage": false, "hasPreviousPage": false
  }
}
```

**Chi tiết `AlertTicketSagaDTO`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `correlationId` | `string` | Không | ID Saga instance (MassTransit Correlation) |
| `currentState` | `string` | Không | State hiện tại (`TicketRequested`, `Completed`, `Failed`, …) |
| `alertId` | `string` | Không | Alert gốc trigger Saga |
| `batteryAssetId` | `string?` | Có | Asset của alert (null nếu alert site-level) |
| `customerId` | `string` | Không | Customer sở hữu |
| `assetSerialNumber` | `string?` | Có | Serial number (snapshot tại thời điểm Saga start) |
| `anomalyType` | `int` | Không | `AnomalyTypeEnum` value |
| `severity` | `int` | Không | `AlertSeverityEnum` value |
| `ticketId` | `string?` | Có | Null khi chưa tạo ticket xong |
| `ticketCode` | `string?` | Có | Null khi chưa tạo ticket xong |
| `ticketIsReused` | `bool` | Không | `true` nếu Saga reuse ticket Open có sẵn thay vì tạo mới |
| `failedAtStage` | `string?` | Có | Stage failed (vd `"CreateTicket"`, `"NotifyCustomer"`) |
| `failureReason` | `string?` | Có | Message exception |
| `failureErrorCode` | `string?` | Có | Mã lỗi nội bộ |
| `failedAt` | `DateTime?` | Có | Thời điểm Failed |
| `retryCount` | `int` | Không | Số lần đã retry |
| `startedAt` | `DateTime` | Không | UTC thời điểm Saga start |
| `completedAt` | `DateTime?` | Có | UTC thời điểm Completed/Failed final |

---

### `GET /api/admin/sagas/alert-ticket/{alertId}`

**Mục đích:** Chi tiết Saga theo `alertId` — đầy đủ trạng thái + lịch sử bước thực thi (step name, status, retry count, error message). Dùng để debug khi Saga rơi vào state `Failed`.

**Auth:** Bắt buộc — role `Admin` hoặc `Manager` (permission `ticket.saga.view`).

**Path param:** `alertId` — UUID của alert gốc trigger Saga.

**Response thành công `200`:** `AlertTicketSagaDetailResponse` = `CommonResponse<AlertTicketSagaDTO>` (shape giống item trong list).

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `403` — Không có role Admin/Manager
- `404` — Không tìm thấy Saga cho `alertId` (alert chưa được Saga consume)

---

### `POST /api/admin/sagas/alert-ticket/{alertId}/reprocess`

**Mục đích:** Reprocess Saga đang ở state `Failed`. Reset bước Failed và enqueue lại để chạy bất đồng bộ (response 202 ngay, Saga chạy trong background). Ghi audit `PerformedBy = user hiện tại`.

**Auth:** Bắt buộc — **chỉ role `Admin`** (permission `ticket.saga.reprocess`). Manager không được phép.

**Path param:** `alertId` — UUID của alert thuộc Saga cần reprocess.

**Headers:**

| Header | Bắt buộc | Mô tả |
|---|---|---|
| `Idempotency-Key` | ✅ | Chuỗi unique cho mỗi lần gọi (vd GUID client gen) — chống double-trigger trong race condition. Server kiểm tra inbox cache: key trùng → trả response cũ qua `409`. |

**Request body:** Không có.

**Response thành công `202 Accepted`:** `SagaReprocessResponse` = `CommonResponse<object>`

```json
{
  "isSuccess": true,
  "statusCode": 202,
  "message": "Saga reprocess enqueued.",
  "data": { "correlationId": "guid" }
}
```

**Lỗi thường gặp:**
- `400` — Thiếu/rỗng `Idempotency-Key`, `alertId` rỗng, hoặc Saga không ở state `Failed`
- `401` — Chưa đăng nhập
- `403` — Role không phải Admin
- `404` — Không tìm thấy Saga
- `409` — `Idempotency-Key` đã được xử lý trước đó (trả response của lần gọi đầu)

---

## Nhóm 7 — Health & Diagnostics

Base path: `/api/ticket/health`
**Auth:** Không yêu cầu (public health endpoint).

---

### `GET /api/ticket/health`

**Mục đích:** Liveness probe đơn giản cho Kubernetes / Docker / ApiGateway aggregated health. Endpoint anonymous, không validate DB/RabbitMQ.

**Auth:** Không yêu cầu.

**Response thành công `200`:**

```json
{
  "status": "Healthy",
  "service": "TicketService",
  "timestamp": "2026-06-12T08:00:00Z"
}
```

---

### `GET /api/ticket/health/sync-lag`

**Mục đích:** Đo độ trễ đồng bộ read model (`CustomerAccount` / `StaffAccount` được TicketService cache từ AuthService events). Giúp Admin phát hiện consumer chậm/bị stop.

**Auth:** Không yêu cầu.

**Response thành công `200`:**

```json
{
  "status": "Healthy",
  "customerLagSeconds": 4.21,
  "staffLagSeconds": 6.05,
  "maxLagSeconds": 6.05,
  "timestamp": "2026-06-12T08:00:00Z"
}
```

| Field | Type | Mô tả |
|---|---|---|
| `status` | `string` | `"Warning"` nếu `maxLagSeconds > 60`, else `"Healthy"` |
| `customerLagSeconds` | `double` | Lag (giây) giữa `UtcNow` và `LastSyncedAt` mới nhất của `CustomerAccount` |
| `staffLagSeconds` | `double` | Tương tự cho `StaffAccount` |
| `maxLagSeconds` | `double` | Max của 2 lag trên |
| `timestamp` | `DateTime` | UTC hiện tại |

> Nếu read model chưa có record nào, lag được set = 365 ngày để báo Critical.

---

### `GET /api/ticket/health/saga`

**Mục đích:** Health metrics cho Alert–Ticket Saga — counter số Saga `Failed` 24h gần nhất, số Saga stuck ở `TicketRequested` > 15 phút. Dùng cho admin dashboard + Prometheus scrape.

**Auth:** Không yêu cầu.

**Response thành công `200`:**

```json
{
  "status": "Healthy",
  "failedLast24h": 0,
  "stuckOver15min": 0,
  "timestamp": "2026-06-12T08:00:00Z"
}
```

| Field | Type | Mô tả |
|---|---|---|
| `status` | `string` | `"Healthy"` (failed ≤ 5 & stuck ≤ 10), `"Warning"` (failed > 5 hoặc stuck > 10), `"Degraded"` (failed > 20 hoặc stuck > 50) |
| `failedLast24h` | `int` | Số Saga ở state `Failed` xảy ra trong 24h qua |
| `stuckOver15min` | `int` | Số Saga ở state `TicketRequested` quá 15 phút (có thể đang treo) |
| `timestamp` | `DateTime` | UTC hiện tại |

---

## Bảng mã lỗi nghiệp vụ thường gặp

| StatusCode | Field | Detail |
|---|---|---|
| `400` | `Title` | Tiêu đề không được để trống |
| `400` | `Description` | Mô tả không được để trống |
| `400` | `Rating` | Điểm đánh giá phải từ 1 đến 5 |
| `403` | `Ticket` | Ticket không ở trạng thái hợp lệ để thực hiện thao tác này |
| `403` | `Ticket` | Quá hạn 7 ngày để mở lại ticket |
| `403` | `Ticket` | Không đủ thẩm quyền xử lý ticket đã Escalated |
| `404` | `Ticket` | Không tìm thấy ticket yêu cầu |
| `409` | `Ticket` | Ticket đã ở trạng thái chờ phê duyệt hoặc đã hoàn thành — không thể gán thêm tài liệu tham khảo KB (xem bảng quy tắc Nhóm 11) |
| `422` | `KbArticle` | Bài viết nội bộ không thể gán với loại tham chiếu "Cung cấp cho khách hàng" (`ProvidedToCustomer`) |

---

## Nhóm 8 — Knowledge Base (tra cứu — Staff/Manager/Admin)

Base path: `/api/knowledge-base`
**Auth:** Bắt buộc — `[Authorize(Roles = "Staff,Manager,Admin")]`. **KHÔNG anonymous.**

> ⚠️ **Sửa so với doc cũ:** controller **KHÔNG** cho mọi role — **Customer gọi bất kỳ endpoint nào trong nhóm này đều nhận `403`**. Mọi mô tả kiểu "Customer chỉ thấy bài Published" ở dưới là **không áp dụng được qua API này** (Customer không vào được endpoint). Nếu cần KB cho Customer, đó là scope chưa có endpoint.

> **Enum serialize:** Toàn bộ response của TicketService dùng `JsonStringEnumConverter` → mọi enum (`status`, `category`, kể cả `KbArticleVersionDTO.status`) trả về dạng **chuỗi** (vd `"Published"`, `"Charging"`). Khi **filter/gửi request** (query string hoặc body), enum cũng nhận **chuỗi tên enum** — gửi đúng tên (vd `Status=Published`, `Category=Charging`), KHÔNG gửi số.

---

### `GET /api/knowledge-base`

**Mục đích:** Tìm kiếm và liệt kê bài viết Knowledge Base. Staff/Manager/Admin thấy mọi trạng thái; lọc tự do theo `Status` (kể cả `PendingReview`, `Draft`, `Archived`) → đây là cách Manager/Admin liệt kê hàng chờ duyệt.

**Auth:** Staff, Manager hoặc Admin (**Customer → `403`**).

> Endpoint này luôn ép `IsTemplate = false` ở controller — **bài mẫu (template) không bao giờ xuất hiện** trong kết quả. Xem template qua `GET /api/internal/knowledge-base/templates` hoặc `/api/admin/knowledge-base/templates`.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Q` | `string?` | Từ khóa — tìm trong `title` và `symptoms` |
| `Category` | `TicketCategoryEnum?` | Lọc theo danh mục lỗi — gửi **chuỗi tên enum** (vd `Charging`) |
| `Status` | `KbArticleStatusEnum?` | Lọc theo trạng thái — gửi **chuỗi tên enum** (vd `Published`). **Chỉ áp dụng cho internal role**; Customer bị bỏ qua |
| `Tag` | `string?` | Lọc theo **một** thẻ (số ít — không phải mảng) |
| `SortBy` | `string?` | Cột sort server-side. Whitelist: `code`, `title`, `category`, `status`, `viewCount`, `helpfulCount`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | `asc` \| `desc` (mặc định `desc`; giá trị lạ → `desc`) |
| `PageNumber` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang |

> ⚠️ Param đúng theo `GetKbArticleListQuery`: tên là **`Q`** (không phải `Keyword`), **`Tag`** số ít (không phải `Tags[]`).
>
> ⚠️ **`IsTemplate` KHÔNG phải query param** — property này gắn `[BindNever]` trong `GetKbArticleListQuery` và bị controller ghi đè `= false` sau khi model binding chạy. Endpoint này **luôn** loại bỏ bài mẫu; gửi `?IsTemplate=true` sẽ bị bỏ qua im lặng. Để liệt kê bài mẫu, dùng `GET /api/internal/knowledge-base/templates` (Nhóm 9) hoặc `/api/admin/knowledge-base/templates` (Nhóm 10bis).

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). Chi tiết enum `KbArticleStatusEnum` + nullable: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:** `CommonResponse<PaginationResponse<KbArticleListItemDTO>>`

---

### `GET /api/knowledge-base/{id}`

**Mục đích:** Lấy thông tin chi tiết một bài viết Knowledge Base để đọc. Không tự động tăng lượt xem.

**Auth:** Staff, Manager hoặc Admin (**Customer → `403`**).

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy bài viết hoặc đã bị xóa.

---

### `GET /api/knowledge-base/suggest`

**Mục đích:** Gợi ý các bài viết liên quan **theo Ticket** (cùng `Category`, ưu tiên `HelpfulCount`/`ViewCount` cao). Trả tối đa 5 bài đã `Published` và **không phải bài nội bộ** (`isInternalOnly = true` bị lọc khỏi kết quả — áp dụng cho cả endpoint kb-suggestions của chat) — vì vậy `isInternalOnly` trong response thường luôn `false`.

**Auth:** Staff, Manager hoặc Admin (**Customer → `403`**).

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `TicketId` | `Guid` | ✅ | ID Ticket để gợi ý bài viết liên quan |

> ⚠️ Theo `SuggestKbArticlesQuery`, param là **`TicketId` (Guid)** — không phải `query` text. (Doc cũ ghi sai.)

**Response thành công `200`:** `CommonResponse<KbArticleSuggestDTO[]>` (tối đa 5 phần tử)

**Lỗi thường gặp:**
- `404` — Không tìm thấy Ticket.

---

### `POST /api/knowledge-base/{id}/helpful`

**Mục đích:** Người dùng đánh giá bài viết là hữu ích (Tăng HelpfulCount).

**Auth:** Staff, Manager hoặc Admin (**Customer → `403`**).

> ⚠️ Theo `MarkHelpfulCommandHandler`, BE chỉ `article.HelpfulCount++` rồi `SaveChanges` — **KHÔNG dedup theo UserId, không chống spam**. Mỗi request là +1. Client nên tự chặn double-tap (disable nút sau khi gọi).

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy bài viết

---

### `GET /api/knowledge-base/{id}/usage-stats`

**Mục đích:** Thống kê số lần bài viết được dùng làm tài liệu tham khảo trong các Ticket, chia theo `KbReferenceTypeEnum`.

**Auth:** Bắt buộc — **chỉ role `Manager` hoặc `Admin`** (`[Authorize(Roles = "Manager,Admin")]`). Staff không gọi được, dù controller cha cho phép Staff.

**Path param:** `id` — UUID của bài viết Knowledge Base.

**Response thành công `200`:** `CommonResponse<KbUsageStatsDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "kbArticleId": "guid",
    "kbArticleCode": "KB-2606-0001",
    "kbArticleTitle": "Pin không sạc được khi nhiệt độ thấp",
    "totalReferences": 12,
    "byType": {
      "consultedDuringResolve": 8,
      "providedToCustomer": 3,
      "generatedAfterResolve": 1
    }
  }
}
```

**Chi tiết `KbUsageStatsDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `kbArticleId` | `string` | ID bài viết |
| `kbArticleCode` | `string` | Mã bài viết |
| `kbArticleTitle` | `string` | Tiêu đề bài viết |
| `totalReferences` | `int` | Tổng số tham chiếu (`TicketKbReference`) chưa bị xóa |
| `byType` | `KbUsageByTypeDTO` | Đếm tham chiếu theo từng `KbReferenceTypeEnum` |

**`KbUsageByTypeDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `consultedDuringResolve` | `int` | Số lần `ReferenceType = ConsultedDuringResolve` |
| `providedToCustomer` | `int` | Số lần `ReferenceType = ProvidedToCustomer` |
| `generatedAfterResolve` | `int` | Số lần `ReferenceType = GeneratedAfterResolve` |

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `403` — Không có role Manager/Admin
- `404` — Không tìm thấy bài viết

---

## Nhóm 9 — Knowledge Base (Internal - Staff/Manager/Admin)

Base path: `/api/internal/knowledge-base`
**Auth:** Bắt buộc — role `Staff`, `Manager` hoặc `Admin` (`[Authorize(Roles = "Staff,Manager,Admin")]`)

---

### `POST /api/internal/knowledge-base`

**Mục đích:** Tạo mới một bài viết Knowledge Base.
Bài viết được khởi tạo ở trạng thái **`PendingReview`**, đồng thời tạo một bản `KbArticleVersion` (V1.0) ở trạng thái `Pending`. Cần Manager/Admin duyệt để xuất bản.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `category` | `TicketCategoryEnum` | ✅ | Danh mục lỗi — gửi **chuỗi tên enum** (vd `Charging`), phải là enum hợp lệ |
| `title` | `string` | ✅ | Tiêu đề — không rỗng, max 200 ký tự |
| `symptoms` | `string` | ✅ | Triệu chứng — không rỗng, max 2000 ký tự |
| `diagnosisSteps` | `string` | ✅ | Bước chẩn đoán — không rỗng, max 4000 ký tự |
| `solutionSteps` | `string` | ✅ | Bước xử lý — không rỗng, max 4000 ký tự |
| `recommendedParts` | `string[]?` | Không | Linh kiện khuyến nghị thay thế |
| `tags` | `string[]?` | Không | Từ khóa — tối đa 10 thẻ, mỗi thẻ ≤ 50 ký tự |
| `isInternalOnly` | `bool` | Không (mặc định `false`) | `true` = ẩn với khách hàng |
**Response thành công `201`:** `CommonResponse<KbArticleActionDTO>` (trả về `id`, `code`, `status`)

**Lỗi thường gặp:**
- `400` — Validation field (`Title`/`Symptoms`/`DiagnosisSteps`/`SolutionSteps` rỗng hoặc quá độ dài; `Category` không hợp lệ; `Tags` > 10)

---

### `PUT /api/internal/knowledge-base/{id}`

**Mục đích:** Cập nhật nội dung bài viết hiện có.
Hệ thống tự động lưu bản hiện tại vào lịch sử. Trạng thái bài viết sẽ chuyển về PendingReview để chờ Manager duyệt (trừ khi người cập nhật là Manager/Admin hoặc chủ sở hữu bài viết).

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết.

**Request body:** Cùng các field như Create (`category`, `title`, `symptoms`, `diagnosisSteps`, `solutionSteps`, `recommendedParts?`, `tags?`, `isInternalOnly`), **thêm**:

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `changeDescription` | `string?` | **Không** | Mô tả thay đổi (lưu vào version history). BE **không validate** — `null`/rỗng vẫn chấp nhận, nên gửi để audit |

> ⚠️ Doc cũ ghi `changeDescription` là "required" — **SAI**. Theo `UpdateKbArticleCommand`, field là `string?` và không có rule validate.

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

---

### `GET /api/internal/knowledge-base/{id}/versions`

**Mục đích:** Xem danh sách lịch sử các phiên bản của bài viết.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleVersionDTO[]>`

---

### `GET /api/internal/knowledge-base/{id}/versions/{versionId}`

**Mục đích:** Lấy chi tiết một phiên bản cụ thể trong lịch sử.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path params:** `id` — UUID bài viết · `versionId` — UUID phiên bản (`KbArticleVersion.id`).

**Response thành công `200`:** `CommonResponse<KbArticleVersionDTO>`

---

### `GET /api/internal/knowledge-base/{id}/compare`

**Mục đích:** So sánh sự khác biệt giữa hai phiên bản của bài viết.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID bài viết.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `fromVersionId` | `Guid` | ✅ | ID phiên bản gốc (`KbArticleVersion.id`) |
| `toVersionId` | `Guid?` | Không | ID phiên bản đích. Bỏ trống → so sánh với **bản hiện tại** |

> ⚠️ **Sửa 2026-08-02:** tên param đúng là **`fromVersionId` / `toVersionId`** (theo `CompareKbArticleVersionsQuery`), KHÔNG phải `fromVersion`/`toVersion` như doc cũ ghi. Gửi sai tên → model binding không map được, `fromVersionId` thành `Guid.Empty` và request hỏng.
>
> Kiểu là **`Guid`** (ID của version), không phải số version hay `int`. `articleId` gắn `[BindNever]` — lấy từ route, không nhận qua query.

**Ví dụ:** `GET /api/internal/knowledge-base/{id}/compare?fromVersionId=<guid>&toVersionId=<guid>`

**Response thành công `200`:** `CommonResponse<KbArticleDiffDTO>` — **3** `DiffSection`: `titleDiff`, **`contentDiff`** (symptoms + diagnosisSteps + solutionSteps gộp chung), `tagsDiff`; mỗi cái có `oldValue`/`newValue`/`isChanged`. ⚠️ Không có `symptomsDiff`/`diagnosisStepsDiff`/`solutionStepsDiff`/`recommendedPartsDiff` như doc cũ ghi.

---

### `GET /api/internal/knowledge-base/templates`

**Mục đích:** Liệt kê các bài viết mẫu (`isTemplate = true`) đang ở trạng thái `Published`, để Staff tra cứu và sao chép.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Query params:** Cùng bộ với `GET /api/knowledge-base` (`Q`, `Category`, `Tag`, `PageNumber`, `PageSize`).

> ⚠️ Server **ép cứng** `IsTemplate = true` và `Status = Published` — param `Status` client gửi sẽ bị ghi đè.

**Response thành công `200`:** `CommonResponse<PaginationResponse<KbArticleListItemDTO>>`

---

### `GET /api/internal/knowledge-base/templates/{id}`

**Mục đích:** Lấy chi tiết một bài viết Knowledge Base bất kỳ theo ID, kể cả bài là template hoặc không. Dùng khi cần preview nội dung template trước khi copy.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài viết (cần có `isTemplate = true` để dùng làm mẫu, nhưng endpoint này không validate điều đó).

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài viết

---

### `GET /api/internal/knowledge-base/{id}/copy-template`

**Mục đích:** Sao chép cấu trúc bài viết mẫu để tạo bài mới. Chỉ áp dụng cho bài viết có gắn tag **`template`** hoặc **`example`** (so khớp không phân biệt hoa thường).

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết mẫu.

**Response thành công `200`:** `CommonResponse<KbArticleTemplateDTO>` — gồm `category` (`TicketCategoryEnum`, chuỗi), `symptoms`, `diagnosisSteps`, `solutionSteps`, `recommendedParts`, `tags` (**không** có `id`/`title`).

---

### `POST /api/internal/knowledge-base/{id}/duplicate`

**Mục đích:** Nhân bản một bài viết KB — tạo **ngay** một bản ghi KB mới copy gần như toàn bộ bài gốc (`category`, nội dung, `tags`), `title` thêm hậu tố **`_copy`**, trạng thái **`Draft`**. Trả `id` bản mới để FE mở thẳng trang chỉnh sửa.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài viết **gốc** cần nhân bản.

**Request body:** Không có. `currentUserId` lấy từ JWT.

**Response thành công `201`:** `CommonResponse<KbArticleActionDTO>` (`id`, `code`, `status`).

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài viết gốc

> **Khác `copy-template`:** `copy-template` chỉ **trả về cấu trúc** để FE fill vào form (không ghi DB, không cần bài là template thật); `duplicate` **tạo luôn bản ghi mới** ở `Draft`.

---

## Nhóm 10 — Knowledge Base (Admin/Manager Workflow)

Base path: `/api/admin/knowledge-base`
**Auth:** Bắt buộc — role `Manager` hoặc `Admin` (`[Authorize(Roles = "Manager,Admin")]`). Ngoại lệ: `DELETE` chỉ `Admin`.

Quản lý vòng đời bài viết: Phê duyệt / từ chối thay đổi, Xuất bản, Lưu trữ, Hoàn tác, Xóa.

---

### `POST /api/admin/knowledge-base/{id}/approve-review`

**Mục đích:** Chấp nhận các thay đổi (`PendingReview → Published`). Nội dung từ bản nháp được đắp lên bài viết chính.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/reject-review`

**Mục đích:** Từ chối thay đổi của Staff.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reason` | `string` | ✅ | Lý do từ chối — không được rỗng/whitespace (`400` nếu thiếu) |

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/publish`

**Mục đích:** Xuất bản bài viết (→ `Published`).

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/archive`

**Mục đích:** Lưu trữ bài viết (→ `Archived`, ngừng hiển thị với Customer).

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/rollback`

**Mục đích:** Hoàn tác nội dung bài viết về một phiên bản cũ trong lịch sử. Lấy nội dung phiên bản cũ đè lên bản hiện tại và tăng Version.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `toVersionId` | `Guid` | ✅ | ID phiên bản (`KbArticleVersion.id`) cần khôi phục (`400` nếu thiếu/rỗng) |

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `DELETE /api/admin/knowledge-base/{id}`

**Mục đích:** Xóa mềm (soft delete) một bài viết Knowledge Base.

**Auth:** Bắt buộc — **chỉ role `Admin`** (`[Authorize(Roles = "Admin")]`). Manager KHÔNG được phép.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `403` — Không phải Admin
- `404` — Không tìm thấy bài viết

---

## Nhóm 10bis — KB Templates (Admin only — quản lý bài mẫu)

Base path: `/api/admin/knowledge-base/templates`
**Auth:** Bắt buộc — **chỉ role `Admin`** (`[Authorize(Roles = "Admin")]`).

Quản lý vòng đời **bài viết mẫu** (`isTemplate = true`) tách biệt khỏi bài viết KB thường. Mọi endpoint trong nhóm này server đều ép `IsTemplate = true`.

> Để **đọc** template với quyền Staff/Manager, dùng `GET /api/internal/knowledge-base/templates` và `.../templates/{id}` (Nhóm 9).

| Method | Path | Response | Ghi chú |
|---|---|---|---|
| `GET` | `/api/admin/knowledge-base/templates` | `CommonResponse<PaginationResponse<KbArticleListItemDTO>>` | Liệt kê bài mẫu ở **mọi trạng thái** (khác Nhóm 9 chỉ trả `Published`) |
| `GET` | `/api/admin/knowledge-base/templates/{id}` | `CommonResponse<KbArticleDTO>` | Chi tiết bài mẫu |
| `POST` | `/api/admin/knowledge-base/templates` | `201` · `CommonResponse<KbArticleActionDTO>` | Tạo bài mẫu — body giống `POST /api/internal/knowledge-base`, `isTemplate` tự đặt `true` |
| `PUT` | `/api/admin/knowledge-base/templates/{id}` | `CommonResponse<KbArticleDTO>` | Cập nhật bài mẫu |
| `POST` | `/api/admin/knowledge-base/templates/{id}/publish` | `CommonResponse<KbArticleActionDTO>` · `409` | Xuất bản — bài mẫu phải `Published` mới hiện ở Nhóm 9 |
| `POST` | `/api/admin/knowledge-base/templates/{id}/archive` | `CommonResponse<KbArticleActionDTO>` | Lưu trữ |
| `POST` | `/api/admin/knowledge-base/templates/{id}/rollback` | `CommonResponse<KbArticleActionDTO>` | Hoàn tác về version cũ — body `{ toVersionId }` |
| `DELETE` | `/api/admin/knowledge-base/templates/{id}` | `CommonResponse<object>` | Xóa mềm |

---

## Nhóm 11 — Ticket–KB References (Staff/Manager/Admin)

Base path: `/api/knowledge-base/references`
**Auth:** Bắt buộc — Staff, Manager hoặc Admin.

Gán bài viết Knowledge Base vào Ticket làm tài liệu tham khảo (lưu vết khi xử lý). `referencedByUserId` resolve từ JWT.

---

### `POST /api/knowledge-base/references`

**Mục đích:** Gán một bài viết KB vào một Ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `ticketId` | `Guid` | ✅ | ID ticket |
| `kbArticleId` | `Guid` | ✅ | ID bài viết KB |
| `referenceType` | `KbReferenceTypeEnum` | ✅ | Loại tham chiếu |
| `note` | `string?` | ❌ | Ghi chú |

**Response thành công `200`:** `CommonResponse<object>` (idempotent theo cặp ticket+bài viết: nếu tham chiếu đã tồn tại — kể cả đã xóa mềm — sẽ được khôi phục và cập nhật `referenceType`/`note` mới).

**Quy tắc trạng thái ticket (theo `referenceType`):**

| Trạng thái ticket | `ConsultedDuringResolve` | `GeneratedAfterResolve` / `ProvidedToCustomer` |
|---|---|---|
| Trước `Resolved` (New → Escalated) | ✅ Gán được | ✅ Gán được |
| `Resolved` | ❌ `409` | ✅ Gán được — 2 type này về ngữ nghĩa xảy ra **lúc/sau khi resolve** |
| `ClosedPendingRate` / `Closed` | ❌ `409` | ❌ `409` |

**Quy tắc bài viết nội bộ:** bài có `isInternalOnly = true` **không thể** gán với `referenceType = ProvidedToCustomer` (không được cung cấp tài liệu nội bộ cho khách hàng) → `422`. Các type khác vẫn gán được bài nội bộ bình thường.

**Bảng status code:**

| Status | Trường hợp | `listErrors` |
|---|---|---|
| `400` | Lỗi field: `ticketId`/`kbArticleId` = Guid rỗng, `referenceType` không phải giá trị enum hợp lệ | Có — từng phần tử ghi rõ `field` + `detail` |
| `401` | Chưa đăng nhập | `null` |
| `403` | **Lỗi quyền:** Staff không phải người được phân công xử lý ticket (`assignedStaffId` khác), hoặc role không hợp lệ | `null` |
| `404` | Không tìm thấy Ticket hoặc Bài viết trong DB | `null` |
| `409` | **Xung đột trạng thái:** ticket đã chờ phê duyệt/hoàn thành theo bảng quy tắc trên | `null` |
| `422` | **Vi phạm rule nghiệp vụ:** bài nội bộ + `ProvidedToCustomer` | `null` |

---

### `GET /api/knowledge-base/references?ticketId={ticketId}`

**Mục đích:** Lấy danh sách bài viết KB đã gán cho một Ticket (sắp xếp mới nhất trước).

**Query param:** `ticketId` — UUID của ticket.

**Response thành công `200`:** `CommonResponse<TicketKbReferenceDTO[]>`

> Trả về toàn bộ array — **không pagination**. Chỉ trả các tham chiếu chưa bị xóa (`!IsDeleted`).

---

### `DELETE /api/knowledge-base/references/{referenceId}`

**Mục đích:** Gỡ một tham chiếu KB khỏi Ticket (xóa mềm).

**Path param:** `referenceId` — UUID của bản ghi tham chiếu (`TicketKbReferenceDTO.id`).

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy tham chiếu

---

## Nhóm 12 — Reports (Sprint 7 #114 · §5.2)

**Quy ước chung mọi report:**
- **Export:** thêm query `?format=csv` hoặc `?format=xlsx` → trả **file download** (XLSX dùng ClosedXML; CSV có UTF-8 BOM). Không truyền `format` → trả **JSON** `CommonResponse<List<...>>` (riêng `csat` trả `CommonResponse<CsatDto>`).
- **Thời gian:** `from`/`to` UTC, tùy chọn. Report time-series mặc định **30 ngày gần nhất** nếu bỏ trống. `granularity`: `day` (mặc định) · `week` · `month`.
- **SLA:** "met"/"breached" lấy từ `SlaTimerStatusEnum` (Met/Breached). "resolved" = Status ∈ {Resolved, ClosedPendingRate, Closed}.
- Route phẳng `api/reports/...`. Tất cả là `GET`.
- **Auth:** mặc định **Admin/Manager** (riêng `saga-failed-rate` chỉ **Admin**).

### `GET /api/reports/sla-by-staff`

**Mục đích:** SLA compliance theo từng staff — tổng assigned, met, breached, tỷ lệ tuân thủ.
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `format?`.
**Response `200`:** `CommonResponse<List<SlaByStaffRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `staffId` | `string` | Không | ID staff (AssignedStaffId) |
| `name` | `string?` | **Null nếu không map được tên** | Tên staff (từ `StaffAccount`) |
| `totalAssigned` | `int` | Không | Tổng ticket được giao trong khoảng |
| `met` | `int` | Không | Số ticket SLA Met |
| `breached` | `int` | Không | Số ticket SLA Breached |
| `complianceRate` | `decimal` | Không | `% = met / (met + breached)` (chỉ tính ticket đã chốt SLA) |

### `GET /api/reports/sla-by-priority`

**Mục đích:** SLA compliance theo priority (luôn trả đủ 3 dòng P1/P2/P3).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `format?`.
**Response `200`:** `CommonResponse<List<SlaByPriorityRow>>`

```json
{
  "isSuccess": true, "statusCode": 200, "message": "",
  "data": [
    { "priority": "P1Critical", "met": 2, "breached": 1, "total": 3, "complianceRate": 66.67 },
    { "priority": "P2High", "met": 5, "breached": 0, "total": 5, "complianceRate": 100 },
    { "priority": "P3Normal", "met": 0, "breached": 0, "total": 0, "complianceRate": 0 }
  ],
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `priority` | `string` | Không | Tên `TicketPriorityEnum` (P1Critical/P2High/P3Normal) |
| `met` | `int` | Không | Số SLA timer Met |
| `breached` | `int` | Không | Số SLA timer Breached |
| `total` | `int` | Không | Tổng SLA timer của priority đó |
| `complianceRate` | `decimal` | Không | `% = met / (met + breached)` |

### `GET /api/reports/ticket-volume`

**Mục đích:** Số lượng ticket tạo mới theo thời gian (time-series).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `granularity?` (mặc định 30 ngày/day), `format?`.
**Response `200`:** `CommonResponse<List<TicketTimeSeriesPoint>>` — `{ date: DateTime, count: int }` (cả 2 không null).

### `GET /api/reports/top-reopen-issues`

**Mục đích:** Top category bị reopen nhiều (chỉ tính ticket `ReopenCount > 0`).
**Auth:** Admin/Manager · **Query:** `limit?` (mặc định 10, tối đa 100), `format?`.
**Response `200`:** `CommonResponse<List<ReopenIssueRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `category` | `string` | Không | Tên `TicketCategoryEnum` |
| `count` | `int` | Không | Số ticket bị reopen của category |
| `avgReopenCount` | `decimal` | Không | Số lần reopen trung bình |

### `GET /api/reports/staff-performance`

**Mục đích:** Hiệu suất staff — số ticket resolved, giờ xử lý TB, rating TB, % SLA compliance.
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `format?`.
**Response `200`:** `CommonResponse<List<StaffPerformanceRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `staffId` | `string` | Không | ID staff |
| `name` | `string?` | **Null nếu không map được tên** | Tên staff |
| `ticketsResolved` | `int` | Không | Số ticket đã resolve |
| `avgResolveHours` | `decimal` | Không | Giờ xử lý trung bình (ResolvedAt − CreatedAt) |
| `avgRating` | `decimal?` | **Null nếu chưa có ticket nào được rate** | Điểm đánh giá trung bình (1–5) |
| `slaCompliance` | `decimal` | Không | `% = met / (met + breached)` |

### `GET /api/reports/csat`

**Mục đích:** Chỉ số hài lòng khách hàng — rating trung bình, tổng số rated, phân bố 1..5 sao.
**Auth:** Admin/Manager · **Query:** `from?`, `to?` (lọc theo `RatedAt`), `format?`.
**Response `200`:** `CommonResponse<CsatDto>` (**object**, không phải list)

```json
{
  "isSuccess": true, "statusCode": 200, "message": "",
  "data": {
    "avgRating": 4.33,
    "totalRated": 3,
    "ratingDistribution": { "1": 0, "2": 0, "3": 1, "4": 0, "5": 2 }
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `avgRating` | `decimal` | Không | Điểm trung bình (0 nếu chưa có rating) |
| `totalRated` | `int` | Không | Số ticket đã được đánh giá |
| `ratingDistribution` | `object<int,int>` | Không (luôn có key 1..5) | Phân bố số sao: key = 1..5, value = số lượng |

### `GET /api/reports/resolution-time-histogram`

**Mục đích:** Histogram thời gian resolution (chỉ tính ticket đã có `ResolvedAt`).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `format?`.
**Response `200`:** `CommonResponse<List<HistogramBucketRow>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `bucket` | `string` | Không | Khoảng thời gian: `0-1h` · `1-4h` · `4-24h` · `1-3d` · `>3d` |
| `count` | `int` | Không | Số ticket rơi vào bucket |

### `GET /api/reports/category-breakdown`

**Mục đích:** Phân bố ticket theo category (sort giảm dần).
**Auth:** Admin/Manager · **Query:** `from?`, `to?`, `format?`.
**Response `200`:** `CommonResponse<List<CategoryBreakdownRow>>` — `{ category: string (tên TicketCategoryEnum), count: int }`.

### `GET /api/reports/saga-failed-rate`

**Mục đích:** Tỷ lệ thất bại của Alert-Ticket Saga theo thời gian — phục vụ demo SRE/SLO. Đọc từ `AlertTicketSagaState`.
**Auth:** **Chỉ Admin** · **Query:** `from?`, `to?`, `granularity?` (mặc định 30 ngày/day), `format?`.
**Response `200`:** `CommonResponse<List<SagaFailedRatePoint>>`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `date` | `DateTime` | Không | Mốc bucket (UTC) |
| `started` | `int` | Không | Số saga bắt đầu trong bucket |
| `completed` | `int` | Không | Số saga hoàn tất |
| `failed` | `int` | Không | Số saga thất bại |
| `failedRate` | `decimal` | Không | `% = failed / started` |
| `p95DurationSec` | `decimal` | Không | p95 thời gian chạy saga hoàn tất (giây) |

**Lỗi thường gặp (mọi report):** `401` chưa đăng nhập · `403` không đủ role (vd non-Admin gọi `saga-failed-rate`).

---

## Nhóm 13 — Blog (Public — mọi role đã đăng nhập)

Base path: `/api/blog`
**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}` (mọi role đã đăng nhập).

Chỉ trả bài có `status = Published`. FE không cần filter thêm.

---

### `GET /api/blog`

**Mục đích:** Danh sách bài blog đã Published (endpoint public — mọi user đăng nhập xem được).

**Auth:** Bắt buộc (mọi role).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Origin` | `BlogPostOriginEnum?` | Lọc theo nguồn gốc — gửi chuỗi (`Manual` hoặc `AiGeneratedFromKb`) |
| `PageNumber` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang (mặc định 10, tối đa 100) |

> ⚠️ `Status` không có trong query params của public endpoint — controller tự set `Status = Published`. Không cần gửi.

**Response thành công `200`:** `CommonResponse<PaginationResponse<BlogPostListItemDTO>>`

---

### `GET /api/blog/{id}`

**Mục đích:** Lấy chi tiết bài blog đã Published.

**Auth:** Bắt buộc (mọi role).

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy, đã xóa mềm, **hoặc bài chưa `Published`** (`Draft`/`Generating`/`GenerationFailed`/`Archived` đều trả 404).

> ⚠️ Endpoint public này **không** đọc được bài chưa xuất bản. Để đọc bài ở trạng thái bất kỳ (kể cả đang `Generating`), dùng `GET /api/internal/blog/{id}` (Nhóm 14).

---

## Nhóm 14 — Blog (Internal — Staff/Manager/Admin)

Base path: `/api/internal/blog`
**Auth:** Bắt buộc — role `Staff`, `Manager` hoặc `Admin`.

Xem toàn bộ bài blog (kể cả `Draft`, `Generating`, `GenerationFailed`), soạn thảo thủ công, xem lịch sử version, so sánh version.

---

### `GET /api/internal/blog`

**Mục đích:** Danh sách bài blog — internal view (filter theo `Status`, xem mọi trạng thái).

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Status` | `BlogPostStatusEnum?` | Lọc theo trạng thái — gửi chuỗi (vd `Draft`, `Generating`) |
| `Origin` | `BlogPostOriginEnum?` | Lọc theo nguồn gốc |
| `PageNumber` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang (mặc định 10, tối đa 100) |

**Response thành công `200`:** `CommonResponse<PaginationResponse<BlogPostListItemDTO>>`

---

### `GET /api/internal/blog/{id}`

**Mục đích:** Lấy chi tiết bài blog ở **mọi trạng thái** — kể cả `Draft`, `Generating`, `GenerationFailed`, `Archived`. Đây là endpoint dùng để **poll** tiến độ sau khi gọi `generate-from-kb`.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy hoặc đã xóa mềm.

---

### `POST /api/internal/blog`

**Mục đích:** Tạo bài blog thủ công — khởi tạo ở trạng thái `Draft`. Cần Manager/Admin publish để xuất bản.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `title` | `string` | ✅ | Tiêu đề — không rỗng, max 256 ký tự |
| `slug` | `string` | ✅ | URL slug — không rỗng, max 300 ký tự, phải unique |
| `summary` | `string` | ✅ | Tóm tắt — không rỗng |
| `contentHtml` | `string` | ✅ | Nội dung HTML — không rỗng |
| `blogTemplateId` | `Guid?` | Không | ID template muốn áp dụng |

**Response thành công `201`:** `CommonResponse<BlogPostActionDTO>`

**Lỗi thường gặp:**
- `400` — Validation field (title/slug/summary/contentHtml rỗng hoặc quá độ dài)
- `409` — Slug đã tồn tại

---

### `PUT /api/internal/blog/{id}`

**Mục đích:** Cập nhật nội dung bài blog. Có **optimistic concurrency check** qua `currentVersion`.

Mỗi lần update thành công: tạo `BlogPostVersion` snapshot + tăng `CurrentVersion`.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `title` | `string` | ✅ | Tiêu đề — max 256 ký tự |
| `slug` | `string` | ✅ | Slug — phải unique (trừ slug hiện tại của bài) |
| `summary` | `string` | ✅ | Tóm tắt |
| `contentHtml` | `string` | ✅ | Nội dung HTML |
| `changeNote` | `string?` | Không | Ghi chú thay đổi (lưu vào version history) |
| `currentVersion` | `int` | ✅ | Phiên bản hiện tại — phải khớp với `BlogPost.currentVersion` trong DB. Dùng để phát hiện concurrent edit |

> ⚠️ `currentVersion` là **bắt buộc** và phải khớp chính xác với giá trị hiện tại trong DB. Nếu không khớp → `409`. Lấy từ `GET /api/internal/blog` hoặc `BlogPostActionDTO` của lần update trước.

**Lỗi `409` có thể gặp:**
- Version mismatch (concurrent edit): `"Bài viết đã được cập nhật bởi người khác. Vui lòng tải lại và thử lại."`
- Bài đang `Generating`: `"Bài viết đang được AI tạo, vui lòng thử lại sau."`
- Bài đã `Archived`: `"Bài viết đã được archive, không thể chỉnh sửa."`
- Slug trùng: `"Slug đã tồn tại."`

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>` (trả `currentVersion` mới sau update)

---

### `GET /api/internal/blog/templates`

**Mục đích:** Danh sách blog templates — dành cho Staff/Manager/Admin dùng khi soạn bài blog mới.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `IsActive` | `bool?` | `true` = chỉ trả template đang hoạt động; `false` = chỉ inactive; bỏ trống = tất cả |

**Response thành công `200`:** `CommonResponse<List<BlogTemplateDTO>>`

---

### `GET /api/internal/blog/templates/{id}`

**Mục đích:** Chi tiết một blog template.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID template.

**Response thành công `200`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy template

---

### `GET /api/internal/blog/{id}/versions`

**Mục đích:** Danh sách lịch sử phiên bản của bài blog (mỗi lần `PUT` thành công tạo 1 version).

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<List<BlogPostVersionDTO>>`

---

### `GET /api/internal/blog/{id}/compare`

**Mục đích:** So sánh nội dung HTML giữa 2 phiên bản.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `OldVersionNumber` | `int` | ✅ | Số version cũ hơn |
| `NewVersionNumber` | `int` | ✅ | Số version mới hơn |

> ⚠️ `OldVersionNumber`/`NewVersionNumber` là **số nguyên** (`versionNumber` trong `BlogPostVersionDTO`) — khác với KB compare dùng Guid.

**Response thành công `200`:** `CommonResponse<BlogDiffDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài blog hoặc version không tồn tại.

---

## Nhóm 15 — Blog (Admin/Manager Workflow)

Base path: `/api/admin/blog`
**Auth:** Bắt buộc — role `Manager` hoặc `Admin`.

Publish, Archive, xóa bài blog. Tạo blog từ KB bằng AI (async).

---

### `POST /api/admin/blog/generate-from-kb/{kbId}`

**Mục đích:** Tạo bài blog từ bài viết Knowledge Base bằng AI — **async, trả `202` ngay**, nội dung được điền sau qua event queue (RabbitMQ → DeepSeek → callback).

**Auth:** Manager hoặc Admin.

**Path param:** `kbId` — UUID bài viết KB nguồn.

**Luồng hoạt động:**
1. BE kiểm tra KB article `Published` và chưa có blog đang tồn tại.
2. Tạo `BlogPost` với `Status = Generating`, `Origin = AiGeneratedFromKb`.
3. Phát `BlogGenerationRequestedEvent` → RabbitMQ.
4. NotificationService/AI worker nhận event → gọi DeepSeek → điền nội dung → phát `BlogGenerationStatusChangedEvent`.
5. TicketService nhận callback → update `ContentHtml`, `Summary`, `Status = Draft`.

**Điều kiện bắt buộc:**
- KB article phải tồn tại và `status = Published` (→ `409` nếu không phải).
- KB article chưa có blog đang tồn tại với `status` không phải `Archived`/`GenerationFailed` (→ `409` nếu trùng).

**Response thành công `202`:** `CommonResponse<BlogPostActionDTO>` — `status = Generating`, `currentVersion = 0`.

```json
{
  "isSuccess": true,
  "statusCode": 202,
  "message": "Đã gửi yêu cầu tạo blog bằng AI. Bài viết sẽ sẵn sàng sau vài giây.",
  "data": {
    "id": "guid",
    "title": "Pin không sạc được khi nhiệt độ thấp",
    "status": "Generating",
    "currentVersion": 0
  }
}
```

**Lỗi thường gặp:**
- `404` — Không tìm thấy KB article
- `409` — KB chưa Published hoặc đã có blog đang tồn tại

> ⚠️ FE cần **poll `GET /api/internal/blog/{id}`** (Nhóm 14) với `id` lấy từ `data.id` của response `202`, hoặc dùng notification, để biết khi `status` chuyển `Draft` (generation hoàn tất) hoặc `GenerationFailed`. Dừng poll khi đạt 1 trong 2 trạng thái đó.
>
> **KHÔNG** poll `GET /api/blog/{id}` — endpoint public trả `404` khi bài chưa `Published`.
>
> Sau khi `GenerationFailed`, vẫn có thể `PUT /api/internal/blog/{id}` để sửa thủ công.

---

### `POST /api/admin/blog/{id}/publish`

**Mục đích:** Publish bài blog (`Draft` → `Published`). Bài sẽ hiển thị trên `GET /api/blog`.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài blog.

**Lỗi `409` có thể gặp:**
- `status = Generating` hoặc `GenerationFailed`: `"Bài viết chưa sẵn sàng để publish."`
- `status = Published`: `"Bài viết đã được publish."`
- `status = Archived`: `"Bài viết đã được archive, không thể publish."`

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>`

---

### `POST /api/admin/blog/{id}/archive`

**Mục đích:** Lưu trữ bài blog (→ `Archived`, ngừng hiển thị trên public endpoint).

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài blog

---

### `DELETE /api/admin/blog/{id}`

**Mục đích:** Xóa mềm (soft delete) bài blog. Không thể khôi phục qua API.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài blog

---

## Nhóm 16 — Blog Templates (Admin only — ghi)

Base path: `/api/admin/blog/templates`
**Auth:** **Chỉ Admin** (POST/PUT/DELETE). Để đọc template, dùng `GET /api/internal/blog/templates` (Nhóm 14).

---

### `POST /api/admin/blog/templates`

**Mục đích:** Tạo template mới.

**Auth:** **Chỉ Admin**.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `name` | `string` | ✅ | Tên template — không rỗng, max 200 ký tự |
| `description` | `string` | Không | Mô tả template |
| `contentHtml` | `string` | ✅ | Nội dung HTML mẫu — không rỗng |

**Response thành công `201`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `400` — Validation field (name/contentHtml rỗng hoặc name > 200 ký tự)

---

### `PUT /api/admin/blog/templates/{id}`

**Mục đích:** Cập nhật template (bao gồm toggle `isActive`).

**Auth:** **Chỉ Admin**.

**Path param:** `id` — UUID template.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `name` | `string` | ✅ | Tên template — max 200 ký tự |
| `description` | `string` | Không | Mô tả |
| `contentHtml` | `string` | ✅ | Nội dung HTML |
| `isActive` | `bool` | Không (mặc định `true`) | `false` để vô hiệu hóa template |

**Response thành công `200`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy template
- `400` — Validation field

---

### `DELETE /api/admin/blog/templates/{id}`

**Mục đích:** Xóa mềm template.

**Auth:** **Chỉ Admin**.

**Path param:** `id` — UUID template.

**Response thành công `200`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy template

---

## KB Chat Integration

Các endpoint trong hệ thống Chat liên quan đến Knowledge Base (xem chi tiết tại `api-ticket.md` — Nhóm Ticket Chats):

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/api/tickets/{ticketId}/chats/{id}/attach-kb` | Staff/Manager/Admin | Gắn KB article vào chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/to-kb-draft` | Staff/Manager/Admin | Chuyển chat thành KB Draft |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/kb-suggestions` | Staff/Manager/Admin | Gợi ý KB articles liên quan đến chat |

---

## Knowledge Base DTOs

> Enum của domain Knowledge Base (`KbArticleStatusEnum`, `KbVersionStatusEnum`, `KbReferenceTypeEnum`) xem ở mục **Enums** đầu tài liệu — không lặp lại ở đây.

### `KbArticleDTO` (detail — `GET /{id}`, response của `update`)

> Enum `category`/`status` trả về **dạng chuỗi** (`JsonStringEnumConverter`).

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bài viết |
| `code` | `string` | Không | Mã bài viết (KB-YYYY-NNNN) |
| `category` | `TicketCategoryEnum` | Không | Enum chuỗi (e.g. `"Charging"`) |
| `title` | `string` | Không | Tiêu đề |
| `symptoms` | `string` | Không | Triệu chứng |
| `diagnosisSteps` | `string` | Không | Các bước chẩn đoán |
| `solutionSteps` | `string` | Không | Các bước xử lý |
| `recommendedParts` | `string[]?` | Có | Danh sách linh kiện (**mảng**, không phải string) |
| `tags` | `string[]` | Không | Danh sách thẻ |
| `status` | `KbArticleStatusEnum` | Không | Enum chuỗi (e.g. `"Published"`) |
| `isInternalOnly` | `bool` | Không | Bài chỉ nội bộ (ẩn với Customer) |
| `version` | `int` | Không | Số phiên bản chính (Major Version) |
| `viewCount` | `int` | Không | Lượt xem |
| `helpfulCount` | `int` | Không | Lượt hữu ích |
| `reviewRequired` | `bool` | Không | Có đang chờ duyệt thay đổi không |
| `pendingReviewBy` | `string?` | Có | ID người đã submit chờ duyệt |
| `managerRejectReason` | `string?` | Có | Lý do Manager từ chối (nếu có) |
| `createdByUserId` | `string` | Không | Người tạo |
| `createdAt` | `string` | Không | Thời điểm tạo (ISO 8601 UTC) |
| `updatedAt` | `string?` | Có | Thời điểm cập nhật gần nhất |

### `KbArticleListItemDTO` (item trong danh sách — `GET /api/knowledge-base`)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài viết |
| `code` | `string` | Mã bài viết |
| `title` | `string` | Tiêu đề |
| `category` | `TicketCategoryEnum` | Enum chuỗi |
| `status` | `KbArticleStatusEnum` | Enum chuỗi |
| `viewCount` | `int` | Lượt xem |
| `helpfulCount` | `int` | Lượt hữu ích |
| `reviewRequired` | `bool` | Có đang chờ duyệt không |
| `createdAt` | `string` | Thời điểm tạo (UTC) |

> ⚠️ List item **KHÔNG** có `tags` (chỉ detail mới có). **CÓ** `reviewRequired` + `createdAt`.

### `KbArticleVersionDTO` (phiên bản trong lịch sử)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID phiên bản (`KbArticleVersion`) — dùng cho `compare`/`rollback` |
| `articleId` | `string` | ID bài viết gốc |
| `majorVersion` | `int` | Major version |
| `minorVersion` | `int` | Minor version |
| `status` | `KbVersionStatusEnum` | Enum chuỗi (e.g. `"Approved"`) — xem mục Enums |
| `title` / `symptoms` / `diagnosisSteps` / `solutionSteps` | `string` | Nội dung snapshot |
| `recommendedParts` | `string[]?` | Linh kiện snapshot |
| `tags` | `string[]` | Thẻ snapshot |
| `changeDescription` | `string` | Mô tả thay đổi của phiên bản |
| `changedBy` | `string` | Người thực hiện thay đổi |
| `createdAt` | `string` | Thời điểm tạo phiên bản (UTC) |

### `KbArticleDiffDTO` (kết quả `compare`)

| Field | Type | Mô tả |
|---|---|---|
| `fromVersion` | `string` | Nhãn phiên bản gốc |
| `toVersion` | `string` | Nhãn phiên bản đích |
| `titleDiff` | `DiffSection` | Diff tiêu đề |
| `contentDiff` | `DiffSection` | Diff **toàn bộ nội dung gộp** (symptoms + diagnosisSteps + solutionSteps ghép chung 1 khối) |
| `tagsDiff` | `DiffSection` | Diff tags — 2 vế là chuỗi tags nối bằng `", "` |

**`DiffSection`:** `{ oldValue: string; newValue: string; isChanged: bool }`

> ⚠️ **Sửa 2026-08-02:** doc cũ ghi **6** `DiffSection` (`symptomsDiff`, `diagnosisStepsDiff`, `solutionStepsDiff`, `recommendedPartsDiff`) — **4 field đó không tồn tại**. `KbArticleDiffDTO` thật chỉ có **3**: `titleDiff`, `contentDiff`, `tagsDiff` (xem `CompareKbArticleVersionsQueryHandler`). FE không thể diff riêng từng mục symptoms/diagnosis/solution.

### `KbArticleTemplateDTO` (kết quả `copy-template`)

| Field | Type | Mô tả |
|---|---|---|
| `category` | `TicketCategoryEnum` | Enum chuỗi — xem mục Enums |
| `symptoms` / `diagnosisSteps` / `solutionSteps` | `string` | Nội dung mẫu |
| `recommendedParts` | `string[]?` | Linh kiện mẫu |
| `tags` | `string[]` | Thẻ mẫu |

> Không có `id`/`title` — chỉ là cấu trúc để fill vào form tạo bài mới.

### `KbArticleSuggestDTO` (kết quả `suggest`)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài viết |
| `code` | `string` | Mã bài viết |
| `title` | `string` | Tiêu đề |
| `symptoms` | `string` | Triệu chứng |
| `helpfulCount` | `int` | Lượt hữu ích |
| `viewCount` | `int` | Lượt xem |
| `isInternalOnly` | `bool` | `true` = bài nội bộ, **không được** gán vào ticket với `referenceType = ProvidedToCustomer` (BE chặn `422` — xem Nhóm 11). ⚠️ Các endpoint suggest hiện **lọc sẵn** bài nội bộ khỏi kết quả nên giá trị thường là `false` — field tồn tại để FE cảnh báo/ẩn nếu nguồn danh sách bài thay đổi (vd chọn từ KB list đầy đủ của Staff) |

### `KbArticleActionDTO`

Payload nhẹ dùng cho các hành động chuyển trạng thái.

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài viết |
| `code` | `string` | Mã bài viết |
| `status` | `KbArticleStatusEnum` | Trạng thái hiện tại sau thao tác (enum chuỗi) |

### `TicketKbReferenceDTO` (Nhóm 11 — `GET .../references`)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bản ghi tham chiếu |
| `ticketId` | `string` | Không | ID ticket |
| `kbArticleId` | `string` | Không | ID bài viết KB |
| `kbArticleCode` | `string` | Không | Mã bài viết (snapshot lúc gán) |
| `kbArticleTitle` | `string?` | Có | Tiêu đề bài viết (join từ KB hiện tại) |
| `referencedByUserId` | `string` | Không | Người gán |
| `referenceType` | `KbReferenceTypeEnum` | Không | Loại tham chiếu (**chuỗi**, vd `"ConsultedDuringResolve"`) — xem mục Enums |
| `note` | `string?` | Có | Ghi chú |
| `createdAt` | `string` | Không | Thời điểm gán (UTC) |

---

## DTOs — Blog

### `BlogPostDTO` (detail — `GET /api/blog/{id}`)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bài blog |
| `title` | `string` | Không | Tiêu đề |
| `slug` | `string` | Không | URL-friendly slug (unique) |
| `summary` | `string` | Không | Tóm tắt ngắn |
| `contentHtml` | `string` | Không | Nội dung HTML đầy đủ |
| `status` | `BlogPostStatusEnum` | Không | Enum chuỗi (e.g. `"Published"`) |
| `origin` | `BlogPostOriginEnum` | Không | Enum chuỗi (`"Manual"` hoặc `"AiGeneratedFromKb"`) |
| `sourceKbArticleId` | `string?` | Có | ID bài KB nguồn (chỉ có nếu `origin = AiGeneratedFromKb`) |
| `blogTemplateId` | `string?` | Có | ID template đã dùng (nếu có) |
| `authorUserId` | `string` | Không | ID người tạo |
| `currentVersion` | `int` | Không | Số phiên bản hiện tại |
| `createdAt` | `string` | Không | Thời điểm tạo (ISO 8601 UTC) |
| `updatedAt` | `string?` | Có | Thời điểm cập nhật gần nhất |

### `BlogPostListItemDTO` (item trong danh sách)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài blog |
| `title` | `string` | Tiêu đề |
| `slug` | `string` | Slug |
| `summary` | `string` | Tóm tắt ngắn |
| `status` | `BlogPostStatusEnum` | Enum chuỗi |
| `origin` | `BlogPostOriginEnum` | Enum chuỗi |
| `authorUserId` | `string` | ID người tạo |
| `currentVersion` | `int` | Phiên bản hiện tại |
| `createdAt` | `string` | Thời điểm tạo (UTC) |
| `updatedAt` | `string?` | Thời điểm cập nhật gần nhất |

### `BlogPostVersionDTO` (phiên bản trong lịch sử)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bản ghi version |
| `blogPostId` | `string` | ID bài blog gốc |
| `versionNumber` | `int` | Số thứ tự version (1, 2, 3...) |
| `title` | `string` | Tiêu đề snapshot |
| `summary` | `string` | Tóm tắt snapshot |
| `contentHtml` | `string` | Nội dung HTML snapshot |
| `changedByUserId` | `string` | ID người thực hiện thay đổi |
| `changeNote` | `string?` | Ghi chú thay đổi (nullable) |
| `createdAt` | `string` | Thời điểm tạo version (UTC) |

### `BlogDiffDTO` (kết quả `compare`)

| Field | Type | Mô tả |
|---|---|---|
| `oldVersionNumber` | `int` | Số version cũ |
| `newVersionNumber` | `int` | Số version mới |
| `oldContentHtml` | `string` | Nội dung HTML version cũ |
| `newContentHtml` | `string` | Nội dung HTML version mới |

> ⚠️ Blog diff **chỉ so sánh `contentHtml`** — không có diff từng field như KB. FE tự render diff từ 2 HTML string.

### `BlogPostActionDTO` (response nhẹ sau action)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài blog |
| `title` | `string` | Tiêu đề |
| `status` | `BlogPostStatusEnum` | Trạng thái sau thao tác (enum chuỗi) |
| `currentVersion` | `int` | Phiên bản hiện tại |

### `BlogTemplateDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID template |
| `name` | `string` | Không | Tên template — max 200 ký tự |
| `description` | `string` | Không | Mô tả template |
| `contentHtml` | `string` | Không | Nội dung HTML mẫu |
| `isActive` | `bool` | Không | Template đang hoạt động hay đã vô hiệu hóa |
| `createdByUserId` | `string` | Không | ID Admin tạo |
| `createdAt` | `string` | Không | Thời điểm tạo (UTC) |
| `updatedAt` | `string?` | Có | Thời điểm cập nhật gần nhất |

---

## Background Services — thứ chạy ngầm ảnh hưởng tới dữ liệu FE thấy

8 `BackgroundService` của TicketService. FE không gọi được, nhưng chúng **âm thầm đổi state ticket/chat**, nên cần biết khi debug "sao dữ liệu tự đổi" hoặc "sao gọi xong chưa thấy".

| Service | Chu kỳ | Tác dụng lên dữ liệu FE |
|---|---|---|
| `ChatReadReceiptBulkWriter` | batch **100 record hoặc mỗi 1 giây** | Ghi read-receipt **bất đồng bộ**. `POST /chats/mark-read` chỉ enqueue ⇒ `unread-count`/`readers` trễ ~1s |
| `SlaTimerBackgroundService` | định kỳ | Cập nhật `slaTimer.status`, phát `SlaWarningEvent` (kèm `StaffId`), đánh dấu `Breached` |
| `EscalationBackgroundService` | định kỳ | Sau khi SLA breach → tự chuyển ticket sang `Escalated` (**đúng 1 lần**/ticket) |
| `AutoCloseBackgroundService` | định kỳ | Ticket treo ở `ClosedPendingRate` quá **7 ngày** → `Closed`, ghi activity `AutoClosed`, publish `TicketClosedEvent(IsAutoClosed: true)` |
| `RatingRequestBackgroundService` | mặc định **60 phút** | Nhắc Customer đánh giá sau **3 ngày** (`Ticket:RatingRequest:*`), idempotent bằng activity `RatingRequested` |
| `ChatRetentionService` | **hằng ngày 03:00 UTC** | Soft-delete (`IsDeleted = true`) chat cũ hơn `Chat:Retention:ArchiveAfterYears` (**mặc định 2 năm**). Không xoá row — chat cũ **biến mất khỏi mọi query** nhưng vẫn còn trong DB |
| `VirusScanWorker` | poll | Quét attachment `VirusScanStatus = Pending` qua ClamAV. **Mặc định TẮT** — chỉ bật khi `Chat:Features:EnableVirusScan = true`.<br>**GH-790:** tải file qua kênh gRPC nội bộ `FileInternal` (trước đây gọi REST `/api/files/{id}/download` không token ⇒ luôn 401). Chiếm bản ghi sang `Scanning` trước khi tải; hỏng tạm thời thì thử lại có giãn cách (`Chat:VirusScan:MaxAttempts`, `RetryBackoffSeconds`), chỉ vào `Failed` khi hết lượt; bản ghi kẹt ở `Scanning` quá `ScanTimeoutSeconds` được thu hồi |
| `TicketAuditOutboxRelayBackgroundService` | **2 giây**, batch 50, retry tối đa 5 | Đẩy audit event sang AuditAggregator. Có **Redis leader election** (`ticket_audit_outbox_leader`) nên chỉ 1 instance relay |

> **Vì sao quan trọng với FE:**
> - **Virus scan tắt mặc định** ⇒ `GET .../attachments/{id}/download` hầu như luôn trả `200`; nhánh `202`/`451` chỉ xuất hiện khi ClamAV đã deploy và bật cờ.
> - **Retention 2 năm** ⇒ chat cũ tự biến mất, không phải bug mất dữ liệu.
> - **Auto-close 7 ngày** ⇒ ticket `ClosedPendingRate` có thể tự thành `Closed` giữa 2 lần refresh; đừng coi là race condition của FE.

---

## Integration Events (RabbitMQ) — tác dụng phụ của endpoint

> **Sprint 6.2 (`#672..#688`)** bổ sung một lớp event trong `SharedContracts` để NotificationService
> consume được. Đây **không phải REST API** — FE không gọi trực tiếp — nhưng là thứ quyết định
> *"bấm nút này thì ai được báo"*, nên cần nắm khi kiểm thử.

### Vì sao phải thêm event mới

TicketService trước đó chỉ publish các record cục bộ trong `TicketService.Application.IntegrationEvents`
(vd `TicketApprovedIntegrationEvent`). **MassTransit route theo full type name**, nên
NotificationService (assembly khác) **không bind được** → 2 giá trị `NotificationTypeEnum.TicketStatusChanged(3)`
và `TicketClosed(5)` có định nghĩa nhưng **không producer, không consumer**.

6 event mới nằm trong `SharedContracts` nên cả hai service dùng chung một contract.
**Publish SONG SONG với event nội bộ cũ (không xoá)** để không phá vỡ subscriber hiện có.

### Event MỚI — Sprint 6.2 NOTI-07 (#678)

| Event | Publish bởi (endpoint / job) | Payload | Ai được báo |
|---|---|---|---|
| `TicketStatusChangedEvent` | `POST /api/admin/tickets/{id}/triage` (New→Open) · `POST /api/staff/tickets/{id}/start` (Assigned→InProgress) · `POST /api/staff/tickets/{id}/hold` · `POST /api/staff/tickets/{id}/resume` (→InProgress) | `TicketId`, `Code`, `CustomerId`, `StaffId?`, `OldStatus` (int), `NewStatus` (int), `OldStatusName`, `NewStatusName` | **Customer** — InApp + Push |
| `TicketApprovedEvent` | `POST /api/admin/tickets/{id}/approve` | `TicketId`, `Code`, `CustomerId`, `ManagerId`, `ManagerComment?`, `ApprovedAt` | **Customer** — InApp + Push + Email (kèm lời mời đánh giá) |
| `TicketRejectedEvent` | `POST /api/admin/tickets/{id}/reject` (`IsClosedRejected = false`) · `POST /api/admin/tickets/{id}/triage-reject` (`IsClosedRejected = true`) | `TicketId`, `Code`, `CustomerId`, `StaffId?`, `Reason`, `IsClosedRejected`, `RejectedAt` | `false` → **Staff đang assign** ("kết quả bị trả lại") · `true` → **Customer** ("ngoài phạm vi dịch vụ") |
| `TicketClosedEvent` | `POST /api/customer/tickets/{id}/rate` (`IsAutoClosed = false`) · `AutoCloseBackgroundService` (`IsAutoClosed = true`, `Rating = null`) | `TicketId`, `Code`, `CustomerId`, `ClosedAt`, `IsAutoClosed`, `Rating?` (`short?`) | **Customer + Manager** — InApp + Push |
| `TicketReopenedEvent` | `POST /api/customer/tickets/{id}/reopen` | `TicketId`, `Code`, `CustomerId`, `StaffId?`, `ReopenReason`, `ReopenCount`, `ReopenedAt` | **Manager + Staff đang assign** — InApp + Push |
| `TicketRatingRequestedEvent` | `RatingRequestBackgroundService` | `TicketId`, `Code`, `CustomerId`, `ApprovedAt`, `DaysPending`, `DaysUntilAutoClose` | **Customer** — InApp + Push |

> `OldStatus`/`NewStatus` là **giá trị int của `TicketStatusEnum`** — cố ý không dùng kiểu enum để
> `SharedContracts` không phải tham chiếu `TicketService.Domain`. Kèm sẵn `*StatusName` để consumer
> hiển thị mà không cần bảng tra cứu.

### Event SỬA payload — Sprint 6.2 NOTI-05 (#676)

⚠️ **Breaking cho consumer khác** (record positional — thêm tham số làm đổi chữ ký constructor):

| Event | Field thêm | Publish bởi | Vì sao |
|---|---|---|---|
| `TicketCreatedEvent` | `CustomerId` (`Guid`), `Priority` (`string?`) | `POST /api/customer/tickets` · auto-tạo từ alert (`TicketAutoCreateFromAlertCommandHandler`) | Payload cũ chỉ có `TicketId`/`Code` nên notification cho Manager **không nói được ticket của ai, ưu tiên gì**. `Priority` **nullable** vì ticket tạo tay chưa qua triage thì `Ticket.Priority` còn `null`; ticket auto từ alert đã có Priority tính sẵn từ matrix Impact × Urgency |
| `TicketAssignedEvent` | `CustomerId` (`Guid`) | `POST /api/admin/tickets/{id}/assign` · `.../reassign` | Mở khoá notify Customer *"Staff đang xử lý sự cố của bạn"* — trước đó consumer bỏ trống với comment `"deferred (event lacks CustomerId)"` |
| `TicketResolvedEvent` | `CustomerId` (`Guid`) | `POST /api/staff/tickets/{id}/resolve` | Event cũ chỉ mang `StaffId` người resolve nên **không notify được Customer** |
| `SlaWarningEvent` | `StaffId` (`Guid?`) | `SlaTimerBackgroundService` | Spec §3.4 yêu cầu SLA warning báo **cả Staff phụ trách lẫn Manager**; payload cũ không có `StaffId` nên consumer chỉ broadcast Manager được. `null` = ticket chưa assign ai |

### `RatingRequestBackgroundService` — MỚI (Sprint 6.2 NOTI-07 / #678)

Nhắc Customer đánh giá ticket đang treo ở `CLOSED_PENDING_RATE`.

**Điều kiện chọn ticket** (tất cả phải đúng):
```
!IsDeleted
AND Status = ClosedPendingRate
AND ApprovedAt != null AND ApprovedAt <= now - AfterDays
AND KHÔNG tồn tại TicketActivity nào có Action = RatingRequested (chưa bị xoá)
ORDER BY ApprovedAt   LIMIT 200
```

Mỗi ticket được chọn → publish `TicketRatingRequestedEvent` + ghi 1 `TicketActivity` với
`Action = RatingRequested` (chính là cờ idempotent, **mỗi ticket chỉ nhắc 1 lần**).

`DaysPending = floor((now − ApprovedAt).TotalDays)` ·
`DaysUntilAutoClose = max(0, AutoCloseAfterDays − DaysPending)`.

| Config | Kiểu | Mặc định | Ý nghĩa |
|---|---|---|---|
| `Ticket:RatingRequest:Enabled` | `bool` | `true` | Tắt worker |
| `Ticket:RatingRequest:AfterDays` | `int` | **`3`** | Số ngày sau `ApprovedAt` mới nhắc |
| `Ticket:RatingRequest:AutoCloseAfterDays` | `int` | `7` | Dùng để tính `DaysUntilAutoClose` — phải **khớp** mốc auto-close thật |
| `Ticket:RatingRequest:PollIntervalMinutes` | `int` | `60` | Chu kỳ quét |

> ⚠️ **Vì sao mặc định 3 ngày chứ không phải 7 như spec:** `AutoCloseBackgroundService` **tự đóng
> ticket đúng mốc 7 ngày** kể từ `ApprovedAt`. Nhắc đúng ngày thứ 7 thì Customer gần như không còn cơ
> hội đánh giá — nhắc xong ticket đóng luôn. Mốc để **cấu hình được** và mặc định **3** (giữa cửa sổ
> 7 ngày). Đặt lại thành `7` nếu muốn bám nguyên văn spec.

### `AutoCloseBackgroundService` — bổ sung publish (Sprint 6.2 NOTI-07)

Ngoài việc đổi `Status = Closed` + ghi `TicketActivity(AutoClosed)` như trước, worker nay còn publish
`TicketClosedEvent` với `IsAutoClosed = true`, `Rating = null` ⇒ **Customer và Manager được báo ticket
đã tự đóng vì quá hạn đánh giá** (trước đây ticket đóng im lặng).

---

## Changelog

### 2026-08-02 (d) — Rà lượt 3: request body, response shape và hạ tầng

Lượt 1–2 mới verify route + enum + auth. Lượt này verify thêm **request body, response shape, gateway routing, SignalR, rate limit, background service**.

**Gateway (YARP, 114 route):** kiểm tra **364/364 endpoint đều reachable và đúng cluster** (áp dụng longest-prefix-wins — `/api/staff/tickets/*` về `ticketCluster`, không bị `/api/staff/*` của Auth nuốt). Chỉ `/live`, `/ready`, `/health` của AuditAggregator không qua gateway — **đúng thiết kế**, doc đã ghi.

**Sai đã sửa:**

- 🔴 **`POST /chats` thiếu 3 field request:** `mentions` (`ChatMentionInput[]`), `groupMentions` (`GroupMentionInput[]` — whitelist `role`/`team` cứng), `requestCustomerInfo`. Bổ sung luôn `ChatAttachmentInput.url`, luật chặn body **chỉ whitespace/emoji**, và 2 mã lỗi `CHAT_DUPLICATE_MESSAGE_LIMIT` / `CHAT_SPAM_CHECK_IN_PROGRESS`.
- 🔴 **`GET /internal/knowledge-base/{id}/compare` sai tên query param:** doc ghi `fromVersion`/`toVersion`, thật là **`fromVersionId`/`toVersionId`** — gửi theo doc cũ thì model binding không map được.
- 🔴 **3 response shape bịa object:** `mark-read` và `unread-count` trả `CommonResponse<int>` (số trần, không phải `{markedCount}`/`{unreadCount}`); `erase-my-data` **`data` luôn `null`**, `erasedCount` không tồn tại — số lượng chỉ nằm trong `message`.
- 🟡 **`DELETE /chats/bulk` thiếu mục chi tiết** — bổ sung: tối đa **50 id**/lần, response `{deleted, skipped, skippedIds}`, không all-or-nothing.
- 🟡 **Rate limit ghi sai phạm vi và thiếu số:** áp cho **8 endpoint** (không chỉ POST/PUT). Hạn mức thật **Customer 30/phút/ticket · Staff 60 · Manager 90 · Admin unlimited** — lưu ý **doc-comment trong `ChatRateLimitingExtensions.cs` ghi 10/30/60 là số cũ, không khớp code**.
- 🟢 **Bổ sung mục Background Services** (8 service) — trước chỉ nhắc 3. Quan trọng nhất: **mark-read ghi bất đồng bộ** (~1s trễ), **virus scan tắt mặc định**, **retention xoá mềm chat > 2 năm**, **auto-close 7 ngày**.
- 🟢 **SignalR:** bổ sung **Redis backplane** — thiếu `ConnectionStrings:Redis` mà chạy nhiều replica thì event không xuyên instance (fallback im lặng, không log lỗi).

### 2026-08-02 (c) — Rà lại lần 2 toàn bộ route/DTO/auth với codebase

Verify lại bằng script đối chiếu tự động: **144/144 route TicketService khớp** (không thiếu, không thừa — 4 endpoint chat đã retire vẫn được đánh dấu REMOVED đúng). Sửa thêm 3 nhóm sai:

- 🔴 **`TicketAttachmentDTO` ghi sai hoàn toàn.** Doc cũ nói DTO này "không xuất hiện trong bất kỳ response nào" và liệt kê **7 field**. Thực tế nó **được trả về** ở `POST .../chats/{id}/attachments`, `.../attachments/batch`, `GET .../chats/{id}/attachments`, `GET .../chats/files` và trong `TicketChatDTO.attachments`; DTO thật có **15 field**. Bổ sung đủ field + 2 enum chưa từng được doc: **`AttachmentSourceEnum`** và **`VirusScanStatusEnum`** (quyết định `200`/`202`/`451` của endpoint download).
- 🔴 **`KbArticleDiffDTO` bịa 4 field.** Doc ghi **6** `DiffSection` (`symptomsDiff`, `diagnosisStepsDiff`, `solutionStepsDiff`, `recommendedPartsDiff`) — **không tồn tại**. Thật chỉ có **3**: `titleDiff`, **`contentDiff`** (symptoms + diagnosis + solution gộp 1 khối), `tagsDiff`. Đã sửa ở cả 2 chỗ (mục DTO và mục endpoint `compare`).
- 🟡 **5 endpoint ghi thiếu role `Admin`:** `reassign`, `approve`, `reject`, `escalate` (code là `Manager,Admin`, doc ghi "Manager") và `POST .../maintenance-logs` (code `Staff,Manager,Admin`, doc ghi "Staff hoặc Manager").

### 2026-08-02 (b) — Gộp `api-ticket-kb-blog.md` vào tài liệu này

`api-ticket-kb-blog.md` đã **xoá**; toàn bộ nội dung Knowledge Base + Blog nằm trong file này.

- **Bổ sung vào Nhóm 9:** `GET /api/internal/knowledge-base/templates`, `.../templates/{id}`, `POST .../{id}/duplicate`.
- **Bổ sung Nhóm 10bis** — KB Templates (Admin only, 8 endpoint `/api/admin/knowledge-base/templates`).
- **Bổ sung Blog** — Nhóm **13–16** (Public · Internal · Admin workflow · Templates) + **DTOs — Blog** (7 DTO) + 2 enum `BlogPostStatusEnum` / `BlogPostOriginEnum` + mục **KB Chat Integration**.
- ⚠️ **Blog đổi số nhóm 12–15 → 13–16** (tránh đụng Nhóm 12 = Reports của file này). Cross-reference trong phần Blog đã cập nhật theo.
- Nhóm 8/9/10/11 giữ bản của file này (đã có `SortBy`/`SortDir` + auth đã sửa), bổ sung thêm ghi chú `IsTemplate` `[BindNever]` từ file cũ.
- Changelog KB/Blog cũ giữ nguyên ở mục **Lịch sử Knowledge Base & Blog** cuối phần Changelog.

### 2026-08-02 — Đối chiếu toàn bộ doc với codebase TicketService (audit)

Rà lại từng enum / DTO / route / command so với code thật. **Sai lệch nghiêm trọng nhất: state `Approved` không tồn tại.**

**🔴 Breaking cho FE/Mobile — phải sửa code:**

1. **`TicketStatusEnum` KHÔNG có `Approved = 14`** — enum chỉ có **13 giá trị** (`New=1` … `Incident=13`). FE/Mobile đang mirror `Approved: 14` phải xóa.
2. **State machine sai hoàn toàn ở đoạn đầu.** Thật: `New → Open → Assigned`. Doc cũ ghi `New → Open → Approved → Assigned`.
   - `POST .../triage` là **`New → Open`** (không phải `Open → Approved`).
   - `POST .../assign` là **`Open → Assigned`** (không phải `Approved → Assigned`).
   - `POST .../triage-reject` là **`New → ClosedRejected`**; từ `Open` **không** có rule này.
   - `GET /api/admin/tickets/queue` lọc **`Status == New`** (không phải `Open`), thêm điều kiện `mergedIntoTicketId == null`, sort `Priority ASC` rồi `CreatedAt` **ASC**.
3. **`TicketDTO` bỏ `assignedStaffId`** → thay bằng **`assignments: TicketAssignmentDTO[]`** (`staffId` + `AssignmentRoleEnum`). Đọc PrimaryHandler qua `assignments.find(a => a.role === 'PrimaryHandler')`.
4. **`POST /api/admin/tickets/{id}/assign`**: `staffId` → **`primaryHandlerStaffId`**, thêm `supporterStaffIds: Guid[]`.
5. **`POST /api/admin/tickets/{id}/reassign`**: `newStaffId` → **`newPrimaryHandlerStaffId`**.
6. **`POST /api/customer/tickets`**: `batteryAssetId` (`Guid?`) → **`batteryAssetIds` (`Guid[]`, bắt buộc ≥1, distinct)**; thêm `incidentDetectedAt` **bắt buộc** (UTC, không được tương lai) và `attachments`.
7. **`GET /api/knowledge-base/*` KHÔNG cho Customer** — controller là `[Authorize(Roles = "Staff,Manager,Admin")]`, Customer nhận **`403`**. Doc cũ ghi "mọi role đã đăng nhập" và mô tả filter riêng cho Customer — không áp dụng được.
8. **`countByStatus` có 13 key, không phải 14** (zero-fill từ `Enum.GetValues<TicketStatusEnum>()`) — không có key `Approved`.
9. **`POST /api/staff/tickets/{id}/start` không nhận body** — controller không có `[FromBody]`; `logType` không gửi được.

**🟡 Field/enum bổ sung (thiếu trong doc):**

- `TicketDTO` thêm 11 field: `batteryAssetIds`, `assignments`, `hasUnreadChat`, `detectedAt`, `batterySerialNumber`, `aiVerifyStatus`, `aiVerifyScore`, `aiVerifyReason`, `suspectedDuplicateOfTicketId`, `duplicateReason`, `mergedIntoTicketId`, `closeReason`.
- `TicketChatDTO` thêm: `activeTranslation`, `isDeleted`, `voiceTranscriptionStatus`, `voiceTranscriptionError`, `transcribedAt`.
- `TicketActionDTO` thêm `warnings: string[]?` (JsonIgnore khi null).
- `TicketActivityDTO` thêm `sourceTicketId` (activity kéo sang khi merge).
- `ActivityActionEnum` thêm **`ParticipantAdded=34`, `ParticipantRemoved=35`, `ParticipantRoleChanged=36`**.
- `PauseReasonEnum` thêm **`AwaitingCustomerChat = 4`**.
- `SlaTimerStatusEnum` thêm **`Stopped = 5`**.
- 5 enum chưa từng có trong doc: `TicketVerifyStatusEnum`, `TicketCloseReasonEnum`, `VoiceTranscriptionStatusEnum`, `AssignmentRoleEnum`, `StaffSkillTierEnum`.
- Định nghĩa `ParticipantHistoryDTO` (trước chỉ được nhắc tên).

**🟢 Endpoint thiếu trong doc — đã bổ sung:**

- `POST /api/admin/tickets/{id}/merge` (Manager) · `POST /api/admin/tickets/{id}/re-verify` (Manager).
- `POST .../chats/{id}/attachments/batch` · `GET .../chats/files` · `DELETE .../chats/bulk` · `POST .../chats/{chatId}/voice/retry` · `GET /api/chats/unread-count`.

**Ghi chú:** `GET /api/tickets/{id}` ví dụ JSON trước đây còn field `comments` — đã sửa thành `chats`.

### 2026-08-01 — GH-866 (PR #955): retire 4 endpoint chat + siết validation tạo ticket

**Breaking changes — 4 endpoint đã bị XÓA, FE không được gọi:**
- `POST /api/tickets/{ticketId}/chats/from-template/{templateId}` — cùng toàn bộ Chat Template API (`ChatTemplatesController`, entity `ChatTemplate`, 2 enum `ChatTemplateCategoryEnum`/`ChatTemplateScopeEnum`). Xóa vĩnh viễn, không khôi phục.
- `GET /api/tickets/{ticketId}/chats/export-pdf` — bỏ luôn dependency QuestPDF.
- `POST /api/tickets/{ticketId}/chats/sentiment-check` — bỏ `ChatSentimentCheckDTO`.
- `PATCH /api/chats/mentions/{id}/acknowledge`.

**`TicketChatMentionDTO`:**
- Bỏ `isAcknowledged`, `acknowledgedAt` (không còn cơ chế ACK mention).
- **Thêm `isInternal` (`bool`)** — mention thuộc chat nội bộ hay public. FE dùng để chọn view và hiển thị chỉ báo; **không phải** authz check, BE vẫn lọc mention theo quyền và validate lại ở mọi API chat.
- `mentionedUserRole` serialize dạng **chuỗi** (`"Staff"`) — như mọi enum khác của TicketService (`JsonStringEnumConverter` đăng ký global tại `Program.cs`). Không phải số.

**`GET /api/chats/mentions/me`:** bỏ query param `unreadOnly`, chỉ còn `page`/`pageSize`.

**`POST /api/customer/tickets`:**
- `incidentDetectedAt` là **một mốc thời gian** (thay cặp from/to cũ) — bắt buộc UTC, không được ở tương lai.
- `batteryAssetIds` bắt buộc, ≥1 phần tử, distinct, mỗi asset phải thuộc quyền customer.
- Attachment là metadata client gửi (`fileId`, `fileName`, `contentType`, `sizeBytes`, `url`). BE validate cấu trúc + chặn `fileId` trùng trong 1 request và chặn attach lại `fileId` đang active vào cùng ticket. **Ownership của file cố ý không được TicketService kiểm tra.**

**Chat duplicate/spam — 2 mã lỗi mới:**
- `400` + `CHAT_DUPLICATE_MESSAGE_LIMIT` — 2 chat trùng nội dung của cùng user trên cùng ticket trong 5 phút vẫn được nhận; cái **thứ 3** bị chặn và không lưu. Khác với `429` rate limit.
- `409` + `CHAT_SPAM_CHECK_IN_PROGRESS` — spam check đang chạy đồng thời; client được phép retry với backoff ngắn.

**Manager queue:** queue items và `totalItems` chỉ tính ticket `New`, chưa xóa, chưa merge.

**Bổ sung doc (đã có trong code từ trước nhưng thiếu tài liệu):** Nhóm — Ticket Participants (7 endpoint `/api/tickets/{ticketId}/participants`), kèm `TicketParticipantDTO` và `ParticipantTypeEnum`; mục chi tiết cho `GET .../chats/{id}/readers`.

**`ChatReaderDTO` thêm `displayName`:** trước đây chỉ có `userId` (UUID) nên FE không hiển thị được tên người đọc. Nay BE resolve từ `CustomerAccounts`/`StaffAccounts` theo `role`, fallback về `userId` khi không tìm thấy — cùng cách `TicketParticipantDTO.displayName` đã làm.

**Sửa doc sai:** `GET .../chats/unread-count` trước ghi `CommonResponse<{ unreadCount: int }>` — thực tế là `CommonResponse<int>` (`data` là số thuần). FE đọc `data.unreadCount` sẽ luôn `undefined`.

### 2026-07-30 — Sprint 6.2 (`#672..#688`): event vòng đời ticket + enrich payload cho NotificationService

- **`ActivityActionEnum` thêm `RatingRequested = 33`** — FE/Mobile phải mirror. Bảng enum trong doc
  cũng đã bổ sung 26–32 (nhóm Chat) vốn bị thiếu và sửa `Commented` → `Chatted` (giá trị `6`).
- **6 event mới trong `SharedContracts`** (NOTI-07): `TicketStatusChangedEvent`, `TicketApprovedEvent`,
  `TicketRejectedEvent`, `TicketClosedEvent`, `TicketReopenedEvent`, `TicketRatingRequestedEvent` —
  publish **song song** event nội bộ cũ, không xoá. ⇒ 2 giá trị enum chết bên NotificationService
  (`TicketStatusChanged`, `TicketClosed`) nay có producer thật.
- **4 event đổi payload** (NOTI-05): `TicketCreatedEvent` (+`CustomerId`, +`Priority?`),
  `TicketAssignedEvent` (+`CustomerId`), `TicketResolvedEvent` (+`CustomerId`),
  `SlaWarningEvent` (+`StaffId?`). ⚠️ Record positional ⇒ **breaking cho mọi consumer khác**.
- **`RatingRequestBackgroundService` mới** — nhắc đánh giá 1 lần/ticket, mặc định sau **3 ngày**
  (không phải 7 — xem lý do ở trên), idempotent bằng `TicketActivity(RatingRequested)`.
- **`AutoCloseBackgroundService`** publish thêm `TicketClosedEvent(IsAutoClosed: true)`.
- **`SlaTimerBackgroundService`** publish `SlaWarningEvent` kèm `StaffId` của Staff đang assign.
- **Hạ tầng bus (Sprint 6.3 NOTI3-08, ảnh hưởng cả 8 service):** consumer nay được retry **3 lần**
  (exponential 200ms→5s) trước khi rơi `_error`; DLQ được giám sát. `UseDelayedRedelivery` **tắt mặc
  định** (cần plugin RabbitMQ chưa có trong image).
- **Sửa lỗi saga 30/07/2026:** thêm `cfg.UsePublishMessageScheduler()` — thiếu nó, `AlertTicketSaga`
  và `ChatEscalationReview` ném `MassTransit.PayloadNotFoundException: MessageSchedulerContext` mỗi
  lần `.Schedule(...)` → retry → rơi `_error`. Đo được **1662 message** kẹt ở
  `AlertTicketSagaState_error`, `qrtz_triggers` = 0 dòng, 2 saga treo vĩnh viễn ở
  `TicketRequested`/`AlertLinkRequested` vì timeout không bao giờ nổ.

> Chi tiết phía nhận (kênh, template, preference, quiet hours): xem [`api-notification.md`](api-notification.md).

### 2026-07-07 — Dashboard aggregate stats + SLA filter + KB reference rules
- **Thêm `GET /api/tickets/dashboard/stats`** (Nhóm 4, Manager/Admin) — snapshot KPI toàn hệ thống: total/openCount, SLA summary + compliancePercent, countByStatus (zero-fill 14 status), countByPriority, createdTrend7Days (UTC), openCountByStaff. Thay cho FE tự đếm trên 1 trang list.
- **Thêm `GET /api/staff/tickets/dashboard/stats`** (Nhóm 3, Staff) — snapshot KPI per-staff từ JWT: open/resolved, nearBreach (≤25%)/breached/paused/slaMonitored, slaRisk, countByStatus, trend 7 ngày.
- **`GET /api/staff/tickets/me`:** thêm query param `SlaOpen` (filter server-side nhóm ticket đang theo dõi SLA — bảng SLA Monitor hết bị cap theo pageSize) và `SortBy=slaRemaining` (sort theo dueAt tăng dần, ticket không timer xếp cuối).
- **`POST /api/knowledge-base/references`:** (1) nới quy tắc trạng thái — state `Resolved` cho phép gán 2 type after-resolve (`GeneratedAfterResolve`, `ProvidedToCustomer`); (2) chặn bài `isInternalOnly` với type `ProvidedToCustomer`; (3) chuẩn hóa status code: state lock đổi `403` → **`409`** (xung đột trạng thái), rule nội bộ trả **`422`**, `403` chỉ còn cho lỗi quyền. ⚠️ FE đang bắt riêng 403 cho state lock cần đổi theo.
- **`KbArticleSuggestDTO`:** thêm field `isInternalOnly` (bool).

### 2026-06-24 — Sprint 7 #114: bổ sung Nhóm 12 — Reports (9 endpoint)
- Thêm 9 endpoint `GET /api/reports/*`: `sla-by-staff`, `sla-by-priority`, `ticket-volume`, `top-reopen-issues`, `staff-performance`, `csat`, `resolution-time-histogram`, `category-breakdown`, `saga-failed-rate`.
- Tất cả hỗ trợ `?format=csv|xlsx` (export ClosedXML/CSV) hoặc trả JSON. Auth Admin/Manager; `saga-failed-rate` chỉ Admin.
- Tài liệu hóa đầy đủ DTO response (field/type/nullable) + query params + JSON mẫu, đối chiếu trực tiếp code (`TicketReportDtos`, `ReportsController`).

### 2026-06-22 — Đối chiếu DTO/request với code thực tế (6 fix)

Verify toàn bộ doc với codebase TicketService. Enums (16/16) và SignalR Hub khớp 100%. Sửa 6 sai lệch:

- **`MaintenanceLogDTO`:** xóa field `ticketId` — DTO thực tế ([`MaintenanceLogDTO.cs`]) **không có** property này (đừng nhầm với `TicketActionDTO.ticketId`).
- **`MaintenanceLogDTO`:** `attachmentFileIds`/`beforePhotosFileIds`/`afterPhotosFileIds`/`relatedKbArticleIds` trước ghi `string[]?` (Null) — thực tế là `List<string>` default `[]`, **không bao giờ null**. Đổi sang `string[]` (luôn mảng).
- **`TicketCommentDTO`:** `authorUserId` thực tế `string=""` (không null, sửa từ `string?`); `attachmentFileIds` thực tế `List<string>` default `[]` (không null, sửa từ `string[]?`).
- **`TicketDTO.slaTimer`:** thực tế `SlaTimerDTO?` — null khi ticket chưa triage. Sửa từ non-null sang nullable.
- **`POST /api/staff/tickets/{id}/start`:** xóa field `latitude`/`longitude` khỏi body — `TicketStartCommand` **chỉ có** `logType`. Tọa độ check-in chỉ ghi qua endpoint maintenance-log.
- **`POST /api/customer/tickets`:** bỏ "max 200/2000 ký tự" khỏi validation `title`/`description` — `TicketCreateCommand.ValidateAsync()` chỉ check không rỗng, **không** enforce độ dài.

### 2026-06-22 — Bổ sung SignalR Hub + sửa doc `POST .../comments`

- **Thêm mục "Realtime — SignalR Hub `/hubs/ticket-comments`"** (sau Nhóm 1) — endpoint, auth qua query `access_token`, 3 method `JoinTicket`/`LeaveTicket`/`Typing`, 2 event `CommentAdded`/`UserTyping`, group routing theo `isInternal`, và snippet test nhanh bằng `@microsoft/signalr` CDN cho FE dán vào console.
- **Sửa `POST /api/tickets/{ticketId}/comments`:** `CommentAttachmentInput.fileName`/`.contentType` trước đây ghi "Không bắt buộc" — **SAI**, `ValidateAsync` thực tế reject `400` nếu rỗng. Đã sửa thành bắt buộc, thêm so sánh với `MaintenanceAttachmentInput` (không validate field-level). Bổ sung `400` vào "Lỗi thường gặp" (trước đó thiếu) và note hành động phụ phát SignalR event sau khi tạo comment.

### 2026-06-27 — Ticket Chats system: thay thế Comments + bổ sung AI endpoints

**Breaking changes:**
- `GET/POST /api/tickets/{ticketId}/comments` — **REMOVED**. Controller `TicketCommentsController` đã bị xóa.
- `TicketDetailDTO.comments` (kiểu `TicketCommentDTO[]`) → `TicketDetailDTO.chats` (kiểu `TicketChatDTO[]`).
- SignalR Hub path: `/hubs/ticket-comments` → `/hubs/ticket-chats`.
- SignalR event: `CommentAdded` → `ChatAdded`. Payload: `TicketCommentDTO` → `TicketChatDTO`.
- Hub notifier class: `SignalRTicketCommentNotifier` → `SignalRTicketChatNotifier`.

**Endpoints mới (TicketChatsController — `/api/tickets/{ticketId}/chats`):**
33 endpoints thay thế và mở rộng hệ thống comment cũ: CRUD + reply thread + pin + reaction + mark-read + readers + unread-count + cursor + attachment (single/batch/download) + KB integration (attach-kb/to-kb-draft/kb-suggestions) + AI (suggest/summarize/translate) + voice (upload/retry) + bulk delete + escalation-review ACK.

**Endpoints mới (AdminTicketChatsController — `/api/admin/tickets/{ticketId}/chats`):**
4 endpoints Admin-only override cho ticket đã Closed: `closed-override` POST/PUT/DELETE, `restore` PATCH.

**Endpoints mới (Chats Utilities — `/api/chats/...`):**
`GET /api/chats/me`, `POST /api/chats/erase-my-data` (GDPR), `GET /api/chats/search` (Manager/Admin), `GET /api/chats/mentions/me`.

**ApiGateway:** Không cần thay đổi — các catch-all routes (`/api/tickets/{**catch-all}`, `/api/admin/tickets/{**catch-all}`, `/api/chats/{**catch-all}`, `/hubs/ticket-chats`) đã cover toàn bộ.

**Enums mới:** `ChatBodyFormatEnum`, `ReactionTypeEnum`, `ChatAiIntentEnum`.

**DTOs mới:** `TicketChatDTO`, `TicketChatReactionsAggregateDTO`, `ChatReactionGroupDTO`, `TicketChatMentionDTO`, `ChatEditHistoryDTO`, `ChatSuggestDTO`, `ChatSummarizeDTO`, `ChatTranslateDTO`.

---

### 2026-06-22 — Fix Knowledge Base enum bị khai sai kiểu `int`

- **Breaking change cho FE:** `GetKbArticleListQuery.Category`/`.Status` (query param `GET /api/knowledge-base`), `KbArticleVersionDTO.status`, `KbArticleTemplateDTO.category` trước đây khai sai kiểu `int`/`int?` — đã sửa sang đúng enum (`TicketCategoryEnum`, `KbArticleStatusEnum`, `KbVersionStatusEnum`). Toàn bộ 4 field này giờ gửi/nhận **chuỗi tên enum** (vd `Category=Charging`, `"status": "Approved"`), không còn ngoại lệ số như trước.
- Bổ sung endpoint `GET /api/knowledge-base/{id}/usage-stats` (Nhóm 8) — đã có trong code (Manager/Admin only) nhưng thiếu trong doc.
- Gom 3 enum của domain Knowledge Base (`KbArticleStatusEnum`, `KbVersionStatusEnum`, `KbReferenceTypeEnum`) vào mục **Enums** chung đầu tài liệu — bỏ bản liệt kê dạng bullet trùng lặp ở cuối file.
- Đổi tên các DTO trong doc từ hậu tố `Dto` sang `DTO` (khớp tên class C# thật, ví dụ `KbArticleDTO`, `TicketActionDTO`, `AlertTicketSagaDTO`) — áp dụng cho toàn bộ file, không chỉ phần Knowledge Base.

---

### Lịch sử Knowledge Base & Blog (gộp từ `api-ticket-kb-blog.md`)

### 2026-07-19 (b) — Sửa contract Blog/KB + đồng bộ docs với code (feat/GH-671-blog)

**Thay đổi code:**

- **`KbArticleDTO` / `KbArticleListItemDTO`:** thêm field `isTemplate` (`bool`) vào **response**. Trước đây BE có cột trong DB và nhận được lúc create, nhưng không trả ra — FE ghi được mà không đọc được.
- **`GET /api/blog/{id}`:** nay trả **`404` khi bài chưa `Published`**. Trước đây handler không filter theo status → endpoint public lộ bài `Draft`/`Generating`/`Archived` cho mọi role kể cả Customer (lỗ rò dữ liệu).
- **`GET /api/internal/blog/{id}` (mới — Nhóm 13):** đọc bài blog ở mọi trạng thái, dành cho Staff/Manager/Admin. Đây là endpoint chuẩn để poll sau `generate-from-kb`.
- **6 endpoint GET trả đúng HTTP status:** `GET /api/blog`, `GET /api/blog/{id}`, `GET /api/internal/blog`, `GET /api/internal/blog/{id}/versions`, `GET /api/internal/blog/templates`, `GET /api/internal/blog/templates/{id}` — trước dùng `Ok()` nên luôn trả HTTP `200` kể cả khi body ghi `statusCode: 404/409`, khiến client không bắt được lỗi qua HTTP status.

**Sửa docs (docs sai so với code, code không đổi):**

- **`GET /api/knowledge-base`:** bỏ `IsTemplate` khỏi bảng query params — property gắn `[BindNever]` và bị controller ghi đè `= false`, client **không** gửi được. Mục 2026-07-17 bên dưới mô tả sai điểm này.
- **`GET /api/internal/knowledge-base/{id}/compare`:** tên param đúng là **`FromVersionId`/`ToVersionId`**, không phải `fromVersion`/`toVersion`.
- **`GET /api/internal/knowledge-base/templates`** (Nhóm 9): bổ sung — endpoint đã tồn tại nhưng chưa được document.
- **Nhóm 10bis** (mới): bổ sung toàn bộ `AdminKbTemplateController` (`/api/admin/knowledge-base/templates`, 8 endpoint, Admin only) — chưa từng được document.

### 2026-07-19 (a) — Di chuyển Blog Template GET, thêm KB Template GET (feat/GH-671-blog)

- **`GET /api/admin/blog/templates`** và **`GET /api/admin/blog/templates/{id}`**: **đã xóa** khỏi admin controller. Hai endpoint này chuyển sang Nhóm 13.
- **`GET /api/internal/blog/templates`** (mới — Nhóm 13): Danh sách blog templates, auth Staff/Manager/Admin.
- **`GET /api/internal/blog/templates/{id}`** (mới — Nhóm 13): Chi tiết blog template, auth Staff/Manager/Admin.
- **`GET /api/internal/knowledge-base/templates/{id}`** (mới — Nhóm 9): Lấy chi tiết bài viết KB kể cả là template, dùng để preview trước khi copy.
- **Nhóm 15**: Đổi mô tả — chỉ còn POST/PUT/DELETE (Admin only); đọc template qua Nhóm 13.

### 2026-07-18 — Thêm Blog module (feat/GH-671-blog)

- **Enums mới:** `BlogPostStatusEnum` (Generating/GenerationFailed/Draft/Published/Archived), `BlogPostOriginEnum` (Manual/AiGeneratedFromKb).
- **DTOs mới:** `BlogPostDTO`, `BlogPostListItemDTO`, `BlogPostVersionDTO`, `BlogDiffDTO`, `BlogPostActionDTO`, `BlogTemplateDTO`.
- **Nhóm 12** — `GET /api/blog`, `GET /api/blog/{id}`: public blog read (Published only).
- **Nhóm 13** — `GET /api/internal/blog`, `POST /api/internal/blog`, `PUT /api/internal/blog/{id}`, `GET /api/internal/blog/{id}/versions`, `GET /api/internal/blog/{id}/compare`: soạn thảo và lịch sử version.
- **Nhóm 14** — `POST /api/admin/blog/generate-from-kb/{kbId}` (async AI, `202`), `POST .../publish`, `POST .../archive`, `DELETE .../{id}`.
- **Nhóm 15** — CRUD template qua `/api/admin/blog/templates` (đọc: Staff+; ghi: Admin only).

### 2026-07-17 — Thêm field `isTemplate` (feat/GH-671.2)

- **`KbArticleDTO`:** thêm field `isTemplate` (`bool`) — đánh dấu bài viết là mẫu.
- **`KbArticleListItemDTO`:** thêm field `isTemplate` để FE filter bài mẫu trong list.
- **`POST /api/internal/knowledge-base`:** thêm field `isTemplate` vào request body (mặc định `false`).
- **`PUT /api/internal/knowledge-base/{id}`:** hỗ trợ cập nhật `isTemplate`.
- ~~**`GET /api/knowledge-base`:** thêm query param `IsTemplate` (`bool?`) để lọc bài mẫu.~~ **← SAI, xem changelog 2026-07-19 (b).** Property gắn `[BindNever]` và bị controller ghi đè `= false`; client không gửi được. Dùng `GET /api/internal/knowledge-base/templates` thay thế.
- **`GET /api/internal/knowledge-base/{id}/copy-template`:** mở rộng điều kiện — ngoài tag `template`/`example`, bài có `isTemplate = true` cũng dùng được.

### 2026-07-07 — KB reference rules update
- **`POST /api/knowledge-base/references`:** (1) nới quy tắc trạng thái — state `Resolved` cho phép gán 2 type after-resolve (`GeneratedAfterResolve`, `ProvidedToCustomer`); (2) chặn bài `isInternalOnly` với type `ProvidedToCustomer`; (3) chuẩn hóa status code: state lock đổi `403` → **`409`**, rule nội bộ trả **`422`**, `403` chỉ còn cho lỗi quyền.
- **`KbArticleSuggestDTO`:** thêm field `isInternalOnly` (bool).

### 2026-06-22 — Fix KB enum bị khai sai kiểu `int`
- `GetKbArticleListQuery.Category`/`.Status`, `KbArticleVersionDTO.status`, `KbArticleTemplateDTO.category` đổi sang đúng enum chuỗi — KHÔNG còn nhận/trả số.
- Bổ sung endpoint `GET /api/knowledge-base/{id}/usage-stats` (Manager/Admin only).

---

## Nhóm — Audit Logs nội bộ (Option C — Sprint audit)

> Endpoint **dự phòng (fallback resilience)**: query trực tiếp bảng nguồn `ticket_audit_logs` ngay tại TicketService, dùng được kể cả khi `AuditAggregatorService` gặp sự cố. Enum `Severity`/`ActionCategory` dùng chung — xem [docs/api-audit.md](api-audit.md#enums--tập-giá-trị-cố-định).
>
> **Lưu ý:** đây là `ticket_audit_logs` (audit forensic), **TÁCH BIỆT** với `TicketActivity` (dòng thời gian hiển thị cho user trên UI).
>
> **Auth:** chỉ role `Admin` (`401` thiếu token / `403` sai role).

### `GET /api/admin/ticket/audit-logs`

**Mục đích:** Tra cứu audit log thao tác trên TICKET, có phân trang + lọc.

**Tác dụng:** Điều tra SLA breach / escalation (ai làm gì với ticket, khi nào), compliance, truy trách nhiệm.

**Auth:** Admin.

**Query params (đều optional):**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `action` | `string?` | Không | Mã action (vd `StateTransitioned`). Bỏ trống = tất cả |
| `ticketId` | `string?` (UUID) | Không | Lọc theo ticket cụ thể (target) |
| `from` | `DateTime?` | Không | Mốc đầu (UTC) |
| `to` | `DateTime?` | Không | Mốc cuối (UTC) |
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 50, trần 100) | Số item/trang |

**Action codes — 28 giá trị** (`TicketAuditActionEnum`, đánh số từ 1).

Param `action` nhận **tên chuỗi** (vd `action=StateTransitioned`), không nhận số. Bỏ trống = tất cả.
So khớp **chính xác, phân biệt hoa-thường**.

**Nhóm A — vòng đời ticket (21, enum 1–21, `#AUDIT-24`):**

| Action code | Enum | Severity | Category | Khi nào ghi |
|---|---|---|---|---|
| `TicketCreated` | 1 | `Info` | `DataModification` | Ticket được tạo (thủ công) |
| `StateTransitioned` | 2 | `Info` | `DataModification` | Đổi trạng thái ticket |
| `PriorityChanged` | 3 | **`Security`** | `DataModification` | Đổi priority — nhạy cảm vì priority quyết định SLA |
| `AssignedToStaff` | 4 | `Info` | `DataModification` | Gán staff xử lý |
| `UnassignedFromStaff` | 5 | `Info` | `DataModification` | Gỡ staff khỏi ticket |
| `SlaPaused` | 6 | `Info` | `DataModification` | Tạm dừng đồng hồ SLA |
| `SlaResumed` | 7 | `Info` | `DataModification` | Chạy lại đồng hồ SLA |
| `SlaBreached` | 8 | **`Critical`** | **`Security`** | SLA hết hạn |
| `EscalatedToManager` | 9 | **`Critical`** | **`Security`** | Leo thang lên Manager |
| `EscalatedToAdmin` | 10 | **`Critical`** | **`Security`** | Leo thang lên Admin |
| `MaintenanceLogAdded` | 11 | `Info` | `DataModification` | Thêm nhật ký bảo trì |
| `CommentAdded` | 12 | `Info` | `DataModification` | Thêm bình luận |
| `AttachmentUploaded` | 13 | `Info` | `DataModification` | ⚠️ **chưa được ghi** — xem ghi chú dưới bảng |
| `AttachmentDeleted` | 14 | `Info` | `DataModification` | ⚠️ **chưa được ghi** |
| `ResolutionAdded` | 15 | `Info` | `DataModification` | Ghi kết quả xử lý |
| `ClosedByUser` | 16 | `Info` | `DataModification` | ⚠️ **chưa được ghi** |
| `ReopenedByAdmin` | 17 | `Info` | `DataModification` | Admin mở lại ticket |
| `RejectedByManager` | 18 | **`Security`** | `DataModification` | Manager từ chối kết quả / đóng ngoài scope |
| `FalseAlarmMarked` | 19 | `Info` | `DataModification` | ⚠️ **chưa được ghi** |
| `CustomerRated` | 20 | `Info` | `DataModification` | Customer chấm điểm |
| `AutoCreatedFromAnomaly` | 21 | `Info` | `DataModification` | Ticket tự sinh từ cảnh báo pin |

> `AutoCreatedFromAnomaly` có `causationId = OriginAlertId` (chuỗi nhân-quả anomaly → ticket).

> ⚠️ **4 mã ĐÃ KHAI BÁO nhưng CHƯA CÓ handler nào ghi** (rà mã nguồn 2026-08-01):
> `AttachmentUploaded` (13) · `AttachmentDeleted` (14) · `ClosedByUser` (16) · `FalseAlarmMarked` (19).
> Chỉ **24/28** mã thực sự xuất hiện trong `ticket_audit_logs`.
>
> **Với FE:** lọc theo 4 mã này luôn trả **`200` + danh sách rỗng**, không phải lỗi. Nếu dựng dropdown
> chọn action thì hoặc ẩn 4 mã này đi, hoặc gắn nhãn "chưa có dữ liệu" — đừng để người dùng tưởng
> mất dữ liệu. Thao tác tương ứng vẫn có vết ở `TicketActivity` (dòng thời gian UI), chỉ là chưa có
> bản ghi **audit forensic**.

**Nhóm B — module Chat (7, enum 22–28, Sprint Chat DoD 2026-07-31):**

| Action code | Enum | Severity | Category | Khi nào ghi |
|---|---|---|---|---|
| `ChatCreated` | 22 | `Info` | `DataModification` | Gửi tin nhắn mới vào ticket |
| `ChatEdited` | 23 | `Info` | `DataModification` | Sửa nội dung tin nhắn |
| `ChatDeleted` | 24 | `Info` | `DataModification` | Xoá mềm tin nhắn |
| `ChatPinned` | 25 | `Info` | `DataModification` | Ghim tin nhắn |
| `ChatUnpinned` | 26 | `Info` | `DataModification` | Gỡ ghim |
| `ChatReacted` | 27 | `Info` | `DataModification` | Thả reaction (kể cả khôi phục reaction đã gỡ) |
| `ChatMentioned` | 28 | `Info` | `DataModification` | Tin nhắn có tag người |

> **Vì sao có nhóm này:** trước 2026-07-31 module Chat **không ghi audit nào**. Kênh trao đổi
> Customer ↔ Staff/Manager là nơi dễ tranh chấp nội dung nhất (sửa/xoá tin, gỡ ghim, tag nhầm người)
> mà lại không có vết forensic.
>
> ⚠️ **`targetId` của nhóm Chat là ID TICKET, KHÔNG phải ID tin nhắn.** ID tin nhắn nằm trong
> `metadata_json.chatId`. Nhờ vậy `?ticketId=` gom được cả thao tác chat của ticket đó. `targetDisplay`
> = mã ticket.
>
> **Gửi một tin có tag người sinh HAI bản ghi** (`ChatCreated` + `ChatMentioned`) — cố ý, để tra
> "ai bị tag vào ticket này" độc lập với "ai gửi tin".
>
> **`metadata_json` theo từng action:**
>
> | Action | Khoá trong `metadata_json` | Kiểu |
> |---|---|---|
> | `ChatCreated` | `chatId` · `isInternal` | UUID · bool (tin nội bộ hay công khai) |
> | `ChatEdited` · `ChatDeleted` · `ChatPinned` · `ChatUnpinned` | `chatId` | UUID |
> | `ChatReacted` | `chatId` · `reactionType` | UUID · chuỗi tên `ReactionTypeEnum` (`ThumbsUp`/`Acknowledged`/`Resolved`/`NeedMoreInfo`/`Disagree`) |
> | `ChatMentioned` | `chatId` · `mentionedUserIds` | UUID · mảng UUID |
>
> ⚠️ **`metadata_json` KHÔNG có trong response của endpoint này.** `TicketAuditLogDto` (bảng bên dưới)
> không chứa field đó. Muốn đọc `chatId`/`reactionType`/`mentionedUserIds` phải dùng Audit Aggregator:
> `GET /api/admin/audit/search?service=TicketService&action=ChatReacted` → `AuditAggregateDto.metadataJson`.
> Xem [docs/api-audit.md](api-audit.md#dto-auditaggregatedto).

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketAuditLogDto>>` (mới nhất trước).

**`DTO TicketAuditLogDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bản ghi audit |
| `eventId` | `string` | Không | Idempotency key |
| `actionCode` | `string` | Không | Mã hành động |
| `actionCategory` | `string` | Không | Category |
| `severity` | `string` | Không | Mức độ |
| `targetId` | `string?` | Null nếu không gắn | ID ticket (target) |
| `actorAccountId` | `string?` | Null nếu hệ thống | Account thực hiện |
| `correlationId` | `string?` | Null nếu không gắn luồng | Id luồng nghiệp vụ |
| `causationId` | `string?` | Null nếu không có | Id event gây ra (vd OriginAlertId) |
| `isSuccess` | `bool` | Không | Thành công/thất bại |
| `reason` | `string?` | Null nếu không có | Lý do/ghi chú |
| `occurredAt` | `DateTime` | Không | Thời điểm xảy ra (UTC) |

**Lỗi:** `401` / `403`.
