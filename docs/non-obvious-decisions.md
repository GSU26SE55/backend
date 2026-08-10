# Quyết định non-obvious — Backend GSU26SE55

> **Vì sao file này nằm ở `docs/` chứ không ở `.claude/memory.md`.**
> `.claude/` và `CLAUDE.md` ở gốc repo được GitHub Action đồng bộ xuống từ repo `workflow-ai`.
> Đồng bộ là **ghi đè**, không phải trộn. Sprint additional-auth từng ghi 59 dòng quyết định vào
> `.claude/memory.md` ở commit `a5efee9`, rồi commit sync `744b0c0` (2026-06-20) **xoá sạch cả 59 dòng**
> — không ai để ý cho tới 2026-08-01. Toàn bộ phần additional-auth dưới đây đã được khôi phục từ
> `a5efee9`, và từ nay đặt ở `docs/` để không bị đồng bộ cuốn đi.
>
> Quy tắc: quyết định của **dự án backend** ghi vào file này. `.claude/` chỉ chứa cấu hình agent
> dùng chung toàn team — muốn sửa phải sửa tại repo `workflow-ai`.

Nội dung: những quyết định KHÔNG suy ra được từ code hay lịch sử git. Ghi lại để người sau không
phải đoán, và để chính mình ba tháng sau không đảo ngược nhầm.

---

## Quyết định non-obvious — Sprint additional-auth (2026-06-18)

Các quyết định không hiển nhiên từ code, ghi lại để team kế thừa.

### Security & policy

- **CORS whitelist (`#AUTH-05` — ~~P0 pending~~ **ĐÃ SỬA 2026-08-01**):** Mô tả cũ ("giữ `SetIsOriginAllowed(origin => true)`") đã lạc hậu. `AddCORS.cs` nay đọc whitelist từ `Cors:AllowedOrigins`:
  - **Production + danh sách rỗng ⇒ ném `InvalidOperationException` ngay lúc khởi động.** Cố ý để service KHÔNG lên còn hơn lên với CORS mở toang — đây chính là lỗ hổng `#AUTH-05`.
  - Development + rỗng ⇒ vẫn cho mọi origin (để FE chạy cổng bất kỳ) kèm cảnh báo ra console.
  - Origin bị cắt dấu `/` cuối khi nạp: `WithOrigins` so khớp chuỗi nguyên văn, dán thừa `/` là whitelist trượt **im lặng**.
  - 5 test ở `shared/tests/SharedInfrastructure.UnitTests/DependencyInjection/CorsExtensionsTests.cs`. Bộ test cũ khẳng định "mọi origin đều được phép" — tức nó **đang bảo vệ chính lỗ hổng cần sửa** — đã viết lại.
  - **Còn treo:** danh sách domain production thật vẫn cần Leader chốt và điền vào `Cors__AllowedOrigins__0..n`.
- **OTP entropy (`#AUTH-14` — followup 2026-06-19):** Giữ 6 số (KHÔNG tăng 8 — UX). Đã giảm TTL 10 → **5 phút** ở 4 handler (ForgotPassword, ResendResetOtp, ReactivateRequest, ChangeEmail) + Redis `EmailReserveTtl` align 5p. Brute-force window co lại 50% so với baseline. Mọi OTP flow trong AuthService giờ thống nhất 5p TTL.
- **Password policy values:** Configurable qua `PasswordPolicy:{MinLength, RequireUppercase, RequireDigit, RequireSpecial}` (`#AUTH-53`). Default MinLength=8 + 4 char class. Có thể tighten qua appsettings không cần code change.
- **Lockout grace period (`#AUTH-82`):** Implicit 5 phút (chu kỳ `LockoutReconcileBackgroundService`), KHÔNG strict 1s. Spec `1s grace` ở Phụ lục là defensive design — với reconcile cycle 5 phút thì grace 1s là noise, bỏ qua.
- **LastLoginIp semantic (`#AUTH-33` — done 2026-06-19):** Chốt option A. KHÔNG update `LastLoginAt`/`LastLoginIp` ở `RefreshTokenCommandHandler` — semantic chuẩn "last login" = lần user thực sự login (Password+2FA / Google / Invite accept), KHÔNG phải mỗi refresh. Audit history đầy đủ ở `LoginAttempt` + `audit_logs` độc lập với field display này.
- **HTTPS redirect Docker (`#AUTH-71`):** SKIP intentional — cloud-native pattern: TLS termination ở reverse proxy (Nginx/Caddy/k8s Ingress), KHÔNG redirect trong app. Deploy runbook PHẢI document requirement này.

### Auth flow

- **Account 90 ngày retention (`#AUTH-30` + `#AUTH-42`):** DeleteAccount KHÔNG anonymize PII ngay — giữ Email/FullName/PhoneNumber 90 ngày cho AUTH-50 reactivation flow. Hard-delete + anonymize qua `AccountHardDeleteBackgroundService` (daily 03:00 UTC, condition `is_deleted=true AND deleted_at < now - 90d`).
- **Concurrent session limit (`#AUTH-41`):** Default `MaxConcurrentSessionsPerAccount=5`. Khi vượt → revoke session cũ nhất (FIFO). Override qua appsettings.
- **Refresh token TTL (`#AUTH-28`):** Lưu `OriginalIssuedAt` lúc cấp lần đầu, mọi rotation tính TTL từ original — KHÔNG rolling-extend forever. Mặc định 7 ngày max lifetime kể từ login.
- **Google OAuth manual retry (`#AUTH-21` deviation):** Dùng manual retry thay Polly để tránh thêm dependency cho 1 endpoint (`GoogleCallbackCommandHandler.cs`). Timeout 10s, retry 2 lần exponential. Justify trong DI comment.

### Error response shape

- **Error code catalog (`#AUTH-73` rollback):** KHÔNG có field `ErrorCode` machine-readable trong `CommonResponseBase`. Pattern hiện tại: FE parse theo HTTP status + message string. Đã thử implement (11 handler wire) → user yêu cầu rollback toàn bộ. Lý do: thêm complexity không đủ value khi FE đã handle bằng status code.

### Testing

- **Outbox integration test (`#AUTH-88` deviation):** TestContainers Postgres ✅ + **InMemory MassTransit** thay RabbitMQ TestContainer. Lý do: speed (RabbitMQ container slow startup) + simplicity. Outbox loop behavior (write → relay → publish → `processed_at` set) vẫn verified end-to-end.
- **ChangePassword revoke test (`#AUTH-87` deviation):** Handler revoke trực tiếp qua `RefreshToken.Status` + `ITokenRevocationStore`, **không qua Mediator `RevokeAllSessionsCommand`**. Test verify state change thay vì verify Mediator call. Spec intent (revocation happens) vẫn pass.
- **ChangeEmail test (`#AUTH-84`):** File tên `EmailChangeCommandHandlerTests.cs` (không phải `ChangeEmailCommandHandlerTests.cs` như spec). 11 Fact tests cover happy + race + OTP fail.

