# Issue Audit — AuthService

> Tổng hợp toàn bộ vấn đề phát hiện qua 4 pass audit sâu của AuthService.
> Tổng cộng: **88 vấn đề** (17 bảo mật · 22 logic/edge case · 26 tính năng thiếu · 16 code quality · 7 test gap)
> Scope audit: `services/AuthService/{src,tests}` + shared infrastructure liên quan auth.

---

## 🔴 Bảo mật (17)

### Critical — fix ngay trong sprint hiện tại

#### 1. `IJwtHelper.IsTokenValid()` → `NotImplementedException`
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:101`
- **Vấn đề:** Method interface throw `NotImplementedException`. Không có endpoint introspect/validate token cho resource server kiểm tra JWT.
- **Hệ quả:** Resource server phải tự verify JWT manually, không có way kiểm tra token bị revoke.
- **Mức:** P1

#### 2. 2FA Disable KHÔNG yêu cầu verify TOTP/password
- **File:** `services/AuthService/src/AuthService.Api/Controllers/AccountsController.cs:365`
- **Vấn đề:** Endpoint `POST /api/accounts/me/2fa/disable` chấp nhận command nhưng handler không yêu cầu verify TOTP hoặc password trước khi disable.
- **Tấn công:** Attacker chiếm session → bypass 2FA ngay lập tức.
- **Fix:** Require TOTP code hoặc password trong `Disable2FACommand`.
- **Mức:** 🔴 P0

#### 3. Enumeration attack tại `LoginCommandHandler`
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/LoginCommandHandler.cs:46-67`
- **Vấn đề:** Audit log ghi rõ "Email không tồn tại" vs "Sai mật khẩu" (response client là chung). Attacker đọc audit hoặc đo response timing → enumerate được email tồn tại.
- **Mức:** 🔴 P0

#### 4. `GoogleCallbackCommandHandler` KHÔNG validate `state` parameter
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/GoogleCallbackCommandHandler.cs:35`
- **Vấn đề:** Handler nhận `request.Code` và `request.RedirectUri` nhưng không validate CSRF state parameter (vi phạm OAuth2 spec).
- **Tấn công:** Attacker craft callback URL → CSRF qua OAuth.
- **Mức:** 🔴 P0

#### 5. `JwtHelper.ValidateToken()` tắt validate quan trọng
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:110-146`
- **Vấn đề:** Set `ValidateIssuer = false`, `ValidateAudience = false`, `ValidateLifetime = false`.
- **Tấn công:** Token confusion attack giữa các service share signing key. Expired token vẫn validate dương tính.
- **Mức:** 🔴 P0

#### 6. CORS `AllowAll` + `AllowCredentials()`
- **File:** `shared/src/SharedInfrastructure/DependencyInjection/Extensions/AddCORS.cs:11-15`
- **Vấn đề:** `SetIsOriginAllowed(origin => true)` + `AllowCredentials()` → bất kỳ origin nào cũng gửi request kèm cookie/auth header.
- **Tấn công:** CSRF/XSS từ attacker site call `/api/auth/logout`, `/api/accounts/me`.
- **Fix:** Whitelist origins cụ thể (`http://localhost:3000`, `https://app.example.com`).
- **Mức:** 🔴 P0

#### 7. OTP/Reset/2FA so sánh string KHÔNG constant-time
- **File:**
  - `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/VerifyOtpCommandHandler.cs:57`
  - `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/Verify2FALoginCommandHandler.cs:111`
  - `VerifyResetOtpCommandHandler`
  - `VerifyPhoneOtpCommandHandler`
- **Vấn đề:** `string.Equals(..., StringComparison.Ordinal)` có timing leak — attacker đo timing để xác định từng ký tự OTP.
- **Fix:** Dùng `CryptographicOperations.FixedTimeEquals()`.
- **Mức:** 🟡 P1

#### 8. `RefreshTokenCommandHandler` không cross-check IP/UA giữa issue & refresh
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/RefreshTokenCommandHandler.cs:82`
- **Vấn đề:** Refresh token bị steal từ network khác vẫn dùng được — không có "device binding".
- **Mức:** 🟡 P1

#### 9. Refresh Token lưu PLAINTEXT trong DB
- **File:** `services/AuthService/src/AuthService.Domain/Entities/RefreshToken.cs:14` · migration `20260427194313`
- **Vấn đề:** Column `token nvarchar(512)` lưu raw Guid string, không hash. DB leak = mọi refresh token còn hạn 7 ngày dùng được ngay.
- **Fix:** SHA-256 hash trước khi lưu, so sánh bằng hash.
- **Mức:** 🔴 P0

#### 10. Password Reset Token KHÔNG single-use
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:148-208`
- **Vấn đề:** `GenerateResetToken` chỉ là JWT signed, `ValidateResetToken` không track "đã dùng". User có thể reuse cùng 1 reset link nhiều lần trong window TTL.
- **Tấn công:** Phishing/share link → attacker reset lại.
- **Mức:** 🔴 P0

