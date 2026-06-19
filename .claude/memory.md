# Memory — Ngữ cảnh bổ sung GSU26SE55

> File này chứa thông tin **không** có trong rules/tech/ hay CLAUDE.md.
> Team + kiến trúc: xem CLAUDE.md | Tech rules: xem rules/tech/*.md

---

## Timeline chi tiết (8 Sprints)

| Sprint | Thời gian | Nội dung chính |
|--------|-----------|----------------|
| Sprint 1 | 11/5 – 1/6 | Tài liệu + thiết kế hệ thống |
| Sprint 2 | 2/6 – 4/6 | Setup BE/FE/Mobile + authentication |
| Sprint 3 | 5/6 – 22/6 | Core battery data APIs |
| Sprint 4 | 23/6 – 6/7 | Ticket management system |
| Sprint 5 | 7/7 – 20/7 | SLA system implementation |
| Sprint 6 | 21/7 – 3/8 | Dashboard + mobile refinement |
| Sprint 7 | 4/8 – 7/8 | System testing toàn diện |
| Sprint 8 | 8/8 – 31/8 | IoT integration + optimization |
| Final | 1/9 – 6/9 | Deployment + chuẩn bị báo cáo |

---

## Dataset công khai

| Dataset | Cells | Ưu tiên | Ghi chú |
|---------|-------|---------|---------|
| NASA Ames Battery Aging | 18650 | **Ưu tiên 1** | nominal = 2.0 Ah |
| CALCE CS2 | Prismatic | Backup | — |
| MIT/Stanford Fast-Charging | 124 cells | Tùy chọn | — |
| Oxford Battery Degradation | — | Tùy chọn | — |

---

## Roster team

| Tên | MSSV | Role chính | Role phụ |
|-----|------|------------|----------|
| Nguyễn Phúc Duy | SE184821 | BE | FE, AI |
| Bùi Phước Thắng | SE180445 | BE | FE, AI |
| Mai Hồng Thái | SE183923 | BE | FE, AI |
| Trần Minh Trí | SE183109 | FE (Leader) | BE, AI |
| Nguyễn Nhật Minh | SE170310 | FE | BE, AI |

> AI là role phụ **chung toàn team** — ai cũng có thể được assign task AI khi cần.

---

## Business Impact

- Giảm 20–30% chi phí bảo trì
- Giảm tới 70% sự cố ngoài kế hoạch
- ROI điển hình: 10:1 đến 30:1

---

## Ghi chú triển khai

- IoT module là **tùy chọn** — chỉ triển khai Sprint 8 nếu core software đã xong
- Ưu tiên 60–70% effort cho core software trước
- GVHD: Trương Long (longt5@fe.edu.vn)

---

## Quyết định non-obvious — Sprint additional-auth (2026-06-18)

Các quyết định không hiển nhiên từ code, ghi lại để team kế thừa.

### Security & policy

- **CORS whitelist (`#AUTH-05` P0 pending):** Hiện giữ `SetIsOriginAllowed(origin => true)` ở `AddCORS.cs:11-15` vì capstone chưa có URL production cố định. **Phải replace bằng `WithOrigins(...)` từ config `AllowedOrigins:[]` trước go-live thật** — Leader chốt list domain FE/Mobile production.
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

- **`#AUTH-05` P0 pending:** CORS whitelist — chờ Leader chốt domain trước go-live thật.
- **`#AUTH-56` P3 defer:** Notification preferences — cross-service impact, hard-code default channel matrix đủ scope capstone. Re-evaluate khi có user spam complaint.
- **`#AUTH-61` P2 skip:** API versioning — single-version, FE+BE coordinate breaking change ad-hoc.
- **`#AUTH-63` P2 skip permanent:** Multi-tenancy OrgId — single-tenant scope, cần 1+ sprint riêng + cross-service impact.
- **`#AUTH-64` P1 defer:** KYC recovery — scope lớn (document upload + admin approval workflow + identity provider integration). Mitigation: admin-side reset qua `#AUTH-57` + `#AUTH-55` + manual support.
- **`#AUTH-71` P2 defer:** HTTPS redirect Docker — TLS termination ở reverse proxy (cloud-native). Deploy runbook chưa viết, sẽ tạo khi setup production env.
- **`#AUTH-73` P2 skip permanent:** Error code catalog `AUTH_*` — đã rollback. FE parse theo HTTP status + message đủ.
