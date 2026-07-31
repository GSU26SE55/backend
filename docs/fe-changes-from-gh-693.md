# Thay đổi cho FE — so với `feat/GH-693-ai-ticket-verify-merge`

## Cập nhật: Skill Tier và lịch sử Primary Handler

Nội dung trong mục này ưu tiên hơn các mô tả assignment cũ bên dưới.

### `skillTier` trong Staff response

AuthService hiện trả thêm `skillTier` dạng số nguyên trong các response có staff profile, gồm:

- Response Account/StaffProfile dùng cho dialog Admin Edit Staff.
- `GET /api/staff`.
- `GET /api/staff/{id}/assignment-profile`.

| Giá trị | Tier | Primary ticket phù hợp |
|---:|---|---|
| `1` | `Generalist` | P3 (`P3Normal`) |
| `2` | `ModuleSpecialist` | P3, P2 (`P2High`) |
| `3` | `SeniorSpecialist` | P3, P2, P1 (`P1Critical`) |

### Dropdown Staff chính

Không tải toàn bộ Staff rồi tự map priority sang tier ở FE. Gọi AuthService theo priority hiện tại của ticket:

```http
GET /api/staff?ticketPriority=P2High
```

`ticketPriority` nhận `P1Critical`, `P2High` hoặc `P3Normal` (không phân biệt hoa/thường). Khi có tham số này, API chỉ trả Staff có account `Active`, `isAvailable = true`, và `skillTier` đạt mức tối thiểu. Giá trị priority không hợp lệ trả HTTP `400`.

### Supporter và người phụ trách trước đó

- `Supporter`: Staff hỗ trợ thực sự của ticket.
- `PreviousPrimaryHandler`: Primary Handler cũ; backend tự gán role này khi reassign hoặc khi escalation tự hạ Primary không còn đủ tier.
- FE hiển thị assignment có role `PreviousPrimaryHandler` với nhãn **Người phụ trách trước đó**, không gộp vào Supporter.
- Một ticket có thể có nhiều `PreviousPrimaryHandler`; giữ toàn bộ để thể hiện lịch sử phân công.

```ts
const primary = ticket.assignments.find(({ role }) => role === "PrimaryHandler");
const supporters = ticket.assignments.filter(({ role }) => role === "Supporter");
const previousHandlers = ticket.assignments.filter(
  ({ role }) => role === "PreviousPrimaryHandler",
);
```

> Phạm vi so sánh: `origin/feat/GH-693-ai-ticket-verify-merge...feat/add-incident-detected-range-and-assign-staffs`.
>
> Tài liệu chỉ ghi các thay đổi FE cần thực hiện khi nâng cấp từ baseline trên.

## Tóm tắt thay đổi

| Hạng mục | FE cần làm |
|---|---|
| Gán ticket nhiều Staff | Thay `assignedStaffId` bằng mảng `assignments`; form gán nhận một Primary Handler và nhiều Supporter. |
| Reassign | Đổi field request từ `newStaffId` sang `newPrimaryHandlerStaffId`; Primary cũ được giữ lại làm Supporter. |
| Tạo ticket | Bổ sung thời gian phát hiện incident bắt buộc `incidentDetectedFrom` và thời điểm kết thúc tùy chọn `incidentDetectedTo`. |
| Queue Manager | Có endpoint count mới để hiển thị badge ticket chờ duyệt. |
| Chat template | Xóa UI, service và mọi call liên quan vì toàn bộ Chat Template API đã bị loại bỏ. |
| SignalR chat | Giữ nguyên contract Hub; không cần FE thay đổi payload, nhưng chỉ cần gọi `JoinTicket` một lần sau khi vào màn ticket. |

## 1. Ticket assignment: một Primary Handler, nhiều Supporter

### Thay đổi response ticket

`TicketDTO` và `TicketDetailDTO` **không còn** `assignedStaffId`. Thay bằng:

```ts
type AssignmentRole = 'PrimaryHandler' | 'Supporter';

type TicketAssignment = {
  staffId: string;
  role: AssignmentRole;
};

type Ticket = {
  // ...các field cũ
  assignments: TicketAssignment[];
};
```

FE cần thay mọi vị trí đọc `ticket.assignedStaffId` bằng:

```ts
const primaryHandler = ticket.assignments.find(
  (item) => item.role === 'PrimaryHandler',
);
const supporters = ticket.assignments.filter(
  (item) => item.role === 'Supporter',
);
```

`staffId` là UUID dạng `string`; `role` là enum dạng chuỗi. Bỏ qua assignment đã bị xóa mềm — API đã tự lọc chúng.

### Gán ticket lần đầu

Endpoint giữ nguyên: `POST /api/admin/tickets/{ticketId}/assign`.

Body cũ:

```json
{ "staffId": "staff-uuid", "notes": "..." }
```

Body mới:

```json
{
  "primaryHandlerStaffId": "staff-uuid",
  "supporterStaffIds": ["staff-uuid-1", "staff-uuid-2"],
  "notes": "Ghi chú phân công"
}
```

Quy tắc UI/validation:

