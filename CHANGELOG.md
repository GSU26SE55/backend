# Changelog

Tuân theo [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.
Versions tuân theo [SemVer](https://semver.org/spec/v2.0.0.html).

## [1.6.0] — 2026-06-18 (Sprint additional-auth)

> Sprint **AuthService Security Hardening** — 76/90 task `#AUTH-01..90` (`#349..#438`) merged qua PR #446 + #441. 14 task defer/skip có ghi rõ ở §69 `overall.md`. Reference: `issue-authservice.md` 88 issue audit gốc.

### Security — Phase A P0 (9 task)

- **#AUTH-01** (`#349`) Hash refresh token DB (SHA-256) — migration `HashRefreshTokens` + backfill, sửa `AuthTokenIssuer`/`RefreshTokenCommandHandler`.
- **#AUTH-02** (`#350`) 2FA Disable require verify TOTP/password.
- **#AUTH-03** (`#351`) Google OAuth `state` CSRF validation (Redis key TTL 10 phút).
- **#AUTH-04** (`#352`) JWT `ValidateToken` enable issuer/audience/lifetime.
- **#AUTH-06** (`#354`) Reset token single-use enforcement.
- **#AUTH-07** (`#355`) Migration `FixEmailUniqueIndexFilter` — unique index filter `is_deleted = false`.
- **#AUTH-08** (`#356`) Logout invalidate pending 2FA challenge token.
- **#AUTH-09** (`#357`) OTP/Reset/2FA constant-time compare — `CryptographicOperations.FixedTimeEquals` ở 4 handler.
- **#AUTH-10** (`#358`) HTML sanitize `FullName`/`PendingEmail` ở email template.

### Security — Phase B P1 (7 task)

- **#AUTH-11** (`#359`) Implement `IJwtHelper.IsTokenValid()` + blacklist check.
- **#AUTH-12** (`#360`) RefreshToken cross-check IP/UA (device binding).
- **#AUTH-13** (`#361`) ForgotPassword per-email rate limit (Redis `otp_attempts:{emailHash}`).
- **#AUTH-15** (`#363`) JWT permission claims revoke realtime — subscribe `PermissionsChangedEvent` + `jti` blacklist Redis.
- **#AUTH-16** (`#364`) `PermissionResolver` cache với event invalidation.
- **#AUTH-17** (`#365`) Login enumeration attack — uniform audit log + timing jitter `Task.Delay(100,200)`.
- **#AUTH-54** (`#402`) Token Revocation List (TRL) — `POST /api/auth/revoke` + Redis `revoked_jti:{jti}`.

### Logic/Edge fixes — Phase C (11 task)

- **#AUTH-18..28** (`#366..#376`) Account Status semantics (Locked vs Inactive), failed login OTP reset rule, Google OAuth email mismatch policy, Google token timeout/retry, 2FA lazy re-encrypt recovery, ChangePassword check OldPassword != NewPassword, ChangeEmail Redis lock, Register PG 23505 unique violation parse, AcceptInvite validate expiry, VerifyOtp off-by-one fix, RefreshToken rotation `OriginalIssuedAt`.

### Audit + GDPR (2 task)

- **#AUTH-29** (`#377`) Migration `AuditLogAppendOnlyTrigger` — PG trigger `BEFORE UPDATE/DELETE ON auth_audit_logs RAISE EXCEPTION`. Sprint audit Phase 1 sẽ upgrade lên soft mode.
- **#AUTH-30** (`#378`) DeleteAccount cascade + anonymize PII (soft-delete + 90 ngày retention window, hard-delete qua `#AUTH-42`).

### Operational hardening — Phase D (15 task)

- **#AUTH-31** (`#379`) `PendingEmailCleanupBackgroundService` daily 02:00 UTC.
- **#AUTH-32** (`#380`) `RefreshTokenExpirationDays` read từ `JwtSettings`.
- **#AUTH-35** (`#383`) `GenericRepository.GetAllAsync()` default `AsNoTracking()` + opt-in tracking overload.
- **#AUTH-36** (`#384`) MediatR `ValidationBehavior` chạy TRƯỚC handler.
- **#AUTH-37** (`#385`) `OutboxRelayBackgroundService` honor `CancellationToken` + flush trước shutdown.
- **#AUTH-38** (`#386`) Inject `ISystemClock`/`SystemClock` toàn AuthService.
- **#AUTH-39** (`#387`) Email/PhoneNumber normalization (trim + lowercase + E.164).
- **#AUTH-40** (`#388`) Token introspection endpoint `POST /api/auth/introspect` (RFC 7662).
- **#AUTH-41** (`#389`) Concurrent session limit per account (`MaxConcurrentSessionsPerAccount=5`).
- **#AUTH-42** (`#390`) `AccountHardDeleteBackgroundService` daily 03:00 UTC drop `is_deleted=true AND deleted_at < now - 90d`.
- **#AUTH-43** (`#391`) `LockoutReconcileBackgroundService` mỗi 5 phút auto-unlock.
- **#AUTH-44** (`#392`) Session Device ID tracking + per-device revoke.
- **#AUTH-45** (`#393`) Backup code recovery rate limit (5 attempts/15min).
- **#AUTH-46** (`#394`) Email change rate limit (`PolicyAuthOtp` 3/min).
- **#AUTH-49** (`#397`) `OtpCleanupBackgroundService` daily clear expired OTP.

### Missing features (8 task)

- **#AUTH-50** (`#398`) Account reactivation sau soft-delete (90d window, verify email OTP).
- **#AUTH-52** (`#400`) Suspicious login alert — publish `SuspiciousLoginDetectedEvent` → email.
- **#AUTH-53** (`#401`) Password strength policy configurable (`PasswordPolicy:{...}`).
- **#AUTH-55** (`#403`) Admin forced logout endpoint.
- **#AUTH-57** (`#405`) Admin account unlock endpoint.
- **#AUTH-58** (`#406`) SMS OTP fallback cho 2FA — integration `SendSmsCommand` qua SmsService.
- **#AUTH-59** (`#407`) JWT `kid` header + key rotation (current + previous).
- **#AUTH-62** (`#410`) GDPR data export endpoint `GET /api/accounts/me/export`.

### Code quality + Ops (14 task)

- **#AUTH-60** (`#408`) Health checks `/health`/`/ready`/`/live` (DB + Redis + RabbitMQ).
- **#AUTH-65** (`#413`) Optimistic concurrency Account (`RowVersion` shadow property xmin) + retry.
- **#AUTH-66** (`#414`) `IValidateOptions<JwtSettings>` ValidateOnStart + `[Required]` annotation.
- **#AUTH-67** (`#415`) Idempotency middleware dedupe verification + integration test.
- **#AUTH-68** (`#416`) `LoginCommandHandler` set `LastLoginIp`/`LastLoginAt` sau pass 2FA.
- **#AUTH-69** (`#417`) Migration `MakeAccountRoleIdNullable` — `Account.RoleId` → `Guid?`.
- **#AUTH-70** (`#418`) `PasswordHasher` Singleton → Scoped (+ AUTH-83 combined).
- **#AUTH-72** (`#420`) `IJwtHelper.IsTokenValid()` implementation (giữ thay xoá — spec literal cho phép).
- **#AUTH-74** (`#422`) `OtpHelper.GenerateOtp` dùng `RandomNumberGenerator.GetInt32`.
- **#AUTH-75** (`#423`) Migration `AddAccountEmailIsDeletedIndex` — composite index `(email, is_deleted)`.
- **#AUTH-76** (`#424`) `GlobalExceptionMiddleware` mask stacktrace + PII redact (Production env).
- **#AUTH-77** (`#425`) `CorrelationIdMiddleware` end-to-end — propagate qua MassTransit header.
- **#AUTH-78** (`#426`) Prometheus metric: `auth_login_total{result}`, `auth_2fa_challenge_total{result}`, `auth_otp_usage_total`.
- **#AUTH-79** (`#427`) Refresh token reuse detection — publish `RefreshTokenReuseDetectedEvent`.
- **#AUTH-80** (`#428`) ClockSkew unify middleware + helper (`TimeSpan.Zero` cả 2 chỗ).
- **#AUTH-82** (`#430`) Lockout reconcile grace period — implicit 5 phút (AUTH-43 cycle).
- **#AUTH-83** (`#431`) `PasswordHasher` Scoped — combined với AUTH-70.

### Test gap — Phase F (7 task)

- **#AUTH-84** (`#432`) `EmailChangeCommandHandlerTests.cs` — 11 Fact tests (happy + race + OTP fail).
- **#AUTH-85** (`#433`) `AcceptInviteCommandHandlerTests.cs`.
- **#AUTH-86** (`#434`) `GoogleCallbackCommandHandlerTests.cs` — happy + state mismatch + email mismatch + timeout.
- **#AUTH-87** (`#435`) ChangePassword test — verify revoke sessions (direct via `RefreshToken.Status` + `ITokenRevocationStore`).
- **#AUTH-88** (`#436`) Outbox publish loop integration test — TestContainers Postgres + InMemory MassTransit.
- **#AUTH-89** (`#437`) `PermissionResolver` perf test — 1000 concurrent call, p99 < 50ms.
- **#AUTH-90** (`#438`) `ChangePasswordCommandHandler` dedicated test — old pwd check + revoke + audit log.

### Migrations

5 migration mới (rollback test PASS):

1. `20260618023820_HashRefreshTokens` (`#AUTH-01`)
2. `20260618022148_FixEmailUniqueIndexFilter` (`#AUTH-07`)
3. `20260618034223_AuditLogAppendOnlyTrigger` (`#AUTH-29`)
4. `20260618065708_AddAccountEmailIsDeletedIndex` (`#AUTH-75`)
5. `20260618071225_MakeAccountRoleIdNullable` (`#AUTH-69`)
6. `20260618032425_AddOriginalIssuedAtToRefreshToken` (`#AUTH-28`)

### Background services mới

4 hosted service đều honor `CancellationToken` graceful shutdown:

1. `PendingEmailCleanupBackgroundService` daily 02:00 UTC (`#AUTH-31`)
2. `LockoutReconcileBackgroundService` mỗi 5 phút (`#AUTH-43`)
3. `OtpCleanupBackgroundService` daily (`#AUTH-49`)
4. `AccountHardDeleteBackgroundService` daily 03:00 UTC (`#AUTH-42`)

### Followup P1+P2+P3 — 2026-06-19 (sau merge PR #446)

#### P1
- **#AUTH-14** (`#362`) Giảm OTP TTL 10 → 5 phút ở 4 handler:
  - `ForgotPasswordCommandHandler.cs:20`
  - `ResendResetOtpCommandHandler.cs:17`
  - `ReactivateRequestCommandHandler.cs:18`
  - `ChangeEmailCommandHandler.cs:20`
  - `EmailReserveTtl` Redis lock cũng align 5p (`ChangeEmailCommandHandler.cs:25`)
  - 4 test assertion updated (`PasswordResetCommandHandlerTests`, `ResendResetOtpCommandHandlerTests`, `EmailChangeCommandHandlerTests`)
  - 3 doc comment cập nhật TTL (PendingEmailCleanup, ConfirmEmailChange, AccountsController)
- **#AUTH-81** (`#429`) Async/await audit + Roslyn analyzer:
  - Audit kết quả: **0 violation** trong toàn AuthService `src/` + `tests/` cho 5 pattern (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`, `async void`, `throw ex;`). Code đã clean.
  - Thêm `services/AuthService/Directory.Build.props` — `Microsoft.VisualStudio.Threading.Analyzers` v17.10.48 + `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` cho tất cả 6 project con (Api/App/Domain/Infra + 2 test project).
  - Thêm 4 rule severity = `error` trong `.editorconfig` root: VSTHRD002 (sync wait), VSTHRD100 (async void), VSTHRD110 (unobserved task), CA2200 (throw ex preserve stack).
  - Silence VSTHRD200 (Async suffix) — tương thích ASP.NET Controller convention.
  - Build verify: 0 error, 5 warning pre-existing (không liên quan), 546/546 unit test PASS.

#### P2
- **#AUTH-33** (`#381`) LastLoginIp semantic — remove update LastLoginAt/LastLoginIp trong `RefreshTokenCommandHandler.cs:171-173`. Refresh token ≠ login event; field "last login" giờ semantic chuẩn (Password+2FA / Google / Invite accept). Audit history giữ ở LoginAttempt + audit_logs độc lập.
- **#AUTH-34** (`#382`) Concurrency retry wire — `ChangePasswordCommandHandler` + `ChangeAccountRoleCommandHandler` wrap business logic + SaveChanges trong `ConcurrencyRetryHelper.ExecuteAsync` (3 attempts, reload entity giữa retries). Thêm `IGenericRepository.ReloadAsync` API trong SharedKernels. `auditPublished` flag tránh double-audit khi retry. Returns 409 nếu account becomes invalid sau reload. 2 unit test mới (retry-success + invalid-after-reload).

#### P3
- **#AUTH-47** (`#395`) **Account Merge/Consolidation** — Implement đầy đủ.
  - Entity `AccountMergeLog` (immutable audit + JSON snapshot secondary + JSON conflict resolution), Account thêm fields `MergedIntoId`/`MergedAt`, migration `AddAccountMergeLog`.
  - Endpoint `POST /api/admin/accounts/{primaryId}/merge` (Admin-only, body: `secondaryAccountId` + `reason`).
  - Logic: revoke active RT của secondary → transfer GoogleId/Profile/StaffProfile sang primary CHỈ KHI primary chưa có (primary thắng conflict) → tombstone secondary (soft-delete + anonymize email `merged-{guid}@anonymized.local` tránh unique index violation) → insert AccountMergeLog → publish `AccountMerged` + meta-audit `AccountDeleted` → post-commit Redis TRL bulk revoke.
  - Audit action mới: `AccountMerged=130`, `AccountMergeRejected=131`.

- **#AUTH-48** (`#396`) **Trusted Device whitelist** — Implement đầy đủ.
  - Entity `TrustedDevice` (DeviceFingerprintHash SHA-256 + IpPrefix /24 IPv4 hoặc /64 IPv6 + Label + ExpiresAt + UsageCount + RevokedAt), migration `AddTrustedDevice`. Composite unique index `(AccountId, DeviceFingerprintHash)` filter active.
  - Helper `TrustedDeviceFingerprintHelper` (compute fingerprint, ipPrefix, auto-gen label "Chrome on macOS").
  - `Verify2FALoginCommand` thêm `TrustDevice: bool` + `TrustDeviceLabel: string?`. TTL 30 ngày. KHÔNG cấp lúc verify qua backup code path (emergency code không trust device).
  - `LoginCommandHandler` match active trusted device → skip 2FA challenge, issue tokens trực tiếp, audit `LoginWithTrustedDevice`. Metric `auth_login_total{result=success_trusted_device}` + `auth_2fa_challenge_total{result=skipped_trusted_device}`.
  - 3 endpoint user: `GET/DELETE/{id}/DELETE-all /api/accounts/me/trusted-devices`.
  - Auto-revoke khi `ChangePassword` + `Disable2FA` (qua `RevokeAllTrustedDevicesCommand` injected qua IMediator).
  - Audit action mới: `TrustedDeviceAdded=110`, `TrustedDeviceRevoked=111`, `TrustedDeviceAllRevoked=112`, `LoginWithTrustedDevice=113`.

- **#AUTH-51** (`#399`) **Cross-Device 2FA Confirmation** — Implement đầy đủ.
  - Redis-backed store `ITwoFactorCrossDeviceConfirmStore` (key `2fa:confirm-token:{token}`, TTL 10 phút, single-use).
  - 2 endpoint: `POST /api/auth/2fa/cross-device-confirm/request` (Device A initiate — sinh secret + 32-byte hex token, publish `SendTwoFactorCrossDeviceConfirmEmailEvent` qua Outbox, trả về OtpAuth URI + secret cho FE hiển thị QR), `POST /api/auth/2fa/cross-device-confirm` (Device B verify TOTP + enable 2FA, anti-stolen-link check: token chỉ confirm được bởi cùng AccountId).
  - SharedContract event `SendTwoFactorCrossDeviceConfirmEmailEvent` (ToEmail/FullName/ConfirmUrl/ExpiresInMinutes).
  - Audit action mới: `TwoFactorSetupCrossDeviceRequested=120`, `TwoFactorSetupCrossDeviceConfirmed=121`, `TwoFactorSetupCrossDeviceExpired=122`.

### Schema changes — followup

3 migration mới:
1. `AddTrustedDevice` — bảng `trusted_devices` + 2 index.
2. `AddAccountMergeLog` — bảng `account_merge_logs` + 2 column mới `accounts.merged_into_id`/`merged_at`.

### Deferred / Skipped (5 task còn lại — chốt final 2026-06-19)

- **P0 pending:** `#AUTH-05` CORS whitelist (Leader chốt domain trước go-live).
- **P1 defer:** `#AUTH-64` KYC recovery (scope lớn — sprint riêng, mitigate bằng admin-side reset).
- **P2 skip/defer:** `#AUTH-63` Multi-tenancy (single-tenant scope), `#AUTH-71` HTTPS redirect Docker (cloud-native TLS termination ở reverse proxy — runbook chưa viết).
- **Huỷ bỏ hoàn toàn (cancelled 2026-06-23):** `#AUTH-61` API versioning + `#AUTH-73` Error code catalog — xoá task definition (overall.md) + issue GitHub #409/#421. Không nằm trong scope capstone (single-version + FE parse theo HTTP status đủ).
- **P3 defer:** `#AUTH-56` Notification preferences (cross-service impact, hard-code default đủ cho capstone).

### Deviations (đã document trong overall.md §69)

- `#AUTH-21` Manual retry thay Polly (tránh thêm dependency cho 1 endpoint Google OAuth).
- `#AUTH-30` Hard-delete defer qua `#AUTH-42` (giữ 90 ngày cho AUTH-50 reactivation window).
- `#AUTH-82` Grace period implicit 5 phút (qua AUTH-43 cycle), không strict 1s.
- `#AUTH-87` Revoke trực tiếp qua `RefreshToken.Status` + `ITokenRevocationStore`, không qua Mediator command.
- `#AUTH-88` InMemory MassTransit thay RabbitMQ TestContainer (Outbox behavior vẫn end-to-end verified).

## [1.5.0] — 2026-07-26 (Sprint 5B)

### Added — Alert–Ticket Saga (BR P0 release gate)

- **#236** Saga contracts (`SharedContracts/Saga/AlertTicket/*`): 7 records + `BatteryAnomalyDetectedV2Event`.
- **#236** TicketService migration `AddAlertTicketSagaFoundation` — bảng `alert_ticket_saga_states`,
  unique filtered index `ux_tickets_origin_alert_id`, partial unique guard
  `ux_tickets_active_auto_per_asset_category`.
- **#236** BatteryService migration `AddAlertTicketLinkIndex` — non-unique filtered
  index `ix_alerts_ticket_id_filtered`.
- **#235** TicketService migration `AddQuartzPersistenceSchema` — 11 bảng `qrtz_*`
  theo official Quartz.NET PostgreSQL DDL.
- **#237** `AlertTicketSagaStateMachine` (MassTransit) — state machine
  Initial→TicketRequested→TicketProvisioned→AlertLinkRequested→Completed/Failed,
  PostgreSQL `xmin` optimistic concurrency, persistent Quartz timeout, bounded retry.
- **#238** Saga participants: `CreateTicketFromAlertConsumer` (TicketService),
  `LinkAlertToTicketConsumer` (BatteryService).
- **#238** `BatteryAlertEscalationRequestedEvent` tách khỏi `BatteryAnomalyDetectedEvent`.
- **#238** NotificationService consumers: `BatteryAlertEscalationRequestedConsumer`,
  `AlertTicketSagaFailedConsumer` + email templates + 2 enum value
  (`BatteryAlertEscalationPending=16`, `AlertTicketSagaFailed=17`).
- **#238** Feature flags `AlertTicketDispatchEnabled` + `AlertTicketSagaEnabled` cho cutover.
- **#239** Admin endpoints `/api/v1/admin/sagas/alert-ticket{,/{alertId}{,/reprocess}}`
  với `Idempotency-Key` requirement cho reprocess.
- **#239** `/api/ticket/health/saga` endpoint.
- **#239** 8 Prometheus metrics: `saga_alert_ticket_started/completed/failed/active/duration/timeout/redelivery/reprocessed`.
- **#239** 3 runbooks (`docs/runbooks/{08-saga-failed,09-saga-stuck,10-saga-duplicate-canonical}.md`).
- **#239** ADR-018: Orchestrated Alert–Ticket Saga.
- **#241** AuthService data migration `SeedSagaPermissions` — seed
  `ticket.saga.view` (Admin + Manager) + `ticket.saga.reprocess` (Admin only).
- **#241** `PermissionsChangedEvent` for cross-service cache invalidation.

### Added — Messaging hardening (#235)

- Tách `IIntegrationEventOutboxWriter` (in-transaction write) khỏi
  `IIntegrationEventTransport` (post-commit publish) — DI split.
- NuGet packages: `MassTransit.EntityFrameworkCore` 8.4.1, `MassTransit.Quartz` 8.4.1,
  `Quartz.AspNetCore` 3.14.0, `Quartz.Extensions.Hosting` 3.14.0, `Quartz.Serialization.Json` 3.14.0.

### Changed

- **#233** Battery scope cleanup — Energy/CO2 analytics loại bỏ permanent. ADR-017 merged.
- **#233** Pre-commit hook `energy-co2-scope-guard` thêm vào `.pre-commit-config.yaml`.
- **#234** BatteryService entity `Site` bỏ field `CapacityKw` + DTO + validation
  + seed + Mapper + Controller XML docs. Migration `RemoveSiteCapacityKw` (Up/Down + rollback).
- **#238** `BatteryAnomalyDetectedConsumer` (TicketService) mark `[Obsolete]` —
  Saga state machine giờ handle anomaly events.

### Deprecated

- `BatteryAnomalyDetectedConsumer` (TicketService) — sẽ remove ở Sprint 6 sau khi
  Saga stable, không có rollback nào trong window cutover.

### Migration order (Sprint 5B — bắt buộc tuần tự)

1. BatteryService `RemoveSiteCapacityKw` (`#234`)
2. BatteryService + TicketService `AddDurableMessagingFoundation` (deferred — pending)
3. TicketService `AddQuartzPersistenceSchema` (`#235`)
4. Preflight data cleanup (runbook `10-saga-duplicate-canonical.md`)
5. TicketService `AddAlertTicketSagaFoundation` (`#236`)
6. BatteryService `AddAlertTicketLinkIndex` (`#236`)
7. AuthService `SeedSagaPermissions` (`#241`) — khác DB, có thể chạy song song.

## [1.4.0] — 2026-07-19 (Sprint 5)

- Ticket SLA timer + escalation flow (`#150`/`#151`).

## [1.0.0] — 2026-05-24 (Sprint 1)

- Khởi tạo monorepo + base services.

[1.6.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.6.0
[1.5.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.5.0
[1.4.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.4.0
[1.0.0]: https://github.com/GSU26SE55/backend/releases/tag/v1.0.0
