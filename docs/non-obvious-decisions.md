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
