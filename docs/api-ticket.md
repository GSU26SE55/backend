# API Documentation — TicketService

> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>`
> **ID fields:** Tất cả `id` trong response đều là `string` (UUID dạng `"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"`). Entity C# dùng `Guid` nhưng serialize thành `string` trong JSON — TypeScript dùng `string` cho mọi id field.

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

**Lỗi HTTP chung:**
- `400` — Validation hoặc input không hợp lệ
- `401` — Token thiếu/hết hạn/không hợp lệ
- `403` — Có token nhưng không đủ quyền hoặc resource không thuộc user hiện tại / sai trạng thái ticket
- `404` — Không tìm thấy resource
- `500` — Lỗi server ngoài dự kiến

### `TicketActionResponse`

Wrapper **riêng biệt** (không phải `CommonResponse<T>`) dành cho các hành động thay đổi trạng thái ticket (POST mutations). `data` trỏ tới `TicketActionDto`.

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
  "listErrors": []
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
interface TicketActionDto {
  id: string;
  ticketId?: string; // chỉ có ở response của POST maintenance-logs
  code: string;
  status: TicketStatusEnum;
}

interface TicketActionResponse {
  isSuccess: boolean;
  statusCode: number;
  message: string | null;
  data: TicketActionDto | null;
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
| `Commented` | 6 | Có bình luận mới |
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
| `AutoClosed` | 22 | Tự động đóng (hệ thống) |
| `ResolvedByEscalatedStaff` | 23 | Được giải quyết bởi Staff cấp cao sau escalation |
| `TriageApproved` | 24 | Manager phê duyệt tính hợp lệ tại bước triage |
| `Closed` | 25 | Ticket đã đóng chính thức |

### `ActorRoleEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Admin` | 1 | Quản trị viên |
| `Manager` | 2 | Quản lý |
| `Staff` | 3 | Nhân viên kỹ thuật |
| `Customer` | 4 | Khách hàng |
| `System` | 5 | Hành động tự động của hệ thống |

---

## DTOs

### `TicketDTO` (item trong danh sách)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID ticket |
| `code` | `string` | Không | Mã hiển thị (e.g. `TKT-2606-0001`) |
| `batteryAssetId` | `string` | Không (default `""`) | ID thiết bị pin — BE trả chuỗi rỗng (không phải `null`) khi không liên quan pin cụ thể |
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
| `slaTimer` | `SlaTimerDTO` | Không | Thông tin SLA timer hiện tại |

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
| `comments` | `TicketCommentDTO[]` | Không (default `[]`) | Danh sách bình luận |
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

### `TicketCommentDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID comment |
| `ticketId` | `string` | Không | ID ticket |
| `authorUserId` | `string?` | Null | ID người viết |
| `authorRole` | `ActorRoleEnum` | Không | Role của người viết |
| `authorDisplayName` | `string?` | Null | Tên hiển thị |
| `body` | `string` | Không | Nội dung bình luận |
| `isInternal` | `bool` | Không | `true` = chỉ Staff/Manager xem được, ẩn với Customer |
| `attachmentFileIds` | `string[]?` | Null | Danh sách FileId đính kèm |
| `createdAt` | `string` | Không | Thời điểm tạo (UTC) |

### `MaintenanceLogDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID nhật ký |
| `ticketId` | `string` | Không | ID ticket |
| `staffId` | `string` | Không | ID Staff tạo nhật ký |
| `logType` | `MaintenanceLogTypeEnum` | Không | Loại nhật ký |
| `summary` | `string` | Không (default `""`) | Tóm tắt công việc |
| `diagnosisDetails` | `string?` | Null | Chi tiết chẩn đoán |
| `actionsTaken` | `string?` | Null | Các hành động đã thực hiện |
| `durationMinutes` | `int` | Không | Thời lượng thực hiện (phút) |
| `resolutionNote` | `string?` | Null | Ghi chú kết quả |
| `startedAt` | `string` | Không | Thời điểm bắt đầu (UTC) |
| `completedAt` | `string?` | Null | Thời điểm hoàn thành (UTC) |
| `attachmentFileIds` | `string[]?` | Null | File đính kèm chung |
| `beforePhotosFileIds` | `string[]?` | Null | Ảnh trước khi sửa |
| `afterPhotosFileIds` | `string[]?` | Null | Ảnh sau khi sửa |
| `relatedKbArticleIds` | `string[]?` | Null | ID bài viết KB liên quan |
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

