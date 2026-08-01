# API Documentation — TicketService

> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`
> **ID fields:** Tất cả `id` trong response đều là `string` (UUID dạng `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Entity C# dùng `Guid` nhưng serialize thành `string` trong JSON — TypeScript dùng `string` cho mọi id field.

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

**`TicketStatusEnum`:** `New=1` (chờ triage) · `Open=2` (chờ Manager duyệt) · `Assigned=3` · `InProgress=4` · `WaitingCustomer=5` · `WaitingParts=6` · `WaitingOnsiteSchedule=7` · `Resolved=8` · `Escalated=9` · `ClosedPendingRate=10` · `Closed=11` · `ClosedRejected=12` · `Incident=13` · `Approved=14`.
**`TicketPriorityEnum`:** `P1Critical=1` (SLA ngắn nhất) · `P2High=2` · `P3Normal=3`.
**`TicketCategoryEnum`:** `Charging=1` · `Overheat=2` · `NoPower=3` · `Performance=4` · `Other=5` · `Repair=6`.

> Sort theo enum = sort theo **giá trị số** (New→Approved, P1→P3…), không theo tên hiển thị.

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

**TypeScript types:**
```ts
interface TicketActionDTO {
  id: string;
  ticketId?: string; // chỉ có ở response của POST maintenance-logs
  code: string;
  status: TicketStatusEnum;
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
| `New` | 1 | Vừa tạo, chờ triage |
| `Open` | 2 | Đã triage sơ bộ, chờ Manager phê duyệt |
| `Assigned` | 3 | Đã gán Staff, chờ Staff xác nhận và bắt đầu |
| `InProgress` | 4 | Staff đang xử lý |
| `WaitingCustomer` | 5 | Tạm dừng — chờ khách hàng phản hồi |
| `WaitingParts` | 6 | Tạm dừng — chờ linh kiện |
| `WaitingOnsiteSchedule` | 7 | Tạm dừng — chờ lịch hẹn tại chỗ |
| `Resolved` | 8 | Staff báo đã xong, chờ Manager kiểm tra |
| `Escalated` | 9 | Đã được chuyển cấp xử lý (SLA breach hoặc Staff/Manager request) |
| `ClosedPendingRate` | 10 | Manager đã phê duyệt kết quả, chờ Customer đánh giá |
| `Closed` | 11 | Đã đóng chính thức (sau khi Customer rate) |
| `ClosedRejected` | 12 | Manager từ chối ticket tại **triage** (`Open → ClosedRejected`) hoặc reject ticket đã `Escalated`. ⚠️ KHÔNG dùng cho reject kết quả resolve — luồng đó chuyển thẳng `Resolved → InProgress` |
| `Incident` | 13 | Sự cố nghiêm trọng được Admin/Manager đánh dấu |
| `Approved` | 14 | Manager đã phê duyệt tính hợp lệ, chờ gán Staff |

**State machine chính:**
```
New → Open → Approved → Assigned → InProgress → Resolved → ClosedPendingRate → Closed
                                        ↕ (hold/resume)
                               WaitingCustomer / WaitingParts / WaitingOnsiteSchedule