#### 11. ForgotPassword brute-force qua nhiều IP
- **File:** `services/AuthService/src/AuthService.Api/Extensions/RateLimitingExtensions.cs:29-38`
- **Vấn đề:** `PolicyAnonOtp` chỉ 5 req/phút/**IP**, không per-email global limit. Attacker xoay IP → flood OTP cho 1 email.
- **Mức:** 🟡 P1

#### 12. OTP entropy 6 số trong TTL 10 phút
- **File:** `services/AuthService/src/AuthService.Application/Interfaces/Helpers/OtpHelper.cs:10`
- **Vấn đề:** 6 số = 10⁶ combination, với rate limit theo IP không theo account → botnet brute-force trong TTL.
- **Fix:** Tăng entropy 8+ số, hoặc per-account rate limit, hoặc exponential backoff.
- **Mức:** 🟡 P1

#### 13. Logout KHÔNG invalidate pending 2FA challenge token
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/LogoutCommandHandler.cs:19-61`
- **Vấn đề:** Flow: login password OK → cấp 2FA challenge (TTL 5 phút) → user logout → challenge token vẫn dùng được → attacker brute-force trong 5 phút.
- **Fix:** Logout phải call `_challengeStore.InvalidateAsync()` trước khi revoke refresh token.
- **Mức:** 🟡 P1

#### 14. JWT permission claims không revoke realtime
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:33-75`
- **Vấn đề:** Permission embed thẳng vào JWT. Admin revoke role → user vẫn dùng quyền cũ tới hết TTL (60 phút).
- **Mức:** 🟡 P1

#### 15. Unique index `Email` THIẾU filter `is_deleted = false`
- **File:** `services/AuthService/src/AuthService.Infrastructure/Persistence/Configurations/AccountConfiguration.cs:147`
- **Vấn đề:** `PhoneNumber` (`:148`) và `GoogleId` (`:149`) đều có `HasFilter("is_deleted = false")`, riêng Email không. Soft-delete account A → user không thể đăng ký lại bằng email cũ.
- **Fix:** `.HasIndex(a => a.Email).IsUnique().HasFilter("\"is_deleted\" = false");`
- **Mức:** 🔴 P0

#### 16. `FullName` / `PendingEmail` không sanitize HTML
- **Files:** `RegisterCommandHandler`, `AcceptInviteCommandHandler`, `ChangeEmailCommandHandler`
- **Vấn đề:** Raw `request.FullName` ghi vào DB rồi inject vào email body. Nếu email render HTML → XSS reflected.
- **Fix:** `HtmlEncode` ở email template layer.
- **Mức:** 🔴 P0

#### 17. `PermissionResolver.GetPermissionCodesAsync()` query 4-bảng-join MỖI lần cấp token
- **File:** `services/AuthService/src/AuthService.Application/Authorization/PermissionResolver.cs:19-40`
- **Vấn đề:** Gọi ở cả `LoginCommandHandler` lẫn `RefreshTokenCommandHandler:86`. Không cache. Concurrent refresh từ nhiều device → DB stress.
- **Fix:** Cache per-role với invalidation khi admin sửa role/permission.
- **Mức:** 🟡 P1

---

## 🟡 Logic / Edge case (22)

### Pass 1

#### 18. Account Status Enum Logic Inconsistency
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/LoginCommandHandler.cs:113-128`
- **Vấn đề:** `Locked` (temp lockout từ failed login) vs `Inactive` (admin deactivate) xử lý không rõ ràng. Không có scheduled job clear lockout.

#### 19. Failed Login OTP Increment Counting
- **Vấn đề:** Auto-lock sau 5 lần fail OTP. Logic reset `FailedLoginAttempts` không rõ giữa OTP vs password path.
- **File cần check:** `VerifyOtpCommandHandler`

#### 20. Google OAuth Email Mismatch Policy Unclear
- **Vấn đề:** Case mơ hồ khi user A đã link Google email X, nay cố link lại email X từ Google account khác.
- **File cần check:** `GoogleAuthCommandHandler` full impl.

#### 21. Google Token Exchange Missing Timeout/Retry
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/GoogleCallbackCommandHandler.cs:35`
- **Vấn đề:** Gọi `ExchangeCodeForIdTokenAsync` không timeout/retry policy. Google API hang → user stuck.

#### 22. 2FA Lazy Re-Encrypt Status Unclear
- **File:** `services/AuthService/src/AuthService.Domain/Entities/Account.cs:47-48`
- **Vấn đề:** Flag `TwoFactorSecretEncryptedAt` chưa rõ behavior nếu crash giữa encryption migration.

### Pass 2

#### 23. `ChangePasswordCommandHandler` KHÔNG check `OldPassword != NewPassword`
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Account/ChangePasswordCommandHandler.cs:59`
- **Vấn đề:** User set new = old vẫn pass — "force revoke session" mà mật khẩu không đổi.

#### 24. `ChangeEmailCommandHandler` không lock/reserve email mới
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Account/ChangeEmailCommandHandler.cs:52-57`
- **Vấn đề:** Race condition — user A request change → user B register cùng email → A confirm OTP fail/conflict.

#### 25. `RegisterCommandHandler` race condition cùng email
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/RegisterCommandHandler.cs:43-45, 66-95, 125`
- **Vấn đề:** 2 request đồng thời cùng email cùng pass check, rơi vào `DbUpdateException`; message catch generic không phân biệt email/phone.

#### 26. `AcceptInviteCommandHandler` không validate `InvitationExpiredAt != null`
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/AcceptInviteCommandHandler.cs:57`
- **Vấn đề:** Nếu `InvitationToken != null` nhưng `InvitationExpiredAt == null` → `HasValue=false` → bỏ qua check expiry → invite không bao giờ hết hạn.

#### 27. `VerifyOtpCommandHandler` dùng `<` thay vì `<=`
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/VerifyOtpCommandHandler.cs:51`
- **Vấn đề:** Edge case on-exact-expiry vẫn pass.

#### 28. RefreshToken rotation không serialize TTL
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/RefreshTokenCommandHandler.cs:96`
- **Vấn đề:** Đổi config `RefreshTokenExpirationDays` ở production → token cũ rotate với TTL cũ, token mới TTL mới → inconsistency audit.

### Pass 3

#### 29. AuditLog "append-only" KHÔNG enforce ở DB
- **File:** `services/AuthService/src/AuthService.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs:7-98`
- **Vấn đề:** Không có CHECK constraint, không có `INSTEAD OF UPDATE/DELETE` trigger, không có RLS policy, không có hash-chain chống tamper. Chỉ rely application code.

#### 30. `DeleteAccount` KHÔNG cascade các table phụ
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Account/DeleteAccountCommandHandler.cs:51-56`
- **Vấn đề:** Chỉ soft-delete Account + revoke refresh + publish event. Backup codes, OTP records, AuditLog vẫn giữ FK. Không anonymize PII (email, phone, fullName) → vi phạm GDPR "right to be forgotten".

#### 31. `PendingEmail` không có cleanup nếu user không confirm
- **File:** `services/AuthService/src/AuthService.Domain/Entities/Account.cs:39`
- **Vấn đề:** Change-email flow set PendingEmail, nếu user không verify OTP → PendingEmail tồn tại mãi. Không có background job hay logic auto-clear.

#### 32. `RefreshTokenExpirationDays = 7` hardcode
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Services/AuthTokenIssuer.cs:15`
- **Vấn đề:** Không lấy từ config (trong khi `AccessTokenExpirationMinutes` đọc từ `JwtSettings`). Inconsistent.

#### 33. `LastLoginIp` semantic mơ hồ
- **Vấn đề:** `RefreshTokenCommandHandler:109-110` update LastLoginIp **mỗi lần refresh**. `LoginCommandHandler:186` chỉ update sau pass 2FA. Hai semantic khác nhau cho cùng field → audit/security forensic sai.

### Pass 4

#### 34. `GenericRepository.UpdateAsync()` dùng `_dbSet.Update(entity)`
- **File:** `shared/src/SharedInfrastructure/Persistence/Repositories/GenericRepository.cs:36-39`
- **Vấn đề:** Đánh dấu TẤT CẢ column là Modified. Race condition: 2 admin sửa 2 field khác nhau cùng lúc → last-write-win.
- **Fix:** `Entry().Property().IsModified = true` riêng từng field, hoặc thêm RowVersion.

#### 35. `GenericRepository.GetAllAsync()` KHÔNG mặc định `AsNoTracking()`
- **Vấn đề:** Handler nào quên gọi `.AsNoTracking()` sẽ track toàn bộ entity → memory pressure khi list lớn (vd `AdminListAccountsQuery` 1000 records).

#### 36. ValidationBehavior chạy SAU khi handler đã query DB
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/LoginCommandHandler.cs:48-51`
- **Vấn đề:** Handler query Account trước → gọi `ValidateAsync()` sau. Nếu validation fail → DB query lãng phí. Pattern nên là MediatR `ValidationBehavior` chạy TRƯỚC handler.

#### 37. `OutboxRelayBackgroundService` có honor `CancellationToken` không?
- **File:** `services/AuthService/src/AuthService.Api/Program.cs:37`
- **Vấn đề:** Nếu loop `while(true)` không check `stoppingToken` → container shutdown kill giữa chừng publish → mất message hoặc duplicate.

#### 38. `DateTime.UtcNow` gọi nhiều lần trong cùng transaction
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/RefreshTokenCommandHandler.cs:95-96`
- **Vấn đề:** `IssuedAt = DateTime.UtcNow, ExpiredAt = DateTime.UtcNow.AddDays(7)` — sai số nhỏ, không mock được.
- **Fix:** Inject `TimeProvider`/`IClock` (.NET 8+).

#### 39. Email/PhoneNumber normalize không nhất quán
- **Vấn đề:** Chưa thấy chỗ chung trim + lowercase email trước save/query. PostgreSQL default case-sensitive → register `User@Example.com` rồi login `user@example.com` có thể miss.
- **File cần check:** `RegisterCommandHandler` có `.ToLowerInvariant()` không.

---

## ❌ Tính năng còn thiếu (26)

### Pass 1

#### 40. Token introspection / blacklist endpoint — P1
- JWT stateless, logout không invalidate access token cho đến hết TTL.
- Cần Redis blacklist + middleware check.

#### 41. Concurrent session limit (max N devices) — P2
- Security best practice, prevent attacker từ fake session.
- `SessionCreatedNotification` đề cập "enforce limit" nhưng không thấy impl.

#### 42. Account cleanup job (hard-delete soft-deleted) sau 90 ngày — P2
- **File:** `services/AuthService/src/AuthService.Api/Controllers/AccountsController.cs:499`
- Doc nói 90-day retention + cleanup job, nhưng job không tồn tại.

#### 43. Lockout auto-unlock scheduled job — P2
- Hiện tại login handler check manual khi user login lại. Cần background job hoặc grace period.

#### 44. Session Device ID Tracking — P2
- `RefreshToken.DeviceId` có populate nhưng logic hash User-Agent derive ID chưa rõ.
- Thiếu query "show sessions from device X" / "revoke all except current device".

#### 45. Backup Code Recovery Attempt Limit — P2
- Có rate limit endpoint regenerate, nhưng KHÔNG limit số lần thử sai backup code khi 2FA login.
- 8 codes × 5 attempts/challenge → low entropy brute-force.

#### 46. Email Change Rate Limiting — P2
- `/api/accounts/me/change-email` không rate limit (trong khi phone change có `PolicyAuthOtp` 3 req/min).

#### 47. Account Merge/Consolidation — P3
- Login Google + local riêng → tạo 2 account riêng. Không có admin merge.

#### 48. IP/UA Whitelist — P3
- Không có "trusted device" / "skip 2FA nếu IP familiar".

#### 49. Expired OTP Auto-Clean Job — P2
- DB tích tụ OTP cũ (Account.OtpCode + OtpExpiredAt).

#### 50. Account Reactivation After Soft-Delete — P2
- DeleteMe soft-delete account. Không có endpoint user restore trong 90 ngày window.

#### 51. Cross-Device 2FA Confirmation — P3
- Setup device A (init → QR) → confirm từ device B (typical email confirmation link).

#### 52. Suspicious Login Alert — P2
- Có LoginAttempt + AuditLog tracking IP/UA. Thiếu logic "unusual location detected" → email alert / require 2FA.

#### 53. Password Strength Policy Configurable — P2
- Hardcode "8-100 ký tự, chữ hoa + thường + số + special".
- Thiếu config-driven policy.

#### 54. Token Revocation List (TRL) — P1
- Logout: access token vẫn valid đến hết TTL.
- Cần BlacklistToken endpoint + Redis cache + middleware check.

#### 55. Admin Forced Logout (Single User) — P2
- `AdminRevokeAccountSessionsCommand` có. Cần verify endpoint expose.

#### 56. Account Notification Preferences — P3
- "Email me on login from new IP", "email me on 2FA setup".

#### 57. Admin Account Unlock — P2
- `UnlockAccountCommand` có. Cần verify endpoint `/api/admin/accounts/{id}/unlock` expose.

#### 58. SMS OTP fallback cho 2FA — P1
- Chỉ có TOTP, không có SMS/email fallback khi mất authenticator app.

### Pass 3

#### 59. JWT không có `kid` header / key rotation — P2
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:33-75`
- Single static signing key. Key leak hoặc rotate định kỳ → invalidate toàn bộ token cùng lúc.

#### 60. Health checks chuẩn k8s — P1
- **File:** `services/AuthService/src/AuthService.Api/Program.cs`
- Không có `app.MapHealthChecks("/health")`, `/ready` cho orchestrator probe.

#### 61. API versioning — P2
- Flat `/api/...`, không có `/api/v1/...`. Breaking change tương lai force migrate hết client.

#### 62. Endpoint export account data — P1 (GDPR)
- GDPR "right to data portability" — không có `/api/accounts/me/export`.

#### 63. Multi-tenancy / OrgId / TenantId — P2
- Account không có `OrgId`/`TenantId`. B2B (Solar Battery cho nhiều khách hàng) không isolate data giữa tenant.

#### 64. Recovery khi mất cả phone + backup codes — P1
- Chỉ có admin reset 2FA. Thiếu self-serve identity-verification (KYC, ID document, security questions).

#### 65. Optimistic concurrency cho Account update — P2
- Không thấy `RowVersion`/`xmin` column trong `Account.cs` hoặc `AccountConfiguration`.
- Hai admin update cùng 1 account → last-write-win.

### Pass 4

#### 66. `IValidateOptions<JwtSettings>` fail-fast startup — P1
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:30`
- Throw `InvalidOperationException` CHỈ KHI `GenerateAccessToken()` được gọi. Service start OK với config thiếu key.
- **Fix:** `services.AddOptions<JwtSettings>().ValidateDataAnnotations().ValidateOnStart()`.

#### 67. Idempotency middleware đăng ký nhưng handler chưa "đọc" idempotency key
- **File:** `services/AuthService/src/AuthService.Api/Program.cs:34` (`AddIdempotencyKey()`)
- Doc Register/ResendOtp nói cache 24h. Cần verify `SharedInfrastructure/Idempotency/*` có thật sự dedupe response hay chỉ pass-through.

---

## ⚠️ Code quality / Inconsistency (16)

### Pass 2

#### 68. `LoginCommandHandler` không set `LastLoginIp`/`LastLoginAt`
- **File:** `services/AuthService/src/AuthService.Application/CQRS/Handler/Auth/LoginCommandHandler.cs`
- `RefreshTokenCommandHandler:110` có set. Sai lệch first login vs refresh.

#### 69. `Account.RoleId` non-nullable, default = `Guid.Empty`
- **File:** `services/AuthService/src/AuthService.Domain/Entities/Account.cs:74`
- Handler phải check `RoleId == Guid.Empty` thay vì `null`. Không type-safe, dễ miss khi viết code mới.

#### 70. `PasswordHasher` đăng ký Singleton
- **File:** `services/AuthService/src/AuthService.Infrastructure/DependencyInjection/ManageDependencyInjection.cs:78`
- Nếu future config động (work factor từ DB) → cache config cũ. Nên Scoped.

#### 71. HTTPS redirect bị skip trong Docker
- **File:** `services/AuthService/src/AuthService.Api/Program.cs:121-125`
- Docker skip HTTPS redirect. Kết hợp CORS AllowAll → nguy hiểm: HTTP-only + cross-origin credentials.

#### 72. `IJwtHelper.IsTokenValid()` dead method
- **File:** `services/AuthService/src/AuthService.Infrastructure/Implements/Helpers/JwtHelper.cs:99-102`
- Throw `NotImplementedException` nhưng vẫn lộ ra interface. Nên xóa hoặc implement.

### Pass 3

#### 73. Error response không có error code chuẩn
- Chỉ có `Message` (string thô). Client không identify lỗi stable (vd `AUTH_INVALID_CREDENTIALS`, `AUTH_2FA_REQUIRED`).
- FE phải parse message → breaks khi đổi text.

#### 74. `OtpHelper.GenerateOtp` dùng `Random` hay `RandomNumberGenerator`?
- **File cần check:** `services/AuthService/src/AuthService.Application/Interfaces/Helpers/OtpHelper.cs:10`
- Nếu dùng `Random` (default seeded) → predictable.

#### 75. Migration không có composite index trên `(Email, IsDeleted)`
- Query login pattern phổ biến `Where(x => x.Email == email && !x.IsDeleted)`.
- Table account lớn → full scan.

#### 76. GlobalExceptionMiddleware log full stacktrace
- **File:** `GlobalExceptionMiddleware.cs:60-62`
- 500 errors return generic message nhưng `logger.LogError()` ghi full stack → leak paths, SQL, internal type names. Không PII masking.

#### 77. Không có Correlation ID xuyên suốt request
- Request đi qua AuthService → publish RabbitMQ → consumer service khác, không có trace ID end-to-end.

#### 78. Không có metric custom cho auth domain
- Chỉ có default ASP.NET metrics. Thiếu: login success/fail rate, 2FA challenge fail rate, OTP usage.
- Khó alert "tăng đột biến login fail" để phát hiện brute-force.

#### 79. Refresh token rotation không log "reuse detection event"
- Khi family bị revoke do reuse, không có event publish để security team alert.

### Pass 4

#### 80. `ClockSkew` mâu thuẫn middleware vs helper
- JWT auth middleware (`AddJWTAuthenticationAuthorization.cs:32`): `ClockSkew = TimeSpan.Zero` (chặt).
- `JwtHelper.ValidateToken()` (`JwtHelper.cs:117-122`): không set → mặc định **5 phút** (lỏng).
- Hai code path validate token khác semantic → bug khó debug.

#### 81. `async/await` patterns chưa kiểm grep
- `.Result`/`.Wait()` block thread (deadlock risk in ASP.NET).
- `throw ex` (mất stack trace) thay vì `throw`.
- `async void` ngoài event handler.

#### 82. Lockout reconcile race
- Login handler check manual khi user login. Không có grace period/event boundary cho user login exactly at boundary.

#### 83. `PasswordHasher` Singleton + future config risk
- Tách riêng khỏi #70 vì hậu quả khác: state-leak nếu thêm cache giữa request.

---

## 🧪 Test gap (7)

### Pass 2 — Missing test files

#### 84. Không có `ChangeEmailCommandHandlerTests.cs`
- Flow đổi email không có unit test coverage.

#### 85. Không có `AcceptInviteCommandHandlerTests.cs`
- Invite flow không được kiểm thử tự động.

#### 86. Không có `GoogleCallbackCommandHandlerTests.cs`
- Google OAuth flow không test.

### Pass 4 — Behavior verification missing (bổ sung)

#### 87. Không test verify `ChangePassword` thực sự gọi `RevokeAllSessionsCommand`
- Nếu sau này dev xóa logic revoke, test vẫn pass.

#### 88. Không có integration test cho `Outbox` event publish loop
- Nếu outbox loop bị broken, không test bắt được.

#### 89. Không có perf test cho `PermissionResolver`
- N+1 query không bị phát hiện cho đến production.

#### 90. Không có dedicated test cho `ChangePasswordCommandHandler`
- Verify old password + revoke sessions logic không cover riêng.

---

## Tổng kết theo nhóm

| Nhóm | Số lượng |
|------|----------|
| 🔴 Bảo mật critical/high | 17 |
| 🟡 Logic/Edge case | 22 |
| ❌ Tính năng thiếu | 24 |
| ⚠️ Code quality/Inconsistency | 16 |
| 🧪 Thiếu test | 7 (4 file thiếu + 3 verify behavior bổ sung pass 4) |
| **TỔNG** | **86** (raw — chưa loại trùng tuyệt đối; thực tế ~50-60 ticket độc lập khi gom) |

---

## Top 10 ưu tiên fix ngay (P0)

1. **#9** Hash refresh token trong DB (không lưu plaintext)
2. **#2** 2FA disable yêu cầu verify password/TOTP
3. **#4** OAuth `state` CSRF validation
4. **#5** JWT `ValidateToken` bật issuer/audience/lifetime
5. **#6** CORS thay AllowAll bằng whitelist origin
6. **#10** Reset token single-use
7. **#15** Email unique index thêm filter `is_deleted = false`
8. **#13** Logout invalidate 2FA challenge token
9. **#7** OTP/2FA constant-time compare (`CryptographicOperations.FixedTimeEquals`)
10. **#16** FullName HTML sanitize trong email template

## P1 — Operational hardening (sprint kế tiếp)

- **#17** PermissionResolver cache
- **#80** ClockSkew unify giữa middleware và helper
- **#66** `IValidateOptions<JwtSettings>` ValidateOnStart
- **#37, #42, #43, #49** Background jobs: graceful shutdown, hard-delete, OTP cleanup, lockout reconcile
- **#60** Health check k8s

## P2 — Feature gap (sprint sau nữa)

- **#40, #54** Token introspection + blacklist
- **#41, #44** Session/device limit
- **#30, #62** GDPR export + anonymize trên delete
- **#63** Multi-tenancy / OrgId
- **#61** API versioning

---

> **Ghi chú:** Đây là danh sách raw từ 4 pass audit. Một số mục có quan hệ nhân quả (vd #14 permission revoke realtime + #17 PermissionResolver cache cùng gốc nhưng giải pháp khác nhau). Khi tạo GitHub issue thực tế nên gom thành ~50-60 ticket độc lập, group theo theme (security batch / ops batch / feature batch).

---

# 📜 PHỤ LỤC A — Kiến trúc AuditLog Hybrid (Decentralized + Aggregator)

> Phụ lục này phân tích chi tiết kiến trúc audit log toàn hệ thống GSU26SE55 theo **Lựa chọn 3: Hybrid (Decentralized + Aggregator)**. Mục tiêu: mỗi service own audit data, đồng thời có 1 view tổng hợp cho admin/compliance.

## A.1. Tổng quan kiến trúc

```
┌─────────────────────────┐     ┌─────────────────────────┐     ┌─────────────────────────┐
│      AuthService        │     │     BatteryService      │     │      TicketService      │
│  ┌──────────────────┐   │     │  ┌──────────────────┐   │     │  ┌──────────────────┐   │
│  │ Business Tx      │   │     │  │ Business Tx      │   │     │  │ Business Tx      │   │
│  │  + audit insert  │   │     │  │  + audit insert  │   │     │  │  + audit insert  │   │
│  └────────┬─────────┘   │     │  └────────┬─────────┘   │     │  └────────┬─────────┘   │
│           │             │     │           │             │     │           │             │
│  ┌────────▼─────────┐   │     │  ┌────────▼─────────┐   │     │  ┌────────▼─────────┐   │
│  │ auth_audit_logs  │   │     │  │battery_audit_logs│   │     │  │ticket_audit_logs │   │
│  └────────┬─────────┘   │     │  └────────┬─────────┘   │     │  └────────┬─────────┘   │
│           │             │     │           │             │     │           │             │
│  ┌────────▼─────────┐   │     │  ┌────────▼─────────┐   │     │  ┌────────▼─────────┐   │
│  │ Outbox Pattern   │   │     │  │ Outbox Pattern   │   │     │  │ Outbox Pattern   │   │
│  └────────┬─────────┘   │     │  └────────┬─────────┘   │     │  └────────┬─────────┘   │
└───────────┼─────────────┘     └───────────┼─────────────┘     └───────────┼─────────────┘
            │                               │                               │
            └───────────────┬───────────────┴───────────────────────────────┘
                            │
                ┌───────────▼────────────┐
                │     RabbitMQ Topic     │
                │  exchange.audit.events │
                │    (fanout / topic)    │
                └───────────┬────────────┘
                            │
                ┌───────────▼────────────────────────────┐
                │     AuditAggregatorService             │
                │  (.NET 8 Worker / BackgroundService)    │
                │  ┌──────────────────────────────────┐  │
                │  │ MassTransit Consumer per event   │  │
                │  │   ↓ map adapter (per service)    │  │
                │  │   ↓ enrich (correlation, geo IP) │  │
                │  │   ↓ dedupe (event_id PK)         │  │
                │  └────────────────┬─────────────────┘  │
                └───────────────────┼────────────────────┘
                                    │
                       ┌────────────▼─────────────┐
                       │   Read-Store (chọn 1):   │
                       │  • PostgreSQL + partition│  ◄── Đề xuất cho capstone
                       │    + GIN index (metadata)│
                       │  • TimescaleDB hypertable│  ◄── Nếu volume lớn
                       │  • ElasticSearch         │  ◄── Nếu cần full-text + log analytics
                       └────────────┬─────────────┘
                                    │
                       ┌────────────▼─────────────┐
                       │   AuditAggregator API    │
                       │  GET /api/admin/audit/search   │
                       │  GET /api/admin/audit/export   │
                       │  GET /api/admin/audit/stats    │
                       └────────────┬─────────────┘
                                    │
                       ┌────────────▼─────────────┐
                       │      Admin Web UI        │
                       │  (Audit Explorer panel)  │
                       └──────────────────────────┘
```

**Tóm tắt 3 layer:**

| Layer | Component | Vai trò |
|-------|-----------|--------|
| **Write** | Mỗi service có audit table riêng + Outbox table | Atomic với business transaction, **source of truth** |
| **Transport** | RabbitMQ topic exchange `exchange.audit.events` | Async delivery, at-least-once |
| **Read** | AuditAggregatorService + Read-store (Postgres/TimescaleDB) | Materialized view tổng hợp, query nhanh |

## A.2. Nguyên tắc kiến trúc

### A.2.1. Source of truth ở đâu?

- **DB của từng service** là source of truth (atomic, không bao giờ mất)
- **Read-store của aggregator** chỉ là **materialized view** — có thể rebuild lại từ source bằng replay
- Nếu read-store hỏng hoàn toàn → vẫn truy vấn được audit qua API riêng của từng service (`GET /api/admin/audit-logs` mỗi service)

### A.2.2. Tại sao cần Outbox Pattern?

Nếu publish event ngay sau commit (không có outbox):

```
BEGIN TX → INSERT audit → COMMIT TX → PublishEvent()  ← nếu broker down ở bước này, audit ghi rồi mà event không publish → aggregator miss
```

**Giải pháp Outbox:**

```
BEGIN TX
  INSERT audit_logs (...)
  INSERT outbox (event_id, payload, status='Pending')   ← cùng transaction
COMMIT TX

Background OutboxRelay loop:
  SELECT * FROM outbox WHERE status='Pending'
  PublishToBroker(payload)
  UPDATE outbox SET status='Published' WHERE event_id=...
```

→ **Đảm bảo at-least-once delivery**, không mất event ngay cả khi broker tạm down.

### A.2.3. At-least-once + idempotent consumer

Vì broker có thể redeliver → aggregator có thể nhận trùng event. Cần dedupe ở consumer:

- `event_id` (Guid v7) là **primary key** trong read-store
- INSERT … ON CONFLICT DO NOTHING (PostgreSQL upsert)
- Hoặc check `event_id` đã tồn tại trước khi process

### A.2.4. Eventual consistency

- Có độ trễ **vài giây → vài chục giây** giữa lúc audit ghi và lúc xuất hiện trên admin UI
- Chấp nhận được cho audit/compliance — không yêu cầu real-time
- Nếu cần real-time → fallback gọi API trực tiếp service đó

## A.3. Schema chi tiết

### A.3.1. Schema chuẩn (mọi service tuân theo)

```sql
-- Mỗi service tạo bảng *_audit_logs với schema gốc:
CREATE TABLE {service}_audit_logs (
    id                  UUID         PRIMARY KEY,
    event_id            UUID         NOT NULL UNIQUE,  -- = id, dùng để dedupe ở aggregator
    service_name        VARCHAR(50)  NOT NULL,         -- 'Auth', 'Battery', 'Ticket', 'File'
    action_code         VARCHAR(100) NOT NULL,         -- 'AccountRegistered', 'BatteryAssigned', ...
    action_category     VARCHAR(50)  NOT NULL,         -- 'Authentication', 'AccountLifecycle', 'TicketLifecycle', ...
    severity            VARCHAR(20)  NOT NULL,         -- 'Info', 'Warning', 'Critical', 'Security'

    -- Subject (đối tượng bị tác động)
    target_type         VARCHAR(50),                   -- 'Account', 'Battery', 'Ticket', 'File'
    target_id           UUID,                          -- ID đối tượng
    target_display      VARCHAR(255),                  -- email, serial, ticket code (snapshot)

    -- Actor (người gây ra)
    actor_account_id    UUID,                          -- nullable nếu system action
    actor_role          VARCHAR(50),                   -- snapshot role lúc action
    actor_display       VARCHAR(255),                  -- email/fullname snapshot

    -- Outcome
    is_success          BOOLEAN      NOT NULL,
    error_code          VARCHAR(50),                   -- 'INVALID_OTP', 'PERMISSION_DENIED'
    reason              VARCHAR(500),

    -- Context
    ip_address          VARCHAR(45),
    user_agent          VARCHAR(500),
    device_id           VARCHAR(128),
    correlation_id      UUID,                          -- xuyên suốt request → trace multi-service
    causation_id        UUID,                          -- event upstream gây ra event này

    -- Payload
    metadata_json       JSONB,                         -- old/new value, extra context

    -- Timing
    occurred_at         TIMESTAMPTZ  NOT NULL,         -- thời điểm action thực sự xảy ra
    recorded_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW()  -- thời điểm ghi DB

    -- KHÔNG có UpdatedAt, IsDeleted, DeletedAt — APPEND-ONLY
);

-- Indexes chuẩn
CREATE INDEX ix_{service}_audit_target ON {service}_audit_logs(target_id, occurred_at DESC);
CREATE INDEX ix_{service}_audit_actor ON {service}_audit_logs(actor_account_id, occurred_at DESC);
CREATE INDEX ix_{service}_audit_action ON {service}_audit_logs(action_code, occurred_at DESC);
CREATE INDEX ix_{service}_audit_correlation ON {service}_audit_logs(correlation_id);
CREATE INDEX ix_{service}_audit_occurred ON {service}_audit_logs(occurred_at DESC);
CREATE INDEX ix_{service}_audit_metadata_gin ON {service}_audit_logs USING GIN(metadata_json);

-- DB trigger enforce append-only
CREATE OR REPLACE FUNCTION fn_audit_immutable() RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'audit_logs is append-only: % blocked', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_{service}_audit_no_update
    BEFORE UPDATE ON {service}_audit_logs
    FOR EACH ROW EXECUTE FUNCTION fn_audit_immutable();

CREATE TRIGGER trg_{service}_audit_no_delete
    BEFORE DELETE ON {service}_audit_logs
    FOR EACH ROW EXECUTE FUNCTION fn_audit_immutable();
```

### A.3.2. Outbox table (mỗi service)

```sql
CREATE TABLE audit_outbox (
    id              UUID         PRIMARY KEY,
    event_id        UUID         NOT NULL UNIQUE,
    event_type      VARCHAR(100) NOT NULL,           -- 'AuditCreated'
    payload         JSONB        NOT NULL,
    status          VARCHAR(20)  NOT NULL DEFAULT 'Pending',  -- Pending, Published, Failed
    retry_count     INT          NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at    TIMESTAMPTZ,
    last_error      VARCHAR(2000)
);

CREATE INDEX ix_audit_outbox_pending ON audit_outbox(status, created_at) WHERE status = 'Pending';
```

### A.3.3. Read-store của Aggregator (PostgreSQL — đề xuất cho capstone)

```sql
-- Bảng tổng hợp, partition theo tháng để xoá dữ liệu cũ nhanh
CREATE TABLE audit_aggregate (
    event_id            UUID         PRIMARY KEY,
    service_name        VARCHAR(50)  NOT NULL,
    action_code         VARCHAR(100) NOT NULL,
    action_category     VARCHAR(50)  NOT NULL,
    severity            VARCHAR(20)  NOT NULL,
    target_type         VARCHAR(50),
    target_id           UUID,
    target_display      VARCHAR(255),
    actor_account_id    UUID,
    actor_role          VARCHAR(50),
    actor_display       VARCHAR(255),
    is_success          BOOLEAN      NOT NULL,
    error_code          VARCHAR(50),
    reason              VARCHAR(500),
    ip_address          VARCHAR(45),
    user_agent          VARCHAR(500),
    device_id           VARCHAR(128),
    correlation_id      UUID,
    causation_id        UUID,
    metadata_json       JSONB,
    occurred_at         TIMESTAMPTZ  NOT NULL,
    recorded_at         TIMESTAMPTZ  NOT NULL,
    ingested_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    -- Geo enrichment (optional)
    geo_country         VARCHAR(2),
    geo_city            VARCHAR(100)
) PARTITION BY RANGE (occurred_at);

-- Partition mẫu (tự động hoá qua pg_partman)
CREATE TABLE audit_aggregate_y2026m06 PARTITION OF audit_aggregate
    FOR VALUES FROM ('2026-06-01') TO ('2026-07-01');

-- Indexes
CREATE INDEX ix_agg_service ON audit_aggregate(service_name, occurred_at DESC);
CREATE INDEX ix_agg_actor ON audit_aggregate(actor_account_id, occurred_at DESC);
CREATE INDEX ix_agg_target ON audit_aggregate(target_id, occurred_at DESC);
CREATE INDEX ix_agg_action ON audit_aggregate(action_code, occurred_at DESC);
CREATE INDEX ix_agg_correlation ON audit_aggregate(correlation_id);
CREATE INDEX ix_agg_severity ON audit_aggregate(severity, occurred_at DESC) WHERE severity IN ('Critical', 'Security');
CREATE INDEX ix_agg_metadata_gin ON audit_aggregate USING GIN(metadata_json);
```

## A.4. Integration Event

### A.4.1. Event payload (SharedContracts)

```csharp
// shared/src/SharedContracts/IntegrationEvents/AuditCreatedEvent.cs
namespace SharedContracts.IntegrationEvents.Audit;

public record AuditCreatedEvent : IntegrationEvent
{
    public Guid EventId { get; init; }                        // = audit_log.id (idempotency key)
    public string ServiceName { get; init; } = string.Empty;  // 'Auth' / 'Battery' / 'Ticket' / 'File'
    public string ActionCode { get; init; } = string.Empty;
    public string ActionCategory { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info";

    public string? TargetType { get; init; }
    public Guid? TargetId { get; init; }
    public string? TargetDisplay { get; init; }

    public Guid? ActorAccountId { get; init; }
    public string? ActorRole { get; init; }
    public string? ActorDisplay { get; init; }

    public bool IsSuccess { get; init; }
    public string? ErrorCode { get; init; }
    public string? Reason { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? DeviceId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }

    public Dictionary<string, object>? Metadata { get; init; }

    public DateTime OccurredAt { get; init; }
    public DateTime RecordedAt { get; init; }
}
```

### A.4.2. RabbitMQ topology

```
Exchange: exchange.audit.events       (type: topic, durable: true)
Routing key pattern: audit.{service}.{category}.{action}
  Example: audit.auth.authentication.login_success
           audit.battery.lifecycle.threshold_changed
           audit.ticket.sla.breached

Queue: queue.audit-aggregator.events  (durable: true, x-dead-letter-exchange: exchange.audit.dlq)
Binding: audit.#  (consume tất cả)

DLQ: exchange.audit.dlq → queue.audit-aggregator.dlq (manual replay)
```

**Lý do dùng topic exchange:** sau này nếu cần thêm consumer (vd `SecurityAlertService` chỉ consume `audit.*.security.*`) thì không phải sửa producer.

## A.5. Implementation chi tiết từng phần

### A.5.1. Mỗi service cần làm gì?

**Phase 1 — Service đã có sẵn AuthService.AuditLog làm mẫu:**

1. Refactor `AuthService.AuditLog` để đồng bộ schema chuẩn:
   - Thêm `event_id`, `service_name`, `action_category`, `severity`, `target_type`, `target_display`, `actor_role`, `actor_display`, `error_code`, `causation_id`, `occurred_at`, `recorded_at`
   - Migration: rename column cũ, default value cho row đã có
2. Thêm `audit_outbox` table + `OutboxRelayBackgroundService`
3. `AuditTrailNotificationHandler`: ngoài INSERT audit_log → INSERT outbox cùng transaction
4. Apply trigger append-only cho `auth_audit_logs`

**Phase 2 — Các service khác (Battery/Ticket/File):**

1. Tạo entity `{Service}AuditLog` follow schema chuẩn
2. Tạo enum `{Service}AuditActionEnum`
3. Tạo `{Service}AuditTrailNotification` (MediatR record)
4. Tạo `{Service}AuditTrailNotificationHandler`:
   - Resolve actor từ `IHttpContextAccessor` (JWT claims)
   - Resolve IP/UA/correlation từ context
   - Insert audit_log + outbox cùng transaction business
5. `OutboxRelayBackgroundService` riêng cho service
6. Migration + trigger append-only
7. **Local admin endpoint** — theo **Option C policy** ở §A.5.1.bis bên dưới (KHÔNG mỗi service đều có).

---

### A.5.1.bis. Endpoint Local Policy — Option C (Minimal local + Aggregator)

**Nguyên tắc:** Mỗi service KHÔNG có "nhóm endpoint audit riêng" giống AuthService hiện tại. Chỉ service **critical** mới expose **1 endpoint local duy nhất** làm fallback + service-specific filter. Cross-service / advanced queries (stats / export / correlation trace / account timeline) đi qua `AuditAggregatorService`.

**Phân loại service:**

| Service | Local endpoint | Lý do |
|---------|---------------|-------|
| **AuthService** | ✅ Đã có (giữ nguyên 2 endpoint hiện tại) | Security investigation, real-time, AuditActionEnum specific |
| **BatteryService** | ✅ Build mới — `GET /api/admin/battery/audit-logs` | IoT incident response, forensic anomaly tracking |
| **TicketService** | ✅ Build mới — `GET /api/admin/ticket/audit-logs` | SLA/escalation investigation, compliance (tách khỏi `TicketActivity` UI timeline) |
| **FileStorageService** | ✅ Build mới — `GET /api/admin/files/audit-logs` | File access compliance, GDPR investigation |
| **AlertService** → **host trong BatteryService** (D14) | ✅ Build mới — `GET /api/admin/alerts/audit-logs` (route qua `batteryCluster`) | Alert acknowledge/suppress history. **Chốt 2026-06-24: KHÔNG tách Alert service riêng cho capstone — `AlertAuditLog`/outbox/relay/controller nằm trong BatteryService** |
| **EmailService** | ❌❌ **AUDIT DESCOPED 2026-06-25** (`#AUDIT-33`) | Delivery log, đã trace gián tiếp qua audit service gốc; thiếu `.Application`/`.Domain` layer |
| **NotificationService** | ❌ Skip — qua Aggregator | Push log low-criticality |
| **SmsService** | ❌ Skip — qua Aggregator | Đã có 8 action, không cần admin UI mới |
| **AI Module** | ❌❌ **AUDIT DESCOPED 2026-06-25** (`#AUDIT-35` phần AI) | Repo Python riêng, ML observability; anomaly→ticket đã audit ở TicketService |
| **Gateway** | ❌❌ **AUDIT DESCOPED 2026-06-25** (`#AUDIT-35` phần Gateway) | Request log đã có ở observability; login/permission denied đã audit ở Auth |

**Tổng: 5 service có local endpoint (1 endpoint mỗi service) + AuditAggregatorService central API.**

**Spec cho local endpoint (5 service):**
- Route: `GET /api/admin/{service-name}/audit-logs`
- Auth: `[Authorize(Roles = "Admin")]`
- Query trực tiếp `{service}_audit_logs` table (KHÔNG qua aggregator)
- Filter: `action` (service-specific enum), entity ID (vd `batteryId`/`ticketId`), `from`/`to`, `pageSize` (default 50, max 100), `pageNumber`
- Response shape: `CommonResponse<PaginationResponse<{Service}AuditLogDto>>`
- KHÔNG có: stats, export CSV, by-correlation, account-timeline (những thứ này đi qua Aggregator)

**Lý do design:**
1. **Resilience** — Aggregator down → admin vẫn xem được service-local audit (critical cho incident response).
2. **Real-time** — local endpoint serve trực tiếp source table → 0ms lag, vs Aggregator p99 ~10s (outbox + RabbitMQ + consumer).
3. **Service-specific filter** — `BatteryAuditActionEnum`/`TicketAuditActionEnum` chi tiết hơn aggregator string `action_code`.
4. **Minimal boilerplate** — 1 endpoint × 5 service = 5 endpoints, không phải full CRUD × 10 service = 50 endpoints.
5. **Tuân B.0 #4** — "Source-of-truth ở từng service" → service self-serve quyền cơ bản.
6. **Tuân B.0 #9** — "Aggregator KHÔNG được phép viết ngược về service" → aggregator chỉ bổ sung capability, không thay thế.

**Khi nào KHÔNG cần xóa AuthService endpoint cũ:**

Spec sau khi áp Option C, AuthService giữ 2 endpoint hiện tại (`GET /api/admin/audit-logs` + `/by-account/{id}`). KHÔNG migrate sang `/api/admin/auth/audit-logs` để tránh breaking FE Admin UI. Service mới (Battery/Ticket/File/Alert) bắt buộc dùng prefix mới `{service}/audit-logs`.

**Boilerplate ước tính per service mới: ~150 line code** (controller + query DTO + handler + response DTO + unit test).

**Pattern code cho Handler publish audit:**

```csharp
// Trong CommandHandler (vd BatteryCreateCommandHandler)
await _unitOfWork.BeginTransactionAsync();
try
{
    await _unitOfWork.Batteries.AddAsync(entity);

    // Publish audit trong cùng transaction
    await _mediator.Publish(new BatteryAuditTrailNotification
    {
        ActionCode = BatteryAuditActionEnum.BatteryCreated,
        ActionCategory = "Lifecycle",
        Severity = "Info",
        TargetType = "Battery",
        TargetId = entity.Id,
        TargetDisplay = entity.SerialNumber,
        IsSuccess = true,
        Metadata = new() { ["chemistry"] = entity.Chemistry, ["capacity"] = entity.Capacity }
    }, ct);

    await _unitOfWork.CommitTransactionAsync();
}
catch (Exception ex)
{
    await _unitOfWork.RollbackTransactionAsync();
    throw;
}
```

### A.5.2. AuditAggregatorService — thiết kế chi tiết

**Project structure:**

```
services/AuditAggregatorService/
├── src/
│   ├── AuditAggregator.Api/             ← REST API cho admin query
│   ├── AuditAggregator.Application/     ← MediatR queries, DTOs
│   ├── AuditAggregator.Domain/          ← AuditAggregate entity, enums
│   ├── AuditAggregator.Infrastructure/  ← DbContext, EF Config, Consumer
│   └── AuditAggregator.Worker/          ← BackgroundService consume RabbitMQ
└── tests/
```

**Consumer logic:**

```csharp
public class AuditCreatedConsumer : IConsumer<AuditCreatedEvent>
{
    private readonly IAuditAggregateRepository _repo;
    private readonly IGeoIpResolver _geoIp;

    public async Task Consume(ConsumeContext<AuditCreatedEvent> context)
    {
        var evt = context.Message;

        // 1. Idempotency check
        if (await _repo.ExistsAsync(evt.EventId))
            return;

        // 2. Map → AuditAggregate entity
        var aggregate = AuditAggregate.FromEvent(evt);

        // 3. Enrich (geo IP, parse UA)
        if (!string.IsNullOrEmpty(evt.IpAddress))
        {
            var geo = await _geoIp.LookupAsync(evt.IpAddress);
            aggregate.GeoCountry = geo?.CountryCode;
            aggregate.GeoCity = geo?.City;
        }

        // 4. Insert (ON CONFLICT DO NOTHING — race condition safe)
        await _repo.UpsertAsync(aggregate);
    }
}
```

**API endpoint:**

```
GET  /api/admin/audit/search?service=&action=&category=&severity=&actorId=&targetId=&from=&to=&correlationId=&page=&size=
GET  /api/admin/audit/{eventId}                                ← chi tiết 1 event
GET  /api/admin/audit/correlation/{correlationId}              ← trace cross-service theo correlation
GET  /api/admin/audit/account/{accountId}/timeline             ← timeline 1 user
GET  /api/admin/audit/stats?from=&to=&groupBy=service|action|severity
GET  /api/admin/audit/export?format=csv|json&...                ← streaming export
POST /api/admin/audit/replay?service=&from=&to=                ← admin replay từ source-of-truth khi read-store hỏng
```

**Authorization:** chỉ role `Admin` (chốt 2026-06-24 — role `SecurityOfficer` đã **gộp vào `Admin`** cho capstone scope, KHÔNG tạo role mới; xem A.9 D13).

> **UX filter cho admin non-tech — giải pháp A+E (chốt 2026-06-26, xem A.9 D17):** Các filter tập-đóng không dùng free-string trôi nổi (gõ sai → `200` rỗng âm thầm, hiểu nhầm "không có gì"):
> - **A (FE):** `service`/`severity`/`category`/`targetType`/`groupBy`/`format` render **dropdown/typeahead** từ `as const` enum tĩnh (giá trị canonical trong `docs/api-audit.md`) — admin **chọn**, không gõ. KHÔNG cần endpoint lấy enum (đúng pattern toàn app).
> - **E (BE):** `/search` + `/export` **validate exact-match** `severity`/`category` bằng `Severities.All`/`AuditCategories.All` (SharedContracts.Audit) → sai value/sai hoa-thường → **`400` + `listErrors[{field,detail}]`** kèm danh sách giá trị đúng. `service`/`action` không validate (free / 100+ mã) → chỉ FE dropdown. Đồng bộ `redact` (400+listErrors) + AuthService `/api/admin/audit-logs` (param enum tự validate).

> **Lưu ý Option C (xem §A.5.1.bis):** Aggregator API **KHÔNG THAY THẾ** local endpoint của 5 service critical (Auth/Battery/Ticket/File/Alert). Aggregator BỔ SUNG capability cross-service + advanced query (stats/export/correlation/timeline). Phân chia trách nhiệm:
>
> | Use case | Endpoint nào |
> |----------|--------------|
> | "Account X làm gì TRONG AuthService" | `GET /api/admin/audit-logs/by-account/{id}` (AuthService local) |
> | "Battery X audit history" | `GET /api/admin/battery/audit-logs?batteryId=X` (BatteryService local) |
> | "Account X làm gì TOÀN HỆ THỐNG (Auth + Battery + Ticket + File + Alert)" | `GET /api/admin/audit/account/{id}/timeline` (Aggregator) |
> | "Trace 1 request xuyên service theo CorrelationId" | `GET /api/admin/audit/correlation/{id}` (Aggregator) |
> | "Stats severity distribution + dashboard" | `GET /api/admin/audit/stats` (Aggregator) |
> | "Export 100k row CSV" | `GET /api/admin/audit/export` (Aggregator) |
> | "Investigation khi Aggregator down" | Local endpoint (resilience fallback) |
> | "Real-time check vừa-mới-xảy-ra" | Local endpoint (0ms lag) |
> | "Cross-service correlation" | Aggregator (single source) |

### A.5.3. CorrelationId & CausationId — trace multi-service

- **CorrelationId**: 1 user request từ FE → AuthService cấp token → BatteryService update → audit cả 2 service phải có cùng `correlation_id`
  - Middleware `CorrelationIdMiddleware`: đọc header `X-Correlation-Id`, nếu không có → tạo Guid v7, set vào `HttpContext.Items["CorrelationId"]`
  - Mọi event/MassTransit message propagate header này (`SendHeaders.Set("X-Correlation-Id", ...)`)
- **CausationId**: event B do event A gây ra → `B.causation_id = A.event_id`
  - Ví dụ: `BatteryAnomalyDetectedEvent` (event_id=X) → consumer ở TicketService tạo ticket → audit `TicketAutoCreated` có `causation_id=X`

Aggregator có endpoint `/api/admin/audit/correlation/{correlationId}` trả về timeline đầy đủ cross-service.

### A.5.4. Read-store technology decision

| Tech | Pros | Cons | Khuyến nghị |
|------|------|------|------------|
| **PostgreSQL + partition** | Đơn giản, tận dụng infra có sẵn, GIN index cho JSON, dễ deploy | Full-text search yếu, scale ngang khó | ✅ **Capstone scope** |
| **TimescaleDB** | Hypertable tự partition theo thời gian, compression, query thời gian nhanh | Thêm phụ thuộc | ✅ Nếu volume > 10M event/tháng |
| **ElasticSearch** | Full-text search mạnh, dashboard Kibana | Phức tạp ops, tốn RAM | ❌ Over-engineering cho capstone |
| **ClickHouse** | OLAP cực nhanh, compression tốt | Schema cứng nhắc, ops phức tạp | ❌ Future scale |

**Quyết định cho dự án GSU26SE55**: PostgreSQL + partition theo tháng + `pg_partman` cho auto-partition.

### A.5.5. Retention & cleanup

- **Source-of-truth (mỗi service)**: giữ 1 năm (sau đó archive sang cold storage S3/Glacier nếu cần)
- **Read-store aggregator**: giữ 6 tháng (drop partition cũ → nhanh, không cần DELETE)
- **Compliance critical event** (P1 incident, data breach, permission grant): giữ vĩnh viễn ở `audit_aggregate_critical` partition riêng (severity='Critical' or 'Security')

**Background job retention:**

```csharp
public class AuditRetentionBackgroundService : BackgroundService
{
    // Daily 02:00 UTC
    // 1. Drop partition aggregate cũ > 6 tháng (trừ Critical/Security)
    // 2. Service-side: DELETE audit_logs WHERE occurred_at < NOW() - INTERVAL '1 year'
    //    (trừ severity = 'Critical'|'Security')
    // 3. Log số row xoá vào audit_log chính nó (action: AuditRetentionExecuted)
}
```

### A.5.6. PII & GDPR

- **Hash IP trong aggregate**: SHA256(ip + salt) — vẫn cluster được theo IP nhưng không lộ IP raw
- **Email/Display lưu snapshot tại thời điểm action** → khi user xoá account, vẫn còn audit nhưng PII bị mask sau N ngày
- **GDPR right-to-forget**: endpoint `POST /api/admin/audit/redact?accountId=` mask actor_display, target_display, ip_address, user_agent — KHÔNG xoá row (giữ tham chiếu cho audit chain)

### A.5.7. Hash chain (optional, advanced)

```
audit_aggregate có thêm column hash_prev, hash_curr:
  hash_curr = SHA256(event_id || occurred_at || action_code || actor_id || target_id || hash_prev)

Job daily verify chain: scan partition, recompute hash, alert nếu lệch.
```

→ Tamper-evident: nếu admin DB sửa 1 row → hash sai → phát hiện được.

## A.6. Implementation phases (roadmap)

### Phase 0 — Chuẩn bị (1 sprint)

- [ ] Tạo `SharedContracts.IntegrationEvents.AuditCreatedEvent`
- [ ] Tạo `SharedKernel.Audit.AuditableActionBase` (interface chung)
- [ ] Thiết kế chuẩn schema + ADR document `docs/adr/0007-audit-hybrid-architecture.md`
- [ ] Quyết định read-store technology (đề xuất PostgreSQL)
- [ ] Thiết kế RabbitMQ topology (exchange, queue, DLQ)

### Phase 1 — Refactor AuthService (1 sprint)

- [ ] Migration: thêm column mới vào `auth_audit_logs` theo schema chuẩn
- [ ] Migration: tạo `audit_outbox` table
- [ ] Update `AuditTrailNotificationHandler` → insert outbox cùng transaction
- [ ] Tạo `AuditOutboxRelayBackgroundService`
- [ ] Apply trigger append-only
- [ ] Fix 22 handler chưa publish audit (đã liệt kê ở Pass 1-4)
- [ ] Test: publish event → verify ở RabbitMQ admin UI

### Phase 2 — Setup AuditAggregatorService (1 sprint)

- [ ] Scaffold project structure
- [ ] Setup DbContext + migration `audit_aggregate` + partition
- [ ] `AuditCreatedConsumer` + idempotency
- [ ] Geo IP enrichment (✅ chốt MaxMind GeoLite2 free — D11)
- [ ] REST API: search, by-eventId, by-correlation, account-timeline, stats, export
- [ ] Authorization: role `Admin` (✅ `SecurityOfficer` gộp Admin — D13)
- [ ] Health check k8s
- [ ] Docker image + deploy config

### Phase 3 — Onboard BatteryService (1 sprint)

- [ ] Entity `BatteryAuditLog` + enum `BatteryAuditActionEnum` (~12 action)
- [ ] Notification + handler
- [ ] Outbox + relay
- [ ] Publish audit ở các handler quan trọng: Create, Update, Delete, AssignCustomer, ThresholdChange, SensorEdit
- [ ] Migration + trigger
- [ ] **Local endpoint (Option C):** `GET /api/admin/battery/audit-logs` (filter: action, batteryId, from/to, paging) — fallback khi Aggregator down + battery-specific filter
- [ ] Test E2E: action → DB → outbox → broker → aggregator → query API

### Phase 4 — Onboard TicketService (1 sprint)

- [ ] Tách `TicketAuditLog` riêng (giữ `TicketActivity` cho UI timeline)
- [ ] Enum `TicketAuditActionEnum` (~21 action — đã liệt kê)
- [ ] Notification + handler
- [ ] Outbox + relay
- [ ] Publish audit ở: state transition, priority, SLA pause/resume/breach, escalation, maintenance log, comment, attachment
- [ ] Migration + trigger
- [ ] **Local endpoint (Option C):** `GET /api/admin/ticket/audit-logs` (filter: action, ticketId, from/to, paging) — SLA/escalation investigation real-time
- [ ] Special: `causation_id` cho ticket tự tạo từ `BatteryAnomalyDetectedEvent`

### Phase 5 — Onboard FileStorage + Alert + Notification + Sms (1 sprint) — ~~Email/AI/Gateway~~ DESCOPED 2026-06-25

- [ ] `FileAuditLog` (Upload, Download, Delete, AccessDenied, PresignedUrlGenerated)
- [ ] **Local endpoint (Option C) cho FileStorageService:** `GET /api/admin/files/audit-logs` — compliance + GDPR file access investigation
- [ ] `AlertAuditLog` (Acknowledged, Suppressed, RuleChanged)
- [ ] **Local endpoint (Option C) cho AlertService:** `GET /api/admin/alerts/audit-logs` — alert acknowledge/suppress history
- [x] ~~EmailService audit (EmailSent, Failed, Bounced)~~ **❌ DESCOPED 2026-06-25** (`#AUDIT-33`) — delivery log, đã trace gián tiếp qua audit service gốc; EmailService thiếu `.Application`/`.Domain` layer. Xem Decision Log overall.md §17.
- [ ] NotificationService audit (Push sent/failed) — **KHÔNG có local endpoint** (skip per Option C)
- [x] SmsService bổ sung 3 audit action ✅ (`#AUDIT-35` phần Sms — done) — **KHÔNG có local endpoint** (skip per Option C)
- [x] ~~AI Module audit (5 action) + Gateway audit (3 action)~~ **❌ DESCOPED 2026-06-25** (`#AUDIT-35` phần AI/Gateway) — AI là repo Python (ML observability); Gateway `RequestRouted` volume cao + đã trùng audit Auth. Xem Decision Log overall.md §17.

### Phase 6 — Admin Web UI Audit Explorer (1 sprint, FE)

- [ ] Page `/admin/audit` với filter panel (service, action, severity, actor, target, time range)
- [ ] Timeline view cho 1 user (`/admin/accounts/{id}/audit-timeline`)
- [ ] Correlation trace view (`/admin/audit/trace/{correlationId}`)
- [ ] Export CSV/JSON
- [ ] Stats dashboard (Recharts: action count by hour, top actors, severity distribution)

### Phase 7 — Hardening (1 sprint)

- [ ] Retention background service
- [ ] GDPR redaction endpoint
- [ ] Hash chain (optional)
- [ ] Performance test: 1000 event/giây không drop
- [ ] DLQ replay tool (`POST /api/admin/audit/replay/dlq`)
- [ ] Monitoring: Prometheus metric (`audit_events_total`, `audit_consumer_lag`, `audit_outbox_pending`)
- [ ] Documentation cuối cùng

## A.7. Rủi ro & mitigation

| Rủi ro | Likelihood | Impact | Mitigation |
|--------|-----------|--------|------------|
| Outbox loop chậm → backlog | Medium | Medium | Tăng số worker, batch publish 100 event/lần, monitor `outbox_pending` |
| RabbitMQ down | Low | High | Outbox giữ event → broker up lại tự replay; multi-broker cluster |
| Aggregator consumer crash | Medium | Low | At-least-once + idempotent → safe; auto-restart bằng k8s |
| Read-store hỏng | Low | High | Source-of-truth ở từng service vẫn còn; có endpoint replay từ source |
| Event schema breaking change | High | High | Versioning event (`AuditCreatedEventV1`, `V2`); consumer hỗ trợ multi-version |
| Volume spike (1M event/ngày) | Medium | Medium | Partition theo tháng, GIN index, archive cold partition |
| PII leak qua aggregator | Medium | High | Hash IP, mask email khi user delete, role-based access |
| Tamper từ DB admin | Low | High | Trigger append-only, hash chain optional, monitoring DDL |
| Duplicate event do retry | High | Low | Idempotency PK trên `event_id`, ON CONFLICT DO NOTHING |
| Correlation ID không propagate | Medium | Medium | Middleware bắt buộc, integration test |
| Outbox table phình to | High | Low | Cleanup published > 7 ngày, partition theo tuần |

## A.8. Test strategy

### Unit test
- Mỗi `{Service}AuditTrailNotificationHandler` test: resolve actor/IP/correlation đúng, build payload đúng
- `OutboxRelayBackgroundService`: test publish OK / fail / retry
- `AuditCreatedConsumer`: test idempotency, dedupe

### Integration test (TestContainers)
- Spin Postgres + RabbitMQ trong test
- E2E: trigger command → kiểm tra `audit_logs` + `outbox` + RabbitMQ message + `audit_aggregate` đều khớp

### Performance test
- Load 1000 event/giây trong 5 phút → metric:
  - Outbox publish lag < 5 giây p99
  - Aggregator ingest lag < 10 giây p99
  - Query `/api/admin/audit/search` < 200ms p95

### Chaos test
- Kill RabbitMQ giữa lúc đang publish → verify outbox replay đúng
- Kill aggregator → restart → verify không miss event, không duplicate
- DB read-store full disk → verify outbox không drop event

## A.9. Tóm tắt quyết định kiến trúc (decisions log)

| # | Decision | Lý do |
|---|----------|------|
| D1 | Hybrid (decentralized + aggregator) | Cân bằng giữa microservice principle và admin UX |
| D2 | Outbox pattern bắt buộc mỗi service | Tránh mất event khi broker down |
| D3 | At-least-once + idempotent consumer | Đơn giản, đủ chính xác cho audit |
| D4 | PostgreSQL + partition cho read-store | Tận dụng infra, đủ cho capstone scope |
| D5 | RabbitMQ topic exchange `exchange.audit.events` | Cho phép mở rộng consumer sau này (SecurityAlert, SIEM…) |
| D6 | `event_id` Guid v7 làm idempotency key | Time-sortable, không collide |
| D7 | Append-only enforce ở DB trigger | Tamper-evident layer cuối |
| D8 | `correlation_id` + `causation_id` xuyên suốt | Trace cross-service flow |
| D9 | Schema chuẩn + metadata_json flexible | Cân bằng query + extensibility |
| D10 | Source-of-truth ở từng service, không phải aggregator | Read-store có thể rebuild nếu hỏng |
| D11 | **Geo IP = MaxMind GeoLite2 free** (chốt 2026-06-24) | File `.mmdb` tra local, không rate-limit; enrichment optional, fallback null |
| D12 | **OutboxRelay = Leader election qua Redis** (chốt 2026-06-24, §B.10 option 1) | `IDistributedCache` lease key `audit_outbox_leader`, renew 30s, non-leader skip |
| D13 | **KHÔNG tạo role `SecurityOfficer` — gộp vào `Admin`** (chốt 2026-06-24) | Capstone scope; tránh thêm role thứ 5 + migration seed. Aggregator + GDPR redact dùng `[Authorize(Roles="Admin")]` |
| D14 | **`AlertAuditLog` host trong BatteryService** (`batteryCluster`) (chốt 2026-06-24) | Không tách Alert service riêng cho capstone (resolve `#AUDIT-31/32`) |
| D15 | **Retention: source 1 năm / aggregate 6 tháng / Critical+Security vĩnh viễn** (ratify 2026-06-24) | Đúng ADR-0007, đủ cho capstone |
| D16 | **Owner = Thắng (`@Alexdev257`)**; gate "ổn định ≥ 2 tuần" waived (2026-06-24) | Sole-dev, 3 hard-blocker code (`#AUTH-29/77/15`) đã merge → kick-off Phase 0 ngay |
| D17 | **UX filter Audit Explorer cho admin non-tech = A+E** (chốt 2026-06-26) | A: FE dropdown/typeahead từ `as const` enum tĩnh cho mọi field tập-đóng (admin chọn, không gõ → không cần endpoint lấy enum). E: BE validate exact-match `severity`/`category` bằng `Severities.All`/`AuditCategories.All` → sai value/case trả `400` + `listErrors` thay vì `200` rỗng âm thầm (foot-gun forensic). Bỏ B (case-insensitive — lệch chuẩn match exact toàn hệ thống; dropdown đã gửi đúng case) + bỏ D (facets endpoint — over-engineering). `service`/`action` chỉ FE dropdown (không có `.All`). Áp `/search` + `/export`. Đồng bộ AuthService `/api/admin/audit-logs` (enum tự validate) + `redact` (400+listErrors). Xem `docs/api-audit.md`. |

## A.10. Coverage gap đã liệt kê (xem thêm phía trên cùng phụ lục này)

Tổng cộng **~89 action thiếu audit** trên 10 service:
- AuthService: 22 handler chưa publish · local endpoint ✅ giữ nguyên (đã có)
- BatteryService: 12 action (tạo từ scratch) · local endpoint ✅ build mới (Option C)
- TicketService: 21 action (tách TicketAuditLog riêng) · local endpoint ✅ build mới (Option C)
- NotificationService: 7 action · ❌ KHÔNG local endpoint (qua Aggregator only)
- ~~EmailService: 5 action~~ · **❌ DESCOPED 2026-06-25** (`#AUDIT-33`)
- FileStorageService: 6 action · local endpoint ✅ build mới (Option C)
- SmsService: bổ sung 3 action (đã có 8) ✅ DONE · ❌ KHÔNG local endpoint mới (qua Aggregator)
- AlertService: 5 action · local endpoint ✅ build mới (Option C) · **host trong BatteryService — D14, không tách service riêng**
- ~~AI Module: 5 action~~ · **❌ DESCOPED 2026-06-25** (`#AUDIT-35` phần AI)
- ~~Gateway: 3 action~~ · **❌ DESCOPED 2026-06-25** (`#AUDIT-35` phần Gateway)

**Local endpoint per Option C: 5 service** (Auth giữ nguyên + 4 service build mới: Battery/Ticket/File/Alert). Chi tiết policy + lý do xem §A.5.1.bis.

## A.11. Effort estimation (summary view)

> Bảng này là tổng quan theo phase. Chi tiết task-level breakdown ở **§B.19**. Số baseline sync với §B.19 (42 dev-day sau khi chi tiết hóa, tăng từ ước tính ban đầu 37).

| Phase | Baseline (dev-day) | Local endpoint delta | Total | Ghi chú |
|-------|--------------------|--------------------|-------|--------|
| Phase 0 — Chuẩn bị + ADR | 3 | — | **3** | — |
| Phase 1 — Refactor AuthService | 7 | 0 | **7** | Auth giữ nguyên 2 endpoint hiện tại, không build mới |
| Phase 2 — AuditAggregatorService | 8.5 | — | **8.5** | Aggregator central API, không phải local |
| Phase 3 — BatteryService onboard | 3 | +0.5 | **3.5** | 1 local endpoint mới `/api/admin/battery/audit-logs` (~150 LOC) |
| Phase 4 — TicketService onboard | 5 | +0.5 | **5.5** | 1 local endpoint mới `/api/admin/ticket/audit-logs` |
| Phase 5 — File + Alert + Notification + Sms onboard (~~Email/AI/Gateway~~ descoped 2026-06-25) | 4 | +1.0 | **5** | 2 local endpoint mới (File + Alert × 0.5 day); Notification/Sms KHÔNG có local endpoint |
| Phase 6 — Admin Web UI | 6 | — | **6** | FE gọi cả local + aggregator |
| Phase 7 — Hardening + perf test | 5 | — | **5** | — |
| **Tổng** | **42** | **+2** | **44** | ≈ 8-9 sprint với 1 BE + 0.5 FE |

**Boilerplate per local endpoint mới (~150 LOC):**
- Controller (`Admin{Service}AuditLogsController.cs`): ~30 LOC
- Query DTO + handler: ~70 LOC
- Response DTO: ~30 LOC
- Unit test (filter + paging happy path): ~20 LOC

**Total local endpoint LOC mới: 4 service × ~150 = ~600 LOC** (Auth giữ 0 LOC mới; Battery + Ticket + File + Alert mỗi service ~150 LOC).

## A.12. Kết luận phụ lục

Kiến trúc **Hybrid Audit** kết hợp:
1. **Decentralized write** (mỗi service own audit data, atomic với business transaction)
2. **Outbox pattern** đảm bảo at-least-once delivery
3. **Centralized read** qua AuditAggregatorService + PostgreSQL partitioned store
4. **Schema chuẩn** + `metadata_json` flexible
5. **Correlation/Causation** trace cross-service
6. **Append-only DB trigger** + optional hash chain cho tamper-evident
7. **Eventually consistent** — chấp nhận lag vài giây cho admin view

Đây là pattern **production-ready, scalable, defend-able trước hội đồng KLTN** — không over-engineering nhưng vẫn đúng best practice. Phù hợp với scope GSU26SE55 (4 BE service + 1 AI + 1 Gateway + Mobile + Web).

---

# 📖 PHỤ LỤC B — IMPLEMENTATION PLAYBOOK (Pre-flight Checklist)

> **Mục tiêu phụ lục này:** Cung cấp đầy đủ chi tiết kỹ thuật, edge case, gotcha và code pattern cụ thể để team implement kiến trúc Hybrid Audit **KHÔNG SAI SÓT**. Đọc kỹ trước khi bắt đầu bất kỳ phase nào. Mỗi mục có cảnh báo ⚠️ là bug đã xảy ra trong production thực tế tại các project khác.

---

## B.0. Nguyên tắc bất di bất dịch (Inviolable Principles)

Trước khi viết bất kỳ dòng code nào, team PHẢI đồng thuận 10 nguyên tắc sau. Mỗi vi phạm = bug khó debug + tốn time refactor:

1. **Audit log + business data PHẢI cùng 1 database transaction.** Không bao giờ publish event TRƯỚC commit, không bao giờ ghi audit sau commit business.
2. **Audit table là APPEND-ONLY tuyệt đối.** Không UPDATE, không DELETE, không soft-delete. Trigger DB enforce.
3. **`event_id` (Guid v7) là primary key idempotency** xuyên suốt từ source → outbox → broker → aggregator. Không bao giờ generate lại.
4. **Source-of-truth ở từng service, KHÔNG ở aggregator.** Aggregator là materialized view, có thể rebuild bất cứ lúc nào.
5. **At-least-once delivery + idempotent consumer.** Chấp nhận duplicate event, không bao giờ exactly-once.
6. **CorrelationId BẮT BUỘC có** trong mọi audit. Nếu request không mang header → middleware tự generate Guid v7.
7. **Thời gian luôn UTC, kiểu `DateTimeOffset` hoặc `TIMESTAMPTZ`.** Không bao giờ `DateTime.Now`, không bao giờ lưu `TIMESTAMP` (without timezone).
8. **PII chỉ lưu plaintext ở source, mask/hash ở aggregator** trước khi exposed qua API.
9. **Aggregator KHÔNG được phép viết ngược về service.** Aggregator chỉ read-only consumer + read API.
10. **Schema event là contract bất biến.** Thay đổi = bump version (`V1` → `V2`), KHÔNG thay đổi event hiện có.

> ⚠️ **Vi phạm nguyên tắc 1:** Service A commit business → publish event → broker timeout → audit không bao giờ ghi → 6 tháng sau audit hỏi cũng không có. **Đây là bug #1 trong audit systems.**

---

## B.1. Terminology Lock — định nghĩa chuẩn

Để tránh confusion khi 5 dev cùng làm:

| Thuật ngữ | Định nghĩa chính xác | Ví dụ |
|-----------|---------------------|-------|
| **Audit Event** | 1 hành động xảy ra trong hệ thống cần ghi lại | "User X login thành công" |
| **Action Code** | Mã định danh duy nhất của loại hành động, format `PascalCase`, max 100 chars | `LoginSuccess`, `BatteryThresholdChanged` |
| **Action Category** | Nhóm chức năng của action | `Authentication`, `AccountLifecycle`, `TicketLifecycle`, `Permission`, `DataAccess` |
| **Severity** | Mức nghiêm trọng — fixed enum 4 giá trị | `Info`, `Warning`, `Critical`, `Security` |
| **Actor** | Người gây ra action (user thực hiện hoặc system nếu auto) | `actor_account_id = X` hoặc `null + actor_display = "System"` |
| **Target** | Đối tượng bị tác động bởi action | Account/Battery/Ticket/File |
| **CorrelationId** | ID xuyên suốt 1 user request, đi qua nhiều service | Guid v7 sinh ở entry point |
| **CausationId** | ID của event/message gây ra event hiện tại | Event B do consume Event A → `B.causation_id = A.event_id` |
| **Source of truth** | Bảng audit local của service ghi ra event | `auth_audit_logs` |
| **Read-store** | Bảng tổng hợp ở aggregator phục vụ query | `audit_aggregate` |
| **Outbox** | Bảng trung gian lưu event chờ publish | `audit_outbox` |
| **Materialized view** | Bản copy read-only được tổng hợp từ source | `audit_aggregate` |
| **At-least-once** | Event được deliver ít nhất 1 lần, có thể nhiều lần | RabbitMQ default |
| **Idempotent consumer** | Consumer xử lý cùng 1 event N lần → kết quả như 1 lần | INSERT ON CONFLICT DO NOTHING |
| **Replay** | Đọc lại từ source-of-truth → re-publish hoặc re-insert vào read-store | `POST /api/admin/audit/replay` |

> ⚠️ **Không dùng từ "log" loosely.** "Log" có thể là `ILogger` console log, file log, hoặc audit log — luôn phải prefix: "console log", "audit log", "ticket activity log".

---

## B.2. Schema Standardization — deep dive

### B.2.1. Action Code naming convention (BẮT BUỘC)

**Format:** `{EntityOrFlow}{Action}{Modifier?}` — PascalCase, không space, không dấu.

```
✅ ĐÚNG:
  LoginSuccess
  LoginFailedWrongPassword
  LoginFailedAccountLocked
  PasswordChanged
  BatteryCreated
  BatteryThresholdChanged
  TicketStateTransitioned
  TicketSlaPaused
  TicketSlaBreached

❌ SAI:
  login_success        (snake_case không nhất quán C# enum)
  Login_Success        (mixed)
  LOGIN_SUCCESS        (SQL style không hợp Roslyn)
  loginsuccess         (không đọc được)
  "login success"      (có space)
  LoginOK              (mơ hồ — OK là gì?)
  UserDidLogin         (verb form mơ hồ)
```

**Rule cụ thể:**
1. Bắt đầu bằng entity hoặc flow domain
2. Theo sau là past-tense verb (`Created`, `Updated`, `Deleted`, `Changed`, `Sent`, `Failed`, `Approved`, `Rejected`)
3. Modifier tùy chọn để phân biệt fine-grain (`LoginFailed**WrongPassword**` vs `LoginFailed**AccountLocked**`)
4. Max 100 chars (DB constraint)
5. Unique toàn hệ thống (không 2 service dùng cùng action code khác nghĩa)

> ⚠️ **Đăng ký action code ở 1 nơi tập trung** — file `SharedContracts/Audit/ActionCodeRegistry.cs` chứa `public static class ActionCodes { public const string LoginSuccess = "LoginSuccess"; ... }`. Compile-time check, tránh typo.

### B.2.2. Action Category — fixed enum

Chỉ được dùng **9 category** sau, không thêm bừa:

```csharp
public static class AuditCategories
{
    public const string Authentication    = "Authentication";    // Login, logout, 2FA, OTP
    public const string AccountLifecycle  = "AccountLifecycle";  // Register, update profile, delete account, deactivate
    public const string Authorization     = "Authorization";     // Role assign, permission grant/revoke
    public const string SecurityEvent     = "SecurityEvent";     // Suspicious login, breach attempt, lockout
    public const string DataAccess        = "DataAccess";        // File upload/download, sensitive query
    public const string DataMutation      = "DataMutation";      // Create/update/delete business entity
    public const string TicketLifecycle   = "TicketLifecycle";   // Ticket state, SLA, escalation
    public const string Communication     = "Communication";     // Email/SMS/Push sent
    public const string SystemOperation   = "SystemOperation";   // Background jobs, retention, replay
}
```

> ⚠️ **Tại sao fix enum?** Nếu cho free string → 6 tháng sau có 30 category trùng lặp (`auth`, `Auth`, `Authentication`, `authentication`, `Login`) → query GROUP BY vỡ.

### B.2.3. Severity — định nghĩa CHÍNH XÁC

Đây là điểm dễ nhất gây tranh cãi. Định nghĩa cứng:

| Severity | Khi nào dùng | Ví dụ | Retention |
|----------|--------------|-------|-----------|
| `Info` | Action thành công bình thường, không cần alert | `LoginSuccess`, `BatteryCreated`, `TicketCommented` | 6 tháng |
| `Warning` | Action thất bại non-malicious hoặc bất thường nhẹ | `LoginFailedWrongPassword` (1-2 lần), `EmailDeliveryFailed`, `OtpExpired` | 1 năm |
| `Critical` | Business event ảnh hưởng lớn cần forensic | `TicketSlaBreached`, `PermissionGranted`, `RoleChanged`, `AccountDeleted`, `BatteryThresholdChanged` | Vĩnh viễn (or 7 năm) |
| `Security` | Sự kiện security cần SOC team review | `LoginFailedAccountLocked` (5+ lần), `TokenReuseDetected`, `Unauthorized2FADisable`, `SuspiciousLocation` | Vĩnh viễn |

**Rule cứng:**
- 1 action code chỉ thuộc 1 severity duy nhất — không bao giờ runtime decide
- `Critical` và `Security` → KHÔNG bị drop bởi retention job
- Bảng quy chiếu lưu trong `ActionCodeRegistry.cs`:
  ```csharp
  public static readonly Dictionary<string, string> ActionSeverity = new()
  {
      [ActionCodes.LoginSuccess] = Severities.Info,
      [ActionCodes.LoginFailedAccountLocked] = Severities.Security,
      [ActionCodes.TicketSlaBreached] = Severities.Critical,
      // ...
  };
  ```

> ⚠️ **Bug phổ biến:** Dev tự ý gán `Severity` lúc publish → mỗi handler 1 kiểu → query "list tất cả critical event" sai vì cùng action có severity khác nhau giữa các lần publish.

### B.2.4. Target Type — fixed enum

```csharp
public static class TargetTypes
{
    public const string Account      = "Account";
    public const string Role         = "Role";
    public const string Permission   = "Permission";
    public const string Battery      = "Battery";
    public const string Ticket       = "Ticket";
    public const string TicketComment = "TicketComment";
    public const string File         = "File";
    public const string Session      = "Session";
    public const string Notification = "Notification";
    public const string EmailMessage = "EmailMessage";
    public const string SmsMessage   = "SmsMessage";
    public const string AlertRule    = "AlertRule";
    public const string System       = "System";  // dùng khi action không target entity cụ thể
}
```

### B.2.5. Metadata JSON convention

`metadata_json` là JSONB flex nhưng PHẢI tuân quy ước để query được:

```json
// ✅ ĐÚNG — flat key, snake_case value, không nested sâu
{
  "old_value": { "threshold": 3.5 },
  "new_value": { "threshold": 3.2 },
  "reason": "calibration adjustment",
  "ticket_id": "uuid-here"
}

// ❌ SAI — nested 4 cấp, query GIN không hiệu quả
{
  "changes": {
    "battery": {
      "config": {
        "thresholds": {
          "voltage": { "old": 3.5, "new": 3.2 }
        }
      }
    }
  }
}
```

**Quy tắc:**
- Key dùng `snake_case` (consistent với Postgres)
- Không nested quá 2 cấp
- Mọi old/new diff dùng key `old_value` + `new_value`
- Reference ID dùng `_id` suffix
- KHÔNG lưu password, token, OTP, secret — kể cả hash
- Size max 10KB per record (DB constraint `CHECK (octet_length(metadata_json::text) < 10240)`)

> ⚠️ **Bug số 1 với JSONB:** dev quăng vào cả object request → token, password lộ trong audit. Phải có whitelist field cụ thể.

---

## B.3. Outbox Pattern — chi tiết kỹ thuật (Bug-prone area #1)

### B.3.1. Transaction flow đúng

```csharp
// ✅ ĐÚNG - cả 3 INSERT trong 1 transaction
await _unitOfWork.BeginTransactionAsync();
try
{
    // 1. Business data
    await _unitOfWork.Accounts.AddAsync(account);

    // 2. Audit log
    var auditLog = new AuthAuditLog
    {
        Id = Guid.CreateVersion7(),  // ⚠️ DÙNG v7 cho time-sortable
        EventId = /* same as Id */,
        ServiceName = "Auth",
        ActionCode = ActionCodes.AccountRegistered,
        // ...
        OccurredAt = DateTime.UtcNow,
    };
    await _unitOfWork.AuthAuditLogs.AddAsync(auditLog);

    // 3. Outbox entry
    var outboxEntry = new AuditOutbox
    {
        Id = Guid.CreateVersion7(),
        EventId = auditLog.EventId,
        EventType = nameof(AuditCreatedEvent),
        Payload = JsonSerializer.Serialize(auditLog.ToEvent()),
        Status = OutboxStatus.Pending,
        CreatedAt = DateTime.UtcNow,
    };
    await _unitOfWork.AuditOutbox.AddAsync(outboxEntry);

    await _unitOfWork.CommitTransactionAsync();
}
catch
{
    await _unitOfWork.RollbackTransactionAsync();
    throw;
}

