# ADR 0019: Đường vận chuyển push — SignalR tự vận hành song song Expo, chọn được lúc chạy

Status: Accepted (2026-08-05).

## Context

Kênh `Push` của NotificationService ban đầu chỉ có một đường: **Expo Push API**
(`ExpoPushChannel` → `https://exp.host/--/api/v2/push/send`). Kèm theo nó là hai worker nền của
Sprint 6.3:

- `ExpoReceiptReconcileBackgroundService` (NOTI3-02 · #702) — hỏi `/push/getReceipts` để biết push
  có thật sự tới máy không. Đây là thứ DUY NHẤT đưa notification lên trạng thái `Delivered`.
- `NotificationFallbackBackgroundService` (NOTI3-05 · #705) — push critical quá hạn mà không có
  biên nhận `Ok` thì bù bằng SMS.

Ba vấn đề đẩy tới quyết định này:

1. **Phụ thuộc hạ tầng ngoài.** Expo cần khoá EAS/FCM và cần device token còn hạn. Người dùng web
   không có device token nên không nhận được gì; máy vừa cài lại app thì token cũ chết im lặng.
2. **Chat không chịu được độ trễ của một hàng đợi HTTP bên ngoài.** Khi module Chat dùng kênh Push
   làm đường realtime, một vòng gọi Expo + đối soát biên nhận là quá chậm cho việc nhắn tin.
3. **Bỏ hẳn Expo thì mất `Delivered` và mất chuỗi bù SMS.** Một nhánh phát triển trước đó đã gỡ
   `ExpoPushChannel` khỏi DI cùng cả hai worker trên. Hệ quả không được nói ra: `Delivered` trở
   thành trạng thái không bao giờ đạt tới, và cảnh báo P1 không còn đường bù nào.

## Decision

Giữ **cả hai** đường vận chuyển, chọn bằng cấu hình **đổi được lúc chạy** — không phải sửa file rồi
khởi động lại.

```
PushTransportEnum: SignalR = 1 | Expo = 2 | Both = 3
```

| Giá trị | Đường đi | Cần device token | Có `Delivered` |
|---------|----------|------------------|----------------|
| `SignalR` | Hub `/hubs/notifications` của chính hệ thống | Không | Không |
| `Expo` | Expo Push API | **Có** | Có (qua đối soát biên nhận) |
| `Both` | Cả hai, thành công khi **ít nhất một** đường thành công | Không bắt buộc | Có, nếu đường Expo đi được |

### Cấu phần

| Thành phần | Vai trò |
|-----------|---------|
| `NotificationSetting` (bảng `notification_settings`) | Lưu khoá–giá trị cấu hình hệ thống. Khoá `push.transport`. Unique index **có lọc** `is_deleted = false` |
| `IPushTransportSettingService` / `PushTransportSettingService` | Đọc/ghi transport, cache 30s, xoá cache ngay khi ghi |
| `CompositePushChannel` | Kênh Push duy nhất dispatcher nhìn thấy; rẽ nhánh sang `ISignalRPushChannel` / `IExpoPushChannel` |
| `GET|PUT /api/admin/notification-settings/push-transport` | Màn hình Admin đọc/đổi. `[Authorize(Roles = "Admin")]` |
| `NotificationPushOptions` (`Notification:Push`) | `DefaultTransport`, `CacheSeconds` — chỉ dùng khi bảng còn trống |

### Ba quyết định nhỏ nhưng dễ làm sai nếu không ghi lại

**1. Hai đường push có interface riêng, không cùng `INotificationChannel`.**
Dispatcher chọn kênh bằng `_channels.FirstOrDefault(c => c.ChannelType == …)`. Cả hai đường đều có
`ChannelType = Push`, nên đăng ký cả hai dưới `INotificationChannel` sẽ khiến cái đăng ký sau **chết
im lặng** và thứ tự đăng ký trở thành cấu hình ngầm. Vì vậy có `ISignalRPushChannel` và
`IExpoPushChannel`; chỉ `CompositePushChannel` đăng ký dưới interface chung.

**2. Device token nạp trong `CompositePushChannel`, không phải trong dispatcher.**
Chỉ đường Expo mới cần token. Nạp ở dispatcher thì mỗi lần gửi đều tốn một truy vấn kể cả khi hệ
thống đang chạy thuần SignalR, và dispatcher phải biết chi tiết của một transport cụ thể.

**3. Thiếu device token: ý nghĩa khác nhau giữa `Expo` và `Both`.**
Ở `Expo` đó là thất bại thật (không có đường nào khác). Ở `Both` đó là chuyện bình thường — người
dùng chỉ xài web — nên vẫn tính thành công nhờ SignalR. Coi là thất bại sẽ khiến dispatcher retry
rồi đánh `Failed` một thông báo thực tế đã tới nơi.

### Hai worker nền tự bật/tắt theo transport

`ExpoReceiptReconcileBackgroundService` và `NotificationFallbackBackgroundService` hỏi lại transport
**mỗi vòng lặp** (không chốt một lần lúc khởi động), nên đổi transport trên màn hình Admin là chúng
tự sống lại hoặc tự nghỉ, không cần khởi động lại service.

Hai worker chọn hướng an toàn **ngược nhau** khi không đọc được cấu hình:

- Đối soát biên nhận → mặc định **vẫn chạy**. Chạy thừa một vòng thì vô hại (không có biên nhận nào
  để xử lý), còn bỏ sót thì mất dữ liệu giao hàng thật.
- Bù SMS → mặc định **nghỉ**. Chạy thừa ở đây không vô hại: nó bắn SMS thật cho người dùng thật. Bỏ
  lỡ một vòng thì vòng sau vẫn bắt được vì điều kiện lọc dựa trên mốc thời gian.

## Consequences

### Được

- Chạy được không cần khoá EAS/FCM (`SignalR`), vẫn giữ nguyên `Delivered` và chuỗi bù SMS khi bật
  `Expo`/`Both`.
- Đổi transport không cần deploy: gạt trên màn hình Admin, có hiệu lực ngay với tiến trình xử lý
  request đó và chậm nhất 30 giây với các tiến trình còn lại.
- `device_tokens` và `push_receipts` vẫn có tác dụng thật, không thành bảng chỉ-ghi.

### Mất / phải chấp nhận

- Đường `SignalR` **không có bằng chứng giao hàng**. `SendAsync` của hub trả về không đồng nghĩa máy
  nhận được. Chạy thuần `SignalR` thì `Sent` là trạng thái cuối cùng, `Delivered` không bao giờ đạt
  tới — và vì thế chuỗi bù SMS cũng không có dữ liệu để làm việc.
- Chế độ `Both` ghi **một** dòng notification nhưng gửi **hai** lần. Máy vừa mở app vừa nhận được
  push nền có thể thấy thông báo hai lần; client phải khử trùng theo `notificationId`.
- Thêm một truy vấn cấu hình cho mỗi lần gửi push. Đã che bằng cache 30 giây.

### Ảnh hưởng tới client

Client nhận **hai** sự kiện SignalR khác nhau cho cùng một thông báo, có chủ ý:

| Sự kiện | Nguồn | Dùng để |
|---------|-------|---------|
| `NotificationCreated` | `InAppChannel` (dòng `Channel = InApp`) | Cập nhật danh sách thông báo trong app + badge |
| `NotificationReceived` | `SignalRPushChannel` (dòng `Channel = Push`) | Dựng thông báo hệ điều hành / bong bóng chat tại máy |

Cả hai mang cùng `entityId`. Client hiện đủ cả hai mà không khử trùng thì người dùng thấy hai lần.

## Triển khai

### Không có biến môi trường nào là bắt buộc

`Notification__Push__DefaultTransport` **chỉ được đọc khi bảng `notification_settings` còn trống** —
tức lần đầu dựng một môi trường mới. Sau khi Admin bấm một lần, database là nguồn sự thật vĩnh viễn,
kể cả khi restart service với biến môi trường đặt ngược lại (đã kiểm chứng: env=`Both`, DB=`Expo`,
restart → hệ thống vẫn chạy `Expo`).

Mặc định trong code là `SignalR`, chạy được ngay mà không cần khoá EAS/FCM nào. Đặt biến chỉ có
nghĩa khi muốn một môi trường mới **khởi đầu** ở chế độ khác.

### Độ trễ — thứ thật sự phải chỉnh

Kênh Push là đường realtime của chat, nhưng thông báo đi qua **hai vòng poll** nối tiếp:

```
chat → outbox (TicketService)  --Outbox__IntervalSeconds-->  RabbitMQ
     → dòng Pending (NotificationService)  --Notification__Dispatch__PollIntervalSeconds-->  SignalR
```

Mặc định trong code của cả hai đều là **5 giây** ⇒ tổng ~10 giây, đủ để người dùng cho là hỏng.
Vì vậy cả `docker-compose.yml` lẫn `docker-compose.prod.yml` đều ghim xuống **1 giây**:

| Biến (đặt trong `.env` hoặc `/opt/solar/.env.prod`) | Mặc định compose | Ý nghĩa |
|---|---|---|
| `TICKET_OUTBOX_INTERVAL_SECONDS` | `1` | Chặng 1 — relay outbox của TicketService |
| `TICKET_OUTBOX_BATCH_SIZE` | `100` | |
| `NOTIFICATION_DISPATCH_POLL_INTERVAL_SECONDS` | `1` | Chặng 2 — worker dispatch của NotificationService |
| `NOTIFICATION_DISPATCH_BATCH_SIZE` | `100` | |
| `NOTIFICATION_PUSH_DEFAULT_TRANSPORT` | `SignalR` | Chỉ áp khi bảng cấu hình còn trống |
| `NOTIFICATION_PUSH_CACHE_SECONDS` | `30` | Bao lâu thì lựa chọn của Admin lan tới worker nền / replica khác |

Máy chủ yếu thì nâng hai giá trị `*_INTERVAL_SECONDS` lên — mỗi vòng poll chỉ là một truy vấn có
index kèm `LIMIT`, nhưng vẫn là truy vấn.

> **Bẫy khi đặt biến.** Trong docker-compose, `environment:` **đè** `env_file:`. Cả hai file compose
> đều khai các biến trên trong `environment:` dưới dạng `${TÊN_HOA:-mặc định}`, nên phải dùng **tên
> viết hoa** ở `.env` / `/opt/solar/.env.prod`. Đặt tên kiểu `Notification__Push__DefaultTransport`
> vào `.env.Docker` sẽ **bị bỏ qua im lặng** — không lỗi, không log.

## Liên quan

- Enum gửi qua hub là **số**, không phải chuỗi — hub cố ý không đăng ký `JsonStringEnumConverter`
  để khớp REST API. Xem `docs/chat/signalr-client-guide.md`.
- Quyết định "chat bỏ qua quiet hours / digest / hạn mức" đi kèm ADR này: xem
  `docs/non-obvious-decisions.md`.