New        → Approved (Manager approve trực tiếp) | → Escalated
Open       → ClosedRejected (Manager triage-reject)
Approved   → Escalated
Assigned   → Assigned (Manager reassign) | → Escalated (System: SLA breach)
InProgress → Escalated (Staff/Manager request hoặc SLA breach)
Resolved   → InProgress (Manager reject — KHÔNG qua ClosedRejected)
Escalated  → Assigned (reassign) | → Incident | → ClosedRejected
Incident   → Assigned
ClosedPendingRate → Open (Customer reopen → Manager triage lại; lần 2+ tự Escalated)
```

> **Lưu ý quan trọng (đã verify với `TicketStateMachine`):**
> - **Reopen** chuyển `ClosedPendingRate → **Open**` (chờ Manager triage lại), **KHÔNG phải `InProgress`**.
> - **Manager reject kết quả** (`reject`) chuyển `Resolved → **InProgress**` trực tiếp; `ClosedRejected` chỉ dùng cho **triage-reject** (`Open/Escalated → ClosedRejected`). Enum `ClosedRejected` được mô tả là "Manager từ chối kết quả... quay về InProgress" — thực tế đó là 2 luồng khác nhau.
> - **Auto-escalate khi reopen**: kích hoạt từ **lần reopen thứ 2** (`ReopenCount >= 2`, count được tăng trước khi check).
> - State `Closed` là terminal — mọi transition tiếp theo bị chặn.

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

### `SlaTimerStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Running` | 1 | Đang đếm ngược |
| `Paused` | 2 | Đang tạm dừng (hold) |
| `Met` | 3 | Đã giải quyết đúng hạn |
| `Breached` | 4 | Đã vi phạm SLA |

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

> ⚠️ **Giá trị `21` không tồn tại** (bị bỏ trống trong enum). FE không được giả định enum liên tục.
> **Sprint 6.2 thêm `RatingRequested = 33`** — FE/Mobile phải mirror.

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
| `batteryAssetId` | `string` | Không (default `""`) | ID thiết bị pin — BE trả **chuỗi rỗng `""`** (không phải `null`, không phải GUID toàn số 0) khi ticket **không liên quan pin cụ thể**: vd ticket site-level từ sự cố môi trường (`origin = System`, Sprint Bonus NS-22 #662). FE nên coi `""` = ticket cấp site, không fetch pin |
| `customerId` | `string` | Không | ID khách hàng tạo ticket |
| `assignedStaffId` | `string?` | Null khi chưa gán | ID Staff được gán |
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
| `slaTimer` | `SlaTimerDTO?` | **Null khi chưa có SLA timer** | Thông tin SLA timer hiện tại. Timer được tạo khi ticket chuyển sang **`Assigned`** (Sprint Bonus NS-12 #656 — trước đó timer không được tạo ở runtime); ticket auto-tạo P1 (cascade NS-13 / env incident NS-22) có timer **ngay khi tạo**. `null` ở các state trước khi có timer (`New`/`Open`/vừa triage chưa assign) |

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
| `mentionedUserRole` | `ActorRoleEnum` | Role người được mention |
| `mentionedDisplayName` | `string?` | Tên hiển thị |
| `isAcknowledged` | `bool` | Đã xem/xác nhận |
| `acknowledgedAt` | `string?` | Thời điểm xác nhận |
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

> ⚠️ **Lưu ý:** DTO này **không xuất hiện trong bất kỳ response nào hiện tại**. `TicketDetailDTO` chỉ trả về `attachmentFileIds: string[]` (mảng FileId), không trả object attachment đầy đủ. Giữ lại đây để tham khảo shape entity nội bộ.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID attachment |
| `fileId` | `string` | Không | FileId từ FileStorageService |
| `fileName` | `string` | Không | Tên file gốc |
| `contentType` | `string` | Không | MIME type |
| `sizeBytes` | `int64` | Không | Kích thước file (bytes) |
| `uploadedByUserId` | `string?` | Null | ID người upload |
| `createdAt` | `string` | Không | Thời điểm upload (UTC) |

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
    "createdAt": "2026-06-05T08:00:00Z",
    "slaTimer": { ... },
    "activities": [...],
    "comments": [...],
    "maintenanceLogs": [...]
  }
}
```

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

#### `UserTyping(ticketId: string, userId: string, displayName: string)`

Push tới **người khác** (không phải chính người gõ) trong group `ticket:{ticketId}:public` khi có client gọi `Typing`. **Lưu ý:** chỉ broadcast qua group public — Staff/Manager/Admin xem comment nội bộ vẫn nhận được (họ cũng ở group public), nhưng hiện tại không có channel typing riêng cho nhóm internal.

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

