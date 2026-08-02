# RabbitMQ Topology — Chat Pipeline

> 🔴 **Viết lại hoàn toàn 2026-08-02.** Bản trước mô tả một topology **thủ công không tồn tại trong
> code**: exchange `chat.events`, queue `notification.chat.events`, routing key `chat.{ticketId}.{eventType}`,
> DLX `chat.events.dlx`, retry 1s/5s/30s. **Không có thứ nào trong số đó** — grep toàn repo không ra
> chuỗi `chat.events` nào ngoài tên metric. Hệ thống dùng **MassTransit convention-based topology**.

Nguồn sự thật: `shared/src/SharedInfrastructure/Bus/MassTransitExtensions.cs`.

---

## 1. Mô hình thật — MassTransit quản lý topology

Không service nào tự khai exchange/queue/binding. Cấu hình gọn lại còn:

```csharp
cfg.Host(configuration["RabbitMQ:Host"], "/", h => { h.Username(...); h.Password(...); });
cfg.ConfigureEndpoints(context);   // ← MassTransit tự sinh exchange + queue + binding
```

Hệ quả:

* **Exchange đặt theo full type name của message** (`SharedContracts.Events.Chats:ChatCreatedEvent`),
  không phải một exchange chung `chat.events`.
* **Queue đặt theo tên consumer** (kebab-case), ví dụ `chat-created`, `chat-mention`, `chat-reaction`.
* **Binding tự tạo** giữa message-exchange → consumer-queue. **Không có routing key thủ công**, không
  có pattern `chat.#`.
* **Queue lỗi là `<queue>_error`** do MassTransit sinh — **không phải** `notification.chat.events.dlq`.

> ⚠️ **Đây là lý do các event nội bộ của `TicketService.Application.IntegrationEvents` không được
> NotificationService nhận:** MassTransit route theo **full type name**, hai assembly khác nhau ⇒ khác
> type ⇒ không bind được. Vì vậy event dùng chung phải đặt trong **`SharedContracts`**.

---

## 2. Event chat thật (trong `SharedContracts/Events/Chats`)

| Event | Consumer (NotificationService) |
|---|---|
| `ChatCreatedEvent` | `ChatCreatedConsumer` |
| `ChatMentionedEvent` | `ChatMentionConsumer` |
| `ChatReactedEvent` | `ChatReactionConsumer` |
| `ChatEscalationReviewRequestedEvent` | saga `ChatEscalationReview` (TicketService) |
| `ChatEscalationReviewAckedEvent` | saga `ChatEscalationReview` (TicketService) |

Thay đổi participant dùng event riêng, do `ParticipantChangeConsumer` xử lý.

> ⚠️ Bản cũ liệt kê `ChatEditedEvent` / `ChatDeletedEvent` như integration event — **không tồn tại**.
> Sửa/xoá chat chỉ phát **SignalR** (`ChatEdited`, `ChatDeleted`) tới client, không lên message bus.

---

## 3. Retry — giá trị thật

Cấu hình ở tầng bus, áp cho **mọi consumer của mọi service** (không riêng chat):

| Tham số | Config key | Mặc định |
|---|---|---|
| Số lần retry | `MessageBus:Retry:Limit` | **3** |
| Khoảng đầu | `MessageBus:Retry:InitialIntervalMs` | **200 ms** |
| Khoảng tối đa | `MessageBus:Retry:MaxIntervalMs` | **5 000 ms** |
| Kiểu | — | **Exponential** |

Hết retry → message rơi vào queue **`<queue>_error`**.

> ⚠️ Bản cũ ghi "retry 1s → 5s → 30s rồi vào DLQ" — **sai cả 3 mốc lẫn tên queue đích**.

### Delayed redelivery — **TẮT mặc định**

| Tham số | Config key | Mặc định |
|---|---|---|
| Bật/tắt | `MessageBus:Redelivery:Enabled` | **`false`** |
| Các mốc | `MessageBus:Redelivery:IntervalsMinutes` | `[5, 15, 60]` |

> ⚠️ **Đừng bật nếu chưa cài plugin.** Redelivery cần `rabbitmq_delayed_message_exchange`; image đang
> dùng (`rabbitmq:3-management-alpine`) **không có**. Bật lên mà thiếu plugin thì việc khai báo
> exchange **thất bại ngay lúc bus khởi động ⇒ chết TẤT CẢ service dùng bus**, không riêng chat.

### Điều kiện an toàn của retry

Retry chỉ an toàn nhờ consumer **idempotent**: NotificationService chặn trùng bằng `SET NX` (debounce)
hoặc `IInboxStore`. Bật retry trên consumer chưa idempotent sẽ **nhân bản notification** thay vì gửi lại.

---

## 4. Throughput

| Tham số | Config key | Mặc định |
|---|---|---|
| Prefetch | `MessageBus:PrefetchCount` | 16 |
| Đồng thời | `MessageBus:ConcurrentMessageLimit` | 8 |

---

## 5. Correlation

Bus gắn sẵn filter hai chiều — `CorrelationIdPublishFilter` (ghi header lúc publish) và
`CorrelationIdConsumeFilter` (đọc lại lúc consume), nên `correlationId` xuyên suốt các service mà
consumer không phải tự truyền.

---

## 6. Vận hành

* **Xem message lỗi:** RabbitMQ Management → queue `<tên-consumer>_error`
  (ví dụ `chat-created_error`), **không phải** `notification.chat.events.dlq`.
* **Reprocess:** dùng Management UI/CLI shovel message từ `<queue>_error` về queue gốc sau khi đã fix.
* **Sự cố đã gặp:** thiếu `cfg.UsePublishMessageScheduler()` khiến saga `ChatEscalationReview` và
  `AlertTicketSaga` ném `MassTransit.PayloadNotFoundException: MessageSchedulerContext` mỗi lần
  `.Schedule(...)` → retry → rơi `_error`. Đã đo được **1662 message** kẹt ở `AlertTicketSagaState_error`
  (fix 30/07/2026).
