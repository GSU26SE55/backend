# Review — Tính năng Notification toàn hệ thống Backend

> **Ngày review:** 2026-07-14
> **Phạm vi:** NotificationService (toàn bộ 4 layer, 174 file .cs) + mọi điểm phát/nhận notification trong AuthService, TicketService, BatteryService, EmailService, SmsService, SharedContracts.
> **Phương pháp:** Đọc code trực tiếp + đối chiếu spec (`overall.md §3` Notification, `§3.4` routing matrix, `.claude/docs/core-business-flow.md` §5–6, `ticket-chat-hub.md`). Các phát hiện nghiêm trọng (mục 3) đã được kiểm chứng trực tiếp bằng grep/đọc file, không chỉ dựa vào phân tích tự động.
> **Trạng thái code:** branch `dev`, commit `3dce378`.

---

## 1. Kết luận tổng quan

**CHƯA đủ đúng logic nghiệp vụ.**

Kiến trúc đúng hướng, độ phủ consumer rộng (22 consumers), phần **in-app notification hoạt động được**, luồng **email OTP/invite** và **SMS OTP/2FA** (đi thẳng AuthService → EmailService/SmsService, không qua NotificationService) hoạt động đúng. Tuy nhiên:

> ⚠️ **Toàn bộ tầng giao nhận (Push / Email / SMS) của notification pipeline KHÔNG bao giờ được gửi ở runtime** — `NotificationDispatcher` đã viết xong, đăng ký DI, nhưng **không có nơi nào gọi nó**. Mọi notification chỉ được ghi vào DB với `Status=Pending` và nằm im ở đó. Người dùng chỉ thấy notification khi tự mở app và poll REST API.

Ví dụ hệ quả trực tiếp: **SLA P1 breach** — nghiệp vụ yêu cầu SMS + Email + Push tức thì cho Manager/Admin — hiện tại **không gửi gì cả** ngoài một dòng nằm im trong bảng `notifications`.

### Bảng tóm tắt mức độ đáp ứng