> Thay thế toàn bộ hệ thống Comments cũ. `POST` và `PUT` áp dụng rate limit (`ChatWritePolicy`).

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
| `DELETE` | `/api/tickets/{ticketId}/chats/{id}/attachments/{attachmentId}` | Mọi role | Xóa attachment |
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
| `POST` | `/api/tickets/{ticketId}/chats/from-template/{templateId}` | Staff/Manager/Admin | Gửi chat từ template |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/attach-kb` | Staff/Manager/Admin | Gắn KB article vào chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/to-kb-draft` | Staff/Manager/Admin | Chuyển chat thành KB Draft |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/kb-suggestions` | Staff/Manager/Admin | Gợi ý KB articles |
| `POST` | `/api/tickets/{ticketId}/chats/suggest` | Staff/Manager/Admin | AI gợi ý nội dung chat |
| `POST` | `/api/tickets/{ticketId}/chats/sentiment-check` | Staff/Manager/Admin | AI phân tích tone Customer |
| `POST` | `/api/tickets/{ticketId}/chats/summarize` | Staff/Manager/Admin | AI tóm tắt thread |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/translate` | Mọi role | Dịch nội dung chat |
| `POST` | `/api/tickets/{ticketId}/chats/voice` | Mọi role | Upload audio → transcribe → tạo chat |
| `GET` | `/api/tickets/{ticketId}/chats/export-pdf` | Staff/Manager/Admin | Export PDF toàn bộ chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/escalation-review/ack` | Manager/Admin | ACK escalation review |

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
| `body` | `string` | **Bắt buộc** | Nội dung chat (1–10 000 ký tự) |
| `isInternal` | `bool` | Không (mặc định `false`) | `true` = ẩn với Customer |
| `bodyFormat` | `ChatBodyFormatEnum` | Không (mặc định `PlainText`) | Định dạng body |
| `attachments` | `ChatAttachmentInput[]?` | Không | Danh sách file đính kèm |

**`ChatAttachmentInput`:** `fileId` (Guid, bắt buộc) · `fileName` (string, bắt buộc) · `contentType` (string, bắt buộc) · `sizeBytes` (int64, tùy chọn)

**Response `201`:** `TicketActionResponse`

**Realtime:** Phát event `ChatAdded` qua SignalR hub `/hubs/ticket-chats` — routing theo `isInternal` (internal group / public group).

**Lỗi:**
- `400` — `body` rỗng, hoặc attachment thiếu `fileId`/`fileName`/`contentType`
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy ticket

---

### `PUT /api/tickets/{ticketId}/chats/{id}`

**Mục đích:** Sửa nội dung chat.

**Quyền:**
- Author: Sửa trong 15 phút kể từ lúc tạo (window configurable `Chat:EditWindowMinutes`)
- Manager/Admin: Sửa bất cứ lúc nào nhưng phải có `editReason`
- Blocked khi ticket `Closed`

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `body` | `string` | **Bắt buộc** | Nội dung mới |
| `editReason` | `string?` | Bắt buộc khi Manager/Admin sửa của người khác | Lý do sửa |

**Response `200`:** `TicketActionResponse`

**Lỗi:** `400` (đã quá window/ticket closed) · `403` (không có quyền) · `404`

---

### `DELETE /api/tickets/{ticketId}/chats/{id}`

**Mục đích:** Soft-delete chat.

**Quyền:**
- Author: Xóa của mình bất kỳ lúc nào
- Manager/Admin: Xóa của ai cũng được, phải có `deleteReason`
- Blocked khi ticket `Closed`

**Request body (tùy chọn):** `{ "deleteReason": "string?" }`

**Response `200`:** `TicketActionResponse`

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

### `POST /api/tickets/{ticketId}/chats/sentiment-check` (AI)

**Mục đích:** Phân tích tone cảm xúc Customer trong ticket. Nếu score < -0.7 → gửi SignalR alert tới Manager.
**Auth:** Staff/Manager/Admin

**Request body:** Không cần (ticketId lấy từ path)

**Response `200`:** `CommonResponse<ChatSentimentCheckDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "score": -0.85,
    "label": "Critical",
    "isAlertSent": true
  }
}
```

**Label values:** `Positive` (score > 0.3) · `Neutral` (-0.3 ≤ score ≤ 0.3) · `Negative` (-0.7 < score < -0.3) · `Critical` (score ≤ -0.7)

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

### `GET /api/tickets/{ticketId}/chats/export-pdf`

**Mục đích:** Export PDF toàn bộ chat thread của ticket. Customer chat nội bộ bị ẩn.
**Auth:** Staff/Manager/Admin

**Response:** `application/pdf` — file `ticket-{ticketId}-chats.pdf`

**Lỗi `404`:** Ticket không tồn tại hoặc không có chat.

---

### `GET /api/tickets/{ticketId}/chats/unread-count`

**Mục đích:** Số chat chưa đọc của user hiện tại trên ticket này.

**Response `200`:** `CommonResponse<{ unreadCount: int }>`

---

### `POST /api/tickets/{ticketId}/chats/mark-read`

**Mục đích:** Mark-read nhiều chat (bulk) — cũng được gọi tự động khi `GET /chats`.

**Request body:** `{ "chatIds": ["guid", "guid"] }`

**Response `200`:** `CommonResponse<{ markedCount: int }>`

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

**Response `200`:** `CommonResponse<{ erasedCount: int }>`

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

**Query params:** `unreadOnly` (bool), `page`, `pageSize`

**Response `200`:** `CommonResponse<PaginationResponse<TicketChatMentionDTO>>`

---

### `PATCH /api/chats/mentions/{id}/acknowledge`

**Mục đích:** Xác nhận đã xem 1 mention.
**Auth:** Mọi role

**Response `200`:** `CommonResponse<object>`

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
| `batteryAssetId` | `Guid?` | Không | — | ID thiết bị đang gặp lỗi |

> **Lưu ý:** `TicketCreateCommand.ValidateAsync()` hiện chỉ check **không rỗng/whitespace** cho `title`/`description` — **KHÔNG** enforce giới hạn độ dài (max 200/2000). FE nên tự giới hạn input để tránh dữ liệu quá dài.

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

**Response thành công `200`:** `TicketActionResponse` — ticket chuyển sang `Open` (chờ Manager triage lại). Lần reopen thứ 2 trở đi (`ReopenCount >= 2`) → tự động `Escalated`.

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
      "ClosedRejected": 1, "Incident": 0, "Approved": 0
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
| `countByStatus` | `Dictionary<string,int>` | Số ticket theo từng status — **luôn đủ 14 key** (`TicketStatusEnum`, key PascalCase), status không có ticket = `0` |
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

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `logType` | `MaintenanceLogTypeEnum?` | Không | Loại nhật ký cho log tự động |

> **Lưu ý:** `TicketStartCommand` chỉ nhận `logType`. **Không** có field tọa độ check-in (`latitude`/`longitude`) ở endpoint này — nếu cần ghi tọa độ, dùng `checkInLatitude`/`checkInLongitude`/`checkInAt` khi tạo maintenance log qua `POST /api/tickets/{ticketId}/maintenance-logs`.

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

**Mục đích:** Manager xem queue ticket đang chờ phê duyệt — các ticket ở trạng thái `Open`, sắp xếp theo Priority (P1 trước).

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

**Mục đích:** Manager phê duyệt tính hợp lệ của ticket và xác định mức độ ưu tiên. Chuyển trạng thái `Open → Approved`. Priority được tính tự động từ `impact × urgency`.

**Auth:** Bắt buộc (Manager)

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

**Mục đích:** Manager/Admin từ chối ticket ngay từ bước phân loại (Triage) khi ticket không hợp lệ (spam, trùng lặp, ngoài scope dịch vụ). Chuyển trạng thái `Open → ClosedRejected`. Lưu activity `Rejected` kèm `reason` vào timeline.

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
- `403` — Không có role Manager/Admin, hoặc ticket không ở trạng thái `Open`
- `404` — Không tìm thấy ticket

---

### `POST /api/admin/tickets/{id}/assign`

**Mục đích:** Manager gán nhân viên xử lý cho ticket đã được phê duyệt. Chuyển trạng thái `Approved → Assigned`.

**Auth:** Bắt buộc (Manager)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `staffId` | `Guid` | Bắt buộc | ID Staff được gán |
| `notes` | `string?` | Không | Ghi chú khi gán |

**Response thành công `200`:** `TicketActionResponse`

**Lỗi thường gặp:**
- `403` — Ticket không ở trạng thái `Approved`

---

### `POST /api/admin/tickets/{id}/reassign`

**Mục đích:** Manager điều chuyển ticket sang cho nhân viên khác. Lưu lịch sử thay đổi Staff.

**Auth:** Bắt buộc (Manager)

**Path param:** `id` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `newStaffId` | `Guid` | Bắt buộc | ID Staff mới |
| `reason` | `string` | **Bắt buộc** | Lý do điều chuyển — không được rỗng/whitespace (`400` nếu thiếu) |

**Response thành công `200`:** `TicketActionResponse`

---

### `POST /api/admin/tickets/{id}/approve`

**Mục đích:** Manager phê duyệt kết quả giải quyết của Staff. Chuyển trạng thái `Resolved → ClosedPendingRate`, kích hoạt yêu cầu đánh giá cho Customer.

**Auth:** Bắt buộc (Manager)

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

**Auth:** Bắt buộc (Manager)

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

**Auth:** Bắt buộc (Manager)

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
      "ClosedRejected": 6, "Incident": 0, "Approved": 1
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
| `countByStatus` | `Dictionary<string,int>` | Số ticket theo từng status — **luôn đủ 14 key** (`TicketStatusEnum`, key PascalCase), status không có = `0`. FE tự nhóm pipeline — **không gộp `ClosedRejected` vào "Hoàn tất"** (bị từ chối ≠ hoàn tất) |
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

**Auth:** Bắt buộc (Staff hoặc Manager)

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

## Nhóm 8 — Knowledge Base (tra cứu — mọi role đã đăng nhập)

Base path: `/api/knowledge-base`
**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}` (controller có `[Authorize]`, mọi role đã đăng nhập). **KHÔNG anonymous.**