// ❌ KHÔNG bao giờ publish trực tiếp ở đây
// await _bus.Publish(...)   ← SAI
```

### B.3.2. Outbox entity (chuẩn cho mọi service)

```csharp
public class AuditOutbox
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }              // = AuditLog.EventId
    public string EventType { get; set; } = string.Empty;  // "AuditCreatedEvent"
    public string EventVersion { get; set; } = "V1";       // ⚠️ Cho phép schema evolution
    public string Payload { get; set; } = string.Empty;    // JSON
    public string Status { get; set; } = OutboxStatus.Pending;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 10;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }     // ⚠️ Exponential backoff
    public DateTime? PublishedAt { get; set; }
    public DateTime? LockedAt { get; set; }        // ⚠️ Cho multi-instance
    public string? LockedBy { get; set; }          // Instance ID
    public string? LastError { get; set; }
}

public static class OutboxStatus
{
    public const string Pending   = "Pending";
    public const string Publishing = "Publishing";  // đang được lock
    public const string Published = "Published";
    public const string Failed    = "Failed";       // hết retry
    public const string Poisoned  = "Poisoned";     // không deserializable
}
```

### B.3.3. OutboxRelay — single vs multi-instance

**Single-instance (đơn giản, đủ cho capstone):**

```csharp
public class AuditOutboxRelayService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPublishEndpoint _bus;
    private readonly ILogger<AuditOutboxRelayService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox relay batch failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);  // ⚠️ Poll interval
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // ⚠️ Đọc batch tối đa 100, sắp xếp theo CreatedAt ASC để giữ thứ tự
        var batch = await db.AuditOutbox
            .Where(x => x.Status == OutboxStatus.Pending
                     && (x.NextRetryAt == null || x.NextRetryAt <= DateTime.UtcNow))
            .OrderBy(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        foreach (var entry in batch)
        {
            try
            {
                // Deserialize + publish
                var payload = JsonSerializer.Deserialize<AuditCreatedEvent>(entry.Payload);
                await _bus.Publish(payload, ctx =>
                {
                    ctx.MessageId = entry.EventId;       // ⚠️ DÙNG event_id làm MessageId
                    ctx.Headers.Set("X-Correlation-Id", payload!.CorrelationId?.ToString());
                    ctx.Headers.Set("X-Event-Version", entry.EventVersion);
                }, ct);

                entry.Status = OutboxStatus.Published;
                entry.PublishedAt = DateTime.UtcNow;
            }
            catch (JsonException jex)
            {
                // Poison message — không retry vô hạn
                entry.Status = OutboxStatus.Poisoned;
                entry.LastError = jex.Message;
                _logger.LogError(jex, "Poisoned outbox entry {EventId}", entry.EventId);
            }
            catch (Exception ex)
            {
                entry.RetryCount++;
                if (entry.RetryCount >= entry.MaxRetries)
                {
                    entry.Status = OutboxStatus.Failed;
                }
                else
                {
                    // ⚠️ Exponential backoff: 1m, 2m, 4m, 8m, 16m, 32m...
                    var delaySeconds = Math.Min(60 * Math.Pow(2, entry.RetryCount), 3600);
                    entry.NextRetryAt = DateTime.UtcNow.AddSeconds(delaySeconds);
                }
                entry.LastError = ex.Message;
                _logger.LogWarning(ex, "Outbox publish failed {EventId} retry {Count}",
                    entry.EventId, entry.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
```

**Multi-instance (production scale, dùng `FOR UPDATE SKIP LOCKED`):**

```sql
-- Trong batch query, dùng raw SQL hoặc EF Core raw để lock row
SELECT * FROM audit_outbox
WHERE status = 'Pending'
  AND (next_retry_at IS NULL OR next_retry_at <= NOW())
ORDER BY created_at ASC
LIMIT 100
FOR UPDATE SKIP LOCKED;   -- ⚠️ Quan trọng — instance khác không lấy lại

-- Hoặc dùng pattern lock_by + locked_at + lock timeout
UPDATE audit_outbox
SET locked_by = :instance_id,
    locked_at = NOW(),
    status = 'Publishing'
WHERE id IN (
  SELECT id FROM audit_outbox
  WHERE status = 'Pending'
  ORDER BY created_at LIMIT 100
  FOR UPDATE SKIP LOCKED
)
RETURNING *;
```

> ⚠️ **Bug nguy hiểm:** Quên `FOR UPDATE SKIP LOCKED` trong multi-instance → 2 instance cùng publish 1 event → duplicate event lên broker → aggregator nhận 2 lần (idempotency PK cứu, nhưng tốn tài nguyên).

### B.3.4. Poll interval — chọn sao?

| Poll interval | Pros | Cons | Khuyến nghị |
|--------------|------|------|------------|
| 100ms | Lag thấp | CPU/DB cao | ❌ |
| 1s | Lag thấp, OK | DB load cao khi outbox rỗng | 🟡 |
| **2s** | Cân bằng | — | ✅ **Dùng cho capstone** |
| 5s | DB nhẹ | Lag cao | 🟡 |

**Tối ưu hơn:** Listen `NOTIFY` từ Postgres:

```sql
-- Trigger sau INSERT outbox
CREATE TRIGGER trg_audit_outbox_notify
AFTER INSERT ON audit_outbox
FOR EACH ROW EXECUTE FUNCTION pg_notify('audit_outbox_new', NEW.id::text);
```

OutboxRelay dùng `Npgsql.NpgsqlConnection.WaitAsync` listen channel → event-driven, không polling. **Nâng cao, có thể skip cho capstone.**

### B.3.5. Outbox cleanup

Outbox table phình to nếu không clean. Background job riêng:

```csharp
public class AuditOutboxCleanupService : BackgroundService
{
    // Mỗi ngày 03:00 UTC
    // DELETE FROM audit_outbox
    // WHERE status = 'Published' AND published_at < NOW() - INTERVAL '7 days'
    // LIMIT 10000 per batch (tránh long-running transaction)
}
```

**Giữ `Failed` và `Poisoned` vĩnh viễn** để admin replay/investigate.

---

## B.4. CorrelationId & CausationId Propagation (Bug-prone area #2)

### B.4.1. HTTP entry — middleware

```csharp
// shared/src/SharedInfrastructure/Middleware/CorrelationIdMiddleware.cs
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    public const string HttpItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        Guid correlationId;
        if (ctx.Request.Headers.TryGetValue(HeaderName, out var value)
            && Guid.TryParse(value, out var parsed))
        {
            correlationId = parsed;
        }
        else
        {
            correlationId = Guid.CreateVersion7();
        }

        ctx.Items[HttpItemKey] = correlationId;
        ctx.Response.Headers[HeaderName] = correlationId.ToString();

        // ⚠️ Thêm vào logging scope
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(ctx);
        }
    }
}
```

**Đăng ký:** Sớm nhất trong pipeline, TRƯỚC `UseAuthentication`.

### B.4.2. Service-to-service HTTP — HttpClient handler

```csharp
public class CorrelationIdForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"];
        if (correlationId != null)
        {
            request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        }
        return await base.SendAsync(request, ct);
    }
}
```

Đăng ký vào mọi `HttpClient`:
```csharp
services.AddHttpClient<IBatteryServiceClient, BatteryServiceClient>()
    .AddHttpMessageHandler<CorrelationIdForwardingHandler>();
