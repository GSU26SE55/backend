# API Documentation — TicketService

> **Base URL:** `http://localhost:7300/api`
> **Content-Type:** `application/json`
> **Response Wrapper:** `CommonResponse<T>` hoặc `TicketActionResponse`

---

## 1. Cấu trúc Response chuẩn

### 1.1. `CommonResponse<T>`
Sử dụng cho các truy vấn dữ liệu (GET).

| Field | Type | Description |
| :--- | :--- | :--- |
| `isSuccess` | `boolean` | `true` nếu xử lý thành công |
| `statusCode` | `int` | Mã HTTP Status Code |
| `message` | `string` | Thông báo kết quả |
| `data` | `T` | Dữ liệu phản hồi thực tế |
| `listErrors` | `Error[]` | Danh sách lỗi nếu `isSuccess` là `false` |

| Field | Type | Mô tả |
|---|---|---|
| `isSuccess` | `bool` | `true` nếu thành công, `false` nếu có lỗi nghiệp vụ |
| `statusCode` | `int` | HTTP status code |
| `message` | `string?` | Thông báo tóm tắt kết quả |
| `data` | `T?` | Dữ liệu trả về, `null` khi thất bại |
| `listErrors` | `Errors[]` | Danh sách lỗi validation — mỗi phần tử có `field` và `detail` |

**Lỗi HTTP chung:**
- `400` — Validation hoặc input không hợp lệ, body vẫn theo `CommonResponse<T>` nếu lỗi đi qua application validation
- `401` — Token thiếu/hết hạn/không hợp lệ hoặc credential sai
- `403` — Có token nhưng không đủ quyền hoặc resource không thuộc user hiện tại
- `404` — Không tìm thấy resource
- `409` — Xung đột dữ liệu/nghiệp vụ
- `423` — Account bị lockout tạm thời
- `429` — Bị rate limit
- `500` — Lỗi server ngoài dự kiến

### 1.2. `TicketActionResponse`
Sử dụng cho các hành động thay đổi dữ liệu (POST).

| Field | Type | Description |
| :--- | :--- | :--- |
| `data.ticketId` | `Guid` | ID của Ticket vừa thao tác |
| `data.ticketCode` | `string` | Mã hiển thị của Ticket |
| `data.newStatus` | `string` | Trạng thái mới của Ticket sau hành động |

### 1.3. `PaginationResponse<T>`
Dữ liệu nằm trong field `data` của `CommonResponse` khi truy vấn danh sách.

| Field | Type | Description |
| :--- | :--- | :--- |
| `items` | `T[]` | Danh sách dữ liệu trang hiện tại |
| `totalItems` | `int` | Tổng số bản ghi thỏa mãn bộ lọc |
| `pageNumber` | `int` | Chỉ số trang hiện tại |
| `pageSize` | `int` | Số lượng bản ghi trên một trang |

---

## 2. Danh mục Enums (Hệ thống dùng String)

### 2.1. `TicketStatusEnum` (Trạng thái Ticket)

| Value | Ý nghĩa |
| :--- | :--- |
| `New` | Vừa tạo, chờ triage |
| `Open` | Đã triage sơ bộ, chờ Manager phê duyệt |
| `Approved` | Manager đã phê duyệt, chờ gán Staff |
| `Assigned` | Đã gán Staff, chờ Staff xác nhận |
| `InProgress` | Staff đang xử lý |
| `WaitingCustomer` | Tạm dừng: Chờ khách hàng phản hồi |
| `WaitingParts` | Tạm dừng: Chờ linh kiện |
| `WaitingOnsiteSchedule` | Tạm dừng: Chờ lịch hẹn tại chỗ |
| `Resolved` | Staff báo đã xong, chờ Manager kiểm tra |
| `ClosedPendingRate` | Manager đã phê duyệt kết quả, chờ Customer đánh giá |
| `Closed` | Đã đóng chính thức |
| `ClosedRejected` | Manager từ chối kết quả (quay lại InProgress) |
| `Incident` | Sự cố nghiêm trọng |

### 2.2. `TicketPriorityEnum` (Mức độ ưu tiên)

| Value | Ý nghĩa |
| :--- | :--- |
| `P1Critical` | Nghiêm trọng (SLA 4h) |
| `P2High` | Cao (SLA 24h) |
| `P3Normal` | Bình thường (SLA h) |

### 2.3. `ImpactScopeEnum` (Phạm vi ảnh hưởng)