> **Enum serialize:** Toàn bộ response của TicketService dùng `JsonStringEnumConverter` → mọi enum (`status`, `category`, kể cả `KbArticleVersionDTO.status`) trả về dạng **chuỗi** (vd `"Published"`, `"Charging"`). Khi **filter/gửi request** (query string hoặc body), enum cũng nhận **chuỗi tên enum** — gửi đúng tên (vd `Status=Published`, `Category=Charging`), KHÔNG gửi số.

---

### `GET /api/knowledge-base`

**Mục đích:** Tìm kiếm và liệt kê bài viết Knowledge Base. **Lọc theo role:**
- **Customer:** chỉ trả bài `Published` và `IsInternalOnly = false`. Param `Status` bị bỏ qua.
- **Staff / Manager / Admin:** thấy mọi trạng thái; lọc tự do theo `Status` (kể cả `PendingReview`, `Draft`, `Archived`). → đây là cách Manager/Admin liệt kê hàng chờ duyệt.

**Auth:** Bắt buộc (mọi role đã đăng nhập).

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

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). Chi tiết enum `KbArticleStatusEnum` + nullable: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:** `CommonResponse<PaginationResponse<KbArticleListItemDTO>>`

---

### `GET /api/knowledge-base/{id}`

**Mục đích:** Lấy thông tin chi tiết một bài viết Knowledge Base để đọc. Không tự động tăng lượt xem.