```

### B.4.3. MassTransit message header

```csharp
// Sender side
await _bus.Publish(message, ctx =>
{
    ctx.Headers.Set("X-Correlation-Id", correlationId.ToString());
});

// Consumer side
public class MyConsumer : IConsumer<MyEvent>
{
    public Task Consume(ConsumeContext<MyEvent> context)
    {
        var correlationId = context.Headers.Get<string>("X-Correlation-Id");
        // ⚠️ Lưu vào AsyncLocal hoặc activity context để inner code đọc được
        CorrelationContext.Current = Guid.Parse(correlationId!);
        // ...
    }
}
```

### B.4.4. Background service entry — KHÔNG có HTTP context

```csharp
// Mỗi vòng lặp tạo correlation id mới
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        var correlationId = Guid.CreateVersion7();
        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (var scope = _scopeFactory.CreateScope())
        {
            var accessor = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
            accessor.Set(correlationId);
            await DoWorkAsync(scope.ServiceProvider, stoppingToken);
        }
    }
}
```

### B.4.5. CausationId — khi nào set

CausationId = ID của event upstream gây ra event hiện tại.

```csharp
// Consumer của BatteryAnomalyDetectedEvent
public class BatteryAnomalyConsumer : IConsumer<BatteryAnomalyDetectedEvent>
{
    public async Task Consume(ConsumeContext<BatteryAnomalyDetectedEvent> ctx)
    {
        var upstreamEventId = ctx.MessageId ?? Guid.Empty;  // ⚠️ ID của event upstream

        // Tạo ticket
        var ticket = new Ticket { /* ... */ };
        await _unitOfWork.Tickets.AddAsync(ticket);

        // Publish audit với CausationId = upstream
        await _mediator.Publish(new TicketAuditTrailNotification
        {
            ActionCode = ActionCodes.TicketAutoCreated,
            // ...
            CausationId = upstreamEventId,  // ⚠️ Link ngược về cha
        });
    }
}
```

**Aggregator dựng causation tree:** từ event_id → causation_id → trace lên gốc.

---

## B.5. Event Schema Versioning

### B.5.1. Rule cứng

1. **Không bao giờ thay đổi field hiện có** (rename, change type, change semantics)
2. **Thêm field mới = OK** nhưng phải nullable và có default
3. **Xoá field cũ = KHÔNG** — đợi V2

### B.5.2. Bump version khi nào

```
Thêm field optional → KHÔNG bump version (V1 vẫn deserialize được)
Xoá field, rename, change type → BUMP version
```

### B.5.3. Multi-version consumer

```csharp
// Consumer phải xử lý cả V1 và V2 trong giai đoạn migration
public class AuditCreatedConsumerV1 : IConsumer<AuditCreatedEventV1> { /* legacy */ }
public class AuditCreatedConsumerV2 : IConsumer<AuditCreatedEventV2> { /* new */ }

