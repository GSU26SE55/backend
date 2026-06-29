# RabbitMQ Topology — Chat Pipeline

Tài liệu này định nghĩa cấu trúc RabbitMQ topology phục vụ module Chat trong `TicketService` và các consumer liên quan (ví dụ: `NotificationService`).

---

## 1. Exchange Configurations

### Main Topic Exchange
- **Name:** `chat.events`
- **Type:** `topic`
- **Durable:** `true`
- **Auto-delete:** `false`

### Dead Letter Exchange (DLX)
- **Name:** `chat.events.dlx`
- **Type:** `topic`
- **Durable:** `true`
- **Auto-delete:** `false`

---

## 2. Queue Configurations

### Main Queue: `notification.chat.events`
- **Durable:** `true`
- **Auto-delete:** `false`
- **Arguments:**
  - `x-max-length`: `500000` (Giới hạn tối đa 500k messages để bảo vệ tài nguyên hệ thống)
  - `x-message-ttl`: `259200000` (Thời gian lưu trữ tối đa 3 ngày = 259,200,000 ms)
  - `x-overflow`: `drop-head`
  - `x-dead-letter-exchange`: `chat.events.dlx`
  - `x-dead-letter-routing-key`: `notification.chat.events.error`

### Dead Letter Queue (DLQ): `notification.chat.events.dlq`
- **Durable:** `true`
- **Auto-delete:** `false`
- **Purpose:** Lưu trữ các tin nhắn bị lỗi sau khi vượt quá số lần retry tối đa.
- **Retention Policy:** Lưu giữ thủ công tối đa 7 ngày để phục vụ điều tra lỗi.

---

## 3. Routing Keys & Bindings

### Routing Key Pattern
Tất cả integration events liên quan đến chat đều phải được định tuyến theo định dạng:
`chat.{ticketId}.{eventType}`

* **`ticketId`**: UUID 36 ký tự dạng chuỗi đại diện cho Ticket.
* **`eventType`**: Tên sự kiện viết thường không dấu (Ví dụ: `created`, `edited`, `deleted`, `mentioned`, `reacted`, `participantadded`, `participantremoved`, `participantrolechanged`, `escalationrequested`).

### Mapping Table

| Event Class | Routing Key | Ví dụ thực tế |
|-------------|-------------|---------------|
| `ChatCreatedEvent` | `chat.*.created` | `chat.550e8400-e29b-41d4-a716-446655440000.created` |
| `ChatEditedEvent` | `chat.*.edited` | `chat.550e8400-e29b-41d4-a716-446655440000.edited` |
| `ChatDeletedEvent` | `chat.*.deleted` | `chat.550e8400-e29b-41d4-a716-446655440000.deleted` |
| `ChatMentionedEvent` | `chat.*.mentioned` | `chat.550e8400-e29b-41d4-a716-446655440000.mentioned` |
| `ChatReactedEvent` | `chat.*.reacted` | `chat.550e8400-e29b-41d4-a716-446655440000.reacted` |
| `ParticipantAddedEvent` | `chat.*.participantadded` | `chat.550e8400-e29b-41d4-a716-446655440000.participantadded` |
| `ParticipantRemovedEvent` | `chat.*.participantremoved` | `chat.550e8400-e29b-41d4-a716-446655440000.participantremoved` |
| `ParticipantRoleChangedEvent` | `chat.*.participantrolechanged` | `chat.550e8400-e29b-41d4-a716-446655440000.participantrolechanged` |
| `ChatEscalationReviewRequestedEvent` | `chat.*.escalationrequested` | `chat.550e8400-e29b-41d4-a716-446655440000.escalationrequested` |

### Queue Bindings
* Queue `notification.chat.events` liên kết với exchange `chat.events` thông qua routing key pattern: **`chat.#`** (nhận toàn bộ các sub-events).
* Queue `notification.chat.events.dlq` liên kết với exchange `chat.events.dlx` thông qua routing key pattern: **`notification.chat.events.error`**.

---

## 4. Retry Policy & DLQ Strategy

* **Retry Policy:**
  - Lần thử lại 1: 1 giây
  - Lần thử lại 2: 5 giây
  - Lần thử lại 3: 30 giây
  - Sau 3 lần thất bại liên tiếp -> Chuyển trực tiếp sang DLQ.
* **Reprocessing:**
  - Quản trị viên sử dụng CLI hoặc giao diện RabbitMQ Management để di chuyển tin nhắn lỗi từ `notification.chat.events.dlq` quay ngược lại exchange `chat.events` để xử lý lại sau khi lỗi hạ tầng hoặc code đã được khắc phục.