### Defensive code chưa active

- **`ConcurrencyRetryHelper` wire (`#AUTH-34` — done 2026-06-19):** Wired vào `ChangePasswordCommandHandler` + `ChangeAccountRoleCommandHandler` (2 handler có race với admin update Account). Thêm `IGenericRepository.ReloadAsync` API + EF Entry.Reload trong GenericRepository. Pattern: wrap business logic + SaveChanges trong `ExecuteAsync`, reload entity sau conflict. `auditPublished` flag tránh duplicate audit log khi retry (1 attempt fail → tracker giữ audit row pending → attempt 2 reuse). Return 409 nếu account becomes invalid sau reload (vd admin disable concurrent). 3 attempts max. RefreshTokenCommandHandler KHÔNG wire (AUTH-33 đã remove Account update từ refresh path; RefreshToken không có xmin token).
- **Roslyn analyzer async/await (`#AUTH-81` — followup 2026-06-19):** Audit toàn AuthService `src/` + `tests/` → **0 violation** cho 5 pattern (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `async void`, `throw ex;`). Code đã clean. Thêm `services/AuthService/Directory.Build.props` chứa `Microsoft.VisualStudio.Threading.Analyzers` v17.10.48 + `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` → enforce cho tất cả 6 project con (Api/Application/Domain/Infrastructure + 2 test project). Severity rule trong root `.editorconfig`: VSTHRD002 (sync wait) / VSTHRD100 (async void) / VSTHRD110 (unobserved task) / CA2200 (`throw ex` preserve stack) = error; VSTHRD200 (Async suffix) = none (Controller convention). CI build sẽ fail nếu future code vi phạm.

### P3 features implemented — 2026-06-19

- **`#AUTH-47` Account Merge/Consolidation:** Implement đầy đủ. `AccountMergeLog` entity (immutable + JSON snapshot + conflict resolution) + Account fields `MergedIntoId`/`MergedAt` + migration `AddAccountMergeLog`. Endpoint `POST /api/admin/accounts/{primaryId}/merge` (Admin-only). Logic: revoke RT secondary → transfer GoogleId/Profile/StaffProfile (primary thắng conflict) → tombstone secondary (anonymize email tránh unique violation) → insert merge log → publish AccountMerged + meta-audit AccountDeleted → Redis TRL revoke.
- **`#AUTH-48` Trusted Device whitelist:** Implement đầy đủ. `TrustedDevice` entity (SHA-256 fingerprint + IP /24-prefix + 30d TTL + UsageCount + RevokedAt) + `TrustedDeviceFingerprintHelper`. `Verify2FALoginCommand.TrustDevice` flag (chỉ TOTP/SMS path, KHÔNG backup code). `LoginCommandHandler` match active device → skip 2FA challenge. 3 endpoint user (GET list / DELETE one / DELETE all). Auto-revoke khi ChangePassword + Disable2FA. Composite unique index `(AccountId, DeviceFingerprintHash)` filter active.
- **`#AUTH-51` Cross-Device 2FA Confirmation:** Implement đầy đủ. Redis store `2fa:confirm-token:{token}` TTL 10p (single-use). 2 endpoint: `POST /api/auth/2fa/cross-device-confirm/request` (Device A) + `POST /api/auth/2fa/cross-device-confirm` (Device B). Anti-stolen-link: token chỉ confirm được bởi cùng AccountId. SharedContract event `SendTwoFactorCrossDeviceConfirmEmailEvent`.

### Audit action enum mới (2026-06-19)

- TrustedDevice: 110-113 (Added/Revoked/AllRevoked/LoginWith).
- Cross-device 2FA: 120-122 (Requested/Confirmed/Expired).
- Account merge: 130-131 (Merged/MergeRejected).

### Deferred / Skipped final (5 task — 2026-06-19)

- **`#AUTH-05`** ~~P0 pending~~ — **cơ chế ĐÃ LÀM XONG 2026-08-01** (xem mục CORS ở đầu tài liệu). Chỉ còn chờ Leader chốt danh sách domain để điền vào `Cors__AllowedOrigins__0..n`.
- **`#AUTH-56` P3 defer:** Notification preferences — cross-service impact, hard-code default channel matrix đủ scope capstone. Re-evaluate khi có user spam complaint.
- **`#AUTH-61` P2 skip:** API versioning — single-version, FE+BE coordinate breaking change ad-hoc.
- **`#AUTH-63` P2 skip permanent:** Multi-tenancy OrgId — single-tenant scope, cần 1+ sprint riêng + cross-service impact.
- **`#AUTH-64` P1 defer:** KYC recovery — scope lớn (document upload + admin approval workflow + identity provider integration). Mitigation: admin-side reset qua `#AUTH-57` + `#AUTH-55` + manual support.
- **`#AUTH-71` P2 defer:** HTTPS redirect Docker — TLS termination ở reverse proxy (cloud-native). Deploy runbook chưa viết, sẽ tạo khi setup production env.
- **`#AUTH-73` P2 skip permanent:** Error code catalog `AUTH_*` — đã rollback. FE parse theo HTTP status + message đủ.

---

## Quyết định non-obvious — Sprint audit (chốt 2026-06-24, owner Thắng `@Alexdev257`)

Sáu quyết định gỡ block cho Sprint audit. Ghi ở đây vì không đọc ra được từ code.