**Auth:** Bắt buộc (mọi role đã đăng nhập).

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy bài viết hoặc đã bị xóa.

---

### `GET /api/knowledge-base/suggest`

**Mục đích:** Gợi ý các bài viết liên quan **theo Ticket** (cùng `Category`, ưu tiên `HelpfulCount`/`ViewCount` cao). Trả tối đa 5 bài đã `Published` và **không phải bài nội bộ** (`isInternalOnly = true` bị lọc khỏi kết quả — áp dụng cho cả endpoint kb-suggestions của chat) — vì vậy `isInternalOnly` trong response thường luôn `false`.

**Auth:** Bắt buộc (mọi role đã đăng nhập).

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

**Auth:** Bắt buộc (mọi role đã đăng nhập — controller có `[Authorize]`).

> ⚠️ Theo `MarkHelpfulCommandHandler`, BE chỉ `article.HelpfulCount++` rồi `SaveChanges` — **KHÔNG dedup theo UserId, không chống spam**. Mỗi request là +1. Client nên tự chặn double-tap (disable nút sau khi gọi).

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy bài viết

---

### `GET /api/knowledge-base/{id}/usage-stats`

**Mục đích:** Thống kê số lần bài viết được dùng làm tài liệu tham khảo trong các Ticket, chia theo `KbReferenceTypeEnum`.

**Auth:** Bắt buộc — **chỉ role `Manager` hoặc `Admin`** (`[Authorize(Roles = "Manager,Admin")]`). Staff/Customer không gọi được, dù controller cha cho phép mọi role đã đăng nhập.

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
| `fromVersion` | `Guid` | ✅ | ID phiên bản gốc (`KbArticleVersion.id`) |
| `toVersion` | `Guid?` | Không | ID phiên bản đích. Bỏ trống → so sánh với **bản hiện tại** |

> ⚠️ Tham số `fromVersion`/`toVersion` trong API là kiểu **`Guid`** (ID của version, không phải số version hay số nguyên `int`).

**Response thành công `200`:** `CommonResponse<KbArticleDiffDTO>` — 6 `DiffSection` (`titleDiff`, `symptomsDiff`, `diagnosisStepsDiff`, `solutionStepsDiff`, `recommendedPartsDiff`, `tagsDiff`), mỗi cái có `oldValue`/`newValue`/`isChanged`.

---

### `GET /api/internal/knowledge-base/{id}/copy-template`

