# RabbitMQ Topology — Comment Pipeline

Tài liệu này định nghĩa cấu trúc RabbitMQ topology phục vụ module Comment trong `TicketService` và các consumer liên quan (ví dụ: `NotificationService`).

---

## 1. Exchange Configurations

### Main Topic Exchange
- **Name:** `comment.events`
- **Type:** `topic`
- **Durable:** `true`
- **Auto-delete:** `false`

### Dead Letter Exchange (DLX)
- **Name:** `comment.events.dlx`
- **Type:** `topic`
- **Durable:** `true`
- **Auto-delete:** `false`

---

## 2. Queue Configurations

### Main Queue: `notification.comment.events`
- **Durable:** `true`
- **Auto-delete:** `false`
- **Arguments:**
  - `x-max-length`: `500000` (Giới hạn tối đa 500k messages để bảo vệ tài nguyên hệ thống)
  - `x-message-ttl`: `259200000` (Thời gian lưu trữ tối đa 3 ngày = 259,200,000 ms)
  - `x-overflow`: `drop-head`
  - `x-dead-letter-exchange`: `comment.events.dlx`
  - `x-dead-letter-routing-key`: `notification.comment.events.error`

### Dead Letter Queue (DLQ): `notification.comment.events.dlq`
- **Durable:** `true`
- **Auto-delete:** `false`
- **Purpose:** Lưu trữ các tin nhắn bị lỗi sau khi vượt quá số lần retry tối đa.
- **Retention Policy:** Lưu giữ thủ công tối đa 7 ngày để phục vụ điều tra lỗi.

---

## 3. Routing Keys & Bindings

### Routing Key Pattern
Tất cả integration events liên quan đến comment đều phải được định tuyến theo định dạng:
`comment.{ticketId}.{eventType}`

* **`ticketId`**: UUID 36 ký tự dạng chuỗi đại diện cho Ticket.
* **`eventType`**: Tên sự kiện viết thường không dấu (Ví dụ: `created`, `edited`, `deleted`, `mentioned`, `reacted`, `participantadded`, `participantremoved`, `participantrolechanged`, `escalationrequested`).

### Mapping Table

| Event Class | Routing Key | Ví dụ thực tế |
|-------------|-------------|---------------|
| `CommentCreatedEvent` | `comment.*.created` | `comment.550e8400-e29b-41d4-a716-446655440000.created` |
| `CommentEditedEvent` | `comment.*.edited` | `comment.550e8400-e29b-41d4-a716-446655440000.edited` |
| `CommentDeletedEvent` | `comment.*.deleted` | `comment.550e8400-e29b-41d4-a716-446655440000.deleted` |
| `CommentMentionedEvent` | `comment.*.mentioned` | `comment.550e8400-e29b-41d4-a716-446655440000.mentioned` |
| `CommentReactedEvent` | `comment.*.reacted` | `comment.550e8400-e29b-41d4-a716-446655440000.reacted` |
| `ParticipantAddedEvent` | `comment.*.participantadded` | `comment.550e8400-e29b-41d4-a716-446655440000.participantadded` |
| `ParticipantRemovedEvent` | `comment.*.participantremoved` | `comment.550e8400-e29b-41d4-a716-446655440000.participantremoved` |
| `ParticipantRoleChangedEvent` | `comment.*.participantrolechanged` | `comment.550e8400-e29b-41d4-a716-446655440000.participantrolechanged` |
| `CommentEscalationReviewRequestedEvent` | `comment.*.escalationrequested` | `comment.550e8400-e29b-41d4-a716-446655440000.escalationrequested` |

### Queue Bindings
* Queue `notification.comment.events` liên kết với exchange `comment.events` thông qua routing key pattern: **`comment.#`** (nhận toàn bộ các sub-events).
* Queue `notification.comment.events.dlq` liên kết với exchange `comment.events.dlx` thông qua routing key pattern: **`notification.comment.events.error`**.

---

## 4. Retry Policy & DLQ Strategy

* **Retry Policy:**
  - Lần thử lại 1: 1 giây
  - Lần thử lại 2: 5 giây
  - Lần thử lại 3: 30 giây
  - Sau 3 lần thất bại liên tiếp -> Chuyển trực tiếp sang DLQ.
* **Reprocessing:**
  - Quản trị viên sử dụng CLI hoặc giao diện RabbitMQ Management để di chuyển tin nhắn lỗi từ `notification.comment.events.dlq` quay ngược lại exchange `comment.events` để xử lý lại sau khi lỗi hạ tầng hoặc code đã được khắc phục.