| Mảng | Trạng thái | Ghi chú |
|---|---|---|
| Email OTP/invite/reset/2FA (AuthService → EmailService) | ✅ Hoạt động đúng | Không đi qua NotificationService |
| SMS OTP/2FA (AuthService → SmsService) | ✅ Hoạt động đúng | Gateway architecture chắc tay |
| In-app notification (ghi + list + read + badge) | ✅ Hoạt động | Client poll REST |
| Push notification (Expo) | ❌ Không bao giờ gửi | Dispatcher chưa được nối vào flow |
| Email từ notification pipeline | ❌ Không gửi | Dispatcher chưa nối **và** `SendNotificationEmailEvent` không có consumer |
| SMS từ notification pipeline | ❌ Không gửi | Dispatcher chưa nối (đường `SendSmsCommand` → SmsService thì có consumer, sẵn sàng) |
| Notification cho Customer trong ticket lifecycle | ❌ Thiếu hoàn toàn | Payload event thiếu CustomerId |
| Escalation chat → Admin (saga timeout) | ❌ Event publish nhưng không ai consume | Đứt mắt xích cuối |
| Cảnh báo bảo mật (suspicious login, token reuse) | ❌ Event publish nhưng không ai consume | |
| SLA breach phân nhánh theo P1/P2/P3 | ❌ Chưa implement | Mọi priority xử lý giống nhau |
| Digest / Daily frequency | ❌ Stub | Field có, logic không |
| Audit log notification (#AUDIT-34) | ❌ Hạ tầng có, không code nào ghi record | |

---

## 2. Kiến trúc thực tế của notification pipeline

### 2.1. Luồng thiết kế (theo code + comment trong code)

```
RabbitMQ Event
  → MassTransit Consumer (NotificationService.Application/Consumers/)
    → [Dedup check: messageId 30' / AlertId 5' / InboxStore]
    → [Recipient resolution: IRecipientResolver → AccountReadModel]
    → Ghi Notification record (Status=Pending) qua 1 trong 2 đường:
        (a) NotificationWriter.WriteAsync        — ghi trực tiếp UnitOfWork
        (b) CreateNotificationCommand (MediatR)  — qua ValidationBehavior
    → ❌ [ĐỨT TẠI ĐÂY — không có gì xử lý tiếp record Pending]
    → (thiết kế dự kiến "Sprint 6"): NotificationDispatcher.DispatchAsync
        → check NotificationPreference + quiet hours + CriticalTypes bypass
        → TypeChannelMatrix → chọn channel
        → ExpoPushChannel / EmailBusChannel / SmsBusChannel / InAppChannel
        → update record Status=Sent/Failed
```

### 2.2. Hai luồng email/SMS HOẠT ĐỘNG THẬT (không qua NotificationService)

```
AuthService handler → publish SendOtpRegisterEvent / SendPasswordResetOtpEvent /
                      SendEmailChangeOtpEvent / SendAdminInviteEvent /
                      SendTwoFactorCrossDeviceConfirmEmailEvent
  → EmailService consumer → render template → Mailjet API          ✅ chạy thật

AuthService handler → publish SendSmsCommand (category otp / 2fa_sms)
  → SmsService consumer → queue DB → Android gateway device poll → gửi SMS  ✅ chạy thật
```

### 2.3. Thành phần chính của NotificationService

| Thành phần | File | Vai trò | Trạng thái runtime |
|---|---|---|---|
| 22 Consumers | `NotificationService.Application/Consumers/` | Nhận event, ghi record | ✅ Chạy |
| `NotificationWriter` | `Consumers/NotificationWriter.cs` | Ghi record trực tiếp | ✅ Chạy (chỉ ghi, không gửi) |
| `CreateNotificationCommandHandler` | `CQRS/Handler/Notification/CreateNotificationCommandHandler.cs` | Ghi record qua MediatR | ✅ Chạy (chỉ ghi, không gửi) |
| `NotificationDispatcher` | `Infrastructure/Services/NotificationDispatcher.cs` | Preference/quiet hours/channel routing | ❌ **DEAD CODE — 0 caller** |
| `ExpoPushChannel` | `Infrastructure/Channels/ExpoPushChannel.cs` | Gửi push qua Expo API | ❌ Dead (chỉ dispatcher gọi) |
| `EmailBusChannel` | `Infrastructure/Channels/EmailBusChannel.cs` | Publish `SendNotificationEmailEvent` | ❌ Dead (và event không có consumer) |
| `SmsBusChannel` | `Infrastructure/Channels/SmsBusChannel.cs` | Publish `SendSmsCommand` | ❌ Dead (dù đường SmsService sẵn sàng) |
| `InAppChannel` | `Infrastructure/Channels/InAppChannel.cs` | Đánh dấu Sent cho in-app | ❌ Dead — nhưng list API vẫn trả record Pending nên in-app "vô tình" hoạt động |
| `RecipientResolver` | `Infrastructure/Services/RecipientResolver.cs` | Broadcast theo role từ AccountReadModel | ✅ Chạy |
| `NotificationAuditOutboxRelayBackgroundService` | `Infrastructure/BackgroundJobs/` | Relay audit outbox (#AUDIT-34) | ✅ Chạy (nhưng không có gì ghi vào outbox) |

---

## 3. Phát hiện NGHIÊM TRỌNG (P1) — đã kiểm chứng trực tiếp

### 3.1. 🔴 Dispatcher không được nối vào flow → Push/Email/SMS không bao giờ gửi

**Bằng chứng (tự verify bằng grep + đọc file):**

1. `INotificationDispatcher.DispatchAsync` chỉ xuất hiện tại 3 nơi trong production code:
   - Khai báo interface: `NotificationService.Application/Services/INotificationDispatcher.cs:8`
   - Implementation: `NotificationService.Infrastructure/Services/NotificationDispatcher.cs:67`
   - Đăng ký DI: `NotificationService.Infrastructure/DependencyInjection/ManageDependencyInjection.cs:62`
   - **Không có bất kỳ caller nào.**

2. `channel.SendAsync(...)` chỉ được gọi duy nhất 1 chỗ: `NotificationDispatcher.cs:182` — tức toàn bộ 4 channel là dead path.

3. `NotificationWriter.cs` (dòng 30–63): chỉ `AddAsync` record `Status=Pending` + `SaveChangesAsync`. Comment dòng 8–11 tự thừa nhận: *"dispatcher fan-out **Sprint 6**"*.

4. `CreateNotificationCommandHandler.cs` (dòng 21–67): cũng chỉ ghi `Pending`. Dòng 23–24 comment: *"merge BypassQuietHours flag vào PayloadJson để **dispatcher (Sprint 6+)** có thể đọc"* — chuẩn bị dữ liệu cho một thứ chưa tồn tại trong flow.

5. Hosted service duy nhất được đăng ký: `NotificationAuditOutboxRelayBackgroundService` (`ManageDependencyInjection.cs:35`). **Không có background job nào quét record `Pending` để dispatch.** Grep `NotificationStatusEnum.Pending` toàn service chỉ ra chỗ set default, không có chỗ query để xử lý.

**Hệ quả nghiệp vụ:**
- Mobile app của Customer **không bao giờ nhận push** — dù đã có đầy đủ device token lifecycle (register/reactivate/deactivate).
- Toàn bộ logic sau đây là **dead code ở runtime**: preference check (`PushEnabled/EmailEnabled/SmsEnabled/InAppEnabled`), quiet hours + timezone (`NotificationDispatcher.IsQuietHours`, dòng 219–234), `CriticalTypes` bypass (dòng 46–53), `TypeChannelMatrix` (dòng 23–43), Polly retry 3x cho Expo, xử lý `DeviceNotRegistered`.
- Spec `overall.md §3.4` yêu cầu Push ngay cho hầu hết loại notification + Email/SMS cho critical → **không đạt bất kỳ dòng nào của routing matrix** ngoài in-app.

### 3.2. 🔴 `SendNotificationEmailEvent` không có consumer — email notification rơi vào hư không

**Bằng chứng:** grep toàn repo, `SendNotificationEmailEvent` chỉ xuất hiện tại:
- `shared/src/SharedContracts/Events/SendNotificationEmailEvent.cs` (định nghĩa)
- `NotificationService.Infrastructure/Channels/EmailBusChannel.cs` (publish)
- `NotificationService.UnitTests/Channels/EmailBusChannelTests.cs` (test)

EmailService chỉ có 5 consumer: `SendOtpRegisterConsumer`, `SendPasswordResetOtpConsumer`, `SendEmailChangeOtpConsumer`, `SendAdminInviteConsumer`, `SendPhoneOtpConsumer` (stub, đã `[ExcludeFromConfigureEndpoints]`). **Không có consumer nào cho `SendNotificationEmailEvent`.**

**Hệ quả:** kể cả khi team sửa xong 3.1 (bật dispatcher), mọi email notification (SLA breach, battery escalation, environmental incident, saga failed, chat mention...) vẫn được publish lên bus rồi **biến mất không dấu vết** — RabbitMQ sẽ drop message không có binding, không error, không log phía consumer. Các consumer như `BatteryAlertEscalationRequestedConsumer`, `EnvironmentalIncidentDetectedConsumer`, `AlertTicketSagaFailedConsumer` đã kỳ công render template HTML (`battery-alert-escalation-pending`, `environmental-incident-detected`, `alert-ticket-saga-failed`) — nội dung đó không bao giờ thành email.

### 3.3. 🔴 Các event được publish nhưng KHÔNG AI consume (orphan events)

Grep toàn bộ `services/` + `shared/` cho `IConsumer<...>` của từng event — kết quả:

| Event | Ai publish | Consumer | Hệ quả nghiệp vụ |
|---|---|---|---|
| `ChatEscalatedToAdminEvent` | `TicketService/Sagas/ChatEscalationReview/ChatEscalationReviewSagaStateMachine.cs:83` (Manager không ACK escalation review trong 30 phút) | **KHÔNG CÓ** | **Admin không bao giờ được báo.** Toàn bộ saga escalation chat P1 Critical (Manager mention → chờ ACK 30' → escalate Admin) đứt ở mắt xích cuối — saga chạy đúng, timeout đúng, publish đúng, rồi... không có gì xảy ra. |
| `SuspiciousLoginDetectedEvent` | `AuthService/Infrastructure/Implements/Services/AuthTokenIssuer.cs:123` (login từ IP/User-Agent lạ, so với 50 session gần nhất) | **KHÔNG CÓ** | User không được cảnh báo đăng nhập bất thường. Detection logic chạy tốn công rồi bỏ. |
| `RefreshTokenReuseDetectedEvent` | `AuthService/Application/CQRS/Handler/Auth/RefreshTokenCommandHandler.cs:84` (phát hiện replay attack, đã revoke toàn bộ token family) | **KHÔNG CÓ** | Hệ thống xử lý attack đúng (revoke hết) nhưng **không báo nạn nhân** — user chỉ thấy "bị logout" không rõ lý do, mất cơ hội đổi password kịp thời. |
| `SmsFailedEvent` / `SmsDeliveryReportEvent` | `SmsService` (`ReportSmsResultCommandHandler.cs`, `SmsGatewayController.cs`) | **KHÔNG CÓ** | Không có feedback loop: nếu SMS OTP/alert fail, service phát không hề biết. Notification record (nếu sau này dispatcher bật) sẽ mãi ở `Sent` dù SMS thực tế fail. |
| `BatteryCascadeRiskHighEvent` | `BatteryService/Application/Services/CascadeRiskService.cs:90-99` | Chỉ `TicketService/Infrastructure/Consumers/BatteryCascadeRiskHighConsumer.cs` (nâng priority ticket) | Không notify ai về cascade risk — chấp nhận được nếu coi việc nâng priority là đủ, nhưng Manager không biết lý do priority thay đổi. |

Lưu ý: `BatteryAnomalyDetectedV2Event` không có consumer trực tiếp dạng `IConsumer<>` nhưng **được saga `AlertTicketSagaStateMachine` (TicketService) consume** để auto-tạo ticket — đây là chủ ý (V1 → NotificationService notify Customer, V2 → TicketService saga), không phải lỗi.

---

## 4. Gap so với spec nghiệp vụ (P2)

Đối chiếu `overall.md §3.4` (notification routing matrix, dòng ~2392–2426) và `core-business-flow.md`:

### 4.1. Customer gần như vô hình trong ticket lifecycle

| Sự kiện | Spec yêu cầu | Thực tế |
|---|---|---|
| Ticket assigned | Customer nhận InApp+Push+Email ("Staff đang xử lý sự cố của bạn") | ❌ Chỉ Staff được ghi noti. Code tự comment "Customer notification deferred (event lacks CustomerId)" |
| Ticket status changed | Customer nhận khi status public thay đổi | ❌ Không có event `TicketStatusChanged` nào được publish; enum `TicketStatusChanged(3)` định nghĩa nhưng không producer/consumer |
| Ticket resolved → approved | Customer nhận khi Manager approve (CLOSED_PENDING_RATE) | ❌ Chỉ Manager được ghi noti khi Resolved; không có gì cho approve |
| Rating request | Customer nhận nhắc rating (auto sau 7 ngày) | ❌ Không tồn tại |
| Ticket closed / rejected | Customer nhận xác nhận | ❌ Enum `TicketClosed(5)` định nghĩa nhưng không có event/consumer; `CLOSED_REJECTED` không có gì |

**Nguyên nhân gốc:** payload các ticket event quá gầy —
- `TicketCreatedEvent`: chỉ `TicketId + Code` (không priority, không CustomerId → noti cho Manager cũng không nói được ticket ưu tiên gì, của ai)
- `TicketAssignedEvent`: `TicketId, Code, StaffId, Priority` — **không có CustomerId**
- `TicketResolvedEvent`: `TicketId, Code, StaffId, ResolutionSummary` — **không có CustomerId**

Muốn notify Customer thì hoặc thêm CustomerId vào event, hoặc NotificationService phải sync read-model ticket→customer (hiện không có).

### 4.2. SLA notification chưa đúng spec

- **SlaWarning (80%)**: spec yêu cầu báo **Staff đang assign + Manager**; thực tế chỉ broadcast Manager (`SlaWarningConsumer.cs`, comment "Individual Staff assignment deferred"). `SlaWarningEvent` payload chỉ có `TicketId, WarningAt, Percentage` — không có StaffId nên có muốn cũng không làm được.
- **SlaBreached (100%)** không phân nhánh theo priority. Spec:
  - P1 → Manager + Admin, InApp+Push+Email+**SMS**, đồng thời escalate
  - P2 → Manager, InApp+Push+Email (không SMS)
  - P3 → Manager, InApp + digest (không push/email)

  Thực tế: `SlaBreachedConsumer.cs` xử lý mọi priority giống hệt nhau (Manager+Admin broadcast, InApp+Push) — và do mục 3.1, thực tế chỉ có in-app.
- Phần **escalation khi breach** thì TicketService làm đúng: `EscalationBackgroundService.cs:63` chỉ escalate P1/P2, P3 chỉ log — khớp Priority Policy trong `.claude/rules/design.md`.

### 4.3. Battery anomaly chưa đủ kênh và thiếu mức Warning

- **Critical anomaly**: spec T#13 yêu cầu Customer nhận InApp+Push+**Email+SMS** (SMS theo preference). `BatteryAnomalyDetectedConsumer.cs` chỉ ghi InApp+Push.
- **Warning anomaly**: spec T#12 yêu cầu Customer nhận InApp+Push. BatteryService **chủ ý không publish event** cho Warning severity (chống spam — `AnomalyDetectionService.cs:124-144` chỉ publish khi Critical) → gap có chủ đích nhưng spec chưa được cập nhật tương ứng. Cần chốt một trong hai phía.
- **Info anomaly**: spec T#11 yêu cầu InApp — không có gì.

### 4.4. Chat notification ghi sai kênh + không tới người offline

- `ChatCreatedConsumer.cs:47` ghi record với `Channel = NotificationChannelEnum.Push` **duy nhất** (không InApp). Khi dispatcher chưa chạy → mất trắng ý nghĩa "push". Record vẫn xuất hiện trong `GET /api/notifications` (list không bắt buộc filter channel) nên user "vô tình" thấy được, nhưng về ngữ nghĩa dữ liệu là sai kênh.
- SignalR hub chat (`TicketCommentHub` bên TicketService) chỉ phục vụ người **đang mở phòng chat**. Customer offline không có push → **không hề biết Staff đã trả lời** cho đến khi tự mở app. Với SLA P1 4h, đây là lỗ hổng vận hành thực sự.
- Routing logic của `ChatCreatedConsumer` (Staff nhắn → notify Customer; Customer nhắn → notify assigned Staff; skip nếu `IsInternal`) — đúng nghiệp vụ. `ChatReactionConsumer` skip self-reaction — đúng. `ChatMentionConsumer` Push+Email — đúng hướng (nhưng email chết theo mục 3.2).

### 4.5. Digest / Frequency là stub

`NotificationPreference.Frequency` (Immediate/Daily) và `DigestWindowMinutes` tồn tại trong entity + API PUT preferences, nhưng **không có bất kỳ logic nào** đọc chúng để batch notification. Spec `overall.md §3.3` mô tả email digest cho non-critical. Hoặc implement, hoặc gỡ field khỏi API để FE không hiểu nhầm là dùng được.

### 4.6. Audit log notification (#AUDIT-34) — hạ tầng có, không ai ghi

- Bảng `NotificationAuditLog` (14 cột chuẩn) + `NotificationAuditOutbox` + `NotificationAuditOutboxRelayBackgroundService` (leader election Redis, batch 50, retry 5) — dựng đầy đủ.
- **Không có dòng code nào tạo record** vào 2 bảng này. Enum `NotificationAuditActionEnum` (PushSent/PushFailed/InAppRead...) chưa được dùng. Background service chạy poll 2s/lần trên bảng rỗng vĩnh viễn.

### 4.7. Template

- **DB template** (`NotificationTemplate`, seed 20+ template qua `NotificationDataSeeder.cs`) gần như không được dùng — chỉ 4 consumer render template (battery escalation, environmental x2, saga failed), còn lại hardcode title/body inline. `TypeChannelMatrix` hardcode trong dispatcher thay vì đọc DB. Chấp nhận được cho capstone nhưng nên chọn 1 pattern.
- **Email template file thiếu:** `OtpPasswordReset.html` và `OtpEmailChange.html` **không tồn tại trên disk** (`EmailService.Api/wwwroot/email-templates/` chỉ có `OtpRegister.html` + `AdminInvite.html`). Consumer fallback về `OtpRegister.html` → **user reset mật khẩu nhận email với nội dung "đăng ký tài khoản"** — sai ngữ cảnh, dễ gây nghi ngờ phishing.

### 4.8. Các gap nhỏ khác

- `AccountDeletedSyncConsumer` tồn tại trong NotificationService nhưng không tìm thấy nơi nào AuthService publish `AccountDeletedEvent` → read-model không bao giờ được dọn khi account bị xoá (mức tin cậy: grep không thấy publish site; nên verify lại nếu có flow xoá account).
- Enum `AdminInvite(13)` trong `NotificationTypeEnum` không có consumer (email invite đi đường AuthService → EmailService riêng — enum này thừa hoặc chờ dùng).
- `SendPhoneOtpEvent` legacy: consumer trong EmailService là stub `[ExcludeFromConfigureEndpoints]`; consumer trong SmsService đánh dấu "Phase 9 XÓA class này" — cần dọn theo kế hoạch.
- Không có SignalR/SSE cho notification real-time trên web — client poll `GET /api/notifications` + `unread-count`. Chấp nhận được cho capstone (spec §34.10 chọn SSE cho telemetry, không bắt buộc cho noti), nhưng nên ghi nhận là quyết định có chủ đích.
- Push không batch: mỗi device token = 1 HTTP call tới Expo (Expo hỗ trợ batch 100 message/call). Chỉ là tối ưu, không phải bug.
- `ExpoPushChannel.DeactivateTokenAsync` có race nhỏ khi 2 thread cùng deactivate 1 token invalid — vô hại thực tế (idempotent set IsActive=false).

---

## 5. Những gì làm ĐÚNG (điểm cộng)

### 5.1. Hoạt động thật, đúng nghiệp vụ

| Luồng | Đường đi | Đánh giá |
|---|---|---|
| OTP đăng ký, resend | `RegisterCommandHandler.cs:134`, `ResendOtpCommandHandler.cs:67` → `SendOtpRegisterEvent` → EmailService → Mailjet | ✅ Publish trước SaveChanges (atomic outbox), Redis inbox dedup phía consumer |
| Reset password, reactivate | `ForgotPasswordCommandHandler.cs:71` (+rate limit 10/h/email, silent fail chống enumeration) | ✅ |
| Đổi email OTP | `ChangeEmailCommandHandler.cs:99` (+Redis reservation 5' chống race) | ✅ |
| Admin invite | `InviteAccountCommandHandler.cs:115` → `SendAdminInviteEvent` → `AdminInvite.html`, token 72h | ✅ |
| 2FA cross-device confirm email | `RequestCrossDevice2FAConfirmCommandHandler.cs:71` | ✅ |
| SMS OTP / 2FA fallback | `SendPhoneOtpCommandHandler.cs:73`, `Request2FASmsCommandHandler.cs:55` → `SendSmsCommand` → SmsService | ✅ Rate limit 60/phút/device + daily limit, retry 3 lần, stale-claim reaper 5', message redactor |
| In-app notification | 22 consumer ghi record; API list/read/read-all/unread-count | ✅ Mark-read idempotent, pagination, sort CreatedAt desc |
| Device token lifecycle | Register 201 / reactivate 200 / conflict 409; deactivate khi Expo trả `DeviceNotRegistered` | ✅ Thiết kế đúng (dù chưa được exercise do 3.1) |
| Preferences + quiet hours | Wraparound qua nửa đêm (22:00–07:00), timezone IANA, cache 5' + invalidate on update, critical bypass | ✅ Logic viết đúng (dù dead code do 3.1) |

### 5.2. Thiết kế tốt

- **Dedup 3 tầng chọn đúng công cụ:** messageId 30' (đa số consumer), AlertId 5' cho escalation (GH-593 — chặn rapid-fire cùng alert), `IInboxStore` transactional cho chat. Có suy nghĩ, không one-size-fits-all.
- **AccountReadModel + 3 sync consumer** (Activated upsert / ProfileUpdated update / Deleted soft-delete) — recipient resolution không phải gọi cross-service, upsert idempotent, transaction đúng pattern Begin/Commit/Rollback.
- **Publish event trước SaveChanges** (outbox pattern) nhất quán ở AuthService/TicketService/BatteryService — event và state change atomic.
- **Escalation P1/P2-only, P3 log-only** (`EscalationBackgroundService`) — khớp Priority Policy.
- **SLA warning 80% chỉ gửi 1 lần** (`WarningSentAt` guard, `SlaTimerBackgroundService.cs:83`) — đúng.
- **Không có HTTP call trực tiếp nào bypass bus** giữa các service cho notification — kiến trúc event-driven sạch.

### 5.3. Tuân thủ rule dự án

- ✅ Mọi query đều có `.Where(x => !x.IsDeleted)` (đã soát toàn bộ handler + dispatcher + resolver).
- ✅ Controller mỏng, chỉ `_mediator.Send()`.
- ✅ Handler chỉ inject `INotificationUnitOfWork`, không inject DbContext.
- ✅ `GetAllAsync()` dùng sync đúng convention, không await nhầm.
- ✅ `UpdateAsync`/`DeleteAsync` không await — **đúng chuẩn SharedKernel của dự án** (lưu ý: nếu tool review nào flag "UpdateAsync not awaited" là bug thì đó là false positive; theo `.claude/rules/tech/be.md §4`, hai method này là void).
- ✅ Enum bắt đầu từ 1, entity extend `AuditableEntity`.

---

## 6. Ma trận đối chiếu: Spec vs Thực tế

Chú thích: **(ghi)** = record được ghi vào DB (in-app thấy được qua poll); **(gửi)** = kênh thực sự phát đi. Do mục 3.1, hiện tại **không có gì "gửi"** ngoài đường AuthService→Email/Sms trực tiếp.

| Sự kiện nghiệp vụ | Spec: ai / kênh | Thực tế | Kết luận |
|---|---|---|---|
| Ticket created (manual + auto-from-alert) | Manager: InApp+Push (+Email digest) | Manager: InApp+Push **(ghi)** | 🟡 Ghi đúng người, không gửi push, không digest |
| Ticket assigned | Staff: InApp+Push+Email (kèm SLA due); Customer: InApp+Push+Email | Staff: InApp+Push **(ghi)**; Customer: ❌ | 🔴 Thiếu Customer, thiếu email, không gửi |
| Ticket reassigned | (tương tự assigned) | Staff mới: **(ghi)** qua cùng event | 🟡 |
| Ticket in-progress / status changed | Customer: InApp+Push+Email | ❌ Không có event | 🔴 |
| Ticket resolved | Manager: InApp+Push; Customer khi approved | Manager: **(ghi)**; Customer: ❌ | 🟡/🔴 |
| Ticket closed / pending-rate / rejected / reopen / rating request | Customer + Manager theo từng case | ❌ Không có event nào | 🔴 |
| Ticket escalated | Manager + Senior Staff: InApp+Push+Email | Manager + Admin: InApp+Push **(ghi)** | 🟡 Không notify Staff tier mới, không email, không gửi |
| SLA warning 80% | Staff + Manager: InApp+Push | Manager: **(ghi)** | 🟡 Thiếu Staff |
| SLA breached | P1: Mgr+Admin InApp+Push+Email+SMS; P2: bỏ SMS; P3: digest | Mọi priority: Mgr+Admin InApp+Push **(ghi)** | 🔴 Không phân nhánh, không gửi |
| Battery anomaly Critical | Customer: InApp+Push+Email+SMS | Customer: InApp+Push **(ghi)** | 🟡 Thiếu Email/SMS, không gửi |
| Battery anomaly Warning / Info | Customer: InApp+Push / InApp | ❌ BatteryService không publish | 🔴 Gap spec-vs-code có chủ đích, chưa chốt |
| Battery alert escalation (unack 5') | Mgr+Admin: InApp+Push+Email | Mgr+Admin: Push+Email+InApp **(ghi, email render sẵn)** | 🟡 Ghi đủ, không gửi được |
| Environmental incident detected | Mgr+Admin+Lead: InApp+Push+Email+SMS | Mgr+Admin: Push+Email+SMS **(ghi, BypassQuietHours=true)** | 🟡 Logic tốt nhất hệ thống, nhưng không gửi |
| Environmental incident resolved | Mgr+Admin: InApp | Mgr+Admin: InApp **(ghi)** | ✅ (theo mức "ghi") |
| IoT device offline (LWT) | Staff/Ops + Customer: InApp+Push | Mgr+Admin: Push+InApp **(ghi)** | 🟡 Spec nói Customer + Staff; code broadcast Mgr/Admin |
| Alert-ticket saga failed | Admin: InApp+Push+Email | Admin+Mgr: **(ghi, email render sẵn)** | 🟡 |
| Incident declared | Mgr+Admin+Lead: all channels | Mgr+Admin: InApp+Push **(ghi, critical bypass)** | 🟡 |
| Chat message | Customer↔Staff push; skip internal | Ghi `Channel=Push` **(ghi)** | 🟡 Routing đúng, kênh đơn, không gửi |
| Chat mention | Người được mention: Push+Email | **(ghi)** Push+Email | 🟡 |
| Chat escalated to Admin (saga 30') | Admin | ❌ **Event không có consumer** | 🔴 |
| OTP register / reset / email change | User: Email | ✅ **GỬI THẬT** (AuthService→EmailService) | ✅ (2 template file thiếu, fallback sai ngữ cảnh) |
| Admin invite | User mới: Email | ✅ **GỬI THẬT** | ✅ |
| SMS OTP / 2FA | User: SMS | ✅ **GỬI THẬT** (AuthService→SmsService) | ✅ |
| Suspicious login / token reuse | User: Email cảnh báo | ❌ **Event không có consumer** | 🔴 |
| Account activated welcome | User: InApp | User: InApp **(ghi)** | ✅ |
| Account locked / disabled | User: Email | ❌ Không có event publish | 🔴 |

---

## 7. Khuyến nghị theo thứ tự ưu tiên

> Chỉ là khuyến nghị từ review — chưa sửa gì trong code. Mỗi mục nên thành 1 GitHub issue riêng theo workflow `/kltn-task` → `/kltn-plan`.

### P1 — Không có thì hệ thống noti coi như chưa tồn tại

1. **Nối `NotificationDispatcher` vào flow.** Hai phương án:
   - (a) Gọi `DispatchAsync` ngay trong consumer/handler sau khi ghi record (đơn giản, latency thấp, nhưng channel fail → MassTransit retry ghi trùng record — cần tách write/dispatch);
   - (b) Background worker quét `Status=Pending` theo batch (khớp thiết kế "Sprint 6" trong comment, tách biệt write/send, retry độc lập — **khuyến nghị phương án này**, tái dùng pattern leader-election sẵn có của audit outbox relay).
2. **Viết consumer `SendNotificationEmailEvent` trong EmailService** (kèm template generic + Redis inbox dedup như 4 consumer hiện có). Không có bước này thì bật dispatcher xong email vẫn mất.
3. **Consumer cho `ChatEscalatedToAdminEvent`** → notify Admin (InApp+Push+Email). Saga đã chạy đúng, chỉ thiếu đầu nhận.
4. **Consumer cho `SuspiciousLoginDetectedEvent` + `RefreshTokenReuseDetectedEvent`** → email cảnh báo bảo mật cho user (đi thẳng đường EmailService như OTP là nhanh nhất).

### P2 — Đúng spec nghiệp vụ

5. **Thêm `CustomerId` vào `TicketAssignedEvent`/`TicketResolvedEvent`** (+ cân nhắc Priority/CustomerId vào `TicketCreatedEvent`) → mở notification cho Customer trong ticket lifecycle. Thêm `StaffId` vào `SlaWarningEvent` để báo Staff đang assign.
6. **Phân nhánh `SlaBreachedConsumer` theo priority** (P1: +SMS +Email; P2: +Email; P3: in-app/digest) — payload đã có `Priority`, chỉ thiếu logic.
7. **Bổ sung event cho các state cuối ticket:** approved/closed/rejected/reopen + rating request, hoặc chốt với leader là ngoài scope rồi gỡ enum `TicketStatusChanged`/`TicketClosed` cho khỏi gây hiểu nhầm.
8. **Chốt gap Battery Warning/Info:** hoặc BatteryService publish event Warning (kèm dedup), hoặc sửa spec T#11/T#12. Thêm Email/SMS cho Critical anomaly theo preference.
9. **Tạo 2 template email thiếu:** `OtpPasswordReset.html`, `OtpEmailChange.html` — fallback hiện tại gửi sai ngữ cảnh.
10. **Chat notification ghi thêm `Channel=InApp`** song song Push, để lịch sử in-app đúng ngữ nghĩa và không phụ thuộc push.

### P3 — Dọn dẹp & hoàn thiện

11. Consumer `SmsFailedEvent` → cập nhật `Notification.Status=Failed` + `FailureReason` (feedback loop).
12. Implement hoặc gỡ digest (`Frequency`/`DigestWindowMinutes`) khỏi API preferences.
13. Ghi `NotificationAuditLog`/`NotificationAuditOutbox` tại các điểm PushSent/PushFailed/InAppRead... hoặc tạm tắt background relay đang poll bảng rỗng 2s/lần.
14. Chọn 1 pattern template (DB template vs inline) và thống nhất; cân nhắc đưa `TypeChannelMatrix` ra config.
15. Xoá `SendPhoneOtpConsumer` stub (EmailService) + legacy (SmsService, "Phase 9") theo kế hoạch đã ghi trong code.
16. Batch push qua Expo API (100 message/call) khi số lượng device tăng.
17. Verify flow `AccountDeletedEvent` — consumer sync có sẵn nhưng chưa thấy publish site.

---

## 8. Phụ lục — Danh sách 22 consumer của NotificationService

| # | Consumer | Event | Người nhận | Kênh ghi | Dedup |
|---|---|---|---|---|---|
| 1 | TicketCreatedConsumer | TicketCreatedEvent | Manager (broadcast) | InApp+Push | msgId 30' |
| 2 | TicketAssignedConsumer | TicketAssignedEvent | Staff được assign | InApp+Push | msgId 30' |
| 3 | TicketResolvedConsumer | TicketResolvedEvent | Manager (broadcast) | InApp+Push | msgId 30' |
| 4 | TicketEscalatedConsumer | TicketEscalatedEvent | Manager+Admin | InApp+Push | msgId 30' |
| 5 | SlaWarningConsumer | SlaWarningEvent | Manager | InApp+Push | msgId 30' |
| 6 | SlaBreachedConsumer | SlaBreachedEvent | Manager+Admin | InApp+Push (critical bypass) | msgId 30' |
| 7 | BatteryAnomalyDetectedConsumer | BatteryAnomalyDetectedEvent (V1) | Customer | InApp+Push | msgId 30' |
| 8 | BatteryAlertEscalationRequestedConsumer | BatteryAlertEscalationRequestedEvent | Manager+Admin | Push+Email+InApp | AlertId 5' (GH-593) |
| 9 | EnvironmentalIncidentDetectedConsumer | EnvironmentalIncidentDetectedEvent | Manager+Admin | Push+Email+SMS (BypassQuietHours) | msgId 30' |
| 10 | EnvironmentalIncidentResolvedConsumer | EnvironmentalIncidentResolvedEvent | Manager+Admin | InApp | msgId 30' |
| 11 | AlertTicketSagaFailedConsumer | AlertTicketSagaFailedEvent | Admin+Manager | Push+Email+InApp | AlertId 5' |
| 12 | IncidentDeclaredConsumer | IncidentDeclaredEvent | Manager+Admin | InApp+Push (critical) | msgId 30' |
| 13 | IotDeviceWentOfflineConsumer | IotDeviceWentOfflineEvent | Manager+Admin | Push+InApp | msgId 30' |
| 14 | ChatCreatedConsumer | ChatCreatedEvent | Customer↔Staff (routing 2 chiều, skip internal) | Push | InboxStore |
| 15 | ChatMentionConsumer | ChatMentionedEvent | Người được mention | Push+Email | InboxStore |
| 16 | ChatReactionConsumer | ChatReactedEvent | Tác giả chat (skip self) | Push | InboxStore |
| 17 | ParticipantChangeConsumer | ParticipantAdded/Removed/RoleChangedEvent (x3) | Participant bị ảnh hưởng | Push | InboxStore |
| 18 | AccountActivatedConsumer | AccountActivatedEvent | Chính user (welcome) | InApp | msgId 30' |
| 19 | AccountActivatedSyncConsumer | AccountActivatedEvent | — (sync AccountReadModel upsert) | — | upsert idempotent |
| 20 | AccountProfileUpdatedSyncConsumer | AccountProfileUpdatedEvent | — (sync read-model) | — | idempotent |
| 21 | AccountDeletedSyncConsumer | AccountDeletedEvent | — (soft-delete read-model) | — | idempotent |

*(ParticipantChangeConsumer implement 3 IConsumer nên tổng class = 21, tổng event consume = 23.)*

---

*Review thực hiện bởi Claude Code — đối chiếu code trên branch `dev` với spec `overall.md`, `core-business-flow.md`, `ticket-chat-hub.md`. Các claim ở mục 3 (dispatcher 0 caller, email event 0 consumer, orphan events) đã được verify trực tiếp bằng grep/đọc file ngày 2026-07-14.*