**Mục đích:** Sao chép cấu trúc bài viết mẫu để tạo bài mới. Chỉ áp dụng cho bài viết có gắn tag **`template`** hoặc **`example`** (so khớp không phân biệt hoa thường).

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết mẫu.

**Response thành công `200`:** `CommonResponse<KbArticleTemplateDTO>` — gồm `category` (`TicketCategoryEnum`, chuỗi), `symptoms`, `diagnosisSteps`, `solutionSteps`, `recommendedParts`, `tags` (**không** có `id`/`title`).

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
| `titleDiff` / `symptomsDiff` / `diagnosisStepsDiff` / `solutionStepsDiff` / `recommendedPartsDiff` / `tagsDiff` | `DiffSection` | Diff từng trường |

**`DiffSection`:** `{ oldValue: string; newValue: string; isChanged: bool }`

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
| `TicketStatusChangedEvent` | `POST /api/admin/tickets/{id}/triage` (Open→Approved) · `POST /api/staff/tickets/{id}/start` (Assigned→InProgress) · `POST /api/staff/tickets/{id}/hold` · `POST /api/staff/tickets/{id}/resume` (→InProgress) | `TicketId`, `Code`, `CustomerId`, `StaffId?`, `OldStatus` (int), `NewStatus` (int), `OldStatusName`, `NewStatusName` | **Customer** — InApp + Push |
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
32 endpoints thay thế và mở rộng hệ thống comment cũ: CRUD + reply thread + pin + reaction + mark-read + readers + unread-count + template + KB integration + AI (suggest/sentiment-check/summarize/translate/voice) + export PDF + escalation-review ACK.

**Endpoints mới (AdminTicketChatsController — `/api/admin/tickets/{ticketId}/chats`):**
4 endpoints Admin-only override cho ticket đã Closed: `closed-override` POST/PUT/DELETE, `restore` PATCH.

**Endpoints mới (Chats Utilities — `/api/chats/...`):**
`GET /api/chats/me`, `POST /api/chats/erase-my-data` (GDPR), `GET /api/chats/search` (Manager/Admin), `GET /api/chats/mentions/me`, `PATCH /api/chats/mentions/{id}/acknowledge`.

**ApiGateway:** Không cần thay đổi — các catch-all routes (`/api/tickets/{**catch-all}`, `/api/admin/tickets/{**catch-all}`, `/api/chats/{**catch-all}`, `/hubs/ticket-chats`) đã cover toàn bộ.

**Enums mới:** `ChatBodyFormatEnum`, `ReactionTypeEnum`, `ChatAiIntentEnum`.

**DTOs mới:** `TicketChatDTO`, `TicketChatReactionsAggregateDTO`, `ChatReactionGroupDTO`, `TicketChatMentionDTO`, `ChatEditHistoryDTO`, `ChatSuggestDTO`, `ChatSentimentCheckDTO`, `ChatSummarizeDTO`, `ChatTranslateDTO`.

---

### 2026-06-22 — Fix Knowledge Base enum bị khai sai kiểu `int`

- **Breaking change cho FE:** `GetKbArticleListQuery.Category`/`.Status` (query param `GET /api/knowledge-base`), `KbArticleVersionDTO.status`, `KbArticleTemplateDTO.category` trước đây khai sai kiểu `int`/`int?` — đã sửa sang đúng enum (`TicketCategoryEnum`, `KbArticleStatusEnum`, `KbVersionStatusEnum`). Toàn bộ 4 field này giờ gửi/nhận **chuỗi tên enum** (vd `Category=Charging`, `"status": "Approved"`), không còn ngoại lệ số như trước.
- Bổ sung endpoint `GET /api/knowledge-base/{id}/usage-stats` (Nhóm 8) — đã có trong code (Manager/Admin only) nhưng thiếu trong doc.
- Gom 3 enum của domain Knowledge Base (`KbArticleStatusEnum`, `KbVersionStatusEnum`, `KbReferenceTypeEnum`) vào mục **Enums** chung đầu tài liệu — bỏ bản liệt kê dạng bullet trùng lặp ở cuối file.
- Đổi tên các DTO trong doc từ hậu tố `Dto` sang `DTO` (khớp tên class C# thật, ví dụ `KbArticleDTO`, `TicketActionDTO`, `AlertTicketSagaDTO`) — áp dụng cho toàn bộ file, không chỉ phần Knowledge Base.

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