- `primaryHandlerStaffId` là bắt buộc.
- `supporterStaffIds` là tùy chọn, có thể là `[]`.
- Không được đưa Primary Handler vào danh sách Supporter.
- Không được có UUID trùng trong `supporterStaffIds`.
- Primary Handler phải đang active, available và đạt skill tier theo priority ticket. Nếu không, API trả `403`.
- Supporter không bị kiểm tra tier trong luồng assign hiện tại.
- Assign thành công sẽ tạo Primary Handler thành `PrimaryAssignee` chat participant; các Supporter thành `Collaborator`, nên họ có thể truy cập chat nội bộ của ticket.

Sau khi thành công, refetch ticket detail/list, participant/chat permission state và các số liệu dashboard liên quan.

### Reassign Primary Handler

> Cập nhật: Primary cũ được chuyển thành `PreviousPrimaryHandler`, không còn là `Supporter`. FE lấy role này từ `assignments` để hiển thị người phụ trách trước đó.

Endpoint giữ nguyên: `POST /api/admin/tickets/{ticketId}/reassign`.

Body cũ:

```json
{ "newStaffId": "staff-uuid", "reason": "..." }
```

Body mới:

```json
{
  "newPrimaryHandlerStaffId": "staff-uuid",
  "reason": "Lý do điều chuyển bắt buộc"
}
```

Hành vi mới:

1. Primary Handler hiện tại tự động đổi vai trò thành `Supporter`.
2. Staff mới trở thành `PrimaryHandler`.
3. Participant chat cũ đổi thành `PreviousAssignee`; participant của Primary mới trở thành `PrimaryAssignee`.
4. SLA timer không reset.

FE không cần gửi lại danh sách supporter lúc reassign. Sau success, phải lấy lại ticket để nhận danh sách `assignments` mới.

### Danh sách ticket của Staff và dashboard

`GET` danh sách ticket cá nhân và KPI Staff chỉ tính ticket mà Staff là **PrimaryHandler**. Supporter được thêm vào chat participant để cộng tác, nhưng không được tính là ticket được giao chính thức trong My Tickets/dashboard.

Màn hình workload/dashboard Manager cũng tính tải theo Primary Handler, không cộng Supporter vào số ticket active.

## 2. Thời gian phát hiện incident khi Customer tạo ticket

Endpoint: `POST /api/customer/tickets`.

Thêm hai field vào request tạo ticket:

```ts
type CreateTicketRequest = {
  // ...field hiện có
  incidentDetectedFrom: string; // ISO-8601 UTC, bắt buộc
  incidentDetectedTo?: string;  // ISO-8601 UTC, tùy chọn
};
```

Validation FE cần áp dụng trước khi submit:

- `incidentDetectedFrom` bắt buộc.
- Cả hai thời điểm không được ở tương lai.
- Nếu có `incidentDetectedTo`, nó phải lớn hơn hoặc bằng `incidentDetectedFrom`.

Nếu không gửi `incidentDetectedTo`, backend lưu thời điểm tạo request (UTC) làm thời điểm kết thúc cho ticket tạo thủ công.

Chi tiết ticket hiện trả thêm:

```ts
type TicketDetail = Ticket & {
  incidentDetectedFrom?: string;
  incidentDetectedTo?: string;
};
```

Hiển thị hai trường này ở ticket detail. Với ticket sinh tự động từ Alert, `incidentDetectedTo` có thể là `null`.

## 3. Badge số ticket chờ duyệt cho Manager/Admin

Endpoint mới:

```http
GET /api/admin/tickets/queue/count
```

Quyền: `Manager` hoặc `Admin`.

Response: `CommonResponse<number>`, trong đó `data` là số ticket `Open`, chưa bị xóa và chưa bị merge vào ticket khác.

FE có thể dùng endpoint này cho badge queue và refetch sau khi tạo ticket, triage, reject, hoặc khi nhận notification realtime. Không dùng count này thay cho API list queue vì nó không trả ticket items.

## 4. Loại bỏ Chat Template

Tính năng Chat Template đã bị xóa khỏi branch này. FE cần xóa/ẩn toàn bộ:

- Trang/modal quản lý template chat.
- Nút chọn hoặc gửi chat từ template.
- Types, hooks, cache keys và service gọi `/api/chat-templates`.
- Lời gọi `POST /api/tickets/{ticketId}/chats/from-template/{templateId}`.

Các route trên không còn tồn tại; không retry khi nhận `404`.

## 5. SignalR Ticket Chat

Contract Hub và tên event không đổi. Thay đổi là backend cache quyền truy cập ticket theo connection:

- Gọi `JoinTicket(ticketId)` một lần sau khi user mở ticket/chat.
- Có thể tiếp tục gọi `Typing(ticketId)` như cũ; Hub sẽ tái sử dụng kết quả quyền đã cache.
- Khi user không có quyền, Hub vẫn trả `HubException` như trước; FE xử lý bằng cách không hiển thị/khóa chat và quay về màn hợp lệ.

Không cần thay đổi payload SignalR hoặc thêm request REST mới.


## Tham chiếu thay đổi

- #697: Ticket assignment 1-n (Primary Handler + Supporter)
- #698: Incident detected range khi tạo ticket
- #696: Cache quyền truy cập Ticket Chat Hub và loại bỏ Chat Template