### `GET /api/tickets/{ticketId}/comments`

**Mục đích:** Lấy danh sách bình luận của Ticket (có phân trang).

**Auth:** Bắt buộc (mọi role)

**Quyền hạn:**
- Customer: Chỉ xem được bình luận công khai (`IsInternal = false`).
- Staff/Manager/Admin: Xem được tất cả bao gồm bình luận nội bộ.

**Path param:** `ticketId` — UUID của ticket.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `page` | `int` | Số trang (mặc định 1) |
| `pageSize` | `int` | Kích thước trang (mặc định 10) |

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketCommentDTO>>`

---

### `POST /api/tickets/{ticketId}/comments`

**Mục đích:** Thêm bình luận vào ticket. Áp dụng cho cả Customer và Staff.

**Auth:** Bắt buộc (mọi role)

**Path param:** `ticketId` — UUID của ticket.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `body` | `string` | Bắt buộc | Không rỗng | Nội dung bình luận |
| `isInternal` | `bool` | Không (mặc định `false`) | — | `true` = chỉ Staff/Manager xem được |
| `attachments` | `CommentAttachmentInput[]?` | Không | — | Danh sách file đính kèm |

**`CommentAttachmentInput`:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `fileId` | `string` (UUID) | Bắt buộc | FileId từ FileStorageService — gửi dạng UUID string |
| `fileName` | `string?` | Không | Tên file gốc |
| `contentType` | `string?` | Không | MIME type |
| `sizeBytes` | `int64` | Không | Kích thước file (bytes) |

**Response thành công `201`:** `TicketActionResponse`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy ticket

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
| `title` | `string` | Bắt buộc | Không rỗng, max 200 ký tự | Tiêu đề ngắn gọn |
| `description` | `string` | Bắt buộc | Max 2000 ký tự | Mô tả chi tiết vấn đề |
| `category` | `TicketCategoryEnum` | Bắt buộc | — | Loại lỗi |
| `batteryAssetId` | `Guid?` | Không | — | ID thiết bị đang gặp lỗi |

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
| `PageNumber` | `int` | Trang (mặc định 1; giá trị ≤ 0 → tự về 1) |
| `PageSize` | `int` | Số item/trang (mặc định 10; max 100; ≤ 0 → về 10) |

> **Internal param:** BE expose thêm `ActorStaffId` (uuid) dùng nội bộ để BE đọc từ JWT. FE **không gửi** param này.

**Response thành công `200`:** `CommonResponse<PaginationResponse<TicketDTO>>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập

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
| `latitude` | `decimal?` | Không | Vĩ độ check-in |
| `longitude` | `decimal?` | Không | Kinh độ check-in |

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
| `IsDescending` | `bool` | Sắp xếp giảm dần theo `createdAt` (**mặc định `true`** — mới nhất lên đầu) |
| `PageNumber` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang |

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

**Response thành công `200`:** `AlertTicketSagaListResponse` = `CommonResponse<PaginationResponse<AlertTicketSagaDto>>`

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

**Chi tiết `AlertTicketSagaDto`:**

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

**Response thành công `200`:** `AlertTicketSagaDetailResponse` = `CommonResponse<AlertTicketSagaDto>` (shape giống item trong list).

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

---

## Nhóm 8 — Knowledge Base (Customer / Public)

Base path: /api/knowledge-base
**Auth:** Không yêu cầu (Anonymous) hoặc Customer role.

---

### GET /api/knowledge-base

**Mục đích:** Tìm kiếm và liệt kê các bài viết Wiki đã xuất bản (Published) và không đánh dấu nội bộ (IsInternalOnly = false).

**Auth:** Không yêu cầu.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| Keyword | string? | Tìm theo tiêu đề hoặc triệu chứng |
| Category | TicketCategoryEnum? | Lọc theo danh mục lỗi |
| Tags | string[]? | Tìm theo thẻ (Tag) |
| PageNumber | int | Trang (mặc định 1) |
| PageSize | int | Số item/trang |