- **Gate "ổn định ≥ 2 tuần" — WAIVED.** Spec ban đầu bắt Sprint audit phải đợi Sprint additional-auth chạy ổn định 2 tuần. Bỏ qua vì dự án là **sole-dev** (Thắng) và 3 hard-blocker code (`#AUTH-29` trigger append-only, `#AUTH-77` CorrelationIdMiddleware, `#AUTH-15` Outbox) đều đã merge và verify trên đĩa. Đợi 2 tuần chỉ để đợi, không mua thêm tín hiệu gì.
- **Geo IP enrichment = MaxMind.** Chọn MaxMind GeoLite2 (file DB cục bộ) thay vì API bên thứ ba: không phụ thuộc mạng lúc consume event, không rò IP người dùng sang bên thứ ba, không quota.
- **OutboxRelay chống chạy trùng = Redis leader election.** Nhiều instance cùng poll outbox sẽ publish trùng. Dùng khoá Redis có TTL để mỗi lúc chỉ một instance relay. KHÔNG dùng advisory lock của Postgres — sẽ ràng buộc relay vào đúng DB đó, cản việc tách service về sau.
- **Vai trò `SecurityOfficer` — GỘP vào `Admin`.** Spec gốc tách riêng vai trò chỉ-đọc audit. Với 5 người trong đội thì tách vai trò là phình quyền hạn mà không ai dùng. Có nhu cầu thật (kiểm toán ngoài) thì tách sau — thêm role dễ hơn gỡ role.
- **`AlertAuditLog` đặt trong BatteryService.** Alert thuộc vòng đời pin, không thuộc ticket. Đặt cùng chỗ với dữ liệu nghiệp vụ để INSERT audit nằm chung transaction — đúng nguyên tắc decentralized write của kiến trúc Hybrid.
- **Retention bất đối xứng — giữ nguyên như ADR-0007.** Bảng local của từng service giữ ngắn hạn, `audit_aggregate` giữ dài hạn có phân vùng theo tháng. Đã rà lại và chốt, không đổi.

### Còn treo (blocker của Phase 2)

- Chưa dựng `audit-aggregator-db` + `pg_partman` trong `docker-compose` (`#AUDIT-13`/`#AUDIT-14`).
- `#AUDIT-36..40` là task FE, nằm ở repo `frontend` — không thuộc repo này.

---

## Quyết định non-obvious — Sprint Chat

Các giá trị dưới đây đọc từ code/cấu hình thật (`.env.Docker`, handler) ngày 2026-08-01, KHÔNG phải từ spec.

### Nhà cung cấp bên thứ ba

- **Quét virus = ClamAV qua REST** (`ClamAvHttpClient` + `VirusScanWorker`). **Mặc định TẮT** — bật bằng `Chat:Features:EnableVirusScan=true` sau khi đã deploy ClamAV. Nghĩa là ở trạng thái hiện tại, file đính kèm **không được quét**; đừng đọc nhầm là "đã có quét virus".
- **Dịch + gợi ý + tóm tắt + phân tích cảm xúc = DeepSeek** (`DeepSeekChatAiClient`, `DeepSeekChatTextAiClient`), model `deepseek-v4-flash`, timeout 60s.
- **Chuyển giọng nói thành văn bản = Gemini** (`Chat__Voice__ModelName=gemini-3.1-flash-lite`), timeout 30s — **KHÔNG dùng Whisper** như spec ban đầu nêu.

### Hạn mức

| Thứ | Giá trị | Nơi khai |
|-----|---------|----------|
| Cửa sổ sửa tin nhắn | 15 phút | `Chat__EditWindowMinutes` |
| Độ dài body | 1 – 10.000 ký tự | `Chat__MinBodyLength` / `MaxBodyLength` |
| File đính kèm mỗi tin | 10 | `Chat__MaxAttachmentsPerChat` |
| Kích thước file tối đa | **50 MB** (52.428.800 byte) | `Chat__MaxAttachmentSizeBytes` |
| MIME được phép | `image/*`, `application/pdf`, `video/mp4`, `text/plain` | `Chat__AllowedAttachmentMimeTypes` |
| **Tin ghim tối đa mỗi ticket** | **3** | **hằng số cứng** `ChatPinCommandHandler.MaxPinnedPerTicket` — KHÔNG đọc từ config, muốn đổi phải sửa code |
| Gợi ý AI mỗi lần gọi | 3 | `Chat__Ai__MaxSuggestionsPerCall` |
| Ngưỡng cảnh báo cảm xúc | -0.7 | `Chat__Ai__SentimentAlertThreshold` |
| Số tin đưa vào phân tích cảm xúc | 20 | `Chat__Ai__SentimentAnalysisMaxChats` |
| Lưu trữ hội thoại | 2 năm | `Chat__Retention__ArchiveAfterYears` |

### Bẫy đã trả giá để biết (2026-08-01)

- **Client SignalR PHẢI khai JSON protocol khớp server**: camelCase + `JsonStringEnumConverter`. Thiếu converter thì client ném ngay khi gặp `"authorRole":"Staff"`, mà **SignalR nuốt lỗi callback** — triệu chứng nhìn y hệt "tin nhắn bị rơi". Mất khá lâu mới truy ra.
- **`SkipNegotiation = true` ⇒ `HubConnection.ConnectionId` phía client là `null`.** Dùng nó làm khoá dictionary sẽ ném trong callback, và lại bị SignalR nuốt — cũng ra đúng triệu chứng "tin không tới".
- **Cú pháp chỗ-trống của mẫu câu trả lời trùng cú pháp biến Postman.** `TemplateRendererService` khớp `{{tên}}` bằng regex `\{\{(\w+)\}\}` — hệt Postman. Bộ sưu tập `docs/chat/chat-hub.postman.json` **cố ý không khai** biến `customerName`/`ticketCode` để hai chỗ-trống được gửi nguyên văn; ai thêm biến trùng tên vào environment là Postman nuốt mất chỗ-trống, mẫu tạo ra thành văn bản chết.
- **`ConnectionStrings__Redis` trong `.env` ở gốc repo trỏ tới Upstash trên cloud**, và `EnvFileLoader` nạp nó cho **cả test**. Test nào chạm SignalR/Redis phải tự ghi đè sang container cục bộ, nếu không sẽ bắn tải vào Redis dùng chung. Ghi đè phải bằng **biến môi trường**, không phải `ConfigureAppConfiguration` — `Program.cs` đọc chuỗi kết nối TRƯỚC `builder.Build()`, mà delegate của `WebApplicationFactory` chỉ được áp lúc `Build()`.

---

## API key thiết bị IoT lưu plaintext — CÓ CHỦ Ý (GH-724, chốt 2026-08-04)

`iot_devices` giữ **cả hai**: `ApiKeyHash` (verify constant-time) **và** `ApiKeyPlaintext`
(để Admin đọc lại trên `GET /api/admin/iot-devices/{id}`). Bắt đầu từ commit `82b56569`
(2026-07-16, *"display iotkey"*).

**Vì sao:** ESP32 nằm ngoài hiện trường; khi phải flash lại firmware, Admin cần đọc lại
đúng key đang dùng. Nếu chỉ giữ hash thì mọi lần flash lại đều buộc rotate key → phải chạm
tay vào thiết bị hai lần.

**Rủi ro đã chấp nhận:** ai đọc được DB, hoặc gọi được endpoint admin GetById, thì lấy được
credential thiết bị và giả mạo được telemetry.