// Outbox payload phải có EventVersion để consumer route đúng
```

### B.5.4. Routing key bao gồm version

```
audit.v1.auth.authentication.login_success
audit.v2.auth.authentication.login_success
```

---

## B.6. Aggregator Consumer — chi tiết

### B.6.1. Idempotency race condition

Hai instance consumer cùng nhận 1 event (rare nhưng có):

```csharp
public async Task Consume(ConsumeContext<AuditCreatedEvent> ctx)
{
    var evt = ctx.Message;

    // ❌ SAI — race condition giữa Check và Insert
    if (await _repo.ExistsAsync(evt.EventId)) return;
    await _repo.InsertAsync(...);

    // ✅ ĐÚNG — atomic upsert
    await _db.Database.ExecuteSqlInterpolatedAsync($@"
        INSERT INTO audit_aggregate (event_id, ...)
        VALUES ({evt.EventId}, ...)
        ON CONFLICT (event_id) DO NOTHING
    ");
}
```

### B.6.2. Batch processing với MassTransit

```csharp
// MassTransit batch consumer - giảm DB round trip
public class AuditCreatedBatchConsumer : IConsumer<Batch<AuditCreatedEvent>>
{
    public async Task Consume(ConsumeContext<Batch<AuditCreatedEvent>> ctx)
    {
        var events = ctx.Message.Select(m => m.Message).ToList();
        await _repo.BulkUpsertAsync(events);
    }
}

// Config trong MassTransit
cfg.ReceiveEndpoint("queue.audit-aggregator.events", e =>
{
    e.PrefetchCount = 100;
    e.Batch<AuditCreatedEvent>(b =>
    {
        b.MessageLimit = 50;
        b.TimeLimit = TimeSpan.FromSeconds(1);
        b.Consumer<AuditCreatedBatchConsumer>(provider);
    });
});
```

### B.6.3. Enrichment — geo IP, parsed UA

```csharp
private async Task<AuditAggregate> EnrichAsync(AuditCreatedEvent evt, CancellationToken ct)
{
    var agg = AuditAggregate.FromEvent(evt);

    // Geo IP (cache LRU 10k entry)
    if (!string.IsNullOrEmpty(evt.IpAddress))
    {
        var geo = await _geoCache.GetOrAddAsync(evt.IpAddress, async () =>
            await _geoIpService.LookupAsync(evt.IpAddress, ct));
        agg.GeoCountry = geo?.CountryCode;
        agg.GeoCity = geo?.City;
    }

    // ⚠️ Mask IP để GDPR (giữ /24 cho IPv4)
    agg.IpAddressMasked = MaskIp(evt.IpAddress);

    return agg;
}

private string? MaskIp(string? ip)
{
    if (string.IsNullOrEmpty(ip)) return null;
    if (IPAddress.TryParse(ip, out var addr))
    {
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            bytes[3] = 0;  // mask octet cuối
            return new IPAddress(bytes).ToString();
        }
        // IPv6 mask /48
    }
    return null;
}
```

### B.6.4. Error handling — không bao giờ ack message lỗi

```csharp
public async Task Consume(ConsumeContext<AuditCreatedEvent> ctx)
{
    try
    {
        await ProcessAsync(ctx.Message);
    }
    catch (DbUpdateException dbex) when (IsTransient(dbex))
    {
        // Transient (deadlock, timeout) — throw để MassTransit retry
        throw;
    }
    catch (JsonException jex)
    {
        // Permanent (deserialization) — vào DLQ
        _logger.LogError(jex, "Poisoned event {EventId}", ctx.MessageId);
        throw;  // MassTransit sẽ move vào _error queue sau N retry
    }
}
```

**Config retry policy:**
```csharp
cfg.UseMessageRetry(r =>
{
    r.Exponential(5,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5));
    r.Ignore<JsonException>();  // Không retry poison
});
```

---

## B.7. DateTime & Timezone — 5 bug cực phổ biến

| Bug | Triệu chứng | Cách phòng |
|-----|------------|------------|
| `DateTime.Now` lẫn với `DateTime.UtcNow` | Audit time lệch 7h | **CẤM** `DateTime.Now`, chỉ `DateTime.UtcNow` hoặc `TimeProvider.GetUtcNow()` |
| Postgres column `TIMESTAMP` (không TZ) | EF Core parse sai khi server timezone đổi | Luôn dùng `TIMESTAMPTZ` |
| `DateTime.Kind = Unspecified` | Truyền qua API → JSON serialize sai timezone | Constructor luôn set `DateTime.UtcNow`, không bao giờ `new DateTime(...)` |
| Clock skew giữa service | Event A xảy ra sau B nhưng timestamp ngược | Dùng `occurred_at` từ service ghi, nhưng sort phụ bằng `event_id` (Guid v7 monotonic) |
| Daylight saving | 1h trùng lặp / mất | Lưu UTC luôn, FE convert |

**Quy tắc cứng cho project:**
```csharp
// shared/src/SharedKernels/Time/ISystemClock.cs
public interface ISystemClock
{
    DateTime UtcNow { get; }
}