**Response thành công 200:** CommonResponse<PaginationResponse<KbArticleListItemDto>>

---

### GET /api/knowledge-base/{id}

**Mục đích:** Lấy thông tin chi tiết một bài viết Wiki để đọc. Không tự động tăng lượt xem.

**Auth:** Không yêu cầu.

**Path param:** id — UUID của bài viết.

**Response thành công 200:** CommonResponse<KbArticleDto>

**Lỗi thường gặp:**
- 404 — Không tìm thấy bài viết, bài viết chưa được Published, hoặc là bài viết nội bộ.

---

### GET /api/knowledge-base/suggest

**Mục đích:** Gợi ý các bài viết liên quan dựa trên text đầu vào (thường dùng khi khách hàng đang nhập nội dung ticket mới).

**Auth:** Không yêu cầu.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| query | string | ✅ | Đoạn text khách hàng đang nhập |

**Response thành công 200:** CommonResponse<KbArticleSuggestDto[]>

---

### POST /api/knowledge-base/{id}/helpful

**Mục đích:** Người dùng đánh giá bài viết là hữu ích (Tăng HelpfulCount).

**Auth:** Tùy chọn (Nếu có Token sẽ ghi nhận UserId để chống spam).

**Path param:** id — UUID của bài viết.

**Response thành công 200:** CommonResponse<object>

---

## Nhóm 9 — Knowledge Base (Internal - Staff/Manager/Admin)

Base path: /api/internal/knowledge-base
**Auth:** Bắt buộc — Staff, Manager hoặc Admin

---

### POST /api/internal/knowledge-base

**Mục đích:** Tạo mới một bài viết Knowledge Base.
Bài viết sẽ được khởi tạo với Version 0 và trạng thái Chờ phê duyệt (PendingReview). Đồng thời tạo một bản KbArticleVersion (V1.0) ở trạng thái Pending. Cần Manager duyệt để xuất bản.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| category | TicketCategoryEnum | Bắt buộc | Danh mục lỗi |
| 	itle | string | Bắt buộc | Tiêu đề bài viết |
| symptoms | string | Bắt buộc | Mô tả triệu chứng / nguyên nhân |
| diagnosisSteps | string | Bắt buộc | Các bước chẩn đoán |
| solutionSteps | string | Bắt buộc | Các bước xử lý / giải pháp |
|
ecommendedParts | string[]? | Không | Danh sách linh kiện khuyến nghị thay thế |
| 	ags | string[]? | Không | Danh sách từ khóa (tối đa 10) |
| isInternalOnly | ool | Không (mặc định alse) | 	rue = Ẩn với khách hàng |

**Response thành công 201:** CommonResponse<KbArticleActionDto> (Trả về id, code, status)

---

### PUT /api/internal/knowledge-base/{id}

**Mục đích:** Cập nhật nội dung bài viết hiện có.
Hệ thống tự động lưu bản hiện tại vào lịch sử. Trạng thái bài viết sẽ chuyển về PendingReview để chờ Manager duyệt (trừ khi người cập nhật là Manager/Admin hoặc chủ sở hữu bài viết).

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** id — UUID của bài viết.

**Request body:** Tương tự như Create, thêm field changeDescription (string, required).

**Response thành công 200:** CommonResponse<KbArticleDto>

---

### GET /api/internal/knowledge-base/{id}/versions

**Mục đích:** Xem danh sách lịch sử các phiên bản của bài viết.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Response thành công 200:** CommonResponse<KbArticleVersionDto[]>

---

### GET /api/internal/knowledge-base/{id}/compare

**Mục đích:** So sánh sự khác biệt giữa hai phiên bản của bài viết.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| romVersion | int | Phiên bản gốc |
| 	oVersion | int | Phiên bản đích (Nếu = 0, so sánh với bản hiện tại) |

**Response thành công 200:** CommonResponse<KbArticleDiffDto> (Chứa OldValue, NewValue, IsChanged cho từng trường).

---

### GET /api/internal/knowledge-base/{id}/copy-template