**Issue #724 báo đúng sự kiện** (doc lúc đó ghi "DB chỉ giữ hash" trong khi thực tế không
phải), nhưng kết luận "phải bỏ plaintext" thì trái quyết định sản phẩm. Chốt: **giữ hành vi,
sửa tài liệu cho khớp**. Đã sửa 4 chỗ: `IIotApiKeyService.GenerateKey`,
`IotDeviceCreatedDto.RawApiKey`, và 3 dòng doc trong `AdminIotDevicesController`
(create + rotate).

**Đừng nhầm với MQTT password:** cái đó **chỉ có `MqttPasswordHash`**, KHÔNG lưu plaintext,
nên "chỉ trả 1 lần" với MQTT là đúng. Hai cơ chế khác nhau trong cùng một entity.

**Đừng nhầm với SmsService gateway device** (`docs/api-sms.md`): đó là hệ khác, API key ở đó
vẫn là chỉ-hiện-1-lần.

---

## Thông báo đẩy — hai đường vận chuyển và ba miễn trừ cho chat (2026-08-05)

Quyết định kiến trúc đầy đủ: **[ADR-0019](adr/0019-push-transport-signalr-expo.md)**. Phần dưới chỉ
ghi những thứ đọc code sẽ không tự suy ra được.

### Vì sao có `CompositePushChannel` thay vì đăng ký thẳng hai kênh

`NotificationDispatcher` chọn kênh bằng `_channels.FirstOrDefault(c => c.ChannelType == …)`. Cả
`SignalRPushChannel` lẫn `ExpoPushChannel` đều khai `ChannelType = Push`, nên **đăng ký cả hai dưới
`INotificationChannel` thì cái thứ hai chết im lặng** — không lỗi, không log, chỉ là không bao giờ
được gọi, và thứ tự đăng ký trong `ManageDependencyInjection` âm thầm trở thành cấu hình. Vì vậy
mỗi đường có interface riêng (`ISignalRPushChannel`, `IExpoPushChannel`) và chỉ bản gộp mới đăng ký
dưới interface chung.

### `Delivered` chỉ tồn tại khi bật Expo

`NotificationStatusEnum.Delivered` **chỉ** do `ExpoReceiptReconcileBackgroundService` đặt. Chạy
thuần `push.transport = SignalR` thì `Sent` là trạng thái cuối cùng — SignalR không có cơ chế biên
nhận nào. Kéo theo: `NotificationFallbackBackgroundService` (bù SMS cho push critical) không có dữ
liệu để làm việc và cũng tự nghỉ. Đây là cái giá đã biết của việc bỏ phụ thuộc EAS/FCM, không phải
lỗi — nhưng đừng báo cáo "push critical luôn có đường bù" khi hệ thống đang chạy thuần SignalR.

### Hai worker Expo chọn hướng an toàn NGƯỢC NHAU khi không đọc được cấu hình

| Worker | Đọc lỗi thì | Vì sao |
|--------|-------------|--------|
| `ExpoReceiptReconcileBackgroundService` | **vẫn chạy** | Chạy thừa vô hại (không có biên nhận nào để xử lý); bỏ sót thì mất dữ liệu giao hàng thật |
| `NotificationFallbackBackgroundService` | **nghỉ** | Chạy thừa **bắn SMS thật** cho người dùng thật; bỏ lỡ một vòng thì vòng sau vẫn bắt được vì lọc theo mốc thời gian |

Ai thấy hai nhánh `catch` trả về hai giá trị ngược nhau mà tưởng là lỗi sao chép thì đọc lại bảng này.

### Chat bỏ qua CẢ BA cơ chế làm chậm

`ChatCreated` và `ChatMentioned` bỏ qua **quiet hours**, **digest** và **hạn mức người dùng**
(`NotificationDispatcher.RealtimeConversationTypes`). Ba cơ chế đó sinh ra để chặn thông báo hệ
thống làm phiền; áp lên hội thoại giữa người thật thì buổi tối nhắn tin không ai nhận được gì, và
người đặt `Frequency = Daily` thì mỗi ngày mới nhận chat một lần.

Hệ quả phải biết: sau thay đổi này, **cách duy nhất để tắt thông báo chat** là tắt kênh Push hoặc
tắt nhóm "Trao đổi" trong tuỳ chọn — quiet hours không còn tác dụng với chat.

### Nội dung chat đi nguyên văn ra màn hình khoá

Template `ChatCreated` là `{{Title}}`/`{{Body}}`, mà consumer dựng sẵn `"{tên người gửi}: {nội dung}"`.
Nghĩa là **nội dung tin nhắn, kể cả ghi chú nội bộ, hiện nguyên văn trên banner thông báo**. Vì thế
`ChatRecipientResolver` là ranh giới bảo mật thật sự chứ không chỉ là chuyện tiện dụng:

- Nó **không được tự chế luật riêng** — phải gọi `TicketQueryHelper.CanViewInternalChats(roles,
  participantCanViewInternal)`, đúng hàm mà tầng đọc dùng, để "được báo" trùng khít "đọc được".
- Luật đó cho phép **Customer đọc nội bộ nếu được cấp cờ `CanViewInternal`** (#522). Câu "Customer
  KHÔNG bao giờ thấy ghi chú nội bộ" trong `CLAUDE.md` đúng với **mặc định** (`TicketCreateCommandHandler`
  đặt `CanViewInternal = false`), không đúng khi có cấp quyền tường minh.
- Mọi thay đổi ở resolver phải kèm test nhánh `isInternal` — xem `ChatRecipientResolverTests`.

### Client nhận HAI sự kiện SignalR cho cùng một thông báo

`NotificationCreated` (từ `InAppChannel`, dòng `Channel = InApp`) dùng để cập nhật feed + badge;
`NotificationReceived` (từ `SignalRPushChannel`, dòng `Channel = Push`) dùng để dựng thông báo hệ
điều hành. Cùng `entityId`. Có chủ ý — nhưng client hiện cả hai mà không khử trùng thì người dùng
thấy hai lần. Chế độ `Both` còn cộng thêm một bản qua Expo nữa.

### Migration đã merge thì KHÔNG sửa tại chỗ

`20260729161154_AddNotificationDispatchRetryColumns` đã có trên `dev` và đã chạy trên các database
hiện có ⇒ tên nó nằm trong `__EFMigrationsHistory` ⇒ **sửa nội dung nó không bao giờ chạy lại**, chỉ
database dựng mới mới thấy bản sửa. Bản vá đúng quy trình là một migration MỚI
(`20260805083909_RepairLegacyNotificationRetryColumns`), viết idempotent để trên database khoẻ mạnh
toàn bộ `Up()` là no-op.