public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;  // ⚠️ chỉ 1 nơi gọi UtcNow
}
```

Inject `ISystemClock` mọi nơi cần thời gian → mock dễ khi test.

---

## B.8. Partition Management (PostgreSQL)

### B.8.1. Tạo partition tự động — pg_partman

```sql
-- Setup pg_partman (Postgres extension)
CREATE EXTENSION pg_partman;

SELECT partman.create_parent(
    p_parent_table => 'public.audit_aggregate',
    p_control => 'occurred_at',
    p_type => 'range',
    p_interval => '1 month',
    p_premake => 3                  -- tạo trước 3 tháng
);

-- Cron job hàng ngày maintain partition
SELECT partman.run_maintenance_proc();
```

### B.8.2. Drop partition cũ

```sql
-- Bỏ partition cũ hơn 6 tháng (đã backup)
SELECT partman.drop_partition_time(
    p_parent_table => 'public.audit_aggregate',
    p_retention => '6 months',
    p_keep_table => false   -- thực sự DROP table partition
);
```

> ⚠️ **Trước khi drop, backup partition `Critical`/`Security` ra cold storage S3.**

### B.8.3. Query luôn có condition `occurred_at` để partition pruning

```csharp
// ❌ SAI — không có occurred_at → scan all partition
db.AuditAggregate.Where(x => x.ActorAccountId == accountId).ToList();

// ✅ ĐÚNG — partition pruning hoạt động
db.AuditAggregate
    .Where(x => x.ActorAccountId == accountId
             && x.OccurredAt >= from
             && x.OccurredAt <= to)
    .ToList();