**Mục đích:** Sao chép cấu trúc bài viết mẫu để tạo bài mới. Chỉ áp dụng cho các bài viết có gắn tag "template" hoặc "example".

**Response thành công 200:** CommonResponse<KbArticleTemplateDto>

---

## Nhóm 10 — Knowledge Base (Admin/Manager Workflow)

Base path: /api/admin/knowledge-base
**Auth:** Bắt buộc — Manager hoặc Admin

Quản lý vòng đời bài viết: Phê duyệt thay đổi, Xuất bản, Lưu trữ và Hoàn tác.

---

### POST /api/admin/knowledge-base/{id}/approve-review

**Mục đích:** Chấp nhận các thay đổi của Staff. Trạng thái bài viết chuyển từ PendingReview sang Published. Nội dung từ bản nháp sẽ được đắp lên bài viết chính.

**Response thành công 200:** CommonResponse<KbArticleActionDto>

---

### POST /api/admin/knowledge-base/{id}/reject-review

**Mục đích:** Từ chối thay đổi của Staff.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
|
eason | string | Bắt buộc | Lý do từ chối |

**Response thành công 200:** CommonResponse<KbArticleActionDto>

---

### POST /api/admin/knowledge-base/{id}/publish

**Mục đích:** Xuất bản bài viết đang ở trạng thái Nháp.

**Response thành công 200:** CommonResponse<KbArticleActionDto>

---

### POST /api/admin/knowledge-base/{id}/archive

**Mục đích:** Lưu trữ bài viết (ngừng hiển thị với Customer).

**Response thành công 200:** CommonResponse<KbArticleActionDto>

---

### POST /api/admin/knowledge-base/{id}/rollback

**Mục đích:** Hoàn tác nội dung bài viết về một phiên bản cũ trong lịch sử. Lấy nội dung từ phiên bản cũ đè lên bản hiện tại và tăng Version.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| 	oVersionId | Guid | Bắt buộc | ID của phiên bản (KbArticleVersion) cần khôi phục |

**Response thành công 200:** CommonResponse<KbArticleActionDto>

---

## Knowledge Base DTOs & Enums

### KbArticleDto

| Field | Type | Mô tả |
|---|---|---|
| id | string | ID bài viết |
| code | string | Mã bài viết (KB-YYYY-NNNN) |
| category | TicketCategoryEnum | Enum dạng chuỗi (e.g., "Charging") |
| 	itle | string | Tiêu đề |
| symptoms | string | Triệu chứng |
| diagnosisSteps | string | Các bước chẩn đoán |
| solutionSteps | string | Các bước xử lý |
|
ecommendedParts | string[]? | Danh sách linh kiện |
| 	ags | string[] | Danh sách thẻ |
| status | KbArticleStatusEnum | Enum dạng chuỗi (e.g., "Published") |
| isInternalOnly | ool | Bài nội bộ |
| ersion | int | Số phiên bản chính (Major Version) |
| iewCount | int | Lượt xem |
| helpfulCount | int | Lượt hữu ích |

### KbArticleActionDto

Payload nhẹ dùng cho các hành động chuyển trạng thái.

| Field | Type | Mô tả |
|---|---|---|
| id | string | ID bài viết |
| code | string | Mã bài viết |
| status | KbArticleStatusEnum | Trạng thái hiện tại sau thao tác |

### KbArticleStatusEnum
- Draft (1): Nháp
- PendingReview (2): Chờ phê duyệt
- Published (3): Đã xuất bản (Customer thấy được)
- Archived (4): Đã lưu trữ (Ẩn)

### KbVersionStatusEnum
Dành cho bảng lịch sử KbArticleVersion.
- Pending (1): Chờ duyệt
- Approved (2): Đã duyệt
- Rejected (3): Bị từ chối
- Archived (4): Bản sao lưu (Snapshot)

### KbReferenceTypeEnum
Dùng khi link Ticket với Article.
- ConsultedDuringResolve (1): Tham khảo khi xử lý
- ProvidedToCustomer (2): Cung cấp cho khách hàng
- GeneratedAfterResolve (3): Tạo ra sau khi xử lý xong