### Tên class consumer chính là tên queue RabbitMQ — trùng tên là mất message

MassTransit `ConfigureEndpoints` sinh tên queue từ **tên class** (bỏ hậu tố `Consumer`), **không**
tính namespace hay tên service. Hai service khai `class AccountActivatedConsumer` ⇒ cùng nghe queue
`AccountActivated` ⇒ RabbitMQ chia round-robin ⇒ **mỗi service chỉ nhận ~50%**, service kia im lặng
mất phần còn lại. Không có log lỗi, không có message vào DLQ — nhìn y hệt "event không được publish".

Đã dính 6 nhóm; nặng nhất là `AuditReplayRequestedConsumer` trùng ở **6** service trong khi thiết kế
của nó là fanout ("mọi service đều nhận") ⇒ ~83% lệnh replay bị nuốt.

Quy ước bắt buộc: **prefix tên service vào mọi class consumer** — `NotificationAccountActivatedConsumer`,
`BatteryAccountActivatedConsumer`, `TicketAccountActivatedConsumer`. `ci/scripts/rule-checks.sh`
RULE 9 quét toàn repo và fail CI nếu có tên trùng.

Hai thứ đi kèm khi đổi tên, quên là hỏng âm thầm:
- `ProcessOnceAsync(_inbox, nameof(XxxConsumer), …)` — khoá idempotency đổi theo, nên consumer sẽ
  xử lý LẠI các message cũ. Chỉ an toàn khi handler là upsert; nếu handler tạo bản ghi mới thì phải
  giữ nguyên chuỗi khoá cũ thay vì dùng `nameof`.
- Queue mang tên cũ vẫn tồn tại trên broker sau khi deploy. Phải xoá tay
  (`rabbitmqctl delete_queue`, `rabbitmqadmin delete exchange`), nếu không message cũ nằm lại mãi.

---

## Hạn mức request — hai bậc theo danh tính, và bốn cái bẫy khi ráp (2026-08-07)

Hạn mức nền áp cho **toàn bộ 9 service kể cả ApiGateway**, cấu hình chung ở
`SharedInfrastructure.RateLimiting`:

| Ai | Hạn mức | Gom theo |
|----|---------|----------|
| Chưa đăng nhập | **60 request / 30 giây** | IP client |
| Đã đăng nhập | **500 request / 30 giây** | từng người dùng / thiết bị |

Các policy chặt hơn theo endpoint (login 10/phút, OTP 5/phút, chat write, sms gateway, audit) **giữ
nguyên** và chạy chồng lên: request phải qua được cả hai.

### 1. `UseStandardRateLimiter()` phải đứng SAU `UseAuthentication()` và `UseAuthorization()`

Đây không phải chuyện thẩm mỹ mà là điều kiện để phân biệt hai bậc:

- `UseAuthentication` mới gán `HttpContext.User` cho JWT. Đặt limiter trước nó thì **mọi** request kể
  cả đã đăng nhập đều rơi xuống bậc ẩn danh 60.
- `UseAuthorization` mới xác thực scheme chỉ định riêng ở endpoint. Thiết bị IoT dùng
  `[Authorize(AuthenticationSchemes = "ApiKey")]` nên chỉ có danh tính **sau** bước này — đặt limiter
  trước là bóp toàn bộ thiết bị xuống 60 req/30s.

AuthService và SmsService từng đặt `UseRateLimiter()` ngay sau `UseCors()`, tức trước cả hai. Hệ quả
âm thầm: các policy khai là "theo UserId" (`AuthOtp`, `TwoFactorDisable`, `BackupCodeRegenerate`) thực
tế **luôn** rơi xuống nhánh dự phòng và gom theo IP. Đã sửa; `RateLimiterWiringTests` đọc thẳng
`Program.cs` của cả 9 service để chặn tái diễn.

### 2. JWT hệ thống KHÔNG có claim `UserId` — claim thật là `AccountId`

Gateway từng phân vùng bằng `User.FindFirst("UserId")`, luôn trả null, nên rơi xuống fallback IP:
**mọi người dùng sau cùng một NAT/reverse proxy dùng chung một bộ đếm**. Claim đúng theo thứ tự ưu
tiên: `AccountId` → `NameIdentifier` → `sub` → `iot:device_id` → `device_code`.

### 3. Sau gateway, `RemoteIpAddress` của mọi request đều là IP container gateway

Thiếu bù đắp thì hạn mức ẩn danh của từng service gom **toàn bộ** traffic chưa đăng nhập vào một bộ
đếm. Gateway vì vậy ghi header `X-Client-Ip` cho upstream (`AddGatewayClientIpForwarding`).

Đi kèm bắt buộc: gateway **tự đọc header đó** để chọn bộ đếm cho chính nó, nên phải xoá giá trị client
gắn vào ở ngay đầu pipeline (`UseClientIpHeaderSanitizer`). Không có bước này thì đổi header mỗi
request là mở vô số bộ đếm và hạn mức ở biên mất tác dụng hoàn toàn. Service phía sau thì TIN header
này — giả định là chúng chỉ nhận traffic qua gateway, không expose thẳng ra ngoài.

### 4. Ba nhóm bắt buộc miễn trừ

- **Health check và metrics** (`/health`, `/live`, `/ready`, `/metrics`, `/swagger`). Docker
  healthcheck gọi mỗi 10 giây và Prometheus scrape đều đặn, đều là request ẩn danh cùng một địa chỉ.
  Tính chúng vào hạn mức nghĩa là một đợt truy cập bình thường có thể làm health check trả 429 →
  container bị đánh dấu unhealthy → khởi động lại. Tự gây sự cố bằng chính cơ chế bảo vệ.
- **gRPC nội bộ** (`Content-Type: application/grpc*`). `BatteryInternalService` và
  `FileInternalGrpcService` đều không gắn `[Authorize]` và chỉ nghe trên cổng nội bộ 8081; tính vào
  hạn mức là chúng rơi vào bậc ẩn danh và TicketService dựng một trang danh sách có thể tự làm nghẽn
  chính nó.
- **Integration test** — các factory set `RateLimiting:Enabled=false`, nếu không test bắn hàng loạt
  request sẽ đỏ vì 429 chứ không phải vì logic sai.

### Hai điều còn lại cần biết

- **Token sai/hết hạn = ẩn danh.** Căn cứ là `User.Identity.IsAuthenticated` chứ không phải sự tồn
  tại của header `Authorization` — nếu không, gắn một chuỗi bất kỳ là nhảy từ 60 lên 500.