```

API endpoint BẮT BUỘC nhận `from`/`to`, default = 30 ngày gần nhất.

---

## B.9. Migration Plan — AuthService.AuditLog hiện có → Schema mới

### B.9.1. Tình trạng hiện tại

`auth_audit_logs` hiện có: `id, action (int), target_account_id, target_email, actor_account_id, is_success, reason, metadata_json, ip_address, user_agent, device_id, correlation_id, created_at, ...`

### B.9.2. Schema mới cần

Thêm các cột:
- `event_id` UUID
- `service_name` VARCHAR(50)
- `action_code` VARCHAR(100) — thay int enum bằng string
- `action_category` VARCHAR(50)
- `severity` VARCHAR(20)
- `target_type` VARCHAR(50)
- `target_id` UUID
- `target_display` VARCHAR(255)
- `actor_role` VARCHAR(50)
- `actor_display` VARCHAR(255)
- `error_code` VARCHAR(50)
- `causation_id` UUID
- `occurred_at` TIMESTAMPTZ
- `recorded_at` TIMESTAMPTZ

### B.9.3. Migration steps (zero-downtime)

**Step 1 — Migration thêm column nullable, default null:**

```csharp
public partial class AddAuditLogStandardColumns : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.AddColumn<Guid>("event_id", "auth_audit_logs", nullable: true);
        mb.AddColumn<string>("service_name", "auth_audit_logs", maxLength: 50, nullable: true);
        mb.AddColumn<string>("action_code", "auth_audit_logs", maxLength: 100, nullable: true);
        // ... các cột khác

        // Backfill cho row cũ
        mb.Sql(@"
            UPDATE auth_audit_logs SET
                event_id = id,
                service_name = 'Auth',
                action_code = CASE action
                    WHEN 1 THEN 'LoginSuccess'
                    WHEN 2 THEN 'LoginFailedWrongPassword'
                    -- ... map từ AuditActionEnum int → string
                    ELSE 'Unknown'
                END,
                action_category = CASE
                    WHEN action BETWEEN 1 AND 19 THEN 'Authentication'
                    WHEN action BETWEEN 20 AND 29 THEN 'AccountLifecycle'
                    -- ...
                    ELSE 'Unknown'
                END,
                severity = 'Info',
                target_type = 'Account',
                target_id = target_account_id,
                target_display = target_email,
                occurred_at = created_at,
                recorded_at = created_at
            WHERE event_id IS NULL;
        ");

        // Set NOT NULL sau khi backfill xong
        mb.AlterColumn<Guid>("event_id", "auth_audit_logs", nullable: false);
        mb.AlterColumn<string>("service_name", "auth_audit_logs", maxLength: 50, nullable: false);
        // ...

        // Unique index event_id
        mb.CreateIndex("ix_auth_audit_logs_event_id", "auth_audit_logs", "event_id", unique: true);
    }
}
```

**Step 2 — Tạo `audit_outbox` + `OutboxRelayService`** (chưa publish, chỉ ghi outbox).

**Step 3 — Update toàn bộ `AuditTrailNotificationHandler`** để set field mới.

**Step 4 — Deploy code mới, monitor outbox table có grow đúng.**

**Step 5 — Apply DB trigger append-only** (vì step 1 backfill update column cũ):

```sql
CREATE OR REPLACE FUNCTION fn_audit_immutable() RETURNS TRIGGER AS $$
BEGIN
    -- Cho phép UPDATE field nội bộ outbox-related, nhưng KHÔNG cho UPDATE field nghiệp vụ
    IF TG_OP = 'UPDATE' THEN
        IF (OLD.action_code IS DISTINCT FROM NEW.action_code)
            OR (OLD.actor_account_id IS DISTINCT FROM NEW.actor_account_id)
            OR (OLD.target_id IS DISTINCT FROM NEW.target_id)
            OR (OLD.occurred_at IS DISTINCT FROM NEW.occurred_at)
        THEN
            RAISE EXCEPTION 'audit_logs business fields are immutable';
        END IF;
        RETURN NEW;
    END IF;
    RAISE EXCEPTION 'audit_logs DELETE not allowed';
END;
$$ LANGUAGE plpgsql;
```

**Step 6 — Setup AuditAggregatorService**, subscribe queue, consume event mới.

**Step 7 — Backfill historic data:** chạy 1 lần script đọc `auth_audit_logs` cũ → publish event → aggregator consume → fill `audit_aggregate`. Hoặc dùng `INSERT … SELECT` trực tiếp nếu cùng cluster DB.

**Step 8 — Sau 1-2 tuần ổn định, drop column `action` (int) cũ.**

### B.9.4. Rollback plan

Mỗi step có rollback riêng:
- Step 1: Drop column mới (data cũ vẫn còn)
- Step 2: Disable OutboxRelay, drop `audit_outbox`
- Step 5: Drop trigger

> ⚠️ Backup DB trước mỗi step. Test trên môi trường staging trước.

---

## B.10. Multi-instance Scaling (k8s)

### B.10.1. AuthService có 3 replica → 3 OutboxRelay chạy song song?

**KHÔNG.** OutboxRelay là background work, không nên chạy trên mọi pod.

**3 lựa chọn:**

1. **Leader election** (recommend cho capstone): dùng `Microsoft.Extensions.Hosting` + `IDistributedCache` (Redis) để 1 pod làm leader, các pod khác idle.

   ```csharp
   public class LeaderElectionService : BackgroundService
   {
       // Mỗi 30s renew lease ở Redis key "outbox_leader"
       // Nếu không phải leader → skip work
   }
   ```

2. **Separate worker pod**: tách OutboxRelay thành deployment riêng với `replicas: 1`. Service Auth chỉ ghi outbox, không relay.

3. **Multi-leader với `FOR UPDATE SKIP LOCKED`**: cho phép nhiều pod cùng poll, lock row ở DB level. Phức tạp hơn.

**Capstone scope:** chọn (1) hoặc (2). *(Khuyến nghị gốc của doc là (2) — dễ test; nhưng owner đã chốt (1), xem dưới.)*

> **✅ CHỐT (2026-06-24):** chọn **(1) Leader election qua Redis** — `IDistributedCache` lease key `audit_outbox_leader`, renew 30s, non-leader pod skip work. Áp dụng cho mọi `*AuditOutboxRelayBackgroundService` (`#AUDIT-08`, `#AUDIT-21`, `#AUDIT-25`, …). Xem A.9 D12.

### B.10.2. Aggregator có scale được không?

Có. MassTransit consumer được consume từ cùng queue → broker tự load balance. Tăng pod = tăng throughput.

> ⚠️ **Lưu ý:** Idempotent INSERT (ON CONFLICT) → safe khi multi-instance. Nhưng nếu enrichment có side-effect (vd call external API) → dùng distributed lock per `event_id`.

---

## B.11. 30 Common Pitfalls — danh sách kiểm tra cuối cùng

| # | Pitfall | Cách phòng |
|---|---------|-----------|
| 1 | Publish event TRƯỚC commit transaction | Chỉ ghi outbox trong transaction, publish ở background |
| 2 | Dùng `DateTime.Now` thay `UtcNow` | Code review + Roslyn analyzer ban |
| 3 | Postgres column TIMESTAMP (không TZ) | Migration luôn `TIMESTAMPTZ` |
| 4 | Quên FOR UPDATE SKIP LOCKED → duplicate publish | Code review outbox query |
| 5 | Idempotency check + Insert không atomic | Dùng `ON CONFLICT DO NOTHING` |
| 6 | Audit log mất khi DB transaction commit nhưng outbox publish fail (không có outbox) | Bắt buộc Outbox Pattern, không skip |
| 7 | CorrelationId không propagate vào background work | Inject `ICorrelationContext` |
| 8 | Lưu password/token trong metadata_json | Whitelist field, code review |
| 9 | metadata_json nested quá sâu → GIN index inefficient | Quy ước max 2 level |
| 10 | Action code duplicate giữa service | Centralize `ActionCodeRegistry.cs` |
| 11 | Free-text severity → query GROUP BY vỡ | Fixed enum 4 giá trị |
| 12 | Quên partition condition trong query → full table scan | API force `from`/`to` param |
| 13 | Outbox table phình → DB slow | Cleanup job daily |
| 14 | Poison message retry vô hạn → DLQ overflow | Distinguish transient vs permanent error |
| 15 | Multi-instance OutboxRelay → duplicate publish | Leader election hoặc skip-locked |
| 16 | Event schema breaking change không bump version | Code review checklist |
| 17 | Consumer crash giữa batch → data inconsistent | Idempotent + ack riêng từng message |
| 18 | RabbitMQ queue grow vô hạn khi aggregator down | Set queue x-max-length, DLQ |
| 19 | Geo IP service rate limit → consumer chậm | Cache LRU, fallback null |
| 20 | Aggregator DB connection leak | Dùng `IDbContextFactory`, scope đúng |
| 21 | Tracker EF Core trên audit query → memory bloat | `AsNoTracking()` mặc định |
| 22 | DBA xoá audit_logs trực tiếp → trigger trigger bypass nếu chạy với superuser | Hạn chế quyền superuser DB |
| 23 | Time skew giữa services → sort sai | Dùng Guid v7 làm secondary sort |
| 24 | JSON serialize null vs missing field | `JsonSerializerOptions.DefaultIgnoreCondition` |
| 25 | Aggregator API trả về nhiều data → OOM | Pagination bắt buộc, max page_size = 100 |
| 26 | Replay endpoint publish duplicate event lên broker | Replay → ghi thẳng `audit_aggregate`, không qua broker |
| 27 | Quên rate limit aggregator API → admin spam → DB stress | Rate limit per role |
| 28 | Causation chain bị break khi consumer recreate event | Bắt buộc forward `MessageId` upstream |
| 29 | Lưu User-Agent raw 5000 ký tự → DB phình | Truncate 500 chars, log warning nếu vượt |
| 30 | Aggregator query qua FE web không có cache → slow | Output cache 30s cho query phổ biến |

---

## B.12. Pre-implementation Checklist

> **📌 Trạng thái chốt 2026-06-24 (owner Thắng `@Alexdev257`):** các quyết định kiến trúc/policy đã chốt (xem A.9 D11–D16). Các item code/scaffold sẽ tick khi làm trong Phase 0.

Trước khi bắt đầu code, đảm bảo:

- [x] Team đã đọc + ký xác nhận Phụ lục A và B — *sole-dev Thắng ký (capstone scope)*
- [x] ADR `docs/adr/0007-audit-hybrid-architecture.md` đã viết và approved — *412 dòng, sign-off override sole-dev; GVHD review ở báo cáo final*
- [ ] PR template có mục "Audit event added?" cho mọi command handler — *làm Phase 0*
- [ ] `ActionCodeRegistry.cs` được tạo trước, action mới phải PR vào file này — *`#AUDIT-02`, Phase 0*
- [ ] Code analyzer ban `DateTime.Now`, `Random` (cho event_id), `Console.WriteLine` — *`#AUDIT-04`, Phase 0*
- [ ] Database staging có RabbitMQ + Postgres ready — *RabbitMQ ✅ sẵn; phải thêm `audit-aggregator-db` + `pg_partman` vào `docker-compose.yml` trước Phase 2 (`#AUDIT-13/14`)*
- [x] Geo IP service quyết định — **✅ MaxMind GeoLite2 free** (D11)
- [x] Quyết định leader election vs separate worker pod — **✅ Leader election qua Redis** (D12, §B.10 option 1)
- [x] Quyết định retention: source 1 năm, aggregate 6 tháng, Critical vĩnh viễn — **✅ ratify** (D15; aggregate giữ thêm severity=Security vĩnh viễn)
- [x] Setup Prometheus metric exporter + Grafana dashboard skeleton — *✅ nền có sẵn từ Sprint 7; Sprint audit chỉ bổ sung metric audit-pipeline ở `#AUDIT-44`*
- [x] Đặt SLO: outbox lag p99 < 5s, aggregator lag p99 < 10s — *✅ giữ nguyên (ADR-0007)*
- [x] Backup strategy: daily snapshot DB Auth/Battery/Ticket/Aggregator — *✅ nền có sẵn từ Sprint 7; thêm Aggregator DB khi dựng Phase 2*
- [ ] Document team về cách add audit cho handler mới (1-page cheatsheet) — *`#AUDIT-45`, Phase 7*
- [x] Chốt **Option C policy** (xem §A.5.1.bis): 5 service có local endpoint (Auth giữ + Battery/Ticket/File/Alert build mới), 5 service KHÔNG (Email/Notification/Sms/AI/Gateway — qua Aggregator only) — **✅ confirm**; `AlertAuditLog` host trong BatteryService → route `batteryCluster` (D14). **Cập nhật 2026-06-25:** Email/AI/Gateway sau đó **DESCOPED** hoàn toàn (`#AUDIT-33` + `#AUDIT-35` phần AI/Gateway) → chỉ Notification/Sms thực sự onboard không-endpoint.
- [ ] FE Admin UI plan: 2 view mode — "Service-local view" gọi `/api/admin/{service}/audit-logs` (real-time, fallback) và "Cross-service view" gọi `/api/admin/audit/*` (aggregator) — *Phase 6*
- [x] Auth cho endpoint — **✅ `[Authorize(Roles = "Admin")]` cho CẢ local endpoint VÀ aggregator endpoint** (role `SecurityOfficer` gộp Admin, D13)

---

## B.13. Acceptance Criteria per Phase

### Phase 0 — Chuẩn bị

- [ ] `SharedContracts.IntegrationEvents.Audit.AuditCreatedEventV1.cs` exists, có XML doc đầy đủ
- [ ] `SharedContracts.Audit.ActionCodes.cs` chứa **toàn bộ** action code project sẽ dùng
- [ ] `SharedContracts.Audit.AuditCategories.cs` chứa 9 category fixed
- [ ] `SharedContracts.Audit.Severities.cs` chứa 4 severity
- [ ] `SharedContracts.Audit.TargetTypes.cs` chứa fixed target types
- [ ] ADR 0007 viết xong, có sign-off của 3 thành viên team
- [ ] Roslyn analyzer cấm `DateTime.Now` chạy CI green

### Phase 1 — Refactor AuthService

- [ ] Migration `AddAuditLogStandardColumns` apply staging thành công
- [ ] Backfill SQL chạy < 5 phút trên data hiện tại
- [ ] `audit_outbox` table tồn tại
- [ ] `AuditOutboxRelayService` chạy, ghi outbox table có entry mới sau mỗi action
- [ ] Trigger append-only apply, manual test DELETE/UPDATE bị reject
- [ ] 22 handler trong danh sách Pass 1-4 đã publish audit, có unit test
- [ ] Integration test E2E: register account → query audit_logs → có row → outbox status Published sau 5s
- [ ] Coverage ≥ 80%
- [ ] PR review pass

### Phase 2 — AuditAggregatorService

- [ ] Project scaffold theo Clean Architecture chuẩn (Api/Application/Domain/Infrastructure/Worker)
- [ ] DbContext + migration `audit_aggregate` partitioned by month
- [ ] `AuditCreatedConsumer` consume từ queue, INSERT có ON CONFLICT
- [ ] Geo IP enrichment + cache (test với 1000 IP khác nhau, cache hit ≥ 80% sau 100 lần)
- [ ] API search endpoint < 200ms p95 với 1M row
- [ ] API export CSV stream được 100k row không OOM
- [ ] Authorization: chỉ `Admin` access (✅ `SecurityOfficer` gộp Admin — D13)
- [ ] Health check k8s endpoint `/health` và `/ready`
- [ ] Docker image build < 200MB
- [ ] Integration test với TestContainers (Postgres + RabbitMQ) pass

### Phase 3 — BatteryService onboard

