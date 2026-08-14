# Báo cáo tổng hợp và audit Notification — Sprint 6 đến Sprint 6.6

> Ngày đối chiếu source: **10/08/2026**  
> Phạm vi tài liệu kế hoạch: [`overall.md`](overall.md), các mục Sprint 6, 6.2, 6.3, 6.4, 6.5 và 6.6  
> Phạm vi code: `NotificationService`, các producer/event/outbox liên quan trong `TicketService`, `AuthService`, `BatteryService`, các consumer gửi thư/tin nhắn trong `EmailService` và `SmsService`, cùng phần Knowledge Base được Sprint 6 nhắc tới.
> Trạng thái cập nhật: **đã đối chiếu lại sau đợt sửa lỗi IoT offline spam ngày 10/08/2026**; chi tiết triển khai và bằng chứng kiểm thử ở [mục 17](#17-cập-nhật-khắc-phục-iot-offline-spam--10082026).

## 1. Mục đích và cách đọc tài liệu

Tài liệu này được viết để một người chưa đọc code vẫn có thể hiểu:

1. Hệ thống notification dùng để làm gì và dữ liệu đi qua những thành phần nào.
2. Mỗi Sprint từ 6 đến 6.6 đã bổ sung nội dung gì, tác dụng thực tế là gì.
3. Hành vi **đang tồn tại trong source code hiện tại**, thay vì chỉ lặp lại kế hoạch trong `overall.md`.
4. API, routing người nhận, kênh gửi, template, preference, realtime, batch và Knowledge Base đang vận hành ra sao.
5. Những chỗ tài liệu và code lệch nhau, những lỗi/rủi ro đã xác minh, cũng như phần nào chưa được chứng minh bằng kiểm thử tích hợp.

### 1.1. Quy tắc xác định “nguồn sự thật”

Khi các nguồn mâu thuẫn, báo cáo ưu tiên theo thứ tự:

1. Source code runtime hiện tại.
2. Migration, cấu hình runtime và đăng ký dependency.
3. Unit test hiện tại.
4. `overall.md` — dùng để hiểu mục tiêu, lịch sử và phạm vi Sprint, không mặc định coi mọi dòng là trạng thái hiện tại.

Các nhãn dùng trong báo cáo:

- **Đã xác minh:** có đường thực thi hoặc cấu hình tương ứng trong code hiện tại.
- **Sai lệch tài liệu/code:** mô tả trong `overall.md`, comment hoặc ma trận không khớp hành vi runtime.
- **Rủi ro:** code có tồn tại nhưng còn trường hợp có thể gây sai nghiệp vụ hoặc cần kiểm thử tích hợp.
- **Chưa chứng minh E2E:** unit test hoặc đọc code chưa đủ để kết luận một luồng liên service đã chạy thành công trong môi trường triển khai thật.

### 1.2. Mốc Sprint được đọc

Trong `overall.md`, các mục chính nằm tại:

| Mục | Vị trí bắt đầu | Nội dung chính |
|---|---:|---|
| Sprint 6 | dòng 5328 | Notification nền tảng, đa kênh, môi trường, Saga, Knowledge Base |
| Sprint 6.2 | dòng 6228 | Hoàn thiện pipeline dispatch và mở rộng event |
| Sprint 6.3 | dòng 6293 | Production hardening, preference matrix, reliability, monitoring, template, realtime |
| Sprint 6.4 | dòng 6475 | Audience/group/broadcast/batch |
| Sprint 6.5 | dòng 6733 | Template có biến, coverage, preview và tích hợp consumer |
| Sprint 6.6 | dòng 7012 | Push transport SignalR/Expo/Both và hoàn thiện chat realtime |

Không có một heading Sprint 6.1 độc lập trong backlog này; chuỗi được tài liệu đặt tên là Sprint 6, sau đó 6.2 đến 6.6.

### 1.3. Số liệu snapshot của source hiện tại

- `NotificationService/src`: **269 file C#**, không tính `*.Designer.cs` sinh tự động; tổng **22.986 dòng C#**.
- **32 file consumer Application**, chứa **39 khai báo `IConsumer<T>`** cho **38 loại contract khác nhau**; `AccountActivatedEvent` có hai consumer độc lập.
- `NotificationTypeEnum`: **37 loại notification**; giá trị 13 là khoảng trống lịch sử và `System = 99`.
- NotificationService có **16 template HTML nhúng**.
- EmailService có **8 loại template email** riêng.
- Worktree tại thời điểm audit có nhiều thay đổi chưa commit sẵn từ trước. Báo cáo mô tả **working tree hiện tại**, không khẳng định đây là trạng thái của `HEAD` hoặc `origin`.

## 2. Bức tranh tổng thể

### 2.1. Mục tiêu nghiệp vụ

Notification không chỉ là bảng thông báo trong ứng dụng. Nó là lớp điều phối thông tin giữa các domain:

- Auth/account: tài khoản được kích hoạt, hồ sơ thay đổi, tài khoản bị xóa.
- Ticket/SLA: tạo, giao việc, đổi trạng thái, xử lý xong, phê duyệt, từ chối, đóng, mở lại, gộp ticket, cảnh báo/breach SLA và incident.
- Battery/IoT/environment: bất thường pin, cảnh báo leo thang, cascade risk, thiết bị offline/recovered/auto-decommissioned và sự cố môi trường.
- Chat/collaboration: phòng chat mới, mention, reaction, thay đổi participant/quyền, escalation.
- AI/blog: sinh bài viết thành công hoặc thất bại.
- Kênh gửi: InApp, SignalR, Expo Push, Email và SMS.

Tác dụng chính là biến một integration event nghiệp vụ thành đúng tập người nhận, đúng kênh, đúng nội dung, có trạng thái theo dõi, có preference và có cơ chế retry/retention/monitoring.

### 2.2. Luồng xử lý chuẩn

```text
AuthService / TicketService / BatteryService
              │
              │ integration event
              ▼
        Outbox hoặc RabbitMQ
              │
              ▼
  NotificationService consumer
              │
              │ resolve người nhận + kênh
              ▼
 NotificationWriter tạo N bản ghi
 (mỗi người nhận × mỗi kênh = 1 hàng Pending)
              │
              ▼
 NotificationDispatchBackgroundService
              │
              ├─ preference kênh và category
              ├─ digest / rate limit / quiet hours
              ├─ render DB template hoặc inline fallback
              └─ chọn transport
                   ├─ InApp + SignalR
                   ├─ Push: SignalR / Expo / Both
                   ├─ Email qua RabbitMQ → EmailService
                   └─ SMS qua RabbitMQ → SmsService
              │
              ▼
 Pending → Processing → Sent/Delivered/Opened/Read/Failed
```

Một event logic có thể tạo nhiều hàng `notifications`. Ví dụ một người nhận bằng InApp + Push + Email sẽ có ba hàng khác nhau. Feed người dùng mặc định chỉ đọc kênh InApp; các hàng Email/SMS/Push vẫn phục vụ dispatch, audit và thống kê.

### 2.3. Ranh giới service

| Service | Trách nhiệm liên quan |
|---|---|
| NotificationService | Nhận event, xác định người nhận/kênh, lưu queue, render template, dispatch, preference, realtime, batch, audit |
| TicketService | Phát event Ticket/SLA/Chat/Blog; chứa toàn bộ Knowledge Base và ticket–KB reference |
| AuthService | Phát event account, OTP/security; là nguồn tài khoản/role gốc |
| BatteryService | Phát event battery, IoT, environmental và Saga |
| EmailService | Nhận yêu cầu gửi email, render template riêng của email và gọi nhà cung cấp email |
| SmsService | Nhận yêu cầu SMS; môi trường dev dùng fake sender; phát `SmsFailedEvent` khi thất bại cuối |

## 3. Dữ liệu và mô hình trạng thái

### 3.1. Các entity chính trong NotificationService

| Entity | Vai trò |
|---|---|
| `Notification` | Một lần gửi cho một user trên một channel; giữ type, category, entity liên quan, payload, retry và trạng thái |
| `AccountReadModel` | Bản sao tối thiểu account/role/contact để NotificationService tự resolve người nhận và địa chỉ Email/SMS |
| `DeviceToken` | Token Expo theo thiết bị, platform, trạng thái active và thời điểm hoạt động |
| `NotificationPreference` | Bật/tắt channel, quiet hours, digest và timezone của user |
| `NotificationCategoryPreference` | Bật/tắt từng channel theo category nghiệp vụ |
| `NotificationTemplate` | Template DB theo type × channel, có version và trạng thái active |
| `NotificationGroup` | Nhóm Static hoặc nhóm động theo Role |
| `NotificationGroupMember` | Thành viên tường minh của nhóm Static |
| `NotificationBatch` | Một chiến dịch broadcast, cấu hình audience/channel/template và thống kê tổng |
| `NotificationBatchTarget` | Snapshot từng người nhận thuộc một batch |
| `PushReceipt` | Ticket/receipt Expo để reconcile kết quả gửi bất đồng bộ |
| `NotificationAuditLog` | Lịch sử hành động/trạng thái phục vụ audit |
| `NotificationAuditOutbox` | Outbox riêng để relay audit an toàn |
| `NotificationSetting` | Cấu hình runtime, hiện có lựa chọn `PushTransport` |

Nguồn đọc nhanh:

- [`Notification.cs`](services/NotificationService/src/NotificationService.Domain/Entities/Notification.cs)
- [`NotificationTypeEnum.cs`](services/NotificationService/src/NotificationService.Domain/Enums/NotificationTypeEnum.cs)
- [`ApplicationDbContext.cs`](services/NotificationService/src/NotificationService.Infrastructure/Persistence/ApplicationDbContext.cs)

### 3.2. Enum quan trọng

**Channel:**

| Giá trị | Channel | Ý nghĩa |
|---:|---|---|
| 1 | Push | Push transport do Admin chọn: SignalR, Expo hoặc cả hai |
| 2 | Email | Gửi gián tiếp qua EmailService |
| 3 | Sms | Gửi gián tiếp qua SmsService |
| 4 | InApp | Lưu feed trong ứng dụng và phát realtime |

**Trạng thái notification:**

- `Pending`: chờ worker lấy.
- `Processing`: đã được claim độc quyền để tránh nhiều instance gửi trùng.
- `Sent`: provider/transport chấp nhận hoặc InApp đã hoàn tất.
- `Delivered`: đã nhận tín hiệu delivered nếu transport hỗ trợ.
- `Opened`: người dùng/provider báo đã mở.
- `Read`: người dùng đánh dấu đã đọc trong feed.
- `Failed`: hết retry hoặc nhận phản hồi thất bại cuối.

**Push transport:** `SignalR = 1`, `Expo = 2`, `Both = 3`.

### 3.3. 37 loại notification hiện tại

Các type được chia đúng theo `NotificationCategoryMap` runtime như sau:

| Category | Notification type |
|---|---|
| Account | `AccountActivated`, `System`, `BlogGenerationCompleted`, `BlogGenerationFailed` |
| Ticket | `TicketCreated`, `TicketAssigned`, `TicketStatusChanged`, `TicketResolved`, `TicketClosed`, `TicketApproved`, `TicketRejected`, `TicketMerged`, `TicketReopened`, `TicketRatingRequested` |
| SLA | `TicketEscalated`, `SlaWarning`, `SlaBreached`, `SlaAutoResumed`, `IncidentDeclared`, `AlertTicketSagaFailed`, `ChatEscalatedToAdmin` |
| Battery | `BatteryAnomalyDetected`, `BatteryAnomalyWarning`, `BatteryAnomalyInfo`, `BatteryAlertEscalationPending`, `CascadeRiskHigh`, `IotDeviceWentOffline`, `IotDeviceRecovered`, `IotDeviceAutoDecommissioned` |
| Environmental | `EnvironmentalIncidentDetected`, `EnvironmentalIncidentResolved` |
| Chat | `ChatCreated`, `ChatMentioned`, `ChatReacted`, `ParticipantAdded`, `ParticipantRemoved`, `ParticipantRoleChanged` |

Ba cách xếp có chủ ý nhưng dễ gây nhầm: `TicketEscalated` và `ChatEscalatedToAdmin` thuộc SLA; `AlertTicketSagaFailed` cũng thuộc SLA; hai type Blog và `System` thuộc Account. `System` không có consumer integration event chuyên biệt trong tập routing bên dưới.

`NotificationCategoryMap` ánh xạ type sang category để preference matrix hoạt động. Entry `TicketMerged` lặp đã được loại bỏ; test coverage hiện phải bao toàn bộ 37 type.

## 4. Tổng hợp theo Sprint

## 4.1. Sprint 6 — nền tảng Notification và Knowledge Base

### Mục tiêu

Xây nền móng notification đa kênh và nối các domain quan trọng vào hệ thống thông báo.

### Phần Notification

- Tạo NotificationService theo 4 layer: Api, Application, Domain, Infrastructure.
- Tạo domain entity, repository/unit of work và PostgreSQL persistence.
- Nhận integration event bằng MassTransit consumer.
- Hỗ trợ bốn channel: InApp, Push/Expo, Email, SMS.
- Có device token registration để push tới thiết bị.
- Có preference theo channel, quiet hours và debounce.
- Thêm các event môi trường; sự cố nghiêm trọng được fan-out nhiều channel và có `BypassQuietHours`.
- Thêm thông báo khi Saga liên kết alert–ticket thất bại.
- Chuẩn bị routing qua API Gateway và health/observability.

### Phần Knowledge Base

- CRUD bài viết hướng dẫn kỹ thuật.
- Mã bài `KB-YYYY-D4`.
- Quy trình Draft/PendingReview/Published/Archived.
- Version major/minor, compare, rollback, template bài viết.
- Gợi ý bài KB theo ticket/chat.
- Liên kết bài KB vào ticket hoặc chat.
- Thống kê lượt xem/helpful/usage.
- Chuyển nội dung chat thành draft KB.

### Tác dụng

Sprint này biến Notification từ một ý tưởng giao diện thành một service có storage và message consumer thật. Knowledge Base cung cấp bộ nhớ tri thức có version để nhân viên tái sử dụng cách xử lý sự cố.

### Hiệu chỉnh so với mô tả lịch sử

- `overall.md` ở Sprint 6 gọi các file template là `.hbs`; source hiện tại dùng **16 file `.html`** có cú pháp Handlebars.
- Một số đoạn cũ trong `overall.md` nói “29 consumer” hoặc đánh dấu Sprint 6.4 “chưa implement”. Source hiện tại đã vượt qua các mốc đó: có 37 binding và nhóm/broadcast đã tồn tại.

## 4.2. Sprint 6.2 — hoàn thiện pipeline dispatch

### Những gì được bổ sung

- Worker thật sự quét các hàng Pending và dispatch.
- Email notification được gửi sang EmailService bằng event.
- Chat escalation đến Admin.
- Luồng email bảo mật trực tiếp: OTP, reset password, refresh-token reuse, suspicious login và 2FA cross-device.
- Payload Ticket/SLA được làm giàu để nội dung thông báo hữu ích hơn.
- Phân cấp SLA breach:
  - P1: Manager/Admin, tất cả channel.
  - P2: Manager, InApp + Push + Email.
  - P3: Manager, InApp.
- Lifecycle ticket: approved, rejected, reopened, closed và rating request.
- Battery severity:
  - Info: InApp.
  - Warning: InApp + Push.
  - Critical: InApp + Push + Email + SMS.
- `ChatCreated` tạo InApp notification.
- `SmsFailedEvent` cập nhật hàng SMS gốc thành Failed thay vì tạo thông báo lỗi mới.
- Digest, audit, retry và template precedence được đưa vào pipeline.
- DB template active được ưu tiên hơn inline template.
- Expo chia batch tối đa 100 message.
- AuthService phát `AccountDeletedEvent`.

### Tác dụng

Nếu Sprint 6 tạo “khung”, Sprint 6.2 khiến hàng Pending thật sự đi tới provider, mở rộng độ phủ event và phân biệt mức nghiêm trọng để tránh gửi quá ít hoặc quá nhiều.

## 4.3. Sprint 6.3 — production hardening

### Feed và trạng thái đồng bộ

- API feed chỉ tập trung vào InApp.
- Mark read/opened có cơ chế cập nhật các sibling notification cùng event để trạng thái giữa channel bớt lệch.
- Có unread count.
- SignalR phát notification và số unread mới.

### Preference và kiểm soát tần suất

- Preference theo `category × channel`.
- Preference tổng theo channel và preference category được kết hợp theo logic AND.
- Quiet hours theo timezone.
- Digest cho Email/Push.
- Redis rate limit theo user và theo type.
- Critical có policy bypass riêng; trong nhóm chat, `ChatCreated` và `ChatMentioned` được xem là realtime conversation.

### Độ tin cậy

- Claim DB atomically để nhiều replica không lấy cùng một notification.
- Retry có backoff, giới hạn số lần và recovery hàng Processing quá hạn.
- Expo receipt reconciliation và tự deactivate token lỗi.
- Fallback Push → SMS cho một số critical notification.
- Retention soft-delete dữ liệu cũ.
- Audit outbox và relay.
- DLQ monitor và metrics.
- MassTransit retry được cấu hình; delayed redelivery là tùy chọn và mặc định tắt vì môi trường không chắc có RabbitMQ delayed-exchange plugin.

### Template và an toàn nội dung

- Template DB có version, preview, test-send; revise tạo version mới và có thể activate lại version cũ để rollback nội dung đang dùng.
- Escape dữ liệu động để giảm nguy cơ chèn HTML.
- Unsubscribe email dùng token HMAC.
- Locale từng được thêm rồi bị loại bỏ ở migration sau; runtime hiện không phải hệ template đa ngôn ngữ.

### Tác dụng

Sprint này giải quyết các vấn đề thường chỉ xuất hiện khi chạy production: gửi trùng khi scale ngang, spam, im lặng ban đêm, token push chết, retry vô hạn, thiếu audit và template không kiểm soát.

## 4.4. Sprint 6.4 — audience, group và broadcast

### Account read model

- Consumer snapshot/profile/activation/deletion đồng bộ bản sao account.
- Recipient resolver không cần gọi đồng bộ sang AuthService mỗi lần gửi.
- Chỉ tài khoản active, chưa bị soft-delete mới được chọn làm audience.

### Notification group

- `Static`: Admin thêm/xóa member tường minh.
- `Role`: membership được suy ra động từ `AccountReadModel.Role` tại thời điểm gửi.
- Seeder tạo bốn nhóm role hệ thống:
  - All Administrators.
  - All Managers.
  - All Technical Staff.
  - All Customers.
- Nhóm role không cho sửa member bằng API; API tạo mới chỉ tạo nhóm Static.

### Broadcast và batch

- Admin preview audience trước khi gửi.
- Admin gửi một nội dung tới group hoặc tập user.
- `NotificationBatch` giữ thông tin chiến dịch.
- `NotificationBatchTarget` snapshot người nhận.
- Có lịch sử batch, chi tiết, tổng số target và thống kê trạng thái/channel.
- `UseTemplate` cho phép broadcast chọn dùng DB template hoặc dùng trực tiếp title/body Admin nhập.

### Tác dụng

Sprint này tách thông báo hệ thống phát sinh từ event khỏi thông báo chủ động của Admin. Group role luôn theo account hiện tại; group static phù hợp nhóm tùy chỉnh ổn định.

### Giới hạn đã xác minh

`NotificationWriter.WriteBatchedAsync` đã được định nghĩa nhưng **không có caller** trong source hiện tại. Vì vậy:

- Broadcast thủ công có `BatchId` và target/batch đầy đủ.
- Notification tự động từ integration event vẫn ghi từng hàng với `BatchId = null`.
- Không nên hiểu rằng mọi event fan-out tự động đã được gom thành một batch nghiệp vụ.

## 4.5. Sprint 6.5 — template thật sự tham gia runtime

### Năng lực quản trị template

- Catalog biến cho từng notification type.
- Syntax guard và variable guard chặn placeholder không hợp lệ.
- Gợi ý tên biến gần đúng khi Admin gõ sai.
- Sample model để preview không cần event thật.
- Ma trận type × channel để đánh giá coverage.
- API list biến và coverage.
- Create/revise/activate/delete/preview/test-send.
- DB seeder hội tụ: cập nhật/seed template theo catalog thay vì chỉ insert một lần.
- Preview broadcast theo từng channel.
- Manual broadcast có thể bypass template bằng `UseTemplate = false`.
- Enum/label hiển thị được chuyển sang text có ý nghĩa; SLA có code trong nội dung.
- `TicketMerged = 34` được bổ sung.
- InApp lưu title/body sau render để feed hiển thị đúng nội dung đã gửi.
- Digest cắt ngắn nội dung và serializer JSON được thống nhất.

### Tác dụng

Trước mốc này, template có thể tồn tại trong DB nhưng consumer vẫn dễ dùng nội dung hard-code. Sprint 6.5 làm template ảnh hưởng trực tiếp đến output runtime, đồng thời cho Admin công cụ kiểm tra biến và preview trước khi kích hoạt.

### Sai lệch cần biết

- Class/catalog có comment hoặc tên gợi ý “Vietnamese”, nhưng nhiều chuỗi seed hiện là **tiếng Anh**.
- `SlaAutoResumed` hiện đã có đủ dispatch matrix, catalog biến và template seed cho InApp + Push.
- `AdminInvite = 13` lịch sử đã bị loại khỏi `NotificationTypeEnum`; email mời Admin được xử lý ở EmailService thay vì notification type hiện tại.

## 4.6. Sprint 6.6 — SignalR song song Expo và chat realtime

### Chat recipient và authorization

- TicketService tính danh sách `RecipientUserIds` cho chat event.
- Loại author khỏi danh sách.
- Kiểm tra internal/external visibility trước khi đưa recipient vào event.
- NotificationService ưu tiên recipient list từ producer; vẫn có fallback legacy cho contract cũ.
- Có consumer cho chat created, mention, reaction, participant added/removed/role changed và escalation.

### Realtime SignalR

- Hub tại `/hubs/notifications`.
- Client nhận các event:
  - `NotificationCreated`.
  - `NotificationReceived`.
  - `UnreadCountChanged`.
- Có Redis backplane để nhiều replica SignalR chia sẻ message.
- Hỗ trợ JWT qua query string cho kết nối hub.
- Payload dùng enum dạng số; client phải giữ contract tương thích.

### Push transport có thể đổi lúc chạy

- Admin chọn `SignalR`, `Expo` hoặc `Both` qua setting DB.
- Setting được cache để không query DB mỗi lần gửi.
- Khi DB chưa có setting, giá trị sai hoặc đọc DB lỗi, code dùng `SignalR` làm mặc định; Docker Compose và Helm hiện cũng đặt mặc định SignalR.
- Composite push sender điều phối transport theo setting.
- Expo payload mang metadata điều hướng và Android channel ID.
- Worker dispatch có poll interval deployment là 1 giây; default trong code vẫn là 5 giây nếu không override.
- `ChatCreated` và `ChatMentioned` bypass rate limit, quiet hours và digest để không làm mất tính tức thời. Các chat type khác không tự động được xếp vào tập realtime này; `ChatEscalatedToAdmin` vẫn bypass vì nằm trong critical set.

### Tác dụng

Web đang kết nối có thể nhận notification tức thời qua SignalR, trong khi mobile/background vẫn dùng Expo. Admin có thể chuyển transport mà không build lại ứng dụng.

### Rủi ro thiết kế còn lại

- SignalR không có application-level acknowledgement. Việc server gọi hub thành công không chứng minh user đang online hoặc client đã hiển thị notification.
- SignalR notifier hiện nuốt exception và coi gửi vào group là thành công; group rỗng cũng không phải lỗi.
- `Both` có thể làm một user đang online nhận cùng nội dung qua SignalR và Expo.
- Fallback SMS chỉ được kích hoạt trong một số chế độ/critical type; chưa có E2E chứng minh toàn bộ chuỗi provider failure → fallback trong môi trường thật.
- Thay đổi thứ tự/giá trị enum có thể phá client vì payload realtime dùng số.

## 5. Routing thực tế theo integration event

Các bảng dưới đây phản ánh consumer hiện tại, không phải ma trận mong muốn trong tài liệu.

### 5.1. Account

| Event | Người nhận | Channel | Tác dụng khác |
|---|---|---|---|
| `AccountActivatedEvent` | Chính account đó | InApp | Đồng thời consumer khác upsert `AccountReadModel` |
| `AccountProfileUpdatedEvent` | Không tạo notification | — | Cập nhật contact/name trong read model |
| `AccountSyncSnapshotEvent` | Không tạo notification | — | Upsert snapshot đầy đủ để resync |
| `AccountDeletedEvent` | Không tạo notification | — | Đánh dấu account read model inactive/soft-deleted |

### 5.2. Ticket lifecycle

| Event | Người nhận | Channel |
|---|---|---|
| `TicketCreatedEvent` | Manager | InApp + Push |
| `TicketAssignedEvent` | Staff chính và Customer | InApp + Push + Email |
| `TicketStatusChangedEvent` | Customer | InApp + Push |
| `TicketResolvedEvent` | Manager và Customer | InApp + Push + Email |
| `TicketEscalatedEvent` | Manager và Admin | InApp + Push |
| `TicketApprovedEvent` | Customer | InApp + Push + Email |
| `TicketRejectedEvent` | Customer nếu đóng/out-of-scope; nếu không thì Staff | InApp + Push + Email |
| `TicketClosedEvent` | Customer và Manager | InApp + Push |
| `TicketReopenedEvent` | Manager và Staff | InApp + Push |
| `TicketRatingRequestedEvent` | Customer | InApp + Push |
| `TicketMergedEvent` | Customer của ticket nguồn | InApp |

### 5.3. SLA và incident

| Event | Người nhận | Channel |
|---|---|---|
| `SlaWarningEvent` | Manager và assigned Staff | InApp + Push |
| `SlaBreachedEvent`, P1 | Manager và Admin | InApp + Push + Email + SMS |
| `SlaBreachedEvent`, P2 | Manager | InApp + Push + Email |
| `SlaBreachedEvent`, P3 | Manager | InApp |
| `SlaAutoResumedEvent`, pause reason 1 | Customer | InApp + Push |
| `SlaAutoResumedEvent`, pause reason 2 | Manager | InApp + Push |
| `IncidentDeclaredEvent` | Manager và Admin | InApp + Push |

### 5.4. Battery, IoT và environment

| Event | Người nhận | Channel | Ghi chú |
|---|---|---|---|
| `BatteryAnomalyDetectedEvent` | Customer | InApp + Push + Email + SMS | Consumer “critical”, nhưng type này không nằm trong critical bypass set |
| `BatteryAnomalyWarningDetectedEvent`, severity Warning | Customer | InApp + Push | Một contract xử lý cả Warning và Info |
| `BatteryAnomalyWarningDetectedEvent`, severity Info | Customer | InApp | Chọn type/channel theo trường `Severity` |
| `BatteryAlertEscalationRequestedEvent` | Manager và Admin | InApp + Push + Email | — |
| `BatteryCascadeRiskHighEvent` | Manager và Admin | InApp + Push + Email | — |
| `AlertTicketSagaFailedEvent` | Manager và Admin | InApp + Push + Email | Cảnh báo lỗi orchestration |
| `EnvironmentalIncidentDetectedEvent` | Manager và Admin | InApp + Push + Email + SMS | Gắn `BypassQuietHours = true`; vẫn qua channel/category preference |
| `EnvironmentalIncidentResolvedEvent` | Manager và Admin | InApp | — |
| `IotDeviceWentOfflineEvent` | Manager, Admin, Staff và Customer sở hữu site | InApp + Push | Một event cho một incident/device; business-key dedupe theo AlertId |
| `IotDeviceRecoveredEvent` | Manager, Admin, Staff và Customer sở hữu site | InApp + Push | Phát khi recovery đã được xác nhận và offline alert được resolve |
| `IotDeviceAutoDecommissionedEvent` | Manager và Admin | InApp + Push | Critical; tách khỏi lỗi kết nối thông thường |

### 5.5. Chat và AI/Blog

| Event | Người nhận | Channel | Điều kiện |
|---|---|---|---|
| `ChatCreatedEvent` | Recipient list do TicketService tính | InApp + Push | Loại author; có kiểm tra internal visibility |
| `ChatMentionedEvent` | User được mention | InApp + Push + Email | — |
| `ChatReactedEvent` | Tác giả message | InApp + Push | Bỏ reaction removed và self-reaction |
| `ParticipantAddedEvent` | Participant mục tiêu | InApp + Push | — |
| `ParticipantRemovedEvent` | Participant mục tiêu | InApp + Push | — |
| `ParticipantRoleChangedEvent` | Participant mục tiêu | InApp + Push | — |
| `ChatEscalatedToAdminEvent` | Admin | InApp + Push + Email | Bypass do type nằm trong critical set |
| `BlogGenerationStatusChangedEvent`, status Draft | Người yêu cầu | InApp | Tạo type `BlogGenerationCompleted` |
| `BlogGenerationStatusChangedEvent`, status khác Draft | Người yêu cầu | InApp | Tạo type `BlogGenerationFailed` |
| `SmsFailedEvent` | Không tạo hàng mới | — | Tìm SMS gốc theo correlation và chuyển Failed |

Nguồn chính của routing:

- [`Consumers`](services/NotificationService/src/NotificationService.Application/Consumers)
- [`NotificationWriter.cs`](services/NotificationService/src/NotificationService.Application/Consumers/NotificationWriter.cs)
- [`NotificationDispatchOptions.cs`](services/NotificationService/src/NotificationService.Application/Services/NotificationDispatchOptions.cs)

## 6. Pipeline dispatch chi tiết

### 6.1. Tạo hàng chờ

Consumer resolve recipient và gọi `NotificationWriter`. Writer:

- Loại recipient rỗng/trùng.
- Tạo một row cho mỗi `recipient × channel`.
- Ghi type, category, entity liên quan, raw data/payload và cờ `bypassQuietHours` khi được yêu cầu.
- Đặt trạng thái `Pending`.
- Có helper batched, nhưng như đã nêu, helper này chưa được event consumer gọi.

### 6.1.1. Chống trùng ở tầng consumer

- `NotificationDebounce` dùng Redis `SET NX` để việc giữ chỗ là atomic.
- Business debounce theo alert có cửa sổ 5 phút.
- Idempotency theo MassTransit `MessageId` giữ lease ngắn 2 phút trong lúc xử lý; chỉ sau khi ghi thành công mới kéo dài dấu “đã xử lý” lên 30 phút.
- Nếu action lỗi, consumer cố nhả lease ngay để broker retry thật; nếu message không có `MessageId`, code vẫn xử lý để không làm mất notification.
- Một số consumer dùng Inbox/idempotency store chung thay vì helper này, ví dụ blog status. Hai cơ chế đều nhằm ngăn broker redelivery tạo row trùng, nhưng không thay thế correlation/batch cho nhiều channel của cùng một event.

### 6.2. Claim và concurrency

Dispatch worker claim atomically từ DB:

- `Pending → Processing` trước khi gọi provider.
- Một lần claim tối đa 100 hàng theo cấu hình batch hiện tại.
- Hàng Processing quá timeout có thể được thu hồi để retry.
- Tối đa 5 lần thử.
- Backoff tăng dần, giới hạn khoảng 30–900 giây.
- Processing timeout khoảng 300 giây.

Mục đích là tránh hai replica cùng gửi một hàng và không để một worker chết làm notification kẹt vĩnh viễn.

### 6.3. Thứ tự chính sách trước khi gửi

Dispatcher áp dụng theo ý nghĩa sau:

1. Channel preference.
2. Category × channel preference.
3. Digest eligibility.
4. Rate limit.
5. Quiet hours.
6. Kiểm tra contact/token cần thiết.
7. Chọn và render template.
8. Gọi transport.
9. Cập nhật status/retry/audit/metrics.

Channel preference và category preference là **AND**: tắt ở một trong hai nơi thì channel bị tắt. Critical và `BypassQuietHours` **không bỏ qua preference**; chúng chỉ tác động đến quiet hours và, tùy type, digest/rate limit.

### 6.4. Critical và realtime bypass

Critical set mặc định trong dispatch options:

- `EnvironmentalIncidentDetected`.
- `IncidentDeclared`.
- `BatteryAlertEscalationPending`.
- `AlertTicketSagaFailed`.
- `SlaBreached`.
- `ChatEscalatedToAdmin`.

Chỉ `ChatCreated` và `ChatMentioned` được `RealtimeConversationTypes` cho bypass rate limit, quiet hours và digest. `ChatEscalatedToAdmin` có cùng hiệu ứng do là critical; reaction và participant-change không thuộc hai tập đó. `BatteryAnomalyDetected` dù consumer gửi bốn channel vẫn **không thuộc critical set mặc định**, nên vẫn có thể chịu quiet/digest/rate policy nếu không có cờ bypass khác.

### 6.5. Digest

- Áp dụng cho Email và Push.
- Không áp dụng cho InApp hoặc SMS.
- Không áp dụng cho critical, `ChatCreated` hoặc `ChatMentioned` theo policy hiện tại.
- Thay vì gửi ngay, hàng đủ điều kiện được gom và worker digest gửi bản tổng hợp sau.
- Nội dung item được truncate để digest không phình không kiểm soát.

### 6.6. Rate limit

- Áp dụng cho channel ngoài InApp.
- Mặc định khoảng 20 notification/user/giờ và 8 notification/type/giờ.
- Khi vượt ngưỡng, worker defer khoảng 60 giây thay vì lập tức fail.
- Critical, `ChatCreated` và `ChatMentioned` bypass.
- Redis lỗi thì rate limiter fail-open: ưu tiên không làm mất thông báo, đổi lại có thể gửi nhiều hơn giới hạn.

### 6.7. Quiet hours

- Dựa trên khoảng giờ và timezone preference của user.
- Không trì hoãn InApp.
- Critical, `ChatCreated` và `ChatMentioned` bypass.
- Channel khác được defer đến khi hết quiet window.

### 6.8. Template precedence

1. Nếu notification thuộc manual batch với `UseTemplate = false`, dùng title/body trực tiếp của batch.
2. Ngược lại, tìm active DB template đúng type × channel.
3. Nếu không có, dùng inline fallback từ consumer/catalog.
4. Render bằng Handlebars và escape dữ liệu động.

### 6.9. Transport theo channel

**InApp**

- Ghi title/body đã render trở lại row.
- Chuyển Sent.
- Phát SignalR event và cập nhật unread count.

**Email**

- Resolve email từ `AccountReadModel` hoặc dữ liệu liên quan.
- Publish yêu cầu qua MassTransit cho EmailService.
- Link unsubscribe dùng HMAC để không cần đăng nhập.

**SMS**

- Resolve phone.
- Publish yêu cầu sang SmsService.
- Nếu provider thất bại cuối, `SmsFailedEvent` quay về cập nhật row gốc.

**Push**

- Đọc setting `PushTransport` từ DB/cache.
- Nếu chưa có/không đọc được setting, fallback cấu hình hiện là `SignalR`.
- `SignalR`: gửi tới group user trong hub.
- `Expo`: lấy active token, chia chunk 100, gửi metadata và lưu receipt.
- `Both`: gọi cả hai; composite được coi là thành công khi ít nhất một transport thành công.
- Fallback Push → SMS chỉ quét khi transport hiện tại là `Expo` hoặc `Both`, notification thuộc critical set, đang `Sent`, quá ngưỡng mặc định 30 phút và chưa có receipt `Ok`; worker chống tạo lại cùng một SMS fallback bằng `fallbackFrom`.

### 6.10. Bảy background worker

| Worker | Trách nhiệm |
|---|---|
| `NotificationDispatchBackgroundService` | Claim và gửi notification Pending |
| `NotificationAuditOutboxRelayBackgroundService` | Relay audit outbox |
| `NotificationDigestBackgroundService` | Gom và gửi digest |
| `NotificationDlqMonitorBackgroundService` | Theo dõi DLQ/metric; không tự replay business message |
| `ExpoReceiptReconcileBackgroundService` | Kiểm tra receipt và deactivate token lỗi |
| `NotificationFallbackBackgroundService` | Push critical thất bại sang SMS theo policy |
| `NotificationRetentionBackgroundService` | Soft-delete notification cũ |

Expo receipt worker mặc định poll khoảng 300 giây và chỉ reconcile receipt đủ tuổi tối thiểu khoảng 15 giây. Retention mặc định xử lý dữ liệu cũ khoảng 90 ngày.

### 6.11. Audit relay và replay

- Các hành động quan trọng ghi `NotificationAuditLog` và `NotificationAuditOutbox` cùng phía NotificationService.
- Audit relay poll 2 giây, lấy tối đa 50 hàng, thử tối đa 5 lần rồi publish `AuditCreatedEventV1` cho read store/audit aggregator.
- Redis distributed lease chọn một leader; nếu Redis lỗi, worker fail-open và vẫn chạy để tránh bỏ toàn bộ audit, nên consumer/read store phía sau phải chịu được event trùng.
- `NotificationAuditReplayRequestedConsumer` kế thừa replay base, đọc chính `NotificationAuditOutbox` làm source of truth và phát lại lịch sử của service khi AuditAggregator yêu cầu.
- Consumer replay là consumer runtime bổ sung ngoài 37 khai báo explicit ở thư mục Application. Nó không tạo một `Notification` cho người dùng, vì vậy không xuất hiện trong bảng routing nghiệp vụ ở mục 5.

## 7. API NotificationService

### 7.1. API người dùng

| Method | Route | Công dụng |
|---|---|---|
| GET | `/api/notifications` | Feed phân trang/lọc của user |
| GET | `/api/notifications/{id}` | Chi tiết notification thuộc user |
| PATCH | `/api/notifications/{id}/read` | Đánh dấu đã đọc |
| PATCH | `/api/notifications/{id}/opened` | Đánh dấu đã mở |
| POST | `/api/notifications/read-all` | Đánh dấu toàn bộ đã đọc |
| GET | `/api/notifications/unread-count` | Đếm InApp chưa đọc |
| POST | `/api/notifications` | Admin tạo một notification thủ công |

### 7.2. Device token

| Method | Route | Công dụng |
|---|---|---|
| POST | `/api/device-tokens` | Đăng ký/upsert token thiết bị |
| DELETE | `/api/device-tokens` | Hủy/deactivate token |
| GET | `/api/device-tokens` | Xem token của user hiện tại |

### 7.3. Preference

| Method | Route | Công dụng |
|---|---|---|
| GET | `/api/notification-preferences` | Lấy preference tổng, quiet hours, digest |
| PUT | `/api/notification-preferences` | Cập nhật preference tổng |
| GET | `/api/notification-preferences/matrix` | Lấy ma trận category × channel |
| PUT | `/api/notification-preferences/matrix` | Cập nhật ma trận |
| GET | `/api/notification-preferences/categories` | Danh mục category hỗ trợ |

### 7.4. Unsubscribe công khai

| Method | Route | Công dụng |
|---|---|---|
| GET | `/api/notification-unsubscribe` | Xem trước thông tin unsubscribe từ token |
| POST | `/api/notification-unsubscribe` | Thực hiện unsubscribe |

Token được ký HMAC; endpoint không dựa vào việc client đang giữ phiên đăng nhập.

### 7.5. Admin group

Base route: `/api/admin/notification-groups`.

- List/detail/create/update/delete group.
- List/add/remove member của nhóm Static.
- Role group là system-managed và membership được resolve tự động.

### 7.6. Admin template

Base route: `/api/admin/notification-templates`.

- List và detail.
- `GET /variables` và `GET /coverage`.
- Create, revise bằng `PUT`, delete.
- Preview, test-send và activate một revision.
- Test-send hiện tập trung vào Email cho chính Admin và có quota khoảng 5 lần/giờ.

### 7.7. Admin broadcast/batch

Base route: `/api/admin/notifications`.

- `POST /broadcast/preview`.
- `POST /broadcast/template-preview`.
- `POST /broadcast`.
- `GET /batches`.
- `GET /batches/{id}`.

### 7.8. Admin runtime setting

- `GET /api/admin/notification-settings/push-transport`.
- `PUT /api/admin/notification-settings/push-transport`.

### 7.9. SignalR hub

- Route: `/hubs/notifications`.
- User được đưa vào group riêng theo user ID.
- JWT cho hub có thể đi qua query parameter khi client không gửi Authorization header theo cách HTTP thông thường.

Controller tham khảo: [`NotificationService.Api/Controllers`](services/NotificationService/src/NotificationService.Api/Controllers).

## 8. Hệ thống template

### 8.1. 16 template HTML nhúng của NotificationService

1. `admin-invite.html`.
2. `alert-ticket-saga-failed.html`.
3. `battery-alert-critical.html`.
4. `battery-alert-escalation-pending.html`.
5. `environmental-incident-detected.html`.
6. `environmental-incident-resolved.html`.
7. `incident-declared.html`.
8. `password-reset.html`.
9. `sla-breach.html`.
10. `sla-warning.html`.
11. `ticket-approved.html`.
12. `ticket-assigned-customer.html`.
13. `ticket-assigned-staff.html`.
14. `ticket-created.html`.
15. `ticket-resolved.html`.
16. `welcome-customer.html`.

Thư mục: [`NotificationService.Application/Templates`](services/NotificationService/src/NotificationService.Application/Templates).

Không phải type nào cũng có một file HTML riêng. Nhiều type dùng inline fallback hoặc DB template được seed từ catalog.

### 8.2. Version và activation

- Khóa logic là notification type × channel.
- Có thể tồn tại nhiều version.
- Chỉ active template được dispatcher ưu tiên.
- Revise không nên ghi đè lịch sử cũ; nó tạo phiên bản mới.
- Preview dùng sample model/biến hợp lệ.
- Coverage endpoint chỉ ra cặp type/channel nào có hoặc thiếu template.

### 8.3. EmailService có hệ template riêng

EmailService định nghĩa tám loại:

1. `AdminInvite`.
2. `NotificationGeneric`.
3. `OtpEmailChange`.
4. `OtpPasswordReset`.
5. `OtpRegister`.
6. `RefreshTokenReuse`.
7. `SuspiciousLogin`.
8. `TwoFactorCrossDeviceConfirm`.

Nguồn: [`EmailTemplates.cs`](services/EmailService/src/EmailService.Infrastructure/Templates/EmailTemplates.cs).

Các email OTP/security được AuthService yêu cầu trực tiếp qua EmailService. Chúng không đi qua preference, quiet hours hoặc digest của NotificationService — đây là chủ ý hợp lý cho email xác thực/bảo mật.

## 9. Knowledge Base trong phạm vi Sprint 6

Knowledge Base không nằm trong NotificationService; code hiện ở TicketService. Nó vẫn được tổng hợp vì Sprint 6 trong `overall.md` đưa KB vào cùng phạm vi bàn giao.

### 9.1. Domain model

**`KnowledgeBaseArticle`**

- `Code`, `Category`, `Title`.
- `Content` dạng JSONB.
- Tags.
- Status: Draft, PendingReview, Published, Archived.
- `IsTemplate`.
- Version hiện tại.
- View count/helpful count.
- Người tạo/reviewer, lý do reject và audit fields.

**`KbArticleVersion`**

- Snapshot nội dung một version.
- Major/minor.
- Trạng thái version.
- Thông tin author/review.

**`TicketKbReference`**

- Ticket, article, article code, user.
- Reference type.
- Note.
- Có thể gắn với chat.

Nguồn:

- [`KnowledgeBaseArticle.cs`](services/TicketService/src/TicketService.Domain/Entities/KnowledgeBaseArticle.cs)
- [`KbArticleVersion.cs`](services/TicketService/src/TicketService.Domain/Entities/KbArticleVersion.cs)
- [`TicketKbReference.cs`](services/TicketService/src/TicketService.Domain/Entities/TicketKbReference.cs)

### 9.2. Vòng đời bài viết

```text
Tạo thường
  └─ Article PendingReview + pending version 1.0

Tạo từ template
  └─ Article Draft

Reviewer approve pending version
  └─ Copy snapshot vào article, nhưng code hiện đặt article về Draft

Publish
  └─ Article Published

Reject
  └─ Published nếu còn bản cũ đã publish, nếu không về Draft

Archive / Rollback / Delete
  └─ Theo endpoint và role tương ứng
```

Điểm rất quan trọng: **approve review không đồng nghĩa publish trong code hiện tại**. Sau approve còn bước publish riêng.

### 9.3. Phân quyền và endpoint

**`/api/knowledge-base` — Staff, Manager, Admin**

- `GET /`: list/filter.
- `GET /{id}`: detail.
- `GET /suggest`: gợi ý.
- `POST /{id}/helpful`: đánh dấu hữu ích.
- `GET /{id}/usage-stats`: Manager/Admin.

Comment ở một số nơi nói customer-facing, nhưng controller hiện **không cho Customer** vì class-level authorize chỉ gồm Staff, Manager, Admin.

**`/api/internal/knowledge-base` — Staff, Manager, Admin**

- Create/update.
- List version, lấy version detail, compare.
- List/detail template.
- Copy template.
- Duplicate article.

**`/api/admin/knowledge-base` — Manager, Admin**

- Publish, archive, approve review, reject review, rollback.
- Delete chỉ Admin.

**`/api/admin/knowledge-base/templates` — Admin**

- CRUD và lifecycle template KB.

**`/api/knowledge-base/references` — Staff, Manager, Admin**

- Add/list/delete ticket–KB reference.

Ngoài ra chat có command attach KB reference, convert chat thành KB draft và query gợi ý KB.

### 9.4. Hành vi tìm kiếm và gợi ý

- List có filter status/category/template và text query.
- Text query hiện chỉ match title, chưa tìm trong content/symptom.
- Suggest chọn bài Published cùng category, xếp theo helpful/view và giới hạn tối đa 5.
- Suggest hiện không loại `IsTemplate`, vì vậy template Published có thể lọt vào gợi ý.
- Get detail hiện không tăng `ViewCount`.

### 9.5. Mã bài và seed

- Mã được tạo dạng `KB-{năm}-{4 chữ số}`.
- Generator đọc mã cuối rồi cộng một. Không có khóa/sequence bảo vệ hai request đồng thời, nên có race condition.
- Luồng seeder được gọi thật tạo **3 bài legacy**.
- Cùng file có method riêng chuẩn bị **5 bài `KB-2026-*`**, nhưng method đó không được gọi; không được coi 5 bài này là dữ liệu seed runtime.

Nguồn: [`TicketDataSeeder.cs`](services/TicketService/src/TicketService.Infrastructure/Persistence/Seeders/TicketDataSeeder.cs) và [`KbCodeGenerator.cs`](services/TicketService/src/TicketService.Infrastructure/Implements/Utils/KbCodeGenerator.cs).

## 10. Các sai lệch và vấn đề đã xác minh

Mức độ bên dưới đánh giá theo khả năng làm mất/sai notification, sai phân quyền/nghiệp vụ hoặc gây khó vận hành. Đây là danh sách ưu tiên xử lý, không phải khẳng định mọi mục đã gây incident production.

### 10.1. Notification — rất cao

#### N-01. TicketService Outbox relay thiếu mapping cho 11 event mà NotificationService đang consume

**Hiện trạng**

Outbox writer lưu tên type ngắn. Relay phải có mapping từ tên đó về CLR type để deserialize/publish. Relay hiện thiếu:

1. `TicketAssignedEvent`.
2. `TicketResolvedEvent`.
3. `TicketStatusChangedEvent`.
4. `TicketApprovedEvent`.
5. `TicketRejectedEvent`.
6. `TicketClosedEvent`.
7. `TicketReopenedEvent`.
8. `IncidentDeclaredEvent`.
9. `SlaWarningEvent`.
10. `SlaBreachedEvent`.
11. `SlaAutoResumedEvent`.

**Tác động**

Nếu các handler ghi những event này vào TicketService outbox, relay gặp type không biết, đánh dấu outbox failed và không publish sang RabbitMQ. Consumer NotificationService có code đúng vẫn không nhận được event; đây là đường có thể làm mất notification ở ranh giới service.

**Nguồn**

- [`IntegrationEventOutboxWriter.cs`](services/TicketService/src/TicketService.Infrastructure/Implements/Services/IntegrationEventOutboxWriter.cs)
- [`OutboxRelayService.cs`](services/TicketService/src/TicketService.Infrastructure/Implements/Services/OutboxRelayService.cs)

Các event đang có mapping và đi được gồm TicketCreated/Escalated/Merged, nhóm chat/blog và một số event khác. Auto-close/rating có đường publish riêng nên cần đánh giá theo từng producer, không suy rộng rằng toàn bộ TicketService đều hỏng.

#### N-02. Xóa account chưa deactivate device token

`AccountDeletedSyncConsumer` chỉ đánh dấu `AccountReadModel` inactive/soft-deleted. Nó không deactivate các `DeviceToken` đã lưu.

**Tác động**

Recipient resolver mới tránh chọn account đã xóa, nhưng notification cũ đang Pending hoặc đường gửi trực tiếp theo ID vẫn có thể còn token active. Cần policy rõ: xóa account phải vô hiệu token ngay, hủy hàng Pending hay chỉ chặn ở dispatch.

#### N-03. Seeder dữ liệu demo Notification chạy không bị giới hạn môi trường

`Program.cs` gọi `NotificationDataSeeder` không chỉ trong Development. Seeder tạo dữ liệu mẫu, gồm preference gắn với user ID sinh ngẫu nhiên, token giả và notification mẫu có thể có trạng thái Pending.

**Tác động**

Production/staging có nguy cơ bị dữ liệu giả làm nhiễu dashboard, tạo account ID không thuộc AuthService và khiến worker thử xử lý notification/token mẫu.

Nguồn:

- [`Program.cs`](services/NotificationService/src/NotificationService.Api/Program.cs)
- [`NotificationDataSeeder.cs`](services/NotificationService/src/NotificationService.Infrastructure/Persistence/Seeders/NotificationDataSeeder.cs)

### 10.2. Notification — cao

#### N-04. Automatic event fan-out chưa dùng batch helper

`WriteBatchedAsync` có tồn tại nhưng không có call site. Event fan-out không có `BatchId`, làm suy yếu correlation và khiến sibling-state phải dựa vào heuristic thời gian.

#### N-05. `SlaAutoResumed` thiếu template metadata — **đã khắc phục 10/08/2026**

Type và consumer đã có từ trước. Đợt sửa ngày 10/08/2026 đã bổ sung dispatch matrix, catalog biến và template seed cho InApp + Push.

**Kết quả:** Admin có thể quản lý/đánh giá coverage nhất quán; type không còn buộc rơi về inline fallback vì thiếu metadata.

#### N-06. Dispatch matrix lệch routing consumer

| Type | Matrix hiện khai báo | Consumer thực tế |
|---|---|---|
| `EnvironmentalIncidentResolved` | InApp + Push | InApp |
| `IncidentDeclared` | InApp + Push + Email + SMS | InApp + Push |
| `AccountActivated` | InApp + Email | InApp |
| `BatteryAlertEscalationPending` | InApp + Push | InApp + Push + Email |
| `AlertTicketSagaFailed` | InApp + Push | InApp + Push + Email |

Hiện consumer tự đưa channel vào `NotificationWriter`; `NotificationDispatchOptions.DispatchAsync` không có caller. Vì vậy matrix chưa trực tiếp sửa routing runtime của các event trên, nhưng nó ảnh hưởng seed/coverage, code legacy và cách người vận hành hiểu hệ thống.

#### N-07. Cờ chat đã được lưu nhưng chưa tham gia policy

Một số flag/payload chat được persist nhưng dispatcher dựa vào tập type hard-code để bypass. Hiện chỉ `ChatCreated` và `ChatMentioned` nằm trong `RealtimeConversationTypes`; `ChatReacted` và ba participant-change type vẫn qua digest/rate/quiet nếu không có rule khác. Cần xác nhận đây là chủ ý, rồi sử dụng/xóa các flag để tránh tạo ảo giác policy chi tiết hơn thực tế.

#### N-08. `read-all` cập nhật quá rộng

Handler read-all không chỉ giới hạn InApp/unread; nó có thể chuyển các row channel khác và trạng thái Pending/Processing/Failed sang Read. Trong khi unread count chỉ tính InApp.

**Tác động:** có thể hủy ý nghĩa queue/retry hoặc che failure chỉ vì user bấm “đọc tất cả”. Nên giới hạn InApp và các trạng thái feed hợp lệ.

#### N-09. Đồng bộ sibling dựa trên cửa sổ ±1 phút

Khi không có `BatchId`, mark read/opened tìm row cùng user/type/entity trong khoảng thời gian gần nhau.

**Tác động:** hai event thật khác nhau nhưng cùng type/entity phát gần nhau có thể bị coi là sibling và đổi trạng thái cùng lúc.

### 10.3. Notification — trung bình

#### N-10. Retention query có thể lấy cả Processing

Comment nói retention chỉ xử lý terminal state, nhưng filter chủ yếu loại Pending; Processing có thể lọt vào nếu đủ cũ.

#### N-11. SignalR success không phải delivery proof

Không có client acknowledgement; gửi vào group rỗng hoặc client đã mất mạng vẫn có thể được coi Sent. Đây là giới hạn thiết kế, đặc biệt nguy hiểm nếu Admin chọn pure SignalR cho push.

#### N-12. Fallback SMS chưa được chứng minh E2E

Code và unit test của fallback có tồn tại, nhưng audit này chưa chạy provider integration để chứng minh Expo failure/receipt failure thật sự dẫn tới SMS đúng một lần trong deployment.

#### N-13. Có integration contract không có Notification consumer

Các contract được thấy ở producer/shared nhưng không có consumer tạo notification tương ứng gồm:

- `BatteryAssetCreatedEvent`.
- `BatteryAssetTransferredEvent`.
- `AlertLinkedToTicketEvent`.
- `AlertLinkToTicketRejectedEvent`.
- `ChatEditedEvent`.
- `ChatDeletedEvent`.

Đây chưa chắc là bug: có thể nghiệp vụ không cần thông báo. Cần product owner xác nhận để tránh coi “event tồn tại” đồng nghĩa “phải có notification”.

#### N-14. Catalog mang tên/comment tiếng Việt nhưng seed content tiếng Anh

Ảnh hưởng chính là kỳ vọng localization và tính nhất quán nội dung, không phải lỗi dispatch.

#### N-15. Entry `TicketMerged` lặp trong category map — **đã khắc phục 10/08/2026**

Entry lặp đã bị xóa. Với dictionary initializer, key lặp có thể ném lỗi khi static map được khởi tạo lần đầu; vì vậy đây không chỉ là noise bảo trì. Unit test category coverage hiện xanh.

### 10.4. Knowledge Base

#### KB-01. Approve review không publish

Handler approve copy pending version vào article nhưng đặt status `Draft`; tài liệu/mô tả API dễ làm người đọc hiểu approve là Published. Runtime yêu cầu bước publish riêng.

#### KB-02. Route có article ID nhưng version detail không scope theo article

`GET /api/internal/knowledge-base/{id}/versions/{versionId}` truyền `versionId`; handler lấy version theo ID mà không xác minh version thuộc article `{id}`.

**Tác động:** caller có thể dùng article A trên route nhưng đọc version của article B nếu biết ID.

#### KB-03. Compare version không ràng buộc cả hai version thuộc article route

Có nguy cơ compare chéo bài viết, làm kết quả sai ngữ cảnh hoặc lộ metadata/nội dung ngoài article được yêu cầu.

#### KB-04. Rollback không scope version vào article

Rollback nhận article và version nhưng cần xác minh version ownership; code hiện thiếu ràng buộc đầy đủ.

#### KB-05. Guard khi copy template bị comment/bỏ qua

Một số kiểm tra template/status dự kiến có trong code nhưng bị comment, làm thao tác copy chấp nhận đầu vào rộng hơn tên endpoint thể hiện.

#### KB-06. Xem detail không tăng `ViewCount`

Thống kê view và thuật toán suggestion dùng view, nhưng query detail không ghi nhận lượt xem. View count do đó không phản ánh việc đọc qua endpoint này.

#### KB-07. Suggestion có thể trả về template

Filter Published/category không loại `IsTemplate`.

#### KB-08. Search query chỉ tìm title

Nếu tài liệu mô tả tìm theo symptom/content/tags thì source hiện chưa đạt: text query đang match title.

#### KB-09. Seed thực tế và method seed chết lệch nhau

Runtime seeder gọi method tạo 3 bài; method tạo 5 bài `KB-2026-*` tồn tại nhưng không được gọi.

#### KB-10. Lookup reference không khớp unique key DB

DB unique theo `ticket + article + reference type`, nhưng handler kiểm tra existing chủ yếu theo `ticket + article`.

**Tác động:** hai reference type hợp lệ theo schema có thể bị application coi là trùng và collapse.

#### KB-11. Sinh mã KB có race condition

Đọc mã cuối + 1 không atomically serialize concurrent create. Hai request đồng thời có thể cùng sinh một code, rồi một request lỗi unique constraint hoặc gây retry không thân thiện.

Nguồn handler chính: [`KnowledgeBase handlers`](services/TicketService/src/TicketService.Application/CQRS/Handler/KnowledgeBase) và [`Ticket KB reference handlers`](services/TicketService/src/TicketService.Application/CQRS/Handler/TicketKbReferences).

## 11. Điểm cần đặc biệt lưu ý khi tích hợp client

### Web/mobile feed

- Feed và unread count là khái niệm InApp; không nên hiển thị trực tiếp mọi row Email/SMS/Push.
- Client phải xử lý `NotificationCreated`, `NotificationReceived`, `UnreadCountChanged` theo contract hiện tại.
- Realtime payload có enum dạng số; không tự đổi ordinal enum ở backend hoặc hard-code khác frontend.
- Mark opened và mark read có ý nghĩa khác nhau.
- Client nên deduplicate nếu deployment dùng push transport `Both`.

### Deep link/navigation

Payload có entity type/id và metadata. Client phải kiểm tra field tồn tại và quyền truy cập trước khi điều hướng; notification không thay thế authorization của endpoint đích.

### Device token

- Register token khi login/refresh hoặc khi Expo token thay đổi.
- Deactivate khi logout.
- Account deletion hiện chưa tự deactivate token, nên đây là phần cần sửa backend hoặc bù ở client/identity flow.

### Unsubscribe

- Link email là public nhưng có HMAC.
- Không log token đầy đủ.
- Email bảo mật/OTP không nên bị unsubscribe như marketing/notification thông thường.

## 12. Kiểm thử đã chạy và mức độ tin cậy

### 12.1. Kết quả tại thời điểm audit ban đầu

| Project/bộ test | Kết quả |
|---|---|
| NotificationService UnitTests | **692/692 pass** |
| TicketService UnitTests | **1006 pass, 1 fail trên 1007** |
| `OutboxRelayServiceTests` targeted | **7/7 pass** |
| Knowledge Base/KbWorkflow targeted | **23/23 pass** |

Lỗi TicketService duy nhất quan sát được nằm ở test Blog compare message, khác biệt hoa/thường giữa `"Version 1 not found."` và expected chứa `"version 1"`. Nó không phải bằng chứng Notification/KB runtime hỏng, nhưng làm full TicketService suite chưa xanh hoàn toàn.

### 12.2. Kết quả kiểm chứng sau đợt sửa IoT offline spam

| Project/bộ kiểm tra | Kết quả ngày 10/08/2026 |
|---|---|
| BatteryService UnitTests | **633/633 pass** |
| BatteryService IntegrationTests | **56/56 pass** |
| NotificationService UnitTests | **698/698 pass** |
| BatteryService.Api build | **succeeded, 0 error**; còn 2 XML-doc warning có sẵn ngoài phạm vi sửa |
| NotificationService.Api build | **succeeded, 0 warning, 0 error** |
| Migration `AddIotDeviceOfflineIncidentGuard` | **Up + unique guard + Resolved history + Down đều pass** trên PostgreSQL tạm biệt lập; EF báo **No changes have been made to the model since the last migration** |
| Frontend TypeScript | `tsc --noEmit --incremental false -p tsconfig.app.json` **pass** |
| Frontend production build | `npm run build` **pass**; chỉ còn cảnh báo bundle > 500 kB đã tồn tại |
| ESP32 firmware | `pio run -d firmware-esp32` **SUCCESS**; RAM 24,7%, flash 18,7%; chỉ có warning từ thư viện OneWire bên thứ ba |
| Whitespace/diff integrity | `git diff --check` **pass** ở backend, frontend và IoT repository |

Battery integration suite lúc chạy lần đầu sau khi đổi fixture sang SQLite đã bắt được một FK seed còn thiếu trong test MQTT telemetry. Fixture đã được sửa, sau đó **chạy lại toàn bộ 56 test** và không còn failure.

### 12.3. Điều test hiện tại chưa chứng minh

- `OutboxRelayServiceTests` pass nhưng chưa assert rằng **mọi** SharedContract được writer ghi đều có mapping relay; vì vậy không bắt được N-01.
- Unit test không thay thế RabbitMQ/PostgreSQL/Redis/Expo/SignalR/Mailjet integration test.
- Chưa chạy một Docker E2E đầy đủ producer → outbox → RabbitMQ → Notification → provider trong audit này.
- Chưa chứng minh pure SignalR có delivery semantics tương đương push notification.
- Chưa chứng minh fallback SMS đúng một lần với lỗi provider thật.
- Các claim frontend/mobile trong `overall.md` không thể xác minh hoàn toàn chỉ từ backend repository này.
- Số test lịch sử được ghi trong `overall.md` không được dùng thay cho kết quả test working tree hiện tại.

## 13. Thứ tự khuyến nghị xử lý

### P0 — tránh mất hoặc gửi sai notification

1. Bổ sung registry/mapping TicketService Outbox cho 11 event và thêm test “mọi event writer hỗ trợ đều relay được”.
2. Giới hạn `NotificationDataSeeder` theo Development/config explicit.
3. Khi account deleted: deactivate device token và quyết định xử lý hàng Pending.

### P1 — làm trạng thái và cấu hình khớp runtime

4. Sửa read-all chỉ áp dụng InApp và trạng thái feed hợp lệ.
5. Dùng correlation/batch ID thật cho event fan-out, bỏ heuristic ±1 phút khi có thể.
6. Đồng bộ dispatch matrix với consumer routing.
7. ~~Bổ sung metadata/template coverage cho `SlaAutoResumed`.~~ **Đã xong 10/08/2026.**
8. Chốt semantics pure SignalR/Both và cơ chế client dedup/ack.

### P2 — độ chính xác Knowledge Base

9. Scope version detail/compare/rollback theo article ID.
10. Chốt workflow approve → Draft hay approve → Published và sửa docs/UI theo quyết định.
11. Loại template khỏi suggestion, tăng view count có kiểm soát và mở rộng search nếu đúng yêu cầu.
12. Đồng bộ uniqueness lookup của reference với DB constraint.
13. Thay sinh code “last + 1” bằng sequence/DB-safe allocation.

### P3 — bảo trì và tính rõ ràng

14. Xóa dead seeder/method hoặc nối đúng luồng.
15. Sửa naming/localization catalog.
16. ~~Xóa duplicate category entry.~~ **Đã xong 10/08/2026**; các flag persist nhưng chưa dùng vẫn cần quyết định nghiệp vụ riêng.
17. Cập nhật các đoạn `overall.md` cũ còn ghi 29 consumer hoặc Sprint 6.4 chưa implement.

## 14. Checklist nghiệm thu đề xuất

### Event delivery

- [ ] Mỗi event producer có test từ outbox type name tới publish CLR type.
- [ ] Mỗi notification consumer có test recipient, channel, title/body fallback và dedup.
- [ ] Account inactive/deleted không còn nhận notification mới.
- [ ] Event critical vẫn tôn trọng preference, đồng thời bypass quiet/rate/digest đúng yêu cầu.

### Dispatch

- [ ] Hai worker không claim cùng một row.
- [ ] Retry không gửi quá số lần và Processing timeout được recovery.
- [ ] Email/SMS failure cuối cập nhật đúng row gốc.
- [ ] Expo receipt error deactivate đúng token.
- [ ] Push fallback tạo tối đa một SMS hợp lệ.
- [ ] `Both` không làm UI hiển thị trùng.

### API và realtime

- [ ] Feed chỉ trả row đúng user và đúng phạm vi InApp.
- [ ] Read/opened/read-all không phá Pending/Processing/Failed của channel khác.
- [ ] Hub authorization và query-token không cho giả mạo user.
- [ ] Client xử lý numeric enum đúng contract.

### Template

- [ ] Mọi type/channel route thực tế xuất hiện trong coverage.
- [ ] Placeholder lạ bị từ chối; dữ liệu HTML được escape.
- [ ] DB active template thắng inline fallback.
- [ ] Manual batch `UseTemplate = false` giữ đúng nội dung Admin nhập.
- [ ] Test-send bị quota và chỉ gửi tới Admin hiện tại.

### Knowledge Base

- [ ] Mọi version operation xác minh version thuộc article route.
- [ ] Workflow approve/publish khớp tài liệu và UI.
- [ ] Suggest không trả template nếu đó là yêu cầu nghiệp vụ.
- [ ] Code allocation chịu được create đồng thời.
- [ ] Reference duplicate rule ở handler khớp unique constraint.

## 15. Bản đồ source để người đọc tra cứu

| Khu vực | Đường dẫn |
|---|---|
| Sprint/backlog | [`overall.md`](overall.md) |
| Notification domain | [`NotificationService.Domain`](services/NotificationService/src/NotificationService.Domain) |
| Event consumers | [`Consumers`](services/NotificationService/src/NotificationService.Application/Consumers) |
| Notification writer | [`NotificationWriter.cs`](services/NotificationService/src/NotificationService.Application/Consumers/NotificationWriter.cs) |
| Dispatch policy | [`NotificationDispatchOptions.cs`](services/NotificationService/src/NotificationService.Application/Services/NotificationDispatchOptions.cs) |
| Dispatcher | [`NotificationDispatcher.cs`](services/NotificationService/src/NotificationService.Infrastructure/Services/NotificationDispatcher.cs) |
| Background jobs | [`BackgroundJobs`](services/NotificationService/src/NotificationService.Infrastructure/BackgroundJobs) |
| Push/realtime | [`Realtime`](services/NotificationService/src/NotificationService.Infrastructure/Realtime) |
| Notification API | [`Controllers`](services/NotificationService/src/NotificationService.Api/Controllers) |
| Embedded templates | [`Templates`](services/NotificationService/src/NotificationService.Application/Templates) |
| Template DB catalog | [`NotificationTemplateCatalog.cs`](services/NotificationService/src/NotificationService.Infrastructure/Persistence/Seeders/NotificationTemplateCatalog.cs) |
| Notification tests | [`NotificationService.UnitTests`](services/NotificationService/tests/NotificationService.UnitTests) |
| Ticket outbox writer | [`IntegrationEventOutboxWriter.cs`](services/TicketService/src/TicketService.Infrastructure/Implements/Services/IntegrationEventOutboxWriter.cs) |
| Ticket outbox relay | [`OutboxRelayService.cs`](services/TicketService/src/TicketService.Infrastructure/Implements/Services/OutboxRelayService.cs) |
| KB controllers | [`TicketService.Api/Controllers`](services/TicketService/src/TicketService.Api/Controllers) |
| KB commands/queries | [`KnowledgeBase CQRS`](services/TicketService/src/TicketService.Application/CQRS) |
| KB entities | [`TicketService.Domain/Entities`](services/TicketService/src/TicketService.Domain/Entities) |
| Email templates | [`EmailService Templates`](services/EmailService/src/EmailService.Infrastructure/Templates) |

## 16. Kết luận bàn giao

Hệ thống hiện có phạm vi rộng hơn một notification bell thông thường: event-driven fan-out, bốn channel, preference matrix, quiet/digest/rate limit, retry/atomic claim, template versioning, audience/broadcast, SignalR/Expo runtime switch, audit, metrics và Knowledge Base có workflow/version.

Phần lớn cấu trúc NotificationService đã khá đầy đủ và bộ unit test của service đang xanh. Tuy nhiên, điểm quan trọng nhất không nằm trong consumer mà ở **đường producer/outbox**: 11 Ticket/SLA event chưa có mapping relay có thể khiến notification không bao giờ tới service. Sau đó là seeder không khóa môi trường, account deletion chưa vô hiệu token, read-all cập nhật quá rộng, batch/correlation tự động chưa được dùng và một số mismatch matrix/template.

Với Knowledge Base, chức năng CRUD/version/review/reference đã có thật, nhưng các thao tác version cần ràng buộc ownership theo article và workflow approve/publish phải được diễn đạt thống nhất. Người tiếp nhận nên ưu tiên checklist P0/P1 trước khi coi toàn bộ Sprint 6–6.6 đã sẵn sàng production.

## 17. Cập nhật khắc phục IoT offline spam — 10/08/2026

### 17.1. Kết luận xác minh incident

Hiện tượng thiết bị kết nối chập chờn tạo notification `IotDeviceWentOffline` lặp là **bug có thật**. Nhận định của frontend đúng ở điểm một gói telemetry đơn lẻ từng có thể đưa device từ `Offline` về `Active`, trong khi background detector lại đưa nó về `Offline` sau khoảng im lặng kế tiếp. Tuy nhiên, nguyên nhân đầy đủ rộng hơn mô tả ban đầu:

1. Background poller và MQTT LWT từng có các đường chuyển `Active → Offline` độc lập.
2. Heartbeat/telemetry/MQTT online từng có thể chuyển `Offline → Active` quá dễ.
3. `DedupWindowEndUtc` được ghi nhưng không phải hàng rào duy nhất theo device và không có unique constraint ở DB.
4. MQTT status parsing quá rộng và LWT retained/stale có thể tham gia đổi trạng thái.
5. Firmware reconnect theo nhịp cố định làm thiết bị flapping tiếp tục tạo các phiên ngắn.
6. Notification chỉ hiển thị kết quả; frontend không phải nguồn tạo incident, nhưng reconnect/invalidation cũ có thể gây request thừa và làm trải nghiệm trông nhiễu hơn.

`OfflineCheckIntervalSeconds = 120` là mặc định poller, nên câu “chính xác mỗi phút một notification” không thể suy ra chỉ từ default trong code. Nhịp quan sát thực tế còn phụ thuộc cấu hình môi trường, nhiều device lệch pha, MQTT LWT và chu kỳ reconnect.

### 17.2. Toàn bộ lỗi đã sửa trong chuỗi IoT → Battery → Notification → Frontend

| ID | Lỗi trước sửa | Cách khắc phục | Tác dụng |
|---|---|---|---|
| IOT-NOTI-01 | Poller và MQTT tự ghi trạng thái offline theo hai luật khác nhau | Cả hai gọi `IotDeviceOfflineDetectionService.TryMarkOfflineAsync` | Chỉ còn một policy `Active → Offline` |
| IOT-NOTI-02 | Hai replica/nguồn có thể cùng nhìn thấy `Active` | Claim nguyên tử bằng `ExecuteUpdateAsync`, ràng buộc cả `Status` và snapshot `LastSeenAt` | Chỉ một writer thắng race |
| IOT-NOTI-03 | Alert offline không có khóa định danh device; dedup window không đủ chống race | Thêm `Alert.IotDeviceId`, FK và filtered unique index cho `DeviceOffline + Open/Acknowledged` | Tối đa một incident offline chưa xử lý trên mỗi device |
| IOT-NOTI-04 | Có thể tạo alert theo asset hoặc chọn asset tùy tiện | Tạo đúng một alert cấp device; số asset ảnh hưởng lấy từ calibration, fallback theo site | Không nhân notification theo số battery và không gắn sai asset đại diện |
| IOT-NOTI-05 | Một reading/heartbeat lẻ tẻ đủ “hồi sinh” device | Dùng chung `IotDeviceAvailabilityService`; device Offline cần hai healthy signal liên tiếp sau lần offline, trong cửa sổ hợp lệ | Chống flapping `Active ↔ Offline` |
| IOT-NOTI-06 | MQTT status dùng so khớp rộng, không phân biệt retained/stale | Chỉ nhận payload chính xác `online`/`offline`; LWT offline vẫn phải vượt freshness/grace check | Retained hoặc LWT đến muộn không bypass detector |
| IOT-NOTI-07 | Ngưỡng toàn cục có thể ngắn hơn heartbeat cadence riêng của device | Ngưỡng hiệu lực = `max(configured offline, heartbeat interval + 30s, 15s)` | Device heartbeat chậm hợp lệ không bị kết luận offline sớm |
| IOT-NOTI-08 | `Take(batchSize)` trước khi lọc cadence có thể làm candidate hợp lệ phía sau bị starvation | Scan có thứ tự theo trang, lọc cadence từng device, giới hạn batch 1..1000 | Không bỏ đói candidate và không nạp toàn bảng vào RAM |
| IOT-NOTI-09 | Recovery không đóng incident cũ và không có thông báo phục hồi | Resolve mọi offline alert chưa xử lý của device và phát `IotDeviceRecoveredEvent` | Vòng đời incident đóng đúng; lần offline thật tiếp theo có thể mở incident mới |
| IOT-NOTI-10 | Offline notification chỉ hướng tới operations hạn chế, thiếu chủ site | Offline/recovery gửi Manager, Admin, Staff và `CustomerId` của site; loại recipient trùng | Đúng audience vận hành và khách hàng bị ảnh hưởng |
| IOT-NOTI-11 | Dedupe chỉ dựa MessageId không chặn hai message broker khác nhau cho cùng incident | Thêm business-key dedupe theo `AlertId` (fallback device ID), lease ngắn và chỉ nâng TTL 7 ngày sau khi xử lý thành công | Retry lỗi không bị nuốt; cùng incident không tạo notification lặp |
| IOT-NOTI-12 | Auto-decommission dùng semantics cảnh báo không tách bạch lỗi kết nối | Thêm anomaly `IotDataIntegrityViolation = 17`, event/type/template `IotDeviceAutoDecommissioned` riêng, critical | Phân biệt rõ thiết bị mất mạng và thiết bị bị vô hiệu vì dữ liệu nguy hiểm |
| IOT-NOTI-13 | Overlap anomaly scan có thể thêm bản ghi `Merged` lặp cho cùng reading | Kiểm tra khóa `(BatteryAssetId, AnomalyType, Reading.Time)` trước khi tạo/merge | Một reading không sinh lại lịch sử anomaly ở lượt scan kế tiếp |
| IOT-NOTI-14 | SignalR client có thể mở retry loop chồng và invalidate feed hai lần cho cùng notification | Serialize reconnect/backoff; `NotificationCreated` là event invalidate feed duy nhất; dispose đúng | Ít kết nối/request thừa và tránh race reconnect |
| IOT-NOTI-15 | Frontend thiếu enum/label/DTO và UI AI prescription chưa nối đủ contract | Đồng bộ type 35/36/37, anomaly 17, `iotDeviceId`, `aiPrescriptionId`; nối regenerate/feedback và hiển thị prescription | UI hiểu đúng payload mới và thao tác AI alert được từ màn hình |
| IOT-NOTI-16 | Staff có hai entry Alerts/Notifications gây chồng chức năng; deep link IoT thiếu Staff | `/staff/alerts` redirect về Inbox; giữ Battery Alerts riêng; thêm Staff deep link IoT | Điều hướng nhất quán, không tạo hai inbox cùng nghĩa |
| IOT-NOTI-17 | ESP32 reconnect nhịp cố định khiến phiên ngắn lặp đều | Exponential backoff 2 giây → 5 phút có jitter; chỉ reset sau phiên ổn định 120 giây | Giảm reconnect storm và giảm flapping từ phía thiết bị |

### 17.3. Hành vi end-to-end sau sửa

```text
Device Active
  │
  ├─ Poller: im lặng >= max(OfflineAfterSeconds, heartbeat+30s, 15s)
  │
  └─ MQTT LWT offline: cũng phải qua cùng detector và grace mặc định 90s
          │
          ▼
Atomic claim Active + LastSeenAt snapshot → Offline
          │
          ├─ tạo/tái sử dụng đúng 1 DeviceOffline alert chưa xử lý
          ├─ phát đúng 1 IotDeviceWentOfflineEvent khi mở incident mới
          └─ NotificationService tạo InApp + Push cho operations + Customer

Device gửi lại tín hiệu
  │
  ├─ tín hiệu khỏe đầu tiên: cập nhật LastSeenAt, vẫn Offline
  └─ tín hiệu khỏe thứ hai đúng cadence: Active
          │
          ├─ resolve offline alert
          └─ phát IotDeviceRecoveredEvent → InApp + Push
```

`Pending → Active` vẫn diễn ra ngay khi provision/heartbeat hợp lệ. Explicit provisioning có thể dùng `forceActivation`; `Disabled` và `Decommissioned` tiếp tục bị chặn. Đây là khác biệt có chủ ý so với recovery của một device đã thực sự offline.

### 17.4. Database và migration

Migration mới:

- [`20260810145022_AddIotDeviceOfflineIncidentGuard.cs`](services/BatteryService/src/BatteryService.Infrastructure/Migrations/20260810145022_AddIotDeviceOfflineIncidentGuard.cs)
- Thêm `alerts.iot_device_id uuid NULL`.
- FK tới `iot_devices.id`, `ON DELETE RESTRICT`.
- Unique index có lọc:

```sql
iot_device_id IS NOT NULL
AND anomaly_type = 7
AND status IN (1, 2)
AND is_deleted = false
```

Index chỉ chặn incident đang `Open` hoặc `Acknowledged`; lịch sử `Resolved` được giữ và không ngăn incident thật trong tương lai. Các alert legacy tạo trước migration có `iot_device_id = NULL` nên không bị tự động suy đoán/backfill sai device; nếu môi trường đã có alert offline legacy dư thừa, cần một đợt cleanup dữ liệu có kiểm duyệt riêng.

### 17.5. Contract và routing mới

| Contract/type | Producer | Recipient | Channel | Dedupe |
|---|---|---|---|---|
| `IotDeviceWentOfflineEvent` / type 18 | BatteryService canonical offline detector | Manager, Admin, Staff, Customer site | InApp + Push | MessageId + AlertId/device |
| `IotDeviceRecoveredEvent` / type 36 | BatteryService availability service | Manager, Admin, Staff, Customer site | InApp + Push | MessageId + AlertId/device |
| `IotDeviceAutoDecommissionedEvent` / type 37 | Sensor ingest outlier policy | Manager, Admin | InApp + Push; critical bypass | MessageId + AlertId/device |

Ba type đều có category `Battery`, dispatch matrix, allowed template variables và template catalog. `SlaAutoResumed = 35` cũng đã được bổ sung coverage trong cùng đợt rà soát.

### 17.6. Cấu hình vận hành liên quan

| Key | Default | Ý nghĩa |
|---|---:|---|
| `Iot:OfflineCheckIntervalSeconds` | 120 | Chu kỳ poll fallback; code clamp tối thiểu 15 giây |
| `Iot:OfflineAfterSeconds` | 300 | Im lặng tối thiểu trước khi offline; còn phải so với heartbeat cadence |
| `Iot:OfflineBatchSize` | theo cấu hình hiện tại | Số device transition tối đa mỗi lượt; clamp 1..1000 |
| `Mqtt:LwtOfflineGraceSeconds` | 90 | LWT không được đánh offline nếu LastSeen còn mới hơn grace |
| Firmware reconnect | 2 giây → 5 phút | Exponential backoff có jitter; reset sau 120 giây ổn định |

Tăng `OfflineAfterSeconds` chỉ là tuning theo mạng thực tế, không còn là hàng rào chống duplicate chính. Không nên hạ các mốc xuống để “nhìn thấy offline nhanh hơn” nếu heartbeat cadence của thiết bị chưa được cấu hình tương ứng.

### 17.7. Bản đồ source của bản sửa

| Khu vực | File chính |
|---|---|
| Canonical offline transition | [`IotDeviceOfflineDetectionService.cs`](services/BatteryService/src/BatteryService.Application/Services/IotDeviceOfflineDetectionService.cs) |
| Shared recovery policy | [`IotDeviceAvailabilityService.cs`](services/BatteryService/src/BatteryService.Application/Services/IotDeviceAvailabilityService.cs) |
| Poller | [`IotDeviceOfflineDetectionBackgroundService.cs`](services/BatteryService/src/BatteryService.Infrastructure/BackgroundServices/IotDeviceOfflineDetectionBackgroundService.cs) |
| MQTT bridge | [`MqttBridgeBackgroundService.cs`](services/BatteryService/src/BatteryService.Infrastructure/Mqtt/MqttBridgeBackgroundService.cs) |
| Telemetry/auto-decommission | [`BatchIngestSensorReadingsCommandHandler.cs`](services/BatteryService/src/BatteryService.Application/CQRS/Handler/SensorReading/BatchIngestSensorReadingsCommandHandler.cs) |
| Alert DB guard | [`AlertConfiguration.cs`](services/BatteryService/src/BatteryService.Infrastructure/Persistence/Configurations/AlertConfiguration.cs) |
| Offline consumer | [`IotDeviceWentOfflineConsumer.cs`](services/NotificationService/src/NotificationService.Application/Consumers/IotDeviceWentOfflineConsumer.cs) |
| Recovery/decommission consumer | [`IotDeviceRecoveredConsumer.cs`](services/NotificationService/src/NotificationService.Application/Consumers/IotDeviceRecoveredConsumer.cs), [`IotDeviceAutoDecommissionedConsumer.cs`](services/NotificationService/src/NotificationService.Application/Consumers/IotDeviceAutoDecommissionedConsumer.cs) |
| Realtime frontend | `/Users/alex/Documents/capstone/frontend/src/shared/hooks/notifications/useNotificationsRealtime.ts` |
| Alert frontend | `/Users/alex/Documents/capstone/frontend/src/shared/components/alerts/AlertsView.tsx` |
| Firmware reconnect | `/Users/alex/Documents/capstone/iot/firmware-esp32/src/net/mqtt_client.cpp` |

### 17.8. Thứ tự triển khai và checklist nghiệm thu incident

1. Backup Battery DB và áp dụng migration `AddIotDeviceOfflineIncidentGuard`.
2. Deploy NotificationService trước để consumer/type mới sẵn sàng nhận recovery/decommission event.
3. Deploy BatteryService; xác nhận `Iot:OfflineAfterSeconds`, heartbeat interval từng device và `Mqtt:LwtOfflineGraceSeconds`.
4. Deploy frontend để nhận enum/deep link/UI mới.
5. Deploy firmware ESP32 để backoff phía edge có hiệu lực.
6. Với một device test: để mất kết nối đủ ngưỡng, xác nhận đúng một alert và một cặp InApp/Push trên mỗi recipient.
7. Gửi một healthy signal, xác nhận device vẫn Offline; gửi signal thứ hai đúng cadence, xác nhận Active + alert Resolved + recovery notification.
8. Lặp lại cùng một `IotDeviceWentOfflineEvent` với MessageId khác, xác nhận business-key dedupe không tạo thêm notification.
9. Theo dõi log/metric `DeviceOfflineRecorded`, auto-decommission và số alert offline đang mở theo `iot_device_id`.

### 17.9. Giới hạn còn lại cần hiểu đúng

- DB guard đảm bảo một offline incident chưa xử lý trên mỗi device trong dữ liệu mới. Alert legacy có `iot_device_id = NULL` cần cleanup riêng nếu muốn dashboard lịch sử tuyệt đối sạch.
- SignalR vẫn không có application-level acknowledgement; “hub send thành công” không chứng minh client đã hiển thị. Đây là giới hạn transport đã nêu ở N-11, không phải nguyên nhân BatteryService tạo spam.
- Business-key dedupe notification dùng cache lease. Nếu Redis bị xóa toàn bộ sau khi side effect đã thành công và broker phát lại event cũ, lớp dedupe cache không còn lịch sử; hàng rào quan trọng nhất vẫn là BatteryService chỉ phát một event khi mở incident mới.
- Chế độ Push `Both` có thể đưa cùng nội dung qua SignalR và Expo; client vẫn nên dedupe khi dựng OS/UI notification.
- Không có thay đổi nào cố tình ngăn một **incident mới có thật** sau khi incident trước đã recovery và `Resolved`; trường hợp đó phải tạo notification mới.

### 17.10. Kết quả E2E trên Docker thật — 11/08/2026

Phép thử được chạy trên `battery_db`, `notification_db`, Redis, RabbitMQ và Mosquitto thật của local compose; BatteryService/NotificationService được build lại từ working tree hiện tại. Migration `20260810145022_AddIotDeviceOfflineIncidentGuard` đã được áp dụng lên DB runtime. Cấu hình thực tế của môi trường test là poll mỗi 60 giây, offline sau 300 giây và LWT grace 90 giây.

| Kịch bản | Kết quả | Bằng chứng chính |
|---|---|---|
| Poller mở incident đầu tiên | **Đạt** | Device `Active → Offline`, một `DeviceOffline` alert và một outbox event |
| Không spam qua nhiều poll tick | **Đạt** | Sau hơn 4 phút vẫn chỉ một alert và 12 notification, không tăng theo tick |
| DB guard | **Đạt** | Insert alert `DeviceOffline` Open thứ hai cùng device bị unique index từ chối |
| Duplicate event khác MessageId | **Đạt** | Bơm bản sao qua chính Battery Outbox → RabbitMQ; outbox được relay nhưng notification vẫn 12 row/1 AlertId |
| Recipient/channel fan-out | **Đạt** | 6 recipient khác nhau × Push/InApp = 12 row cho mỗi lifecycle event |
| Recovery debounce | **Đạt** | Heartbeat thứ nhất giữ Offline; heartbeat thứ hai mới Active, resolve incident và phát type 36 |
| Incident mới sau recovery | **Đạt** | Incident mới có AlertId mới được phép tạo thêm đúng 12 notification type 18 |
| REST notification | **Đạt** | JWT-authenticated test: feed mặc định chỉ InApp; diagnostic mode trả cả Push/InApp; unread/read/opened đúng DB |
| SignalR thật | **Đạt** | Nhận `NotificationCreated`, `NotificationReceived`, `UnreadCountChanged`; unread payload là số nguyên thô |
| Retained LWT cũ | **Đạt** | Sau bridge reconnect, log nhận `retained=True` nhưng bỏ qua do device còn fresh; không tạo incident giả |
| LWT hợp lệ | **Đạt** | Với silence 120 giây (> grace 90, < poll 300), MQTT `offline` không-retained chuyển Offline và queue event; chứng minh không phải poller thắng |
| RabbitMQ drain | **Đạt** | `IotDeviceWentOffline` và `IotDeviceRecovered`: 0 ready, 0 unacked, mỗi queue có 1 consumer |
| Regression tự động | **Đạt** | Battery unit 2/2; Battery MQTT integration 3/3; Notification lifecycle/SignalR unit 6/6; fallback unit 14/14 |

REST + SignalR client E2E có 9 assertion cùng đạt: authorization; feed chỉ InApp; diagnostic có hai channel; recovery phát Created và Push event; unread payload nguyên; recovery tăng badge một; mark-read giảm badge một; opened được persist. Fixture business đã được xóa khỏi Battery DB, Notification DB và Redis sau test. Ba audit row phát sinh bởi read/opened được giữ vì `notification_audit_logs` là append-only theo chủ ý thiết kế.

#### Lỗi runtime còn tồn tại phát hiện trong E2E

1. **P1 — Push→SMS fallback worker đang hỏng trên PostgreSQL.** `NotificationFallbackBackgroundService` dòng 229 gọi `n.PayloadJson.Contains(push.Id.ToString())` trong khi `payload_json` là `jsonb`. EF sinh `payload_json LIKE ...`, PostgreSQL báo `42883: operator does not exist: jsonb ~~ jsonb`; worker lỗi lặp mỗi chu kỳ khoảng 2 phút. Hệ quả: critical push không có receipt không được bù SMS. Bộ 14 unit test fallback vẫn xanh vì provider test không tái hiện semantics `jsonb`; cần thêm integration test PostgreSQL thật và đổi query sang JSON operator/cast phù hợp.
2. **P2 — Mark-read/opened không broadcast unread count sang client khác.** Server chỉ gọi `NotifyUnreadCountAsync` khi `InAppChannel` tạo notification; các handler mark-read/opened/read-all không gọi notifier. Tab vừa thao tác vẫn đúng vì frontend invalidate/refetch, nhưng tab/device khác đang mở có thể giữ badge cũ tới lúc focus/reconnect. Đây không làm phát sinh spam incident nhưng là lỗ hổng realtime đồng bộ đa client.
3. **P2 — Error queue saga tồn đọng ngoài luồng IoT mới.** `AlertTicketSagaState_error` có 20 message; mẫu được đọc bằng `ack_requeue_true` có thời điểm 01/08/2026 và lỗi `AlertLinked event is not handled during Completed state`. Nó có trước E2E và không chứa fixture IoT, nhưng chứng minh state machine chưa idempotent với response trùng khi đã Completed.
4. **P3 — BatteryService và NotificationService không expose `/health`.** Hai URL trả 404; trong local test chỉ có thể dùng Swagger endpoint/process/log để xác nhận readiness. Đây là khoảng trống vận hành, không phải lỗi của notification lifecycle.

#### Vấn đề môi trường đã phân loại, không phải source regression

Mosquitto ban đầu là container cũ từ 08/08/2026: mount/entrypoint không khớp compose hiện tại, `/mosquitto/config/passwd` không tồn tại, broker healthcheck và `battery-service-bridge` đều bị `not authorised` mỗi 5 giây. Recreate riêng service theo compose hiện tại tạo đúng bind mount; Mosquitto chuyển `healthy`, bridge kết nối và toàn bộ retained/LWT E2E đạt. Vì vậy lỗi auth này là stale runtime container; không được dùng làm bằng chứng rằng code LWT mới sai.

Phiên này không có runtime browser IAB và frontend repository không có Playwright/Cypress harness, nên không tuyên bố đã click-through UI bằng trình duyệt. Phần frontend được phủ ở ranh giới thật REST + SignalR; visual interaction vẫn cần một browser E2E suite riêng nếu muốn nghiệm thu UI tuyệt đối.