- **Ở tầng service, request bị 401 tại `UseAuthorization` KHÔNG bị tính hạn mức** (limiter đứng sau).
  Lớp chặn cho luồng đó là gateway: route YARP không gắn authorization policy nào nên request token
  rác vẫn đi qua limiter của gateway và bị tính ở bậc ẩn danh.
- Mọi con số chỉnh được qua `RateLimiting__*` (đã khai trong `.env` / `.env.Docker`). Khoá cũ
  `RateLimiting__PermitLimit` đã bỏ; đừng thêm lại `RateLimiting__WindowSeconds=10` vì nó âm thầm rút
  cửa sổ xuống 10 giây ở mọi service.
- Limiter là **in-memory theo từng instance**, không dùng Redis. Chạy N replica thì hạn mức thực tế
  nhân N.

---

## Năm nợ kỹ thuật phát hiện khi chạy thật (2026-08-08, cuối Sprint IoT-3)

Nợ #1–#4 **đã sửa** ở IOT3-106 (#1172); nợ #5 đã sửa cùng ngày. Giữ lại nguyên văn vì cách chúng
ẩn mình mới là bài học, không phải bản vá.

Bốn cái đầu ban đầu **chưa sửa**, không nằm trong 105 task của sprint, và cùng một tính chất: **mọi tầng đều
báo thành công trong khi dữ liệu biến mất**. Không cái nào bị 657 unit test bắt được, vì cả ba chỉ
lộ ra khi chạy qua bind mount thật, payload thật, và hai lượt quét liên tiếp.

Hai cái đầu thuộc đường MQTT; #3–#4 thuộc `AnomalyDetectionService`; #5 là lệch hợp đồng giữa frontend và firmware.

Ba trong bốn cái chỉ lộ ra khi **dữ liệu ở trạng thái nhất định** — nợ #4 chỉ thấy được sau khi
dọn sạch DB, nợ #3 chỉ thấy khi đọc đúng một cột. Đó là lý do nên chạy
`iot-test-lai.sh --reset` chứ không phải chạy chồng lên dữ liệu cũ.

### 1. File `passwd` không tự nạp lại → thiết bị mới KHÔNG đăng nhập được

**Triệu chứng.** Tạo thiết bị trên UI → backend log `MqttPasswordFileSync: đã ghi N bản ghi` → file
trên đĩa có dòng mới, đúng định dạng `$7$` → container mosquitto `grep` cũng thấy dòng đó. Nhưng
thiết bị nối vào thì `Connection Refused: not authorised`. Không có dòng lỗi nào ở bất kỳ đâu.

**Đo được (macOS + Docker Desktop).**

| | Giá trị |
|---|---|
| mtime host | `1786187127` |
| mtime container thấy | `1786186407` — **chậm 720 giây** |
| Số lần vòng `passwd-watch` in ra | **0** |
| Sau khi `docker exec solar-mosquitto kill -HUP 1` | đăng nhập được **ngay** |

**Nguyên nhân.** `MqttPasswordFileSyncService.WriteAtomicallyAsync` ghi file tạm rồi
`File.Move(temp, path, overwrite: true)` — đổi tên, tạo **inode mới**. Đó là lựa chọn đúng để broker
không đọc phải file ghi dở. Nhưng `docker-compose.yml` mount **một file lẻ** cho mosquitto:

```yaml
- ./infra/mqtt/mosquitto/passwd:/mosquitto/config/passwd:ro
```

Nội dung theo kịp, **mtime thì không**. Vòng `passwd-watch` so `stat -c %Y` → thấy không đổi → không
bao giờ gửi SIGHUP → mosquitto giữ nguyên bảng mật khẩu nạp lúc khởi động.

**Hệ quả:** mọi thiết bị tạo sau khi broker khởi động đều câm cho tới khi ai đó restart broker.

**⚠️ Trên Linux có thể TỆ HƠN — chưa đo được.** Bind mount một file lẻ gắn theo **inode**; sau
`File.Move` inode đổi nên container nhiều khả năng thấy **cả nội dung lẫn mtime đều cũ**. Đây là suy
luận từ hành vi đã biết của Docker, **chưa phải số liệu** — phải kiểm trên VPS trước khi ship.

**Hướng sửa.** Mount **thư mục** thay vì file lẻ, để lần đổi tên hiện ra được với container. Phải sửa
cả ba nơi: `backend/docker-compose.yml`, `backend/docker-compose.prod.yml`,
`iot/infra/docker-compose.prod.yml` (file viết ở IOT3-81 dính đúng lỗi này).
Đổi cách ghi sang in-place là **sai hướng** — nó bỏ mất tính nguyên tử vốn đang bảo vệ đúng chỗ.

**Cách chữa cháy tạm:** `docker exec solar-mosquitto kill -HUP 1` sau mỗi lần tạo/xoay thiết bị.

### 2. `DispatchTelemetryAsync` im lặng khi payload sai tên trường

`MqttBridgeBackgroundService.DispatchTelemetryAsync` deserialize payload thành
`BatchIngestSensorReadingsCommand` rồi duyệt `cmd.Items`. Payload dùng sai tên mảng (ví dụ
`"readings"` thay vì `"items"`) sẽ deserialize **thành công** với `Items` rỗng — không ngoại lệ,
không log, không bản ghi nào vào DB.

Đây là kiểu thất bại tệ nhất: firmware báo publish OK (QoS 0), broker chuyển tin OK, cầu nối chạy
OK, chỉ có dữ liệu là không tồn tại. Đã làm mất 15 phút truy vết ngay trong buổi kiểm thử đầu tiên;
ngoài hiện trường thì đó là hàng ngày mất số liệu không ai biết.

**Hướng sửa.** Một dòng trong `DispatchTelemetryAsync`, sau khi deserialize:

```csharp
if (cmd.Items.Count == 0)
{
    _logger.LogWarning(
        "MQTT telemetry từ {DeviceCode} không có mục nào — payload sai tên trường? "
        + "Mảng phải tên `items`. Payload: {Payload}", device.DeviceCode, payload);
    return;
}
```

Cùng lý do đó, `DispatchHeartbeatAsync` nên được rà lại một lượt.

### 3. `PromotedToAlertId` không bao giờ được gán — chuỗi breach mất dấu vết

**Đo được (2026-08-08).** Sau khi test chống nhiễu sinh 3 alert từ 6 breach:
`SELECT count(*) FILTER (WHERE promoted_to_alert_id IS NOT NULL) FROM noise_breach_events` → **0/11**
trên toàn bảng.