- [ ] `BatteryAuditLog` entity follow schema chuẩn
- [ ] `BatteryAuditActionEnum` chứa 12 action liệt kê (mục #2 trong phụ lục audit gap)
- [ ] `BatteryAuditTrailNotificationHandler` resolve actor/IP/correlation đúng
- [ ] Outbox + relay riêng cho BatteryService
- [ ] Trigger append-only
- [ ] Migration zero-downtime tested
- [ ] Handler quan trọng (Create/Update/Delete/AssignCustomer/ThresholdChange) publish audit
- [ ] Unit test + integration test
- [ ] **Local endpoint (Option C):** `GET /api/admin/battery/audit-logs` — filter `action` + `batteryId` + `from/to`, paging max 100, Authorize Admin role, query trực tiếp `battery_audit_logs` table
- [ ] E2E: create battery → query aggregator API sau 10s → tìm thấy event
- [ ] E2E: create battery → query LOCAL endpoint ngay lập tức → tìm thấy event (0ms lag verify)

### Phase 4 — TicketService onboard

- [ ] `TicketAuditLog` entity TÁCH KHỎI `TicketActivity` (TicketActivity giữ cho UI timeline user-facing)
- [ ] `TicketAuditActionEnum` chứa 21 action
- [ ] Handler resolve actor/IP/correlation + `causation_id` cho ticket auto-tạo từ saga
- [ ] Outbox + relay riêng
- [ ] Trigger append-only + migration zero-downtime
- [ ] Publish audit ở: state transition, priority change, SLA pause/resume/breach, escalation, maintenance log, comment, attachment
- [ ] **Local endpoint (Option C):** `GET /api/admin/ticket/audit-logs` — filter `action` + `ticketId` + `from/to`, paging max 100, Authorize Admin role
- [ ] Unit test + integration test + E2E

### Phase 5 — FileStorage + Alert + Notification + Sms onboard — ~~Email/AI/Gateway~~ DESCOPED 2026-06-25

- [ ] `FileAuditLog` + 6 action (Upload/Download/Delete/AccessDenied/PresignedUrlGenerated/...)
- [ ] **Local endpoint cho FileStorage:** `GET /api/admin/files/audit-logs` — compliance + GDPR file access
- [ ] `AlertAuditLog` + 5 action (Acknowledged/Suppressed/RuleChanged/...)
- [ ] **Local endpoint cho Alert:** `GET /api/admin/alerts/audit-logs` — alert acknowledge history
- [x] ~~EmailService 5 action publish + outbox + relay~~ **❌ DESCOPED 2026-06-25** (`#AUDIT-33`)
- [ ] NotificationService 7 action publish + outbox + relay (KHÔNG local endpoint per Option C)
- [ ] SmsService bổ sung 3 action mới (KHÔNG local endpoint per Option C)
- [x] ~~AI Module 5 action publish~~ **❌ DESCOPED 2026-06-25** (`#AUDIT-35` phần AI)
- [x] ~~Gateway 3 action publish~~ **❌ DESCOPED 2026-06-25** (`#AUDIT-35` phần Gateway)
- [ ] E2E test các service onboard (File/Alert/Notification/Sms): action → outbox → broker → aggregator → query aggregator API tìm thấy

### Phase 6-7

(Tương tự, mỗi phase có acceptance riêng — sẽ chi tiết khi tới phase)

---

## B.14. Rollback Procedure

Mỗi phase phải có rollback procedure document. Mẫu cho Phase 1:

### Rollback Phase 1 (Refactor AuthService)

**Khi nào rollback:**
- Audit log không ghi được (transaction fail rate > 1%)
- Outbox grow uncontrolled (>100k pending entries)
- Performance degradation > 30% trên login endpoint

**Bước rollback:**

1. Deploy version code cũ (Git tag `pre-audit-refactor`)
2. Dừng `AuditOutboxRelayService` (env var `OUTBOX_RELAY_ENABLED=false`)
3. KHÔNG drop column mới (chỉ ngưng dùng) — data đã backfill vẫn còn
4. Monitor:
   - Audit log vẫn ghi vào column `action` cũ (int)
   - Outbox không grow nữa
5. Sau 1 ngày ổn định, planning lại migration

**Chỉ DROP COLUMN khi:**
- Đã backup full DB
- Confirm aggregator KHÔNG bao giờ cần data đó
- Sign-off của tech lead

---

## B.15. Monitoring & SLO

### B.15.1. Metrics PHẢI có

```
# Source service (Auth/Battery/Ticket/File)
audit_logs_inserted_total{service, action_code, severity}
audit_outbox_pending_total{service}
audit_outbox_published_total{service}
audit_outbox_failed_total{service, reason}
audit_outbox_retry_count{service}
audit_outbox_publish_duration_seconds{service}  # histogram

# Aggregator
audit_aggregator_consumed_total{action_code}
audit_aggregator_duplicate_total
audit_aggregator_enrichment_duration_seconds  # histogram
audit_aggregator_query_duration_seconds{endpoint}  # histogram
audit_aggregator_dlq_size

# Cross
audit_correlation_chain_length  # histogram
audit_event_lag_seconds{stage}  # outbox-to-broker, broker-to-aggregator
```

### B.15.2. SLO (Service Level Objective)

| SLO | Target | Cách đo |
|-----|--------|--------|
| Outbox publish lag p99 | < 5 giây | `now() - outbox.created_at WHERE status='Published'` |
| Broker → aggregator lag p99 | < 10 giây | `ingested_at - recorded_at` ở `audit_aggregate` |
| Audit log loss rate | 0% | `audit_logs.count - audit_aggregate.count` per service per day < threshold |
| Query API p95 | < 200ms | Prometheus histogram |
| Aggregator availability | > 99.5% | k8s probe |
| Outbox table size | < 10k pending | Alert nếu vượt |

### B.15.3. Alert rules

```yaml
- alert: AuditOutboxBacklog
  expr: audit_outbox_pending_total > 5000
  for: 5m
  annotations:
    summary: "Outbox backlog cho service {{ $labels.service }}"

- alert: AuditPublishFailing
  expr: rate(audit_outbox_failed_total[5m]) > 0.1
  for: 10m

- alert: AuditAggregatorLagHigh
  expr: histogram_quantile(0.99, audit_event_lag_seconds_bucket{stage="broker-to-aggregator"}) > 30
  for: 5m
```

---

## B.16. Security Considerations

### B.16.1. Authorization model cho Aggregator API

| Role | Quyền |
|------|-------|
| `Admin` | Full access tất cả endpoint (bao gồm search, correlation trace, export **và** GDPR redact) — **chốt 2026-06-24: role `SecurityOfficer` gộp vào `Admin`, không tạo role mới (D13)** |
| ~~`SecurityOfficer` (role mới)~~ | ❌ KHÔNG triển khai cho capstone — quyền gộp vào `Admin` |
| `Manager` | Chỉ xem audit của account thuộc tenant/team mình (nếu có multi-tenant) |
| `Staff` | KHÔNG access |
| `Customer` | KHÔNG access |

**Endpoint `/api/admin/audit/me`** cho user xem audit của chính mình (GDPR self-service).

### B.16.2. Sensitive data trong audit

KHÔNG bao giờ log vào audit:
- Password (plaintext hoặc hash)
- Access/Refresh token
- OTP code, backup code
- 2FA secret
- Credit card / payment info
- Health data (nếu có)
- API key

**Whitelist field cho phép:**
- email (có thể mask sau)
- IP address (mask /24)
- User-Agent (truncate 500)
- Device ID
- Resource ID (Battery serial, Ticket code)

### B.16.3. PII masking khi export

```csharp
// Export CSV cho non-Admin → mask sensitive
public string MaskEmail(string email, bool isAdmin)
{
    if (isAdmin) return email;
    var atIdx = email.IndexOf('@');
    if (atIdx <= 0) return "***";
    var local = email[..atIdx];
    return $"{local[..1]}***{local[^1..]}@{email[(atIdx + 1)..]}";
}
```

### B.16.4. Rate limiting

```
GET /api/admin/audit/search          → 60 req/phút per user
GET /api/admin/audit/export          → 5 req/phút per user (heavy)
POST /api/admin/audit/replay         → 1 req/phút (Admin only)
```

---

## B.17. Testing Strategy chi tiết

### B.17.1. Unit test mỗi `*AuditTrailNotificationHandler`

```csharp
[Fact]
public async Task Handle_ShouldResolveActorFromHttpContext()
{
    // Arrange
    var httpContext = new DefaultHttpContext();
    httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim("UserId", "11111111-1111-1111-1111-111111111111"),
        new Claim(ClaimTypes.Role, "Admin"),
    }));
    httpContext.Items["CorrelationId"] = Guid.CreateVersion7();

    var handler = new AuthAuditTrailNotificationHandler(/* deps */);

    var notification = new AuthAuditTrailNotification
    {
        ActionCode = ActionCodes.LoginSuccess,
        // ...
    };

    // Act
    await handler.Handle(notification, CancellationToken.None);

    // Assert
    _unitOfWorkMock.Verify(x => x.AuthAuditLogs.AddAsync(
        It.Is<AuthAuditLog>(a =>
            a.ActorAccountId == Guid.Parse("11111111-...") &&
            a.ActorRole == "Admin" &&
            a.CorrelationId != null
        )), Times.Once);
    _unitOfWorkMock.Verify(x => x.AuditOutbox.AddAsync(
        It.IsAny<AuditOutbox>()), Times.Once);
}
```

### B.17.2. Integration test với TestContainers

```csharp
public class AuditE2ETests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();
    private RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _rabbit.StartAsync();
    }

    [Fact]
    public async Task RegisterAccount_ShouldFlowEndToEnd()
    {
        // Arrange: spin AuthService + AuditAggregator pointing to test containers
        var authApp = CreateAuthApp(_postgres.GetConnectionString(), _rabbit.GetConnectionString());
        var aggApp = CreateAggregatorApp(_postgres.GetConnectionString(), _rabbit.GetConnectionString());

        // Act: register account via AuthService API
        var client = authApp.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { /* ... */ });
        response.EnsureSuccessStatusCode();

        // Wait for eventual consistency
        await WaitForAsync(async () =>
        {
            var queryResponse = await aggApp.CreateClient()
                .GetAsync($"/api/admin/audit/search?action=AccountRegistered");
            var body = await queryResponse.Content.ReadFromJsonAsync<AuditSearchResponse>();
            return body!.Items.Count > 0;
        }, TimeSpan.FromSeconds(30));
    }
}
```

### B.17.3. Chaos test

```csharp
[Fact]
public async Task RabbitMqDownDuringPublish_ShouldRetryWhenUp()
{
    // 1. Register account (audit ghi vào DB + outbox status=Pending)
    // 2. Stop RabbitMQ container
    // 3. Wait 10s (outbox relay sẽ fail vài lần)
    // 4. Start RabbitMQ
    // 5. Wait 30s
    // 6. Assert: outbox status = Published, aggregator nhận event
}
```

---

## B.18. Documentation deliverables

Hoàn thành Phase 7, team phải có:

1. `docs/adr/0007-audit-hybrid-architecture.md` — ADR chính
2. `docs/audit/contributor-guide.md` — How to add new audit event (cheatsheet 1 page)
3. `docs/audit/action-code-registry.md` — danh sách action code + ý nghĩa (auto-gen từ code)
4. `docs/audit/api-reference.md` — Aggregator API doc (Swagger)
5. `docs/audit/operations-runbook.md` — Troubleshooting (outbox backlog, replay, DLQ)
6. `docs/audit/security-considerations.md` — PII, retention, GDPR
7. `docs/audit/monitoring-dashboard.md` — Grafana dashboard JSON + alert rules

---

## B.19. Effort breakdown chi tiết (dev-day)

| Task | Effort | Note |
|------|--------|-----|
| Phase 0 — ADR + SharedContracts + ActionCodeRegistry | 3 | 1 BE senior |
| Phase 1 — Migration backfill auth_audit_logs | 1 | Risky, test kỹ staging |
| Phase 1 — Outbox entity + relay service | 1.5 | |
| Phase 1 — Update AuditTrailNotificationHandler | 0.5 | |
| Phase 1 — Fix 22 handler chưa publish | 2 | Theo danh sách |
| Phase 1 — DB trigger append-only | 0.5 | |
| Phase 1 — Unit + integration test | 1.5 | TestContainers setup |
| Phase 2 — AuditAggregator scaffold + DI | 1 | |
| Phase 2 — DbContext + migration partitioned | 1 | pg_partman setup |
| Phase 2 — Consumer + idempotency | 1.5 | |
| Phase 2 — Enrichment (Geo IP, UA parser) | 1 | |
| Phase 2 — Search + Export + Stats API | 2 | |
| Phase 2 — Auth + rate limit + health check | 0.5 | |
| Phase 2 — Test E2E TestContainers | 1.5 | |
| Phase 3 — BatteryAuditLog | 1.5 | Pattern đã có từ Phase 1 |
| Phase 3 — 12 action publish | 1 | |
| Phase 3 — Migration + trigger + test | 0.5 | |
| Phase 3 — Local endpoint `/api/admin/battery/audit-logs` (Option C) | 0.5 | ~150 LOC: controller + query DTO + handler + unit test |
| Phase 4 — TicketAuditLog (tách khỏi Activity) | 2 | Phức tạp hơn vì có Activity sẵn |
| Phase 4 — 21 action publish | 2 | |
| Phase 4 — Causation chain test (anomaly → ticket) | 0.5 | |
| Phase 4 — Migration + test | 0.5 | |
| Phase 4 — Local endpoint `/api/admin/ticket/audit-logs` (Option C) | 0.5 | ~150 LOC |
| Phase 5 — FileStorage audit (6 action) + outbox + relay | 1.5 | |
| Phase 5 — Alert audit (5 action) + outbox + relay | 1 | |
| Phase 5 — Notification/Sms audit (publish only, no local endpoint) (~~Email/AI/Gateway~~ descoped 2026-06-25) | 1.5 | 2 service × ~0.3 day |
| Phase 5 — Local endpoint `/api/admin/files/audit-logs` (Option C) | 0.5 | ~150 LOC |
| Phase 5 — Local endpoint `/api/admin/alerts/audit-logs` (Option C) | 0.5 | ~150 LOC |
| Phase 6 — Admin Web UI Audit Explorer | 6 | FE work |
| Phase 7 — Retention background service | 1 | |
| Phase 7 — GDPR redaction endpoint | 1 | |
| Phase 7 — Perf + chaos test | 1.5 | |
| Phase 7 — Monitoring + alert rules | 0.5 | |
| Phase 7 — Documentation deliverables | 1 | |
| **TỔNG** | **~44 dev-day** | |

> Tăng từ ước tính ban đầu 37 → 42 (chi tiết hóa) → 44 (cộng +2 day cho 4 local endpoint mới theo Option C: Battery + Ticket + File + Alert × 0.5 day).

---

## B.20. Risk Register (chi tiết)

| ID | Risk | Probability | Impact | Mitigation | Owner |
|----|------|------------|--------|------------|-------|
| R1 | Outbox backlog gây DB slow | Medium | High | Cleanup job, monitor metric, alert | BE Lead |
| R2 | RabbitMQ down dài → outbox đầy | Low | High | Multi-broker, alert sớm, manual flush | DevOps |
| R3 | Schema event V1 → V2 breaking | Medium | High | Versioning rule, multi-version consumer | BE Lead |
| R4 | Aggregator DB hỏng → mất view | Low | Medium | Replay endpoint, source still intact | DevOps |
| R5 | Multi-instance OutboxRelay → duplicate | High | Low | Leader election (test E2E) | BE Senior |
| R6 | Causation chain broken khi consumer recreate event | Medium | Medium | Code review checklist | BE Senior |
| R7 | PII leak qua aggregator API | Medium | High | Whitelist field, mask khi export, role-based | Security |
| R8 | Performance regression trên login | Low | High | Benchmark trước/sau, rollback plan | BE Lead |
| R9 | Hội đồng KLTN hỏi "tại sao không centralized" | High | Low | Đã có defend trong ADR 0007 | Team |
| R10 | Team không kịp 7 sprint | Medium | Medium | Phase 5/7 có thể split, ưu tiên Phase 1-4 | PM |
| R11 | Migration backfill fail trên production | Low | Critical | Test kỹ staging, có rollback | BE Lead |
| R12 | Time skew khiến sort sai | Medium | Low | Guid v7 secondary sort | BE Senior |
| R13 | Geo IP service rate limit | Medium | Low | Cache + fallback null | BE |
| R14 | Audit log size explode (10M+/ngày) | Low | Medium | Partition + archive cold storage | DevOps |

---

## B.21. Final Cheatsheet — "Tôi vừa thêm Command Handler mới, cần gì?"

```
□ 1. Action code đã có trong ActionCodeRegistry.cs chưa?
     - Nếu chưa: thêm constant + map vào ActionSeverity dictionary
     - PR riêng cho việc thêm action code

□ 2. Trong handler:
     ✓ BeginTransactionAsync
     ✓ AddAsync business entity
     ✓ Publish {Service}AuditTrailNotification
        - ActionCode = ActionCodes.XXX
        - ActionCategory = AuditCategories.YYY
        - Severity = (auto resolve từ registry)
        - TargetType, TargetId, TargetDisplay
        - IsSuccess
        - Metadata: chỉ field SAFE (không password/token/OTP)
     ✓ CommitTransactionAsync
     ✓ catch → RollbackTransactionAsync

□ 3. Unit test:
     ✓ Verify AddAsync audit log được gọi đúng
     ✓ Verify outbox entry được tạo
     ✓ Verify rollback khi exception

□ 4. Integration test (nếu là handler critical):
     ✓ E2E: call API → wait → query aggregator → assert event xuất hiện

□ 5. Update doc:
     ✓ Action code registry doc (auto-gen)
     ✓ Nếu là Critical/Security: add vào retention exemption list
```

---

## B.22. Liên kết với issue list

Phụ lục B liên kết trực tiếp với các issue đã liệt kê:

| Issue # | Liên quan Phase | Action |
|---------|----------------|--------|
| Audit gap 22 handler AuthService | Phase 1 | Fix theo danh sách Pass 1-4 |
| #29 AuditLog không enforce append-only DB | Phase 1 | Trigger SQL ở B.9.3 step 5 |
| #30 DeleteAccount không cascade/anonymize | Phase 7 | GDPR redaction |
| #77 Composite index (Email, IsDeleted) | Phase 1 | Index tổng audit |
| #79 Correlation ID xuyên suốt | Phase 0+1 | Middleware B.4.1 |
| #80 Metric custom auth domain | Phase 7 | Metric B.15.1 |
| #81 Reuse detection event không log | Phase 1 | Publish AuditCreated khi reuse |

---

## B.23. Kết luận Phụ lục B

Phụ lục này là **playbook chi tiết để implement không sai sót**. Trước khi bắt đầu phase nào:

1. Đọc lại B.0 (10 nguyên tắc bất di bất dịch)
2. Check B.11 (30 pitfalls)
3. Verify B.12 (pre-implementation checklist)
4. Theo dõi B.13 (acceptance criteria)
5. Có B.14 (rollback procedure) sẵn sàng

**Đặc biệt cảnh báo:**
- ⚠️ Bug số 1: Publish event TRƯỚC commit → audit mất → cứ tưởng có nhưng không có
- ⚠️ Bug số 2: Quên FOR UPDATE SKIP LOCKED → duplicate event
- ⚠️ Bug số 3: Time skew + non-monotonic ID → audit timeline sai
- ⚠️ Bug số 4: PII trong metadata_json → GDPR breach
- ⚠️ Bug số 5: Migration backfill production fail → audit data mismatch

**Nếu gặp bất kỳ tình huống nào KHÔNG có trong phụ lục này → STOP, hỏi tech lead, KHÔNG tự quyết.**

Tài liệu này là sản phẩm sống — mỗi phase hoàn thành, update lesson learned vào đây.