| Value | Ý nghĩa |
| :--- | :--- |
| `SingleAsset` | Một thiết bị |
| `Site` | Một khu vực/trạm |
| `MultiSite` | Nhiều khu vực |

### 2.4. `UrgencyLevelEnum` (Độ khẩn cấp)

| Value | Ý nghĩa |
| :--- | :--- |
| `Low` | Thấp |
| `Medium` | Trung bình |
| `High` | Cao |

---

## 3. Nhóm 1: API Chung (Mọi Role)

Base Path: `/api/tickets`

### `GET /api/tickets/{id}`
**Mục đích:** Lấy thông tin chi tiết của một Ticket.

**Quyền hạn:**
- Khách hàng: Chỉ xem được Ticket của mình.
- Staff: Chỉ xem được Ticket được gán.
- Manager/Admin: Xem toàn bộ.

**Response thành công (200):**
```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "code": "TKT-2606-0001",
    "title": "...",
    "description": "...",
    "status": "InProgress",
    "priority": "P2High",
    "category": "Charging",
    "createdAt": "2026-06-05T...",
    "activities": [...],
    "comments": [...],
    "maintenanceLogs": [...]
  }
}
```

---

## 4. Nhóm 2: API Khách hàng (Customer)

Base Path: `/api/customer/tickets`

### `POST /api/customer/tickets`
**Mục đích:** Tạo yêu cầu hỗ trợ mới.

**Request Body:**
| Field | Type | Required | Validation | Description |
| :--- | :--- | :--- | :--- | :--- |
| `title` | `string` | Có | Max 200 ký tự | Tiêu đề ngắn gọn của lỗi |
| `description` | `string` | Có | Max 2000 ký tự | Mô tả chi tiết vấn đề |
| `category` | `string` | Có | TicketCategoryEnum | Loại lỗi (Charging, Overheat...) |
| `batteryAssetId` | `Guid` | Không | - | ID thiết bị đang gặp lỗi |

---

### `POST /api/customer/tickets/{id}/rate`
**Mục đích:** Đánh giá chất lượng xử lý và đóng ticket.

**Request Body:**
| Field | Type | Required | Validation | Description |
| :--- | :--- | :--- | :--- | :--- |
| `rating` | `short` | Có | 1 - 5 | Số sao đánh giá |
| `ratingComment` | `string` | Không | - | Nhận xét chi tiết |

---

## 5. Nhóm 3: API Nhân viên kỹ thuật (Staff)

Base Path: `/api/staff/tickets`

### `POST /api/staff/tickets/{id}/hold`
**Mục đích:** Tạm dừng xử lý (Pause SLA).

**Request Body:**
| Field | Type | Required | Validation | Description |
| :--- | :--- | :--- | :--- | :--- |
| `reason` | `string` | Có | PauseReasonEnum | Lý do (WaitingParts, WaitingCustomer...) |
| `note` | `string` | Không | - | Ghi chú chi tiết lý do dừng |

---

## 6. Nhóm 4: API Quản trị (Admin/Manager)

Base Path: `/api/admin/tickets`

### `POST /api/admin/tickets/{id}/triage`
**Mục đích:** Phê duyệt và phân loại ưu tiên cho Ticket.

**Request Body:**
| Field | Type | Required | Validation | Description |
| :--- | :--- | :--- | :--- | :--- |
| `impact` | `string` | Có | ImpactScopeEnum | Phạm vi ảnh hưởng |
| `urgency` | `string` | Có | UrgencyLevelEnum | Độ khẩn cấp |
| `manualPriority` | `string` | Không | TicketPriorityEnum | Gán ưu tiên thủ công (nếu cần) |
| `managerComment` | `string` | Không | - | Nhận xét của quản lý |

---

## 7. Phụ trợ: Bình luận & Nhật ký

### `POST /api/tickets/{id}/comments`
**Mục đích:** Thêm bình luận hoặc thảo luận nội bộ.

**Request Body:**
| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `body` | `string` | Có | Nội dung bình luận |
| `isInternal` | `boolean` | Không | `true` nếu chỉ Staff/Manager xem được |
| `attachments` | `object[]` | Không | Danh sách file đính kèm |

---

## 8. Bảng mã lỗi thường gặp

| StatusCode | Field | Detail |
| :--- | :--- | :--- |
| `400` | `Title` | Tiêu đề không được để trống. |
| `403` | `Ticket` | Ticket không ở trạng thái hợp lệ để thực hiện thao tác này. |
| `404` | `Ticket` | Không tìm thấy Ticket yêu cầu. |