**Nguyên nhân.** `AnomalyDetectionService.cs:133` gác lời gọi bằng `if (recordedBreach is not null)`.
Nhưng `ShouldSuppressByNoiseAsync` trả `recorded = null` khi `alreadyRecorded == true`, tức ở lượt
quét LẠI — mà alert của đường chống nhiễu **chỉ nổ ở lượt quét lại**: lượt đầu
`effectiveCount = breachCount + 1` chưa đạt `NoiseSuppressionCount` nên luôn bị chặn. Hai điều kiện
loại trừ nhau: lượt nào có `recordedBreach` thì không nổ alert; lượt nào nổ alert thì nó đã null.

**Hậu quả.** (1) Mất dấu vết kiểm toán "alert này nổ từ chuỗi vi phạm nào" — đúng thứ NS-10/N2 sinh
ra để làm. (2) Nghiêm trọng hơn: XML doc ghi *"retention sẽ giữ các row đã promote"*, mà không row
nào được đánh dấu ⇒ retention sẽ **xoá sạch** chuỗi breach làm bằng chứng cho alert.

**Hướng sửa.** Bỏ gác theo `recordedBreach`, đổi tham số `pendingBreach` thành nullable — hàm đã tự
truy vấn cả chuỗi từ DB, `pendingBreach` chỉ dùng để xử lý riêng row còn pending:

```csharp
if (threshold.NoiseSuppressionEnabled && threshold.NoiseSuppressionCount > 1)
{
    await PromoteBreachChainAsync(
        reading.BatteryAssetId, anomaly.Type, threshold, alert.Id,
        recordedBreach, cancellationToken);   // nhận null
}
```

**Ghi chú kèm — hành vi ĐÚNG, đừng "sửa" nhầm:** số alert của một đợt vi phạm liên tiếp phụ thuộc
nhịp quét, không phải hằng số. Test 6 gói quá áp cho ra 6 breach + 3 alert (không phải 2) vì lượt
quét sau đánh giá lại những reading còn nằm trong tầm lookback. Chống nhiễu vẫn đúng: 3 gói đầu
không sinh alert nào.

### 4. Dedup alert không thấy alert do CHÍNH lượt quét đó vừa tạo

**Đo được (2026-08-08, sau khi dọn sạch DB).** 6 reading quá áp gửi cách nhau 2 giây →
**5 alert `status=1` (Open)**, cùng `battery_asset_id`, cùng `anomaly_type`, trong 9 giây,
`merged_into_alert_id` đều NULL.

```sql
SELECT detected_at, status, merged_into_alert_id FROM alerts
WHERE anomaly_type=2 AND detected_at BETWEEN '2026-08-08 12:16:00+00' AND '2026-08-08 12:17:00+00';
-- 12:16:20 | 1 | (null)
-- 12:16:22 | 1 | (null)
-- 12:16:24 | 1 | (null)
-- 12:16:27 | 1 | (null)
-- 12:16:29 | 1 | (null)
```

**Nguyên nhân.** `FindActiveAlertToMergeAsync` truy vấn **DB** bằng `.FirstOrDefaultAsync()`. Các
alert vừa `AddAsync` trong cùng lượt quét còn **pending trong change tracker**, chưa `SaveChanges`,
nên truy vấn không thấy. Mỗi reading vì thế tự tạo một alert Open mới.

Đây đúng cơ chế mà `ShouldSuppressByNoiseAsync` đã lường trước cho `noise_breach_events` — ghi chú
ngay trong file: *"row pending không được DB đếm"*. `FindActiveAlertToMergeAsync` thì không.

**Vì sao lâu nay không lộ.** Cần có sẵn một alert cùng loại **đã persisted** và còn trong
`DedupWindowEndUtc` thì dedup mới chạy đúng. Lần đo trước đó (DB còn alert cũ từ 11:44) cho ra 12
alert **đều Merged** — nhìn như dedup hoàn hảo. Xoá sạch DB rồi chạy lại mới lộ.
**Nghịch lý: DB càng sạch, lỗi càng dễ thấy** — nên nó sống sót qua cả 657 unit test.

**Hậu quả.** Chống nhiễu chặn được *báo động giả*; nó KHÔNG chặn *báo động trùng*. Một pin lỗi thật
(điện áp vọt và giữ nguyên) đẩy 5–10 alert Open giống hệt nhau vào hàng đợi trực.

**Hướng sửa.** Cho `FindActiveAlertToMergeAsync` nhìn cả phần chưa lưu — kiểm change tracker trước,
rồi mới hỏi DB. Ví dụ giữ một `Dictionary<(Guid assetId, AnomalyTypeEnum type), AlertEntity>` cục bộ
trong một lượt quét, tra nó trước khi gọi DB. Cách khác — `SaveChangesAsync` sau mỗi alert — sửa
được triệu chứng nhưng đánh đổi bằng N round-trip mỗi lượt quét, và làm mất tính nguyên tử của
cả lượt.

**Cách đo lại:** xoá sạch `alerts` + `noise_breach_events` của loại đang thử, gửi ≥6 reading vi phạm
cách nhau 2 giây, rồi đếm `status=1`. Nhiều hơn **1** là lỗi còn nguyên.

### 5. Dropdown "Gửi command" liệt kê 5 loại lệnh mà firmware KHÔNG hiểu loại nào

**Đo được (2026-08-08).** Đối chiếu `IOT_COMMAND_TYPES` (frontend) với `classifyType`
(`iot/firmware-esp32/src/cmd/cmd_logic.cpp:33-39`):

| Dropdown admin liệt kê | Firmware phân loại |
|---|---|
| `reboot` · `ota` · `sample-now` · `calibrate` · `set-config` | **`Unknown`** — cả 5 |

Firmware chỉ hiểu **ba** loại: `set_interval` · `trigger_ota` · `request_heartbeat` (chấp cả biến
thể gạch ngang).

**Chuỗi nhân quả.** XML doc của `IotDeviceCommandPayloadDto` ghi *"reboot | ota | calibrate |
sample-now | set-config | …"* — một danh sách **chưa bao giờ khớp firmware**. Frontend chép nguyên
vào dropdown; `docs/api-battery.md` chép lại lần nữa. Ba nơi cùng sai, và không nơi nào là nguồn
sự thật.

**Vì sao không ai phát hiện.** Admin chọn `reboot` → backend trả **202** + toast *"Đã gửi command"*
→ thiết bị **nhận đúng topic** → ack `status: "unknown"` → backend ghi `LogInformation` → chìm giữa
hàng nghìn dòng log. Mọi tầng báo thành công; chỉ có việc là không xảy ra.

Đây cũng là lý do phép thử downlink ngày 08/08 (dùng `sample-now`) *vẫn kết luận đúng* rằng đường
truyền thông — nhưng nếu là thiết bị thật thì nó đã không làm gì cả.

**Đã sửa (cùng ngày).**
1. `IOT_COMMAND_TYPES` → đúng ba loại.
2. XML doc `IotDeviceCommandPayloadDto` + `docs/api-battery.md` — ghi rõ nguồn sự thật là
   `classifyType`, **không phải tài liệu**.
3. `DispatchCommandAck` — `status` khác `ok` (tức `failed` · `rejected` · `unknown`) nay là
   **`LogWarning`** kèm danh sách ba loại hợp lệ, thay vì `LogInformation` cho mọi thứ.
4. Toast đổi thành *"Đã đẩy lệnh xuống {topic} — thiết bị sẽ báo kết quả riêng"*: **202 không có
   nghĩa là đã thực thi**.

### Bỏ hẳn ô JSON khỏi đường đi thường ngày (09/08/2026)

Bản sửa 08/08 mới thay đúng ba chuỗi trong dropdown, còn hình thức nhập thì giữ nguyên: chọn tên
lệnh dạng mã nguồn (`set_interval`), rồi **tự gõ JSON** vào ô Params. Đó là đẩy việc của lập trình
viên sang người vận hành — và JSON gõ tay hỏng theo những kiểu không ai thấy: `{"pollingSeconds": 5}`
đúng, `{'pollingSeconds': 5}` sai, `[{"pollingSeconds": 5}]` **parse được nhưng firmware bỏ qua**
vì nó đọc `params["pollingSeconds"]` trên một mảng.

Thiết kế lại (`DeviceCommandDialog.tsx`):

| Trước | Sau |
|---|---|
| Dropdown `set_interval` \| `trigger_ota` \| `request_heartbeat` | Ba thẻ chọn, tên tiếng Việt + một dòng mô tả |
| Ô textarea "Params (JSON)" | Nút nhanh 1s/5s/10s/30s/1p/5p + ô số, chỉ hiện với `set_interval` |
| Không kiểm dải giá trị | Chặn tại form theo `kPollingMinSec`/`kPollingMaxSec` = [1, 3600] |
| Không nói hệ quả | Mỗi lệnh kèm điều người bấm cần biết trước (xem dưới) |
| Không biết thiết bị còn sống | Cảnh báo khi thiết bị không ở trạng thái Hoạt động |
| Ô "Khác (tự nhập)" nằm ngay trên form chính | Chuyển vào `<details>` "Tuỳ chọn nâng cao", đóng sẵn |

Ba dòng "hệ quả" đọc ra từ **mã firmware**, không phải từ tài liệu — đây là thứ tên lệnh không nói:

| Lệnh | Điều không đoán được từ tên | Nguồn |
|---|---|---|
| `set_interval` | Chỉ đổi RAM, **mất khi reboot** | `main.cpp:672` gán `s_provCfg.pollingIntervalMs` rồi thôi; `nvsPutInt32` chỉ được gọi lúc provision |
| `request_heartbeat` | Xuống bằng MQTT, **trả lời bằng HTTPS** | `heartbeat.cpp:105` gọi `net::httpPostJson` |
| `trigger_ota` | Bị từ chối khi OTA tắt / đang xác minh bản vừa nạp | `ota_update.cpp:517` |

**Cảnh báo thiết bị offline** không phải trang trí: `PubSubClient.cpp:220` bật cờ Clean Session vô
điều kiện, nên broker **không giữ lệnh hộ** thiết bị đang ngắt kết nối. Gửi lệnh cho máy offline vẫn
được 202, vẫn hiện toast xanh, và lệnh mất luôn — không hề "chờ tới khi thiết bị online lại".

**Chế độ thô giữ lại nhưng dời chỗ.** Vẫn cần đường gửi lệnh mới trước khi giao diện kịp cập nhật,
nhưng nó nằm sau một `<details>` đóng sẵn + một công tắc, nên người dùng thường ngày không gặp JSON.
Đóng phần nâng cao thì form **quay hẳn** về chế độ hướng dẫn — nếu không, có thể còn ở chế độ thô mà
mọi ô nhập của nó bị giấu, tức một cái nút Gửi không rõ sẽ gửi cái gì.

Kiểm tra chuyển hết vào `deviceCommandSchema` (zod) thay vì `JSON.parse` trong `onSubmit`: chặn số
thập phân, số âm, `"5s"`, ngoài dải [1, 3600], và JSON không phải object. `pollingSeconds` **giữ dạng
chuỗi** ở tầng form — `z.coerce.number()` biến `""` thành `0`, tức bỏ trống sẽ báo "phải ≥ 1" thay vì
"chưa nhập".

**Chốt chặn hồi quy — hai bài, đã kiểm chứng ngược:**
- `iot/firmware-esp32/test/test_cmd_logic`: `test_admin_dropdown_legacy_types_are_all_unknown` +
  `test_only_three_supported_types_exist`. Thêm `reboot` vào `classifyType` → test đỏ.
- `BatteryService.IntegrationTests`: `CommandAck_WithUnknownStatus_LogsWarning` (broker thật).
  Lùi bản sửa log → test đỏ.

> ⚠️ **Frontend KHÔNG có test runner** (CLAUDE.md: *"FE — không có test suite, chỉ build + lint"*),
> nên không đặt được chốt chặn ngay tại `IOT_COMMAND_TYPES`. Chốt nằm ở phía firmware: ai thêm loại
> lệnh mới buộc phải sửa `classifyType` trước, và test sẽ nhắc cập nhật cả ba nơi.
>
> Luật của `deviceCommandSchema` (09/08) cũng vì lý do đó **không có test thường trực**. Nó được
> kiểm bằng một bộ 25 trường hợp chạy tạm ngoài repo (`node` v26 chạy thẳng TypeScript), có kiểm
> chứng ngược — gỡ chặn cận trên thì đúng một trường hợp `3601` đỏ — rồi **xoá bỏ**. Sửa schema này
> về sau thì phải dựng lại bộ đó, `tsc`/`eslint`/`build` không bắt được lỗi luật.

**Còn thiếu.** Ack của thiết bị **chưa hiển thị lên UI** — Admin vẫn không có cách nào biết lệnh có
được thực thi hay không, ngoài việc đọc log backend. Cần lưu ack vào DB rồi hiện ở trang chi tiết
thiết bị; đây là việc riêng, chưa làm.
