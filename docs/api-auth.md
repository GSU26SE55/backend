# API Documentation — AuthService

> Base URL: `http://localhost:{port}/api`
> Content-Type mặc định: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem phần [Cấu trúc Response chung](#cấu-trúc-response-chung)

---

## Server-side Sort (`SortBy` + `SortDir`) — cập nhật đợt này

**Mục đích:** cho phép sort **toàn bộ dataset** ở server rồi mới phân trang (thay client-side sort chỉ sort được 1 page hiện tại). FE bấm header cột → gửi `SortBy`/`SortDir`.

**Tác dụng:** BE `ORDER BY <cột> <chiều>, Id ASC` **trước** `Skip/Take` → page trả về đúng thứ tự toàn cục. Không truyền `SortBy` → giữ nguyên thứ tự mặc định cũ (không phá behavior). **Response shape KHÔNG đổi** — chỉ đổi thứ tự phần tử trong `items`.

**Request — 2 query param mới (PascalCase, đều optional):**

| Param | Type | Nullable | Default | Mô tả |
|---|---|---|---|---|
| `SortBy` | string | ✓ | field mặc định của endpoint | Whitelist per-endpoint; ngoài whitelist → coi như field mặc định |
| `SortDir` | string | ✓ | `desc` | `asc` \| `desc`; giá trị lạ → `desc` |

**Response:** `CommonResponse<PaginationResponse<AccountDto>>` — giữ nguyên (`items`/`totalItems`/`pageNumber`/`pageSize`/`totalPages`/`hasNextPage`/`hasPreviousPage`).

### `GET /api/admin/accounts`

Ví dụ: `GET /api/admin/accounts?PageNumber=1&PageSize=10&SortBy=fullName&SortDir=asc`

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `fullName` | tên đầy đủ | string | Không |
| `role` | tên role | string | Không (`""` nếu chưa gán role) |
| `status` | trạng thái tài khoản | enum `AccountStatusEnum` | Không |
| `createdAt` *(default)* | ngày tạo | datetime | Không |

**`AccountStatusEnum`** — tác dụng từng giá trị:

| Giá trị | Số | Ý nghĩa |
|---|---|---|
| `PendingVerification` | 0 | Vừa đăng ký, chưa xác thực email/OTP. ⚠️ FE đừng treat `0` là falsy |
| `Active` | 1 | Đã xác thực, hoạt động bình thường |
| `Locked` | 2 | Khoá tạm bởi system (5 lần fail password/OTP liên tiếp) — auto-unlock khi hết hạn |
| `Inactive` | 3 | Admin deactivate — KHÔNG auto-unlock, phải request admin |
| `Suspended` | 4 | Đình chỉ do vi phạm policy (admin set/clear) |
| `Banned` | 5 | Cấm vĩnh viễn, không reactivate được |

### `GET /api/accounts/me/login-history`

| `SortBy` | Sort theo | Kiểu | Nullable |
|---|---|---|---|
| `createdAt` *(default)* | thời điểm login attempt | datetime | Không |
| `result` | kết quả đăng nhập | enum `LoginAttemptResult` | Không |
| `method` | `"Password"` / `"Google"` / `"VerifyOtp"` | string | Không |
| `ipAddress` | IP client | string | **Có** (null nếu không ghi được) |

**`LoginAttemptResult`:** `Success=1` · `WrongPassword=2` · `AccountNotFound=3` · `AccountLocked=4` · `AccountSuspended=5` · `AccountBanned=6` · `AccountInactive=7` · `AccountNotVerified=8`.

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `isSuccess` | `bool` | `true` nếu thành công, `false` nếu có lỗi nghiệp vụ |
| `statusCode` | `int` | HTTP status code |
| `message` | `string?` | Thông báo tóm tắt kết quả |
| `data` | `T?` | Dữ liệu trả về, `null` khi thất bại |
| `listErrors` | `Errors[] \| null` | Field-level validation errors — null nếu không có lỗi field-level (business/system errors chỉ ghi vào `message`) |

**Quy ước ListErrors vs Message (GH-295 strict):**
- **Field validation** (user nhập sai body field) → `listErrors` chứa `{field, detail}`, `message = "Dữ liệu không hợp lệ."` generic
- **Business rule / system error** (vd wrong password, expired session, conflict state) → `message` chứa mô tả cụ thể, `listErrors = null`
- `ErrorsListJsonConverter` tự convert `List<Errors>` rỗng → JSON `null` — handler không cần handle thủ công

**HTTP status code convention (GH-295 strict):**
- `200` — Thành công
- `201` — Tạo resource mới
- `400` — Field validation fail (body field user submit format/required sai) → có `listErrors`
- `401` — Token thiếu/hết hạn — chỉ cho endpoint có `[Authorize]`. **Body LUÔN có `data.errorCode`** (7 giá trị, xem bảng "Bảng errorCode chi tiết" bên dưới). FE PHẢI parse `response.data?.errorCode` để phân biệt UX (auto-refresh token vs hard logout vs show "Login again").
- `403` — Có token nhưng không đủ permission / sai role. **Body**: `{isSuccess: false, statusCode: 403, message: "Bạn không có quyền truy cập tài nguyên này.", data: {errorCode: "FORBIDDEN"}, listErrors: null}` (từ JwtBearer `OnForbidden` event).
- `404` — Resource không có trong DB
- `409` — Conflict với state hiện tại (vd 2FA đã enable khi user gọi `/enable` lại)
- `410` — Endpoint đã deprecated (vd `/2fa/enable` cũ sau GH-295)
- `422` — Business rule violation: format đúng nhưng value/state sai (vd wrong TOTP, wrong password khi disable, expired challenge token)
- `423` — Account bị lockout tạm thời (sai password quá số lần)
- `429` — Rate limit — response **luôn** kèm header `Retry-After: <seconds>` để FE đếm ngược disable button; body cố định `{isSuccess: false, statusCode: 429, message: "Quá nhiều yêu cầu. Vui lòng thử lại sau."}` (KHÔNG có `data` hay `listErrors`)
- `500` — Lỗi server ngoài dự kiến

**Rate limit policies — tổng hợp:**

| Policy | Endpoints áp dụng | Limit | Partition theo |
|---|---|---|---|
| `Login` | `POST /api/auth/login` | 10 / phút | IP |
| `AnonOtp` | `register`, `resend-otp`, `forgot-password`, `resend-reset-otp`, `reactivate-request`, `reactivate-verify` | 5 / phút | IP |
| `AuthOtp` | `me/change-email`, `me/send-phone-otp`, `me/2fa/init`, `me/2fa/confirm` | 3 / phút | userId (JWT NameIdentifier), fallback IP |
| `TwoFactorVerify` | `login/verify-2fa`, `login/2fa/sms` | 5 / 5 phút | **Header `X-Challenge-Token`**, fallback IP |
| `TwoFactorDisable` | `me/2fa/disable` | 3 / 5 phút | userId, fallback IP |
| `BackupCodeRegenerate` | `me/2fa/backup-codes/regenerate` | 3 / **giờ** | userId, fallback IP |

**Endpoints KHÔNG có middleware rate limit** (chỉ dựa vào lockout / business rule ở handler): `verify-otp`, `verify-reset-otp`, `reset-password`, `refresh-token`, `logout`, `revoke`, `introspect`, `accept-invite`, `google/login`, `google/callback`, và toàn bộ endpoint authenticated khác (sessions, accounts profile, admin/*).

---

## Bảng errorCode chi tiết — 401 / 403 từ Auth middleware

Mọi endpoint authenticated (`[Authorize]`) trả 401/403 với **body có `data.errorCode`** — FE PHẢI parse field này để quyết định UX (refresh, login lại, hiển thị error message). Có **2 middleware** đều có thể trả 401 với errorCode khác nhau:

### Từ `JwtBearerEvents` (chạy TRƯỚC TokenRevocation):

| errorCode | Status | Message Vietnamese | Trigger | FE UX recommendation |
|---|---|---|---|---|
| `MISSING_TOKEN` | 401 | "Không tìm thấy thông tin xác thực (thiếu Authorization header)." | Request không có header `Authorization` | Redirect `/login`, không cần refresh |
| `TOKEN_EXPIRED` | 401 | "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại hoặc làm mới token." | JWT `exp < now` (`SecurityTokenExpiredException`). **Header response thêm `Token-Expired: true`** | **Auto-trigger `/auth/refresh-token`** với current refresh token; nếu fail → logout |
| `INVALID_SIGNATURE` | 401 | "Token không hợp lệ (chữ ký không đúng)." | Signature verification fail (sai SecretKey hoặc đã rotate) | Hard logout, clear tokens, redirect `/login` |
| `INVALID_TOKEN` | 401 | "Token không hợp lệ. Vui lòng đăng nhập lại." | Other AuthenticateFailure (vd issuer/audience mismatch, malformed JWT) | Hard logout |
| `UNAUTHORIZED` | 401 | "Bạn chưa đăng nhập. Vui lòng cung cấp token hợp lệ." | Fallback (header có nhưng không parse được như JWT) | Hard logout |
| `FORBIDDEN` | 403 | "Bạn không có quyền truy cập tài nguyên này." | Đã authenticated nhưng `[Authorize(Roles=...)]` reject | Hiển thị "Không có quyền", **không** redirect login |

### Từ `TokenRevocationMiddleware` (chạy SAU JwtBearer, #AUTH-54):

| errorCode | Status | Message Vietnamese | Trigger | FE UX recommendation |
|---|---|---|---|---|
| `TOKEN_REVOKED` | 401 | "Token đã bị thu hồi." | `jti` cụ thể trong blacklist Redis `revoked_jti:{jti}`. Set bởi `/auth/revoke` user-facing | Hiển thị "Token này đã bị thu hồi", refresh có thể work (token mới sẽ có jti mới) |
| `TOKEN_REVOKED_ACCOUNT` | 401 | "Token đã bị thu hồi." | Bulk account cutoff: `token.iat < cutoff`. Set bởi `/auth/logout`, `/me/password`, `/reset-password`, `/admin/accounts/{id}/sessions/revoke-all` | Hard logout — refresh token cùng account cũng đã bị revoke, phải login lại |

**Response body shape (cố định cho mọi 401/403 từ middleware):**

```json
{
  "isSuccess": false,
  "statusCode": 401,
  "message": "<message tiếng Việt>",
  "data": { "errorCode": "<TOKEN_EXPIRED | TOKEN_REVOKED | ...>" },
  "listErrors": null
}
```

**Đặc biệt — `Token-Expired: true` header:**

JwtBearer middleware set header `Token-Expired: true` trên response 401 **CHỈ KHI** `errorCode = TOKEN_EXPIRED`. FE có thể check header này nhanh hơn parse body để trigger auto-refresh:

```js
// Axios interceptor pattern
axios.interceptors.response.use(null, async (error) => {
  if (error.response?.status === 401 && error.response.headers['token-expired'] === 'true') {
    // Auto-trigger refresh, retry original request
    return refreshAndRetry(error);
  }
  // Other 401 errorCodes → hard logout
  if (error.response?.status === 401) {
    const code = error.response.data?.data?.errorCode;
    if (code === 'TOKEN_REVOKED_ACCOUNT' || code === 'INVALID_SIGNATURE') {
      hardLogout();
    }
  }
  return Promise.reject(error);
});
```

> **Lưu ý**: Handler-level 401 (vd `/login` wrong password message *"Email hoặc mật khẩu không chính xác"*) **không** dùng pattern này — body chỉ có `message`, không có `data.errorCode`. Pattern errorCode chỉ áp dụng cho 401 từ auth middleware (token validation).

---

## FE Migration Guide — GH-295 Breaking Changes

**Endpoints affected:**

| Endpoint | Method | Trước GH-295 | Sau GH-295 |
|---|---|---|---|
| `/api/auth/login` | POST | Trả `data.accessToken` + `data.refreshToken` trực tiếp | Trả discriminated union `data.tokens.*` HOẶC `data.challenge.*` |
| `/api/auth/refresh-token` | POST | Tương tự login cũ | Wrap trong `data.tokens.*` (luôn `data.challenge = null`) |
| `/api/auth/google/callback` | GET | Tương tự login cũ | Wrap trong `data.tokens.*` |
| `/api/auth/accept-invite` | POST | Tương tự login cũ | Wrap trong `data.tokens.*` |
| `/api/accounts/me/2fa/enable` | POST | Activate ngay, trả secret + URI | **410 Gone** — đổi sang `/2fa/init` + `/2fa/confirm` |
| `/api/accounts/me/2fa/disable` | POST | Body rỗng | Body bắt buộc `{password, totpCode}` |

**Endpoints mới:**

| Endpoint | Method | Mục đích FE |
|---|---|---|
| `/api/auth/login/verify-2fa` | POST | Bước 2 login khi user bật 2FA |
| `/api/auth/login/2fa/sms` | POST | (#AUTH-58) Gửi OTP qua SMS như fallback khi user mất Authenticator app |
| `/api/auth/revoke` | POST | (#AUTH-54) RFC 7009 — user revoke 1 access token cụ thể qua `jti` |
| `/api/auth/introspect` | POST | (#AUTH-40) RFC 7662 — resource server check token active/inactive (service-to-service) |
| `/api/auth/reactivate-request` | POST | (#AUTH-50) Bước 1 khôi phục account đã soft-delete trong window 90 ngày |
| `/api/auth/reactivate-verify` | POST | (#AUTH-50) Bước 2 khôi phục — verify OTP, restore account về Active |
| `/api/accounts/me/2fa/init` | POST | Bước 1 enroll — sinh QR + pendingToken |
| `/api/accounts/me/2fa/confirm` | POST | Bước 2 enroll — verify TOTP, trả 8 backup codes (1 lần) |
| `/api/accounts/me/2fa/backup-codes/regenerate` | POST | User sinh lại 8 codes mới (cần TOTP) |
| `/api/accounts/me/export` | GET | (#AUTH-62) GDPR Article 20 — user download toàn bộ data của mình dưới dạng JSON file |
| `/api/admin/accounts/{id}/2fa` | DELETE | Admin reset 2FA của user khác |

**Migration steps cho FE:**

1. **Sửa Login handler:**
   ```js
   const res = await api.post('/api/auth/login', { email, password });
   if (res.data.data.requiresTwoFactor) {
     // 2FA on: lưu challengeToken trong memory, redirect màn hình OTP
     setChallengeToken(res.data.data.challenge.challengeToken);
     navigate('/login/2fa');
   } else {
     // 2FA off: save tokens như cũ
     saveTokens(res.data.data.tokens.accessToken, res.data.data.tokens.refreshToken);
     navigate('/');
   }
   ```
2. **Màn hình 2FA verify** — POST `/api/auth/login/verify-2fa` với `{challengeToken, code, isBackupCode}`. Response giống login Case A (`data.tokens.*`).
3. **Cập nhật Refresh + Google callback + Accept invite handler** — đường truy cập tokens đổi từ `data.accessToken` → `data.tokens.accessToken`.
4. **Setup 2FA wizard** (replace single-screen enable):
   - Step 1: POST `/2fa/init` → render QR từ `data.otpAuthUri`, hiển thị `data.secret` để nhập tay
   - Step 2: User scan + nhập 6 số → POST `/2fa/confirm` với `pendingToken` + `code`
   - Step 3: Hiển thị 8 backup codes từ `data.backupCodes` với UI bắt buộc user "Tôi đã lưu" trước khi đóng (codes không hiển thị lại được)
5. **Disable 2FA form** — thêm 2 input password + totpCode, submit `{password, totpCode}`.
6. **Settings page** — thêm button "Regenerate backup codes" (modal nhập TOTP) + button "Disable 2FA".
7. **Admin panel** — thêm button "Reset 2FA" cho admin user (confirm dialog → DELETE `/api/admin/accounts/{id}/2fa`).

**Error handling pattern theo status code:**
- `422` (wrong code, expired session, wrong password) → hiển thị message dưới input field hoặc toast, KHÔNG redirect login
- `429` (rate limit) → countdown disable button + hiển thị "Thử lại sau {Retry-After}s"
- `403` (account suspended/locked giữa 2FA challenge) → clear local session, redirect login với toast warning
- `404` (account deleted giữa challenge) → clear local session, redirect login

---

---

## Enums

### `AccountStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `PendingVerification` | 0 | Vừa đăng ký, chưa xác thực email/OTP |
| `Active` | 1 | Đã xác thực, đang hoạt động bình thường |
| `Locked` | 2 | Bị khóa tạm thời (nhập sai mật khẩu nhiều lần) |
| `Inactive` | 3 | Bị vô hiệu hóa bởi Admin |
| `Suspended` | 4 | Bị đình chỉ do vi phạm chính sách |
| `Banned` | 5 | Bị cấm theo nghiệp vụ/quản trị |

**Lưu ý:** `PendingVerification = 0` là exception có chủ đích vì đây là trạng thái mặc định của account mới tạo trước khi verify OTP/accept invite. FE phải xem `status = 0` là giá trị hợp lệ, không coi là missing data. User tự xóa `DELETE /api/accounts/me` dùng soft delete (`IsDeleted = true`), không dùng `Banned` để biểu diễn lý do tự xóa.

### `RefreshTokenStatus`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Active` | 1 | Token còn hiệu lực, có thể dùng để refresh |
| `Used` | 2 | Token đã được dùng để cấp token mới (rotation) |
| `Revoked` | 3 | Token đã bị thu hồi thủ công (logout, đổi mật khẩu, admin revoke) **HOẶC** bị revoke do reuse-attack detection (`RevokedReason = "RefreshToken reuse detected"`) hoặc device binding mismatch (`RevokedReason = "DeviceBindingMismatch"`) |
| `Expired` | 4 | Token đã hết hạn theo thời gian |
| `Compromised` | 5 | Enum value reserved — **handler hiện tại chưa sử dụng**. Khi phát hiện replay attack, `RefreshTokenCommandHandler` set `Status = Revoked` với `RevokedReason = "RefreshToken reuse detected"` (xem `POST /api/auth/refresh-token`). FE filter theo `revokedReason` thay vì status để phân biệt logout thủ công vs reuse attack. |

### `RoleStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Active` | 1 | Role đang hoạt động, có thể gán cho account |
| `Inactive` | 2 | Role tạm thời bị vô hiệu hóa, không thể gán mới |
| `Deprecated` | 3 | Role không còn dùng nữa |

### `OtpPurposeEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Register` | 1 | OTP dùng để kích hoạt tài khoản sau đăng ký |
| `PasswordReset` | 2 | OTP dùng để xác thực luồng quên mật khẩu |
| `PhoneVerify` | 3 | OTP gửi qua SMS để xác thực số điện thoại |
| `EmailChange` | 4 | OTP gửi để xác thực yêu cầu đổi email |

### `AvatarSourceEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `None` | 0 | Chưa có avatar |
| `Uploaded` | 1 | Avatar được upload thủ công lên FileStorageService |
| `Google` | 2 | Avatar lấy từ tài khoản Google |

### `StaffSkillTierEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Generalist` | 1 | Staff đa năng — đảm nhận incident P3 Standard, scope SingleAsset urgency thấp/TB |
| `ModuleSpecialist` | 2 | Chuyên 1 module (BMS, Inverter, LiFePO4…) — đảm nhận P2 High hoặc P3 chuyên sâu |
| `SeniorSpecialist` | 3 | Senior tier 3 — đảm nhận P1 Critical, escalation, scope Site/MultiSite |

**Lưu ý:** Tier khớp với SLA priority theo `overall.md §7` (Tier 1 ↔ P3 / Tier 2 ↔ P2 / Tier 3 ↔ P1). Field này set qua `PUT /api/admin/staff/{id}/profile`.

---

### `LoginAttemptResult`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Success` | 1 | Đăng nhập thành công |
| `WrongPassword` | 2 | Sai mật khẩu |
| `AccountNotFound` | 3 | Email không tồn tại |
| `AccountLocked` | 4 | Tài khoản đang bị khóa |
| `AccountSuspended` | 5 | Tài khoản đang bị đình chỉ |
| `AccountBanned` | 6 | Tài khoản đã bị banned |
| `AccountInactive` | 7 | Tài khoản đang bị vô hiệu hóa |
| `AccountNotVerified` | 8 | Tài khoản chưa xác thực email |

### `AuditActionEnum`

| Giá trị | Int | Nhóm |
|---|---|---|
| `LoginSuccess` | 1 | Auth |
| `LoginFailedWrongPassword` | 2 | Auth |
| `LoginFailedAccountLocked` | 3 | Auth |
| `LoginFailedAccountSuspended` | 4 | Auth |
| `LoginFailedAccountBanned` | 5 | Auth |
| `LoginFailedAccountInactive` | 6 | Auth |
| `LoginFailedNotVerified` | 7 | Auth |
| `AccountAutoLocked` | 8 | Auth |
| `Logout` | 9 | Auth |
| `GoogleLoginSuccess` | 10 | Auth |
| `GoogleLoginFailed` | 11 | Auth |
| `TokenRefreshed` | 12 | Auth |
| `TokenReuseDetected` | 13 | Auth — phát hiện replay attack |
| `PasswordChanged` | 20 | Password/OTP |
| `PasswordReset` | 21 | Password/OTP |
| `OtpVerifySuccess` | 22 | Password/OTP |
| `OtpVerifyFailed` | 23 | Password/OTP |
| `EmailChangeRequested` | 24 | Password/OTP |
| `EmailChangeConfirmed` | 25 | Password/OTP |
| `PhoneVerified` | 26 | Password/OTP |
| `TwoFactorEnabled` | 40 | 2FA — user kích hoạt thành công qua `POST /me/2fa/confirm` |
| `TwoFactorDisabled` | 41 | 2FA — user tắt qua `POST /me/2fa/disable` (sau khi verify password + TOTP) |
| `TwoFactorReset` | 42 | 2FA — reserved cho self-reset flow (chưa expose endpoint) |
| `BackupCodeRedeemed` | 43 | 2FA — user dùng backup code để login (single-use); ghi cùng `LoginWith2FA` |
| `BackupCodesRegenerated` | 44 | 2FA — user gọi `POST /me/2fa/backup-codes/regenerate` (vô hiệu hóa codes cũ) |
| `Admin2FAReset` | 45 | 2FA — admin reset 2FA của user khác qua `DELETE /api/admin/accounts/{id}/2fa` |
| `LoginWith2FA` | 46 | 2FA — login hoàn tất bước 2 (`POST /api/auth/login/verify-2fa`) — metadata.method=`totp`/`backupCode` |
| `LoginPending2FA` | 47 | 2FA — login bước 1 đã verify password OK, đang chờ verify TOTP (status pending) |
| `GoogleLinked` | 50 | Google |
| `GoogleUnlinked` | 51 | Google |
| `AccountRegistered` | 60 | Account Lifecycle |
| `AccountCreatedByAdmin` | 61 | Account Lifecycle |
| `AccountUpdated` | 62 | Account Lifecycle |
| `AccountStatusChanged` | 63 | Account Lifecycle |
| `AccountUnlocked` | 64 | Account Lifecycle |
| `AccountDeactivated` | 65 | Account Lifecycle |
| `AccountDeleted` | 66 | Account Lifecycle |
| `AccountInviteSent` | 67 | Account Lifecycle |
| `AccountInviteAccepted` | 68 | Account Lifecycle |
| `SessionRevoked` | 80 | Session |
| `AllSessionsRevoked` | 81 | Session |
| `AdminForceLogout` | 82 | Session |
| `SessionLimitExceededOldestRevoked` | 83 | Session |
| `RoleAssigned` | 90 | Role/Permission |
| `RoleRevoked` | 91 | Role/Permission |
| `RoleTemporaryAssigned` | 92 | Role/Permission |
| `RoleCreated` | 93 | Role/Permission |
| `RoleUpdated` | 94 | Role/Permission |
| `RoleStatusChanged` | 95 | Role/Permission |
| `RoleDeleted` | 96 | Role/Permission |
| `PermissionGranted` | 97 | Role/Permission |
| `PermissionRevoked` | 98 | Role/Permission |
| `TrustedDeviceAdded` | 110 | Trusted Device (#AUTH-48) — user opt-in trust device sau khi verify 2FA TOTP/SMS thành công. Metadata: `label`, `ipPrefix`, `ttlDays` |
| `TrustedDeviceRevoked` | 111 | Trusted Device — user revoke 1 device cụ thể qua `DELETE /me/trusted-devices/{id}`. Metadata: `trustedDeviceId`, `label` |
| `TrustedDeviceAllRevoked` | 112 | Trusted Device — user revoke toàn bộ qua `DELETE /me/trusted-devices`, HOẶC auto-revoke khi `ChangePassword`/`Disable2FA`. Metadata: `revokedCount`. Reason: `"User revoked all"`, `"Password changed"`, `"2FA disabled"` |
| `LoginWithTrustedDevice` | 113 | Trusted Device — login bypass 2FA challenge nhờ trust device match. Metadata: `trustedDeviceId`, `label`, `sessionId` |
| `TwoFactorSetupCrossDeviceRequested` | 120 | Cross-Device 2FA (#AUTH-51) — user request setup 2FA xuyên thiết bị qua `POST /api/auth/2fa/cross-device-confirm/request`. Metadata: `ttlMinutes`, `requestingSessionId` |
| `TwoFactorSetupCrossDeviceConfirmed` | 121 | Cross-Device 2FA — Device B confirm thành công TOTP + token → 2FA enabled. Metadata: `requestingSessionId`. Ghi cùng `TwoFactorEnabled=40` với `metadata.method="cross-device"` |
| `TwoFactorSetupCrossDeviceExpired` | 122 | Cross-Device 2FA — token confirm hết hạn HOẶC bị reject (account mismatch — chống stolen-link). Metadata có thể có `expectedAccountId`/`actualAccountId` nếu reject |
| `AccountMerged` | 130 | Account Merge (#AUTH-47) — admin merge 2 account qua `POST /api/admin/accounts/{id}/merge`. Metadata: `primaryAccountId`, `secondaryAccountId`, `sessionsRevoked`, `auditLogsLinked`, `mergeLogId` |
| `AccountMergeRejected` | 131 | Account Merge — reserved cho future workflow nếu yêu cầu merge bị reject (vd 1 trong 2 account đã merge trước đó). Hiện handler return 409 KHÔNG publish audit; enum chuẩn bị sẵn nếu thêm reject workflow |

---

## DTOs dùng chung

Các DTO bên dưới được tham chiếu bởi nhiều endpoint (`GET /api/auth/me`, `PUT /api/auth/me/profile`, `POST /api/auth/me/avatar`, `GET /api/admin/accounts/{id}`, `GET /api/admin/accounts`, …). FE dùng lại cùng 1 type cho mọi response trả về account.

### `AccountDto`

**Dùng cho:** body `data` của các endpoint trả profile tổng hợp 1 account — `GET /api/auth/me`, `GET /api/accounts/me/profile`, `PUT /api/auth/me/profile`, `POST /api/auth/me/avatar`, `GET /api/admin/accounts/{id}`. Trong `GET /api/admin/accounts`, mỗi item trong `data.items` cũng là `AccountDto`.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | AccountId |
| `email` | `string` | Không | Email đăng nhập (lowercase normalized) |
| `phoneNumber` | `string?` | Null nếu chưa nhập | Số điện thoại |
| `fullName` | `string` | Không | Họ tên hiển thị |
| `avatarUrl` | `string?` | Null nếu chưa set hoặc dùng `profile.externalAvatarUrl` | URL avatar legacy (direct URL lưu trên `Account.AvatarUrl`). FE **không nên** render trực tiếp field này — dùng `displayAvatarUrl` |
| `dateOfBirth` | `DateTime?` | Null nếu chưa nhập | Ngày sinh UTC |
| `address` | `string?` | Null nếu chưa nhập | Địa chỉ ở Account level |
| `emailConfirmed` | `bool` | Không | Email đã verify chưa |
| `phoneConfirmed` | `bool` | Không | Phone đã verify chưa (chỉ true sau khi user verify OTP SMS) |
| `twoFactorEnabled` | `bool` | Không | 2FA đang bật không |
| `isGoogleLinked` | `bool` | Không | `true` nếu account đã liên kết đăng nhập Google (`Account.GoogleId != null`). Màn Cài đặt dùng field này để hiện nút "Liên kết" / "Hủy liên kết" Google (`POST /api/accounts/me/link-google` / `unlink-google`). **Không expose `googleId`** — chỉ trả cờ bool |
| `status` | `AccountStatusEnum` | Không | Trạng thái account — int (`0` = PendingVerification, …) |
| `lastLoginAt` | `DateTime?` | Null nếu chưa từng login | Lần login cuối UTC |
| `createdAt` | `DateTime` | Không | Account tạo lúc nào UTC |
| `updatedAt` | `DateTime?` | Null nếu chưa từng update | Lần update cuối UTC |
| `roleId` | `Guid?` | Null nếu role bị xóa (`#AUTH-69` — sau refactor 1-N có thể null) | Id role hiện tại |
| `role` | `string` | Không (có thể empty string nếu role bị xóa/disable) | Tên role (`"Admin"`, `"Manager"`, `"Staff"`, `"Customer"`) |
| `roleAssignedAt` | `DateTime?` | Null nếu chưa từng đổi role | Lần đổi role gần nhất UTC |
| `roleAssignedBy` | `Guid?` | Null nếu là seed data hoặc self-register | AccountId của người gán role lần cuối |
| `profile` | `AccountProfileDto?` | **Có** — null nếu user chưa từng cập nhật profile mở rộng | Profile mở rộng (timezone, avatar Google, …) |
| `staffProfile` | `StaffProfileDto?` | **Có** — null nếu user không phải staff (Customer) | Staff profile (department, skills, …) |
| `displayAvatarUrl` | `string?` | Null nếu user chưa có avatar nào | **URL avatar đã resolve sẵn** — FE render trực tiếp field này. Quy tắc resolve: (1) ưu tiên upload qua FileStorageService `/api/files/{avatarFileId}/download`, (2) fallback `profile.externalAvatarUrl` (Google), (3) null nếu cả hai không có |

> **Quan trọng — quy tắc render avatar:** FE **luôn** dùng `displayAvatarUrl` để hiển thị. Các field `avatarUrl` (legacy) và `profile.externalAvatarUrl` chỉ có tính debug/legacy, không phải single source of truth.

### `AccountProfileDto`

**Dùng cho:** field `profile` trong `AccountDto`. Lưu các field profile mở rộng (tách khỏi bảng `Account` để giảm column count + dễ extend).

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `accountId` | `Guid` | Không | AccountId chủ profile (= `AccountDto.id`) |
| `avatarFileId` | `Guid?` | Null nếu chưa upload | FileId từ FileStorageService (sau khi gọi `POST /api/auth/me/avatar`) |
| `externalAvatarUrl` | `string?` | Null nếu chưa link Google | URL avatar lấy từ Google account khi login bằng Google |
| `avatarSource` | `AvatarSourceEnum` | Không | Nguồn avatar đang dùng — int (`0` = None, `1` = Uploaded, `2` = Google) |
| `address` | `string?` | Null nếu chưa nhập | Địa chỉ ở profile mở rộng (có thể khác `account.address`) |
| `birthDate` | `DateTime?` | Null nếu chưa nhập | Ngày sinh ở profile mở rộng (set qua `PUT /api/auth/me/profile`) |
| `timeZone` | `string?` | Null nếu chưa nhập | Timezone code (vd `"Asia/Ho_Chi_Minh"`) |

### `StaffProfileDto`

**Dùng cho:** field `staffProfile` trong `AccountDto`. Chỉ có giá trị nếu account có role Staff/Manager/Admin và đã được Admin cấu hình staff profile qua `PUT /api/admin/staff/{id}/profile`. Customer luôn có `staffProfile = null`.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `accountId` | `Guid` | Không | AccountId chủ staff profile |
| `employeeCode` | `string?` | Null nếu chưa gán | Mã nhân viên nội bộ (max 50 ký tự) |
| `department` | `string?` | Null nếu chưa gán | Phòng ban (max 100 ký tự) |
| `maxConcurrentTickets` | `int` | Không | Số ticket tối đa đồng thời (1–50, mặc định 3) |
| `isAvailable` | `bool` | Không | Staff đang sẵn sàng nhận assignment hay không (mặc định true) |
| `notes` | `string?` | Null nếu không có | Ghi chú vận hành nội bộ (max 1000 ký tự) |
| `skills` | `StaffSkillDto[]` | Không (có thể empty array) | Danh sách skill đã gán cho staff này |

> **Lưu ý:** `StaffProfileDto` trong `AccountDto` **không có** field `skillTier` (dù DB có column). Nếu cần tier, gọi `GET /api/staff/{id}/assignment-profile` (trả `StaffAssignmentProfileDto`) hoặc lookup qua admin endpoint.

### `StaffSkillDto`

**Dùng cho:** field `skills` trong `StaffProfileDto` và `StaffAssignmentProfileDto`.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `skillCode` | `string` | Không | Mã kỹ năng (vd `"LiFePO4"`, `"Inverter"`, `"BMS"`) — max 64 ký tự |
| `skillLevel` | `int` | Không | Mức độ kỹ năng (1–5, 1 = Junior, 5 = Expert) |
| `certifiedUntil` | `DateTime?` | Null nếu skill không cần chứng chỉ | Ngày hết hạn chứng chỉ UTC |

### `TrustedDeviceDto` — **(#AUTH-48)**

**Dùng cho:** response của `GET /api/accounts/me/trusted-devices`.

Mô tả thiết bị đã được user đánh dấu "trust" — login từ device này skip 2FA challenge trong TTL 30 ngày kể từ `trustedAt`.

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | Id row `TrustedDevice` — dùng để revoke qua `DELETE /me/trusted-devices/{id}` |
| `label` | `string` | Không | User-friendly label (vd `"Chrome on macOS"`, `"MacBook nhà"`). Auto-generate từ User-Agent nếu user không truyền `trustDeviceLabel` lúc verify 2FA; ngược lại lấy giá trị user nhập (trim, max 120 ký tự) |
| `ipPrefix` | `string` | Không | Subnet prefix khi user trust device (`/24` cho IPv4, `/64` cho IPv6). Vd `"203.0.113.0/24"` hoặc `"2001:db8::/64"`. Login chỉ skip 2FA nếu IP request match prefix này → tolerate DHCP cùng subnet, reject từ mạng lạ |
| `userAgentSnapshot` | `string?` | Null nếu UA rỗng lúc trust | UserAgent đầy đủ lúc user trust device. Display-only — KHÔNG dùng để match (đã include trong fingerprint hash) |
| `trustedAt` | `DateTime` | Không | Thời điểm UTC user trust device (hoặc refresh trust lần cuối nếu re-trust cùng fingerprint) |
| `expiresAt` | `DateTime` | Không | Thời điểm UTC trust hết hạn = `trustedAt + 30 ngày`. Sau hạn → device cần re-trust |
| `lastUsedAt` | `DateTime?` | Null nếu chưa từng login skip 2FA qua device này | Thời điểm UTC lần cuối device match active → skip 2FA challenge |
| `usageCount` | `int` | Không (mặc định 0) | Số lần device skip 2FA challenge thành công (audit metric) |
| `isCurrentDevice` | `bool` | Không | `true` nếu device đang gọi `GET /me/trusted-devices` match fingerprint của row này (qua header `X-Device-Id` + `User-Agent`). FE hiển thị highlight "thiết bị này" |

**Security note:** Hash `device_fingerprint_hash` (SHA-256 của `deviceId + userAgent`) **KHÔNG** trả về API — chỉ dùng nội bộ để match. Field `id` là chỉ định row riêng biệt cho revoke.

---

## JWT Access Token — claim structure

`accessToken` trả về từ `POST /api/auth/login` (Case A) / `/login/verify-2fa` / `/refresh-token` / `/accept-invite` / `/google/callback` là **JWS HS256** (HMAC-SHA256). FE có thể decode 1 chiều bằng `jwt-decode` để đọc claim nhưng KHÔNG verify được signature.

**Header:**
```json
{ "alg": "HS256", "typ": "JWT", "kid": "v1" }
```

| Field | Mô tả |
|---|---|
| `alg` | `"HS256"` (cố định) — handler reject token alg khác để chống alg-confusion attack |
| `kid` | (#AUTH-59) Key ID — match với `JwtSettings:SigningKeyId` (default `"v1"`). Hỗ trợ key rotation: server có thể chấp nhận cả `kid = current` và `kid = previous` cho đến khi token cũ tự expire. |

**Payload (claims) — tạo bởi `JwtHelper.GenerateAccessToken`:**

| Claim | JSON name | Type | Mô tả |
|---|---|---|---|
| JTI | `jti` | string (32 hex, no dash) | Random Guid `N` format — dùng cho `/revoke` blacklist và introspection. **Unique per token**, đổi mỗi lần issue. |
| Subject | `nameid` | string (Guid) | Maps `ClaimTypes.NameIdentifier` — AccountId. ASP.NET Core dùng claim này cho `User.FindFirst(ClaimTypes.NameIdentifier)`. |
| AccountId | `AccountId` | string (Guid) | Custom claim, **giá trị giống `nameid`**. FE đọc qua `AccountId` để rõ ràng hơn `nameid`. Cả 2 đều tồn tại do legacy compat. |
| Email | `email` | string | Maps `JwtRegisteredClaimNames.Email`. Có thể empty string nếu account thiếu email (rất hiếm). |
| FullName | `FullName` | string | Custom claim. Có thể empty string. |
| Role | `role` | string | Maps `ClaimTypes.Role`. Tên role (`"Admin"`, `"Manager"`, `"Staff"`, `"Customer"`, hoặc custom role). **KHÔNG có claim nào nếu account chưa gán role** (`Account.RoleId = null` — vd Google OAuth user chưa onboard). |
| Permissions | `perm` | string[] (multi-value claim) | Mỗi permission code 1 claim entry. JWT có thể có nhiều claim `perm` cùng tên. Resolved qua `PermissionResolver` (xem [GET /api/admin/permissions](#get-apiadminpermissions) catalog). Empty nếu role không Active hoặc account không có role. |
| Issued At | `iat` | number (Unix seconds) | Lúc issue |
| Expires | `exp` | number (Unix seconds) | `iat + JwtSettings:AccessTokenExpirationMinutes × 60`. Default 60 phút. |
| NotBefore | `nbf` | number (Unix seconds) | = `iat` |
| Issuer | `iss` | string | `JwtSettings:Issuer` config |
| Audience | `aud` | string | `JwtSettings:Audience` config |

> **ClockSkew = 0**: server validate `exp` strict, không tolerance — nếu client clock đi trước server vài giây có thể nhận 401 "Token expired." Đề xuất FE refresh token khi `exp - now < 30s` thay vì đợi 401.

**Refresh token shape:** **KHÔNG phải JWT** — là Guid `N` format (32 ký tự hex, ví dụ `8f3a5b9d2c1e4d6a9c0b7e8f1a2b3c4d`). DB chỉ lưu hash (`RefreshTokenHasher.Hash`), không lưu plaintext. FE chỉ cần lưu giá trị raw từ response và gửi lại nguyên giá trị đó ở `/refresh-token` body.

**Reset token** (trả từ `/verify-reset-otp`) — cũng là JWS HS256 nhưng claim khác:

| Claim | Mô tả |
|---|---|
| `jti` | Random Guid |
| `AccountId` | AccountId của user reset password |
| `email` | Email |
| `purpose` | Cố định `"password-reset"` — handler `/reset-password` reject nếu claim này không khớp (chống reuse access token làm reset token) |
| `iss`, `aud` | **KHÔNG validate** trong reset token flow (validation params đặt `ValidateIssuer = false`, `ValidateAudience = false`) — vì reset token chỉ dùng nội bộ, không cross-service |
| `exp` | `iat + 900s` (15 phút) — value `expiresInSeconds` trả về từ `/verify-reset-otp` |

---

## Nhóm 1 — Xác thực (Public, không cần token)

Base route: `/api/auth`

---

### `POST /api/auth/login`

**Mục đích:** Đăng nhập bằng email + mật khẩu. **Response shape là discriminated union** — tuỳ theo `Account.TwoFactorEnabled`:
- 2FA **OFF** → trả tokens ngay (`data.tokens` set, `data.challenge` null)
- 2FA **ON** → trả challenge token để bước 2 verify TOTP/backup code (`data.tokens` null, `data.challenge` set)

**Auth:** Không yêu cầu

**Rate limit:** `Login` (#AUTH-04) — **10 requests / phút / IP**. Cao hơn `AnonOtp` (5/phút) vì user typo password là common → tránh false-block; vẫn đủ chặn credential stuffing/brute force trước cả khi DB-level lockout 5 lần fail kích hoạt. Vượt limit → `429 Too Many Requests` + header `Retry-After: <seconds>`.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Max 256 ký tự, đúng định dạng email | Email đăng ký tài khoản |
| `password` | `string` | Bắt buộc | Không rỗng | Mật khẩu |

**Lưu ý:** Login chỉ validate password ở mức sanity check để tránh gửi field rỗng. Đây không phải security gate; server vẫn verify password bằng hash hiện có và không áp dụng regex strong-password tại endpoint login.

**Bảo vệ user enumeration (#AUTH-17):** Khi email **không tồn tại**, handler vẫn áp dụng delay ngẫu nhiên **100–200ms** (`RandomNumberGenerator.GetInt32(100, 201)`) trước khi trả response để response time match BCrypt verify time (~100–200ms) của email tồn tại. Attacker không thể phân biệt email tồn tại/không qua side-channel timing. Email không tồn tại còn audit với `LoginFailedWrongPassword` (cùng action với sai password) thay vì action riêng để log không leak thông tin.

**Side effect khi success — qua `IAuthTokenIssuer.IssueAsync` (#AUTH-68):**

`POST /api/auth/login` (2FA off) và `POST /api/auth/login/verify-2fa` **dùng chung `IssueAsync`**. `POST /api/auth/accept-invite` và `GET /api/auth/google/callback` **làm inline tương đương** (issue token + refresh row + reset counter + update LastLoginAt) nhưng **không** gọi `IssueAsync` → **không** trigger `DetectAndPublishSuspiciousLoginAsync`. Xem note "Side effect khác cho `/accept-invite` và `/google/callback`" ở các section tương ứng.

Mỗi lần `IssueAsync` được gọi:

- **Reset auth counters** trên `Account`: `FailedLoginAttempts = 0`, `LockoutEndAt = null` (idempotent — không phải auto-unlock từ Locked status; chỉ reset counter).
- **Update last-login fields**: `LastLoginAt = UtcNow`, `LastLoginIp = ipAddress`.
- **Insert `RefreshToken`** với:
  - `Token = RefreshTokenHasher.Hash(plaintext)` (#AUTH-01)
  - `IssuedAt = now`, `OriginalIssuedAt = now` (first issue), `ExpiredAt = now + 7d` (default config)
  - `Status = Active`, `IpAddress`, `UserAgent`, `DeviceId` từ HTTP context
- **Publish `SessionCreatedNotification`** → handler enforce session limit: nếu account đã có ≥ `Session:MaxConcurrentSessions` (default 5) session `Active`, revoke session **cũ nhất** với reason `"Session limit exceeded"` + audit `SessionLimitExceededOldestRevoked`.
- **Suspicious login detection (#AUTH-52)**: query 50 session gần nhất của account, so sánh `IpAddress` + `UserAgent` của request hiện tại. Nếu IP HOẶC UA chưa từng thấy → publish `SuspiciousLoginDetectedEvent` với `reason ∈ {"new_ip", "new_user_agent", "new_ip_and_user_agent"}` → NotificationService email user cảnh báo. Không block login, chỉ cảnh báo. First login (historical empty) → KHÔNG treat là suspicious.

**Side effect riêng cho `POST /api/auth/login` (chỉ khi 2FA OFF):**
- Audit log `LoginSuccess` với metadata `role` + `sessionId`.
- `LoginAttempt` row với `Method = "Password"`, `Result = Success`.
- Metric `AuthLoginTotal{result="success"}` increment.

**Edge case — auto-recover from Locked state:**
- Nếu account đang `Status = Locked` nhưng `LockoutEndAt <= UtcNow` → handler tự reset `Status = Active`, `FailedLoginAttempts = 0`, `LockoutEndAt = null` **TRƯỚC khi verify password** (không cần admin can thiệp).

**Nếu account bật 2FA — 2 sub-case:**

**Sub-case B1 — Trusted Device match (#AUTH-48):** TRƯỚC khi trả challenge, server check active `TrustedDevice` row:
- Compute fingerprint `SHA-256(deviceId + userAgent)` + IP prefix `/24`/`/64` từ HTTP context.
- Query: `WHERE AccountId = ? AND DeviceFingerprintHash = ? AND IpPrefix = ? AND RevokedAt IS NULL AND ExpiresAt > UtcNow AND IsDeleted = false`.
- **Nếu match ⇒ skip challenge, issue token trực tiếp như Case A:**
  - Update `TrustedDevice`: `LastUsedAt = UtcNow`, `UsageCount += 1`.
  - Gọi `IssueAsync` (như flow 2FA OFF): reset counter + update LastLoginAt + insert RefreshToken + SessionCreatedNotification + suspicious login detection.
  - Audit log `LoginWithTrustedDevice=113` với metadata `trustedDeviceId`, `label`, `sessionId`.
  - Metric: `AuthLoginTotal{result="success_trusted_device"}` + `Auth2FAChallengeTotal{result="skipped_trusted_device"}`.
  - `LoginAttempt` row Success với `Note = "Trusted device — 2FA skipped"`.
  - Response shape giống Case A (tokens + `requiresTwoFactor=false`, KHÔNG có `challenge`).

**Sub-case B2 — Không match (default 2FA flow):**
- KHÔNG gọi `IssueAsync` → KHÔNG reset `FailedLoginAttempts`, KHÔNG update `LastLoginAt`, KHÔNG insert refresh token.
- Trả challenge → audit `LoginPending2FA` (không phải `LoginAttempt` row).
- Brute force TOTP attempts vẫn tốn quota password counter của step 1 vì counter chỉ reset khi `IssueAsync` được gọi (tức `/login/verify-2fa` thành công).

**Sub-error messages 400 (FE có thể parse `message` để hiển thị counter):**
- `"Email hoặc mật khẩu không chính xác."` — email không tồn tại (không leak counter, không tạo failed-attempt row)
- `"Email hoặc mật khẩu không chính xác. Còn N lần thử."` — email đúng, password sai. `N = 5 - failedLoginAttempts`. Khi N = 0 thay vào đó → 423 *"Sai mật khẩu quá 5 lần. Tài khoản bị khóa 15 phút."*
- `"Tài khoản đang bị khóa. Vui lòng thử lại sau N phút."` (423) — `LockoutEndAt` còn future
- `"Tài khoản chưa được xác thực. Vui lòng kiểm tra email."` (403) — `PendingVerification`
- `"Tài khoản đã bị vô hiệu hóa."` (403) — `Inactive`
- `"Tài khoản đang bị đình chỉ."` (403) — `Suspended`
- `"Tài khoản đã bị cấm."` (403) — `Banned`

**Response `200` — Case A: 2FA OFF (login hoàn tất):**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đăng nhập thành công.",
  "data": {
    "tokens": {
      "accessToken": "eyJ...",
      "refreshToken": "abc123..."
    },
    "challenge": null,
    "requiresTwoFactor": false
  },
  "listErrors": null
}
```

**Response `200` — Case B: 2FA ON (cần verify bước 2):**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Yêu cầu xác thực 2FA. Gửi mã TOTP hoặc backup code qua /api/auth/login/verify-2fa.",
  "data": {
    "tokens": null,
    "challenge": {
      "challengeToken": "e7b9c1a2f0d44e0d9c5b3a1e8f2c0d3b",
      "expiresInSeconds": 300,
      "methods": ["totp", "backupCode"]
    },
    "requiresTwoFactor": true
  },
  "listErrors": null
}
```

**Field reference (LoginResultDto):**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.tokens` | `TokenDTO` | **Có** | Set khi login complete (Case A). Null trong Case B. |
| `data.tokens.accessToken` | `string` | Có thể null khi lỗi | JWT access token, thời hạn 1 giờ |
| `data.tokens.refreshToken` | `string` | Có thể null khi lỗi | Refresh token, thời hạn 7 ngày, lưu trong Redis |
| `data.challenge` | `TwoFactorChallengeDto` | **Có** | Set khi 2FA on (Case B). Null trong Case A. |
| `data.challenge.challengeToken` | `string` | Không (nếu challenge set) | Token (32 ký tự hex) để gửi kèm `/login/verify-2fa`. Lưu Redis TTL 5 phút. |
| `data.challenge.expiresInSeconds` | `int` | Không | Luôn `300` (TTL của challenge token) |
| `data.challenge.methods` | `string[]` | Không | Phương thức cho phép — luôn `["totp", "backupCode"]` |
| `data.requiresTwoFactor` | `bool` | Không | Computed: `challenge != null`. Tiện cho FE detect flow. |

**FE flow:**
```
POST /api/auth/login
  ├─ data.requiresTwoFactor == false → save tokens.accessToken + tokens.refreshToken, redirect home
  └─ data.requiresTwoFactor == true  → giữ data.challenge.challengeToken trong memory,
                                        hiển thị màn hình nhập TOTP / backup code,
                                        gọi POST /api/auth/login/verify-2fa
```

**Lỗi thường gặp:**
- `400` — Dữ liệu không hợp lệ (email sai định dạng, password rỗng) — field validation, có `listErrors`
- `400 isSuccess=false` — Email hoặc mật khẩu không chính xác (counter tăng, gần khóa)
- `403` — Tài khoản chưa verify, inactive, suspended hoặc banned
- `423` — Tài khoản bị khóa tạm thời do sai mật khẩu quá số lần cho phép

> **Breaking change (GH-295):** Trước GH-295, response shape là `data.accessToken` / `data.refreshToken` trực tiếp. Sau GH-295, được wrap trong `data.tokens.*`. Client cũ cần migrate đường truy cập. `AcceptInvite`, `GoogleAuth`, `RefreshToken` cũng dùng shape mới.

---

### `POST /api/auth/login/verify-2fa`

**Mục đích:** Bước 2 của 2FA login flow — verify TOTP code (hoặc backup code) bằng `challengeToken` từ bước 1 → cấp JWT + refresh token. **GH-295.**

**Auth:** Không yêu cầu (nhưng cần `challengeToken` hợp lệ — tương đương "session đã verify password")

**Rate limit:** `TwoFactorVerify` — **5 attempts / 5 phút / `challengeToken`** (vượt → 429 + challenge bị invalidate, user phải login lại).

> **Quan trọng — partition theo HEADER, không phải body field:** Rate limiter đọc challenge token từ HTTP header **`X-Challenge-Token`**, fallback IP nếu thiếu. **FE PHẢI gửi header `X-Challenge-Token: <challengeToken>`** kèm request — nếu thiếu, mọi user trong cùng 1 NAT/IP bị share counter, dễ false-block khi 1 attacker spam. Body field `challengeToken` (vẫn bắt buộc) là cái server dùng để lookup pending session trong Redis; header là cái rate limiter partition.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `challengeToken` | `string` | Bắt buộc | Không rỗng | Lấy từ `data.challenge.challengeToken` của `/api/auth/login` Case B |
| `code` | `string` | Bắt buộc | TOTP / SMS code: đúng 6 chữ số. Backup code: không validate format (server tự normalize). | Mã TOTP 6 số từ Authenticator, OTP 6 số nhận qua SMS, hoặc backup code (`xxxx-xxxx`, không phân biệt hoa thường, dash optional) |
| `isBackupCode` | `bool` | Mặc định `false` | — | `true` khi user dùng backup code thay vì TOTP. **Mutex với `isSmsCode`** — nếu cả 2 cùng `true`, server trả `400` "Chỉ chọn 1 loại code (TOTP/Backup/SMS)." |
| `isSmsCode` | `bool` | Mặc định `false` | — | (#AUTH-58) `true` khi `code` là OTP nhận qua SMS từ luồng `POST /api/auth/login/2fa/sms`. **Mutex với `isBackupCode`.** Nếu cả `isBackupCode` và `isSmsCode` đều `false` ⇒ server treat `code` là TOTP từ Authenticator app. |
| `trustDevice` | `bool` | Mặc định `false` | — | **(#AUTH-48)** `true` để đánh dấu thiết bị này là "trusted" — login lần sau từ device này skip 2FA challenge trong **30 ngày**. **CHỈ có hiệu lực khi `isBackupCode=false`** (TOTP/SMS path). Nếu `isBackupCode=true` ⇒ server bỏ qua `trustDevice` (emergency backup code KHÔNG được trust device). Yêu cầu request có header `X-Device-Id` + `User-Agent` ổn định — nếu thiếu fingerprint, server skip silently (không lỗi). |
| `trustDeviceLabel` | `string?` | Không bắt buộc | Max 120 ký tự (trim) | **(#AUTH-48)** Friendly label cho thiết bị (vd `"MacBook nhà"`, `"Phone công ty"`). Hiển thị trong `GET /me/trusted-devices`. Nếu null/rỗng ⇒ server auto-generate từ UA (`"Chrome on macOS"`, `"Safari on iPhone"`). Chỉ dùng khi `trustDevice=true`. |

**Response thành công `200`:** Giống `/login` Case A — `data.tokens.accessToken` + `data.tokens.refreshToken`.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đăng nhập thành công.",
  "data": {
    "tokens": { "accessToken": "eyJ...", "refreshToken": "abc..." },
    "challenge": null,
    "requiresTwoFactor": false
  },
  "listErrors": null
}
```

**Side effect khi success:**
- Account `FailedLoginAttempts` reset về 0
- Account `LastLoginAt`, `LastLoginIp` cập nhật
- `RefreshToken` row mới insert (session limit enforcement có thể revoke session cũ nhất)
- Nếu `isBackupCode=true`: row `BackupCode` đó set `RedeemedAt = UtcNow` (single-use); audit `BackupCodeRedeemed` với metadata.backupCodeId
- Nếu `isSmsCode=true`: SMS OTP store invalidated (single-use)
- Nếu `TwoFactorSecret` còn dạng plaintext legacy (pre-GH-295) HOẶC `TwoFactorSecretEncryptedAt = null`: tự động lazy re-encrypt sau khi verify thành công (#AUTH-22)
- **(#AUTH-48)** Nếu `trustDevice=true` và `isBackupCode=false`:
  - Server compute fingerprint `SHA-256(deviceId + userAgent)` + IP prefix `/24` (IPv4) hoặc `/64` (IPv6)
  - Nếu fingerprint hoặc ipPrefix null (thiếu header) ⇒ skip silently, KHÔNG fail request
  - Nếu đã có row `TrustedDevice` cùng fingerprint cho account ⇒ refresh: update `IpPrefix`/`Label`/`UserAgentSnapshot`/`TrustedAt`/`ExpiresAt = now+30d`, clear `RevokedAt`
  - Ngược lại ⇒ insert row mới với TTL 30 ngày
  - Audit log `TrustedDeviceAdded=110` với metadata: `label`, `ipPrefix`, `ttlDays=30`
- Audit log: `LoginWith2FA` (metadata: `role`, `sessionId`, `method` = `"totp"` hoặc `"backupCode"` — **lưu ý SMS path hiện cũng ghi `method = "totp"` do code branch chung**, không phải `"sms"`), `BackupCodeRedeemed` (nếu backup code), `LoginAttempt` row Success với `method = "TOTP"` hoặc `"BackupCode"`
- Challenge token bị invalidate (`InvalidateAsync`) — không thể replay

**Per-account backup code rate limit (#AUTH-45) — bảo vệ thêm ngoài 5 attempts/challenge:**

Ngoài counter "5 attempts/challenge" ở trên, riêng **backup code path** có thêm rate limit **5 attempts / 15 phút / account** (lưu Redis key `backup_code_attempts:{accountIdNoHyphen}`). Lý do: với 8 backup codes × 5 attempts/challenge = 40 attempts có thể tích lũy nếu attacker spam tạo nhiều challenge mới. Per-account counter ngắt brute-force ở tầng cao hơn challenge-level.

Vượt 5 attempts/15min → 429 *"Vượt quá số lần thử backup code. Vui lòng thử lại sau 15 phút hoặc dùng TOTP."* + challenge bị invalidate + audit `OtpVerifyFailed` với reason `"Backup code rate limit exceeded"`. TOTP và SMS path **KHÔNG** bị giới hạn này.

**Sub-error messages (FE có thể parse `message`):**

| Status | Message | Trigger |
|---|---|---|
| `422` | "Phiên xác thực đã hết hạn hoặc không hợp lệ. Hãy login lại." | Challenge token không có trong Redis hoặc đã expire |
| `404` | "Tài khoản không tồn tại hoặc đã bị xóa." | Account bị xóa giữa lúc challenge sống |
| `403` | "Tài khoản không khả dụng cho đăng nhập." | Status mid-challenge changed (lock/suspend/ban) |
| `409` | "2FA không còn được bật. Hãy login lại." | Account disable 2FA giữa lúc challenge sống |
| `422` | "Mã xác thực không đúng. Còn N lần thử." | TOTP/backup/SMS code sai. `N = 5 - attempts`. |
| `429` | "Vượt quá số lần thử cho phiên này. Hãy login lại." | Vượt 5 attempts/challenge → challenge bị xóa |
| `429` | "Vượt quá số lần thử backup code. Vui lòng thử lại sau 15 phút hoặc dùng TOTP." | Vượt 5 backup code attempts/15min/account (#AUTH-45) |

**Lỗi thường gặp (status code summary):**
- `400` — Field validation (challengeToken/code rỗng, TOTP code không phải 6 chữ số, `isBackupCode` và `isSmsCode` cùng `true`) — có `listErrors`
- `403` — Account suspended/banned/inactive giữa lúc challenge còn sống → challenge bị invalidate
- `404` — Account bị xóa giữa lúc challenge còn sống → challenge bị invalidate
- `409` — Account đã disable 2FA giữa lúc challenge còn sống → challenge bị invalidate
- `422` — Challenge token expired/invalid, hoặc mã TOTP/backup code/SMS sai
- `429` — Vượt 5 attempts/challenge, HOẶC vượt 5 backup attempts/15min/account

---

### `POST /api/auth/register`

**Mục đích:** Đăng ký tài khoản mới. Hệ thống gửi OTP 6 số về email để xác thực.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Max 256 ký tự, đúng định dạng email | Email đăng nhập |
| `password` | `string` | Bắt buộc | 8–100 ký tự, có chữ hoa, chữ thường, số, ký tự đặc biệt | Mật khẩu mạnh |
| `fullName` | `string` | Bắt buộc | Max 150 ký tự | Họ và tên đầy đủ |
| `phoneNumber` | `string?` | Tùy chọn | Max 20 ký tự | Số điện thoại |
| `dateOfBirth` | `DateTime?` | Tùy chọn | Không ở tương lai, năm >= 1900 | Ngày sinh (ISO 8601) |
| `address` | `string?` | Tùy chọn | Max 500 ký tự | Địa chỉ |

**Response thành công `201`:**
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": {
    "email": "user@example.com",
    "otpExpiresInSeconds": 300
  }
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.email` | `string` | Không | Email vừa đăng ký |
| `data.otpExpiresInSeconds` | `int` | Không | Thời gian hết hạn OTP tính bằng giây (thường 300 = 5 phút) |

**Lưu ý:** Register trả `201 Created` (không phải 200). Sau khi đăng ký, account ở trạng thái `PendingVerification`. Cần gọi `POST /api/auth/verify-otp` để kích hoạt.

**Side effect khi success:**
- Account mới: `Status = PendingVerification`, `RoleId = CustomerRoleId` (hardcoded `44444444-4444-4444-4444-444444444444`), `RoleAssignedAt = UtcNow`, `EmailConfirmed = false`, `PhoneConfirmed = false`, `OtpCode = <6 số>`, `OtpExpiredAt = now + 5min`, `OtpPurpose = Register`.
- Email gửi qua outbox `SendOtpRegisterEvent` (publish TRƯỚC `SaveChanges` để event đi cùng transaction). User nhận email với độ trễ tối đa ~2s (theo `OutboxRelay` poll interval).

**Idempotent re-register (PendingVerification):**

Nếu email tồn tại VÀ `Status = PendingVerification` → handler **không reject 409**, thay vào đó **overwrite**: cập nhật `PhoneNumber`, `PasswordHash` (re-hash), `FullName`, `DateOfBirth`, `Address`, OTP mới (`OtpCode`, `OtpExpiredAt`, `OtpPurpose = Register`), reset `FailedLoginAttempts = 0`, `LockoutEndAt = null`. Nếu `RoleId` null hoặc `Guid.Empty` (legacy) → set lại Customer. Trả `201` như tạo mới.

> **Dùng case**: User register sai, không nhận email, register lại với cùng email + sửa fullName → thành công (replace record cũ, không cần admin cleanup).

**Phone uniqueness check:**

Nếu `phoneNumber` truyền lên (sau normalize bằng `PhoneNormalizer.Normalize`) **đã thuộc account khác** (không phải account đang re-register) → 409 *"Số điện thoại đã được sử dụng."*

**#AUTH-25 — Race condition handling (PostgreSQL unique constraint):**

Sau khi pre-check duplicate pass, 2 request đồng thời cùng email/phone vẫn có thể cùng INSERT row → PostgreSQL raise `23505` (unique violation). Handler catch `DbUpdateException`, đọc `ConstraintName` qua reflection (`Npgsql.PostgresException`):
- ConstraintName chứa `"email"` → 409 *"Email đã được sử dụng."*
- ConstraintName chứa `"phone"` → 409 *"Số điện thoại đã được sử dụng."*
- Khác → 409 *"Email hoặc số điện thoại đã được sử dụng."*
- DB error khác (không phải 23505) → 500 *"Đăng ký thất bại do lỗi hệ thống. Vui lòng thử lại."* + log warning.

**Lỗi thường gặp:**
- `400` — Field validation (`listErrors` chi tiết)
- `409` — Email đã active / Phone đã được dùng / Email đã ở status khác `PendingVerification` (vd `Locked`, `Active`, `Suspended`, `Banned`, `Inactive`)
- `429` — Vượt rate limit `AnonOtp` (5/min/IP)
- `500` — DB error ngoài unique violation

---

### `POST /api/auth/verify-otp`

**Mục đích:** Xác thực OTP 6 số để kích hoạt tài khoản sau đăng ký. Chuyển account sang `Active`.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Đúng định dạng email | Email đã đăng ký |
| `otp` | `string` | Bắt buộc | Đúng 6 chữ số | Mã OTP nhận qua email |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Xác thực OTP thành công. Tài khoản đã kích hoạt. Vui lòng đăng nhập.",
  "data": null
}
```

> **Sửa từ docs cũ**: message thực tế dài hơn — docs cũ ghi *"Xác thực thành công."* nhưng code trả full string trên. FE parse `message` để hiển thị toast nên cần dùng đúng.

**Side effect khi success (verified với handler):**

| Bước | Action |
|---|---|
| 1 | `Account.EmailConfirmed = true`, `Status = PendingVerification → Active` |
| 2 | Clear OTP state: `OtpCode = null`, `OtpExpiredAt = null`, `OtpPurpose = null` |
| 3 | Reset shared lockout counter: `FailedLoginAttempts = 0`, `LockoutEndAt = null` |
| 4 | (#AUTH-69 defensive) Nếu `RoleId` null/empty → set Customer role + `RoleAssignedAt = UtcNow` |
| 5 | **Publish outbox `AccountActivatedEvent`** (TRƯỚC `SaveChanges` để đi cùng transaction): `{accountId, email, fullName, phoneNumber, role, CreationSource: "SelfRegister"}` → NotificationService consume để gửi welcome email + UserDirectory sync |
| 6 | Metric `AuthOtpUsageTotal{purpose="register", result="verified"}` increment |

> **KHÔNG có** audit log `AccountRegistered` được publish ở step này — event được gắn vào `AccountActivatedEvent` outbox và downstream service tự ghi audit nếu cần. `AccountActivatedEvent` cũng được publish từ `/accept-invite` (CreationSource = "AdminInvite") và `/google/callback` (CreationSource = "GoogleOAuth") — total 3 nguồn.

**Sub-error messages (verified):**

| Status | Message | Trigger |
|---|---|---|
| 401 | `"OTP đã hết hạn. Vui lòng yêu cầu gửi lại."` | OTP expired (`OtpExpiredAt <= now`, #AUTH-27 exact-expiry edge case) |
| 401 | `"OTP không chính xác. Còn N lần thử."` | OTP sai value, chưa lock. N = 5 - failedAttempts |
| 404 | `"Tài khoản không tồn tại."` | Email không có account match |
| 409 | `"Tài khoản đã được xác thực hoặc không ở trạng thái chờ verify."` | Status ≠ PendingVerification |
| 422 | `"OTP không phải dành cho đăng ký."` | `OtpPurpose ≠ Register` (vd OTP đang là PasswordReset/PhoneVerify/EmailChange) |
| 423 | `"Tài khoản đang bị khóa. Vui lòng thử lại sau N phút."` | LockoutEndAt > now (pre-check) |
| 423 | `"Sai OTP quá 5 lần. Tài khoản bị khóa 15 phút."` | Vừa hit threshold sau khi increment counter |

**Rate limit / retry / lockout:** Endpoint **KHÔNG có** rate limit policy ở tầng middleware (verify trực tiếp DB OTP) — chỉ bảo vệ bằng cơ chế lockout tại handler.

> **⚠️ Shared lockout counter (#AUTH-19):** `Account.FailedLoginAttempts` là counter **CHUNG** cho tất cả luồng auth fail: wrong password ở `/login`, wrong OTP ở `/verify-otp` / `/verify-reset-otp` / `/verify-phone-otp` / `/confirm-email-change`. Tăng +1 khi fail bất kỳ luồng nào, threshold **5 lần tổng** → set `LockoutEndAt = now + 15 phút` → mọi entrypoint xác thực sau đó nhận `423 Locked` trong cùng cửa sổ. Counter **reset = 0** khi: (a) verify thành công ở bất kỳ luồng nào (qua `IssueAsync` hoặc OTP correct), HOẶC (b) `/forgot-password` được trigger thành công (chấp nhận tradeoff: attacker có thể spam forgot-password để xóa counter, nhưng vẫn tốt hơn để victim bị lock vĩnh viễn).

Sau 15 phút lockout tự hết (không cần admin can thiệp — `LockoutReconcileBackgroundService` poll mỗi 5 phút để reconcile state, nhưng login handler cũng tự auto-recover khi user thử lại). Nếu verify thành công, account chuyển sang `Active` nhưng không trả token; FE cần gọi `POST /api/auth/login`.

**Phân biệt với password lockout:** Khi sai mật khẩu login 5 lần, `account.Status` bị set `Locked` — trường hợp đó Admin mới cần dùng `POST /api/admin/accounts/{id}/unlock`. OTP lockout ở endpoint này chỉ dùng `LockoutEndAt`, không set `Status = Locked`.

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | Validation: email sai định dạng hoặc OTP không đúng 6 chữ số |
| `401` | OTP đã hết hạn HOẶC OTP sai giá trị (vẫn coi là credential invalid) |
| `404` | Account không tồn tại |
| `409` | Account đã verified hoặc không ở trạng thái `PendingVerification` |
| `422` | OTP không phải dành cho mục đích đăng ký (purpose mismatch — business rule). Ví dụ: user gửi OTP reset password đến endpoint verify-otp này |
| `423` | Lockout 15 phút do sai OTP ≥ 5 lần (dựa trên `LockoutEndAt`, không set `Status = Locked`) |

**Phân biệt `401` vs `422`:**
- `401` — OTP **sai giá trị** hoặc **hết hạn**: vẫn cùng mục đích Register nhưng credential không hợp lệ.
- `422` — OTP **đúng giá trị** nhưng `OtpPurpose` không phải `Register` (ví dụ OTP được tạo cho luồng reset password, change-email, hoặc verify-phone). Đây là vi phạm business rule về purpose, không phải lỗi credential.

---

### `POST /api/auth/resend-otp`

**Mục đích:** Gửi lại OTP đăng ký khi OTP cũ hết hạn. Chỉ dùng cho luồng `Register`.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email đã đăng ký nhưng chưa verify |

**Response thành công `200`:** `isSuccess = true`, message xác nhận.

**Rate limit / cooldown:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Ngoài ra resend OTP đăng ký có cooldown 60 giây dựa trên lần gửi gần nhất; gọi quá sớm trả `429`.

---

### `POST /api/auth/forgot-password`

**Mục đích:** Gửi OTP 6 số về email để bắt đầu luồng đặt lại mật khẩu.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email tài khoản cần reset mật khẩu |

**Response thành công `200`:** `isSuccess = true`, hệ thống gửi OTP về email.

**Lưu ý bảo mật:** Response trả cùng message dù email tồn tại hay không (tránh user enumeration).

**OTP TTL: 5 phút (#AUTH-14, 2026-06-19, giảm từ 10p)** — `Account.OtpExpiredAt = UtcNow + 5 phút`, `Account.OtpPurpose = PasswordReset`. Brute-force window co lại 50% so với baseline.

**Side effect quan trọng — RESET shared lockout counter (#AUTH-19):** Khi tìm thấy account hợp lệ và gửi OTP thành công, handler **reset `Account.FailedLoginAttempts = 0`** và **clear `LockoutEndAt = null`**. Đây là chủ ý design (xem #AUTH-19): cho phép user bị lock vì sai mật khẩu 5 lần có thể tự unlock bằng cách reset password. Tradeoff: attacker có thể spam endpoint này để xóa counter của victim, nhưng vẫn tốt hơn để user bị lock vĩnh viễn. Vì endpoint có rate limit `AnonOtp` (5/min/IP) → spam khả thi chỉ ở mức 5 req/phút.

---

### `POST /api/auth/verify-reset-otp`

**Mục đích:** Xác thực OTP reset mật khẩu. Trả về `resetToken` ngắn hạn để dùng ở bước tiếp theo.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email đã request forgot-password |
| `otp` | `string` | Bắt buộc | OTP 6 chữ số nhận qua email |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "resetToken": "a1b2c3...",
    "expiresInSeconds": 900
  }
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.resetToken` | `string` | Không | Token ngắn hạn dùng để đặt lại mật khẩu (bước sau) |
| `data.expiresInSeconds` | `int` | Không | Thời gian hết hạn của resetToken (900 giây = 15 phút) |

**Rate limit / retry / lockout:** Endpoint **KHÔNG có** rate limit policy ở tầng middleware — chỉ bảo vệ bằng cơ chế lockout tại handler. **Sai OTP tính vào shared counter `Account.FailedLoginAttempts` (#AUTH-19)** — xem chi tiết ở `POST /api/auth/verify-otp` section. Tổng 5 fail (gộp với wrong-password ở /login + sai OTP ở các endpoint OTP khác) → `423 Locked` trong 15 phút.

**Sub-error messages (verified với handler — 401 là catch-all cho 4 case khác nhau):**

| Status | Message | Trigger |
|---|---|---|
| 404 | `"Tài khoản không tồn tại hoặc OTP không hợp lệ."` | Email không match account nào (intentionally vague — chống user enumeration) |
| 423 | `"Tài khoản đang bị khóa. Vui lòng thử lại sau."` | `LockoutEndAt > now` |
| **401** | `"OTP không hợp lệ hoặc đã hết hạn."` | Catch-all cho 4 case: (a) `OtpPurpose ≠ PasswordReset`, (b) `OtpCode = null` (chưa từng request forgot-password), (c) `OtpExpiredAt = null`, (d) `OtpExpiredAt <= now` (expired). **FE không phân biệt được 4 case này từ message** — đây là intentional security design (không leak state info). |
| 401 | `"OTP không chính xác."` | OTP sai value, counter tăng |

> **FE UX recommendation**: với 401 catch-all "không hợp lệ hoặc đã hết hạn", FE nên hiển thị CTA "Gửi lại OTP" qua `/resend-reset-otp` (cover cả case (a), (b), (d)) thay vì block user.

**Side effect khi success:**
- Reset `FailedLoginAttempts = 0` (note: KHÔNG clear `LockoutEndAt` — nếu user vừa bị lock cuối lần fail trước, vẫn phải đợi hết lockout).
- Gen JWT reset token TTL 15 phút (`ResetTokenLifetimeMinutes = 15`) với claim `purpose = "password-reset"`, `AccountId`, `email`, `jti`.
- Metric `AuthOtpUsageTotal{purpose="password_reset", result="verified"}` increment.
- **KHÔNG** clear OTP state (OtpCode/OtpExpiredAt/OtpPurpose). OTP state chỉ clear khi `/reset-password` thành công.

---

### `POST /api/auth/resend-reset-otp`

**Mục đích:** Gửi lại OTP reset mật khẩu khi OTP cũ hết hạn.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email đang trong luồng reset password |

**Rate limit / cooldown:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Nếu account đang trong luồng reset password, resend reset OTP có cooldown 60 giây; gọi quá sớm trả `429`. OTP reset password có **TTL 5 phút (#AUTH-14, 2026-06-19, giảm từ 10p)**. Response vẫn tránh tiết lộ email tồn tại hay không.

---

### `POST /api/auth/reset-password`

**Mục đích:** Đặt lại mật khẩu mới sau khi đã xác thực OTP thành công (có `resetToken`).

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `resetToken` | `string` | Bắt buộc | Không được rỗng | Token lấy từ bước verify-reset-otp |
| `newPassword` | `string` | Bắt buộc | 8–100 ký tự, có chữ hoa/thường/số/ký tự đặc biệt | Mật khẩu mới |

**Response thành công `200`:** `isSuccess = true`, message `"Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại."`, `data = accountId` (Guid string).

**#AUTH-06 — Single-use reset token (Redis SET NX):**

Reset token chỉ dùng được **1 lần**. Sau khi validate JWT pass, handler set key Redis `pwd_reset_used:{jti}` với TTL = thời gian còn lại của token (max 15 phút) bằng `SET NX` (atomic check-and-set). Nếu key đã tồn tại → token đã dùng → trả 401 *"Reset token đã được sử dụng. Vui lòng yêu cầu OTP mới."*

**Side effect khi success (#AUTH-54):**
- `Account.PasswordHash` cập nhật.
- Clear OTP state: `OtpCode = null`, `OtpExpiredAt = null`, `OtpPurpose = null`.
- Reset auth lockout: `FailedLoginAttempts = 0`, `LockoutEndAt = null` (cùng cơ chế với /forgot-password).
- Toàn bộ `RefreshToken` active → `Revoked` với `RevokedReason = "Password reset"`.
- (#AUTH-54) **Bulk revoke ALL access tokens** đã issue trước thời điểm reset: blacklist 1h trên Redis. Access token cũ còn hạn nhưng đã invalidated.
- Audit `PasswordReset` với metadata `revokedSessions = <count>`.

**Lỗi thường gặp:**
- `400` — Field validation (`resetToken` rỗng, `newPassword` không đạt độ phức tạp)
- `401` — Reset token: invalid signature / expired / wrong `purpose` claim / đã dùng (single-use) / thiếu AccountId claim
- `404` — Account không tồn tại

---

### `POST /api/auth/refresh-token`

**Mục đích:** Làm mới cặp access token / refresh token (rotation). Token cũ sẽ bị đánh dấu `Used`.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `refreshToken` | `string` | Bắt buộc | Refresh token hiện tại còn hiệu lực |

**Response thành công `200`:** Dùng cùng shape `LoginResultDto` (GH-295) — `data.tokens.accessToken` + `data.tokens.refreshToken`. `data.challenge` luôn null cho refresh-token endpoint (không cần re-verify 2FA khi đã có refresh token hợp lệ).

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Cấp lại token thành công.",
  "data": {
    "tokens": {
      "accessToken": "eyJ...",
      "refreshToken": "newtoken..."
    },
    "challenge": null,
    "requiresTwoFactor": false
  },
  "listErrors": null
}
```

**Side effect / bảo mật:**
- **Hash-only storage (#AUTH-01):** DB chỉ lưu hash của refresh token (`RefreshTokenHasher.Hash`), không lưu plaintext. Server lookup bằng `Token == Hash(plaintext_client_gửi)`. Leak DB không cho attacker dùng được token.
- **Rotation chain shared expiry (#AUTH-28):** Khi rotate, refresh token mới có `ExpiredAt = OriginalIssuedAt + 7 ngày` (KHÔNG phải `now + 7 ngày`). Tức là **7 ngày tính từ lần issue đầu tiên của chain**, không reset mỗi lần refresh. Edge case: nếu Admin giảm `RefreshTokenExpirationDays` config giữa chain → `newExpiredAt` có thể đã past → 401 *"Refresh token chain đã hết hạn theo policy mới. Vui lòng đăng nhập lại."*
- **Reuse-attack detection:** Nếu phát hiện refresh token đã được dùng lại (gửi token có `Status = Used`), toàn bộ refresh token đang `Active` của account bị set `Status = Revoked` (KHÔNG phải `Compromised` — enum `Compromised` chưa được handler này sử dụng) với `RevokedReason = "RefreshToken reuse detected"`. Đồng thời publish `RefreshTokenReuseDetectedEvent` (#AUTH-79) → NotificationService gửi email cảnh báo user + Grafana raise security alert. Response: 401 *"Phát hiện refresh token bị tái sử dụng. Toàn bộ phiên đã bị thu hồi."*
- **Device binding (#AUTH-12, optional):** Nếu config `AuthSecurity:EnforceDeviceBinding = true`, server compare IP + User-Agent của request hiện tại với giá trị lưu lúc issue token; mismatch → 401 *"Refresh token không hợp lệ cho thiết bị này."* + token bị set `Revoked` với reason `"DeviceBindingMismatch"`. Mặc định config này tắt — chỉ enable cho environment có yêu cầu compliance cao. Null/empty (request không có IP/UA info) được treat là "không enforce".
- **`LastLoginAt`/`LastLoginIp` KHÔNG update khi refresh (#AUTH-33, 2026-06-19):** Trước đây refresh-token update 2 field này mỗi lần rotate. Hiện đã **bỏ** — semantic "last login" chỉ tính lần user thực sự login (Password+2FA / Google / Invite accept / Login-with-trusted-device). Refresh token = continue session, KHÔNG phải login event. FE muốn track "Last activity" thì dùng `Session.LastActivityAt` qua `GET /api/sessions/me` (đang plan), HOẶC query `LoginAttempt` table cho lần Success gần nhất.

**Sub-error messages 401 (FE có thể parse `message` để hiển thị UX khác nhau):**
- `"Refresh token không hợp lệ."` — không tìm thấy trong DB
- `"Phát hiện refresh token bị tái sử dụng. Toàn bộ phiên đã bị thu hồi."` — reuse attack
- `"Refresh token đã bị thu hồi hoặc hết hạn."` — status ≠ Active (đã Revoked / Expired / Compromised)
- `"Refresh token đã hết hạn."` — time-expired, vừa được auto-mark Expired
- `"Tài khoản không khả dụng."` — account bị xóa hoặc status ≠ Active
- `"Refresh token chain đã hết hạn theo policy mới. Vui lòng đăng nhập lại."` — admin giảm RefreshTokenExpirationDays config
- `"Refresh token không hợp lệ cho thiết bị này."` — device binding mismatch (nếu enable)

---

### `POST /api/auth/logout`

**Mục đích:** Đăng xuất khỏi 1 phiên cụ thể — revoke refresh token + (#AUTH-54) **bulk blacklist toàn bộ access token đã issue trước thời điểm logout** + (#AUTH-08) **invalidate pending 2FA challenge** của account.

> ⚠️ **Sửa từ docs cũ**: docs sớm hơn ghi *"không dùng blacklist"* — đó là **sai**. Code thực tế (`LogoutCommandHandler.cs:69-71`) gọi `_revocationStore.RevokeAllByAccountAsync(accountId, TTL=1h)` → access token còn hạn của account bị blacklist trong tối đa 1h. Không phải chỉ 1 access token (như `/revoke`), mà **toàn bộ access token** của account đó.

**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}`

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `refreshToken` | `string` | Bắt buộc | Refresh token cần thu hồi |

**Response thành công `200`:** `isSuccess = true`, token đã bị revoke.

**Side effect chi tiết:**

| Bước | Action |
|---|---|
| 1 | DB lookup `RefreshToken` bằng `Hash(plaintextFromClient)` (#AUTH-01) |
| 2 | Nếu không tìm thấy hoặc `Status ≠ Active` → vẫn invalidate 2FA challenge của account (best-effort), trả 200 với `data = "AlreadyInactive"`, message *"Refresh token đã không còn hiệu lực."* |
| 3 | Nếu `AccountId` của token ≠ `AccountId` từ JWT → 403 *"Không có quyền đăng xuất session này."* |
| 4 | (#AUTH-08) Invalidate pending 2FA challenge của account (qua `_challengeStore.InvalidateByAccountAsync`) — nếu user đang giữa flow `/login` Case B chưa verify → challenge đó bị xóa |
| 5 | Set `Status = Revoked`, `RevokedAt = UtcNow`, `RevokedReason = "UserLogout"` cho refresh token đó |
| 6 | (#AUTH-54) **Bulk revoke ALL access tokens** của account: `_revocationStore.RevokeAllByAccountAsync(accountId, TTL=1h)`. Blacklist trong Redis tối đa 1 giờ (= max access token life), sau đó tự dọn khi token đã expire tự nhiên |
| 7 | Trả 200 với `data = "Revoked"`, message *"Đăng xuất thành công."* |

**Lưu ý bảo mật:** Backend lấy `accountId` từ access token trong header. Sau bước 6, MỌI access token còn hạn của account đều bị blacklist — bao gồm cả access token của các session khác đang hoạt động. FE phải clear cả access token và refresh token khỏi cookie/local state ngay khi logout thành công, và **các thiết bị khác cùng account sẽ nhận 401 ở request tiếp theo** (mặc dù refresh token của họ vẫn `Active` trong DB và có thể dùng `/refresh-token` để lấy access token mới — đây là design có chủ ý: logout chỉ kill session hiện tại + access token hiện hành, refresh token các thiết bị khác vẫn dùng được).

---

### `GET /api/auth/google/login`

**Mục đích:** Khởi tạo OAuth flow với Google. Backend redirect browser sang trang đăng nhập Google.

**Auth:** Không yêu cầu

**Query params:** Không có

**Response thành công `302`:** Redirect sang Google OAuth consent screen.

**Lưu ý bảo mật:** Redirect URI không nhận từ query/body của client. Whitelist hiện tại là redirect URI cố định trong cấu hình `GoogleOAuth:RedirectUri` hoặc `GOOGLE_REDIRECT_URI` đã đăng ký với Google; request không thể truyền URI khác. Backend đồng thời sinh cookie HttpOnly `g_oauth_state` để chống CSRF OAuth.

---

### `GET /api/auth/google/callback`

**Mục đích:** Server-side callback sau khi Google redirect về. Exchange authorization code lấy token.

**Auth:** Không yêu cầu

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `code` | `string` | Bắt buộc | Authorization code từ Google |
| `state` | `string` | Bắt buộc | State Google trả về, phải khớp cookie `g_oauth_state` |
| `error` | `string` | Không | Lỗi Google trả về nếu user hủy hoặc OAuth fail |

**Response thành công `200`:** Dùng cùng shape `LoginResultDto` như `POST /api/auth/login` Case A (Google login bypass 2FA — không trả challenge). `data.tokens.accessToken` + `data.tokens.refreshToken`, `data.challenge = null`.

**Lưu ý bảo mật:** Endpoint callback không accept `redirectUri` từ query param. Backend exchange code bằng redirect URI cố định trong whitelist cấu hình; request không thể override redirect URI nên không mở hướng open redirect theo input từ FE.

**`GoogleOAuth:AllowedRedirectUris` (config array):**

Backend đọc `GoogleOAuth:AllowedRedirectUris` (array) từ config. Nếu set, redirect URI dùng để exchange phải nằm trong whitelist; mismatch → 400 *"RedirectUri không hợp lệ."* Nếu **array rỗng** (default) thì handler không enforce — `GoogleOAuth:RedirectUri` đơn (1 giá trị) ở `/google/login` vẫn được dùng để gen authorization URL. Production luôn nên set whitelist non-empty để defense-in-depth.

**#AUTH-20 — Email mismatch policy (3 nhánh):**

Khi callback exchange code thành công → server có `googleUser.Email`, `googleUser.Subject` (Google ID). Server lookup account theo email:

| Trường hợp | Behavior |
|---|---|
| **Email chưa tồn tại** → auto-create account: `EmailConfirmed = true`, `Status = Active`, `Provider = "Google"`, `GoogleId = subject`, role = Customer, password hash random. Profile mới với `AvatarSource = Google` nếu có Google picture. Publish outbox `AccountActivatedEvent` với `CreationSource = "GoogleOAuth"`. | 200 + tokens |
| **Email tồn tại, chưa link Google** (`GoogleId = null`) — `EmailConfirmed = true` → auto-link Google: set `GoogleId = subject`, `Provider = "Google"`. Login bình thường. | 200 + tokens |
| **Email tồn tại, chưa link Google**, `EmailConfirmed = false` | 409 *"Vui lòng verify email trước khi liên kết Google."* |
| **Email tồn tại, đã link đúng Google subject** | 200 + tokens (login bình thường) |
| **Email tồn tại, đã link Google subject KHÁC** | 409 *"Email này đã liên kết với một Google account khác. Vui lòng dùng đúng Google account đã đăng ký, hoặc đăng nhập bằng email/mật khẩu."* (chống user A đã link email X với Google #1, user B login email X bằng Google #2) |
| Account `IsDeleted` / `Banned` / `Suspended` | 403 *"Tài khoản không khả dụng."* |
| Account `PendingVerification` (chưa verify OTP đăng ký) | 409 *"Email đã đăng ký nhưng chưa xác thực. Vui lòng verify OTP trước."* |
| Account `Locked` với `LockoutEndAt > now` | Handler **auto-recover** sang `Active` (clear `LockoutEndAt`, `FailedLoginAttempts = 0`), login bình thường |
| Google `EmailVerified = false` (Google chưa verify email user dù login được) | 401 *"Email Google chưa được xác thực."* |
| `idToken` không validate được hoặc rỗng email | 401 *"Google ID token không hợp lệ."* |

**Side effect khi success (inline issuance — không dùng `IssueAsync`):**
- Reset `FailedLoginAttempts = 0`, `LockoutEndAt = null`, update `LastLoginAt`, `LastLoginIp`.
- Insert `RefreshToken` row (#AUTH-01 hash, #AUTH-28 OriginalIssuedAt = now).
- Publish `SessionCreatedNotification` → session limit enforcement.
- **KHÔNG** chạy `DetectAndPublishSuspiciousLoginAsync` (vì handler không gọi `IssueAsync`).
- **KHÔNG** publish `LoginAttempt` row (login-history không có entry Google).
- **Chỉ với account mới**: publish outbox `AccountActivatedEvent` (`CreationSource = "GoogleOAuth"`).

---

### `POST /api/auth/login/2fa/sms` — **(#AUTH-58)**

**Mục đích:** Bước fallback của 2FA login flow — gửi OTP 6 số qua **SMS** tới số điện thoại đã verify khi user mất Authenticator app. Dùng kèm `challengeToken` từ bước 1 (`POST /api/auth/login` Case B). Sau khi nhận SMS, FE vẫn submit về `POST /api/auth/login/verify-2fa` (với `isBackupCode=false` — server đọc OTP từ SMS store cùng `challengeToken`).

**Auth:** Không yêu cầu (challengeToken thay vai trò "session đã verify password").

**Rate limit:** `TwoFactorVerify` — **5 attempts / 5 phút / `challengeToken`**. Partition theo HEADER `X-Challenge-Token` (cùng cơ chế với `/login/verify-2fa`) — **FE PHẢI gửi header `X-Challenge-Token: <challengeToken>`** kèm request để partition đúng.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `challengeToken` | `string` | Bắt buộc | Không rỗng | Token lấy từ `data.challenge.challengeToken` của `POST /api/auth/login` Case B |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "OTP đã gửi tới số điện thoại đã đăng ký. Có hiệu lực 3 phút.",
  "data": "********1234",
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data` | `string` | Không | Số điện thoại đã được **mask** (chỉ giữ 4 ký tự cuối, các ký tự còn lại thay bằng `*`) để FE hiển thị xác nhận "đã gửi tới số ******1234" mà không leak full number |

**Side effect khi success:**
- Sinh OTP 6 số TTL 3 phút, lưu vào Redis key gắn với `challengeToken` (`ITwoFactorSmsOtpStore`).
- Publish `SendSmsCommand` lên message bus → SmsService gửi SMS qua provider.

**Lỗi thường gặp:**
- `400` — Field validation (challengeToken rỗng).
- `404` — Challenge token hợp lệ nhưng account đã bị xoá giữa lúc challenge còn sống.
- `409` — Account **chưa verify số điện thoại** (`PhoneConfirmed = false` hoặc `PhoneNumber = null`) → không thể nhận SMS OTP fallback. FE phải báo user dùng TOTP từ Authenticator app hoặc backup code.
- `422` — Challenge token expired hoặc không tồn tại trong Redis (business rule, không phải field format).
- `429` — Vượt rate limit `TwoFactorVerify`.

> **Sau khi nhận SMS**, FE gọi `POST /api/auth/login/verify-2fa` với `{challengeToken, code: <OTP từ SMS>, isBackupCode: false, isSmsCode: true}` — **bắt buộc set `isSmsCode = true`** để server đọc code từ SMS OTP store thay vì validate TOTP. Nếu FE quên set flag, server sẽ verify code như TOTP và fail vì OTP SMS không khớp HMAC-SHA1 của TOTP secret.

---

### `POST /api/auth/2fa/cross-device-confirm/request` — **(#AUTH-51)**

**Mục đích:** Bước 1/2 của flow **setup 2FA xuyên thiết bị** — user đang login trên Device A (vd Laptop) nhưng Laptop không có camera để scan QR code → request gửi email chứa confirm link đến Device B (Phone). User mở email trên Phone, scan QR + nhập TOTP, gọi confirm endpoint → 2FA enable cho account.

**Auth:** Bắt buộc (mọi role) — user phải đang login trên Device A.

**Use case so với `/2fa/init` + `/2fa/confirm` (single-device flow GH-295):**

| Flow | Setup ở đâu | Confirm ở đâu | Khi dùng |
|---|---|---|---|
| **Single-device** (`/2fa/init` → `/2fa/confirm`) | Device A | Device A | Device A có camera scan QR HOẶC user copy secret manual vào Authenticator |
| **Cross-device** (this endpoint + `/cross-device-confirm`) | Device A (request) | Device B (confirm) | Device A KHÔNG có camera HOẶC user muốn dùng Authenticator trên Phone (Device B) trong khi đang login Laptop (Device A) |

**Request body:** Không có (server tự sinh secret + token).

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Link xác nhận đã được gửi tới email. Mở email trên thiết bị thứ 2 (Phone) để hoàn tất.",
  "data": {
    "confirmToken": "a1b2c3d4e5f6...64-char-hex",
    "expiresInSeconds": 600,
    "otpAuthUri": "otpauth://totp/GSU26SE55%20Auth:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GSU26SE55%20Auth&algorithm=SHA1&digits=6&period=30",
    "secret": "JBSWY3DPEHPK3PXP3PXP"
  },
  "listErrors": null
}
```

**Response fields:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `confirmToken` | `string` | Không | Token 32 bytes hex (64 char). FE dùng để hiển thị URL trong UI (vd debug), **KHÔNG** cần gửi lại — Device B đọc từ email link. Server lưu Redis `2fa:confirm-token:{token}` TTL 10 phút |
| `expiresInSeconds` | `int` | Không | TTL của token tính bằng giây — luôn `600` (10 phút). FE Device A hiển thị countdown để user biết khi nào link hết hạn |
| `otpAuthUri` | `string` | Không | URI chuẩn `otpauth://totp/...` để Device A render QR code (vd qrcode.js) cho Device B scan. Format gồm `secret`, `issuer = JwtSettings:Issuer` (default `"GSU26SE55 Auth"`), `algorithm=SHA1`, `digits=6`, `period=30` |
| `secret` | `string` | Không | Plain secret base32 (vd 32 ký tự) — **fallback** nếu Device B KHÔNG scan QR được (vd Authenticator chỉ accept text input). User copy-paste secret vào Authenticator |

**Side effect khi success:**
- Generate `secret = TotpService.GenerateSecret()` (Base32, 20 bytes random).
- Generate `confirmToken = RandomNumberGenerator.GetBytes(32).ToHex()` — 256-bit cryptographic random, single-use.
- Lưu Redis key `2fa:confirm-token:{confirmToken}` value `{accountId, secret, requestingSessionId, createdAtUtc}` TTL **10 phút**.
- Publish outbox event `SendTwoFactorCrossDeviceConfirmEmailEvent(toEmail, fullName, confirmUrl, expiresInMinutes=10)`:
  - `confirmUrl = {Frontend:WebBaseUrl}/2fa/cross-device-confirm?token={confirmToken}` (FE base URL từ config — xem Appendix A).
  - EmailService consume event → render template → gửi email với link click-to-confirm.
- Audit `TwoFactorSetupCrossDeviceRequested=120` với metadata `ttlMinutes=10`, `requestingSessionId`.

**Security note — Secret leak qua Device A:** API trả về cả `secret` plaintext cho Device A. Đây là intentional — Device A là device user đang login, server tin tưởng Device A đủ để show secret (giống `/2fa/init` cũng trả secret). Risk khi Device A bị compromise: attacker có thể trigger flow này, sau đó dùng email mình kiểm soát để confirm — nhưng email vẫn đến mailbox của user → user phát hiện. Mitigation thêm: anti-stolen-link check ở Confirm endpoint (xem dưới).

**Lỗi thường gặp:**
- `400` — `AccountId = Guid.Empty` từ JWT
- `401` — Chưa đăng nhập
- `404` — Account không tồn tại (rare — JWT valid nhưng account đã bị hard-delete)
- `409` — 2FA đã được bật trên account: `"2FA đã được bật. Hãy disable trước khi enroll lại."` — phải gọi `/2fa/disable` trước

---

### `POST /api/auth/2fa/cross-device-confirm` — **(#AUTH-51)**

**Mục đích:** Bước 2/2 của cross-device 2FA — Device B (Phone) confirm với token từ email link + TOTP code từ Authenticator → enable 2FA cho account.

**Auth:** Bắt buộc (mọi role) — user phải login trên Device B với **cùng account** đã trigger Request bước 1.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `confirmToken` | `string` | Bắt buộc | Đúng 64 ký tự hex (case-insensitive) | Token lấy từ URL email link (query param `?token=...`). FE Device B parse URL khi user mở link |
| `totpCode` | `string` | Bắt buộc | Đúng 6 chữ số | Mã TOTP từ Authenticator app sau khi user scan QR / nhập secret từ Device A vào Authenticator |

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã bật 2FA thành công. Thiết bị gốc sẽ tự refresh trạng thái.",
  "data": "8f3a5b9d-2c4e-4a1f-9b8d-1e2f3a4b5c6d",
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `string (Guid)` | AccountId vừa enable 2FA (= JWT.AccountId của Device B) |

**Side effect khi success:**
- `Account.TwoFactorSecret = ITwoFactorSecretProtector.Protect(secret)` (encrypted DPAPI per GH-295).
- `Account.TwoFactorSecretEncryptedAt = UtcNow`.
- `Account.TwoFactorEnabled = true`.
- Audit `TwoFactorSetupCrossDeviceConfirmed=121` với metadata `requestingSessionId` (track Device A session).
- Audit `TwoFactorEnabled=40` với metadata `method="cross-device"` (phân biệt với single-device flow).
- Redis key `2fa:confirm-token:{token}` bị xóa (single-use) — gọi lần 2 với cùng token sẽ 404.

**Lưu ý — backup codes:**
- Endpoint này **KHÔNG** sinh backup codes (khác với `/2fa/confirm` single-device flow GH-295).
- User PHẢI gọi thêm `POST /api/accounts/me/2fa/backup-codes/regenerate` sau confirm thành công để có backup codes — nếu không, mất Authenticator app = không thể login.
- Best practice FE: sau response 200 → tự động prompt user gọi regenerate ngay.

**Security — Anti-stolen-link (#AUTH-51):**

Server check `data.AccountId == request.AccountId` (`JWT.AccountId` của Device B). Mục đích: chống case email link bị forward/leak → kẻ xấu login bằng account của mình + click link đó → confirm 2FA cho **account mình** với secret của victim (nonsensical) HOẶC tệ hơn nếu logic không check.

- Nếu `data.AccountId ≠ request.AccountId` → **403 Forbidden** + audit `TwoFactorSetupCrossDeviceExpired=122` với metadata `expectedAccountId`, `actualAccountId`. Token **KHÔNG** bị xóa (giữ để user gốc retry nếu link còn TTL).

**Race condition handling:**
- Nếu giữa lúc Request và Confirm, account đã enable 2FA bằng flow khác (vd single-device `/2fa/init` + `/2fa/confirm`) → return **409** + xóa token (single-use): `"2FA đã được bật trước khi confirm. Token đã được xoá."`

**TOTP verify fail handling:**
- Wrong TOTP → return **422** `"Mã TOTP không đúng. Vui lòng thử lại."` — Token **KHÔNG** xóa (cho phép user retry với code mới vì TOTP rotate mỗi 30s).
- KHÔNG có rate limit handler-level cho retry — bảo vệ bởi Redis TTL 10 phút (sau TTL, token tự expire → 404).

**Sub-error messages:**

| Status | Message | Trigger |
|---|---|---|
| `400` | "Dữ liệu không hợp lệ." | Field validation (token format sai, TOTP không 6 số, AccountId từ JWT rỗng) — có `listErrors` |
| `401` | Auth middleware | JWT thiếu/sai/hết hạn |
| `403` | "Link xác nhận không thuộc về tài khoản của bạn." | Token issued cho account khác (anti-stolen-link) |
| `404` | "Link xác nhận đã hết hạn hoặc không hợp lệ. Vui lòng request lại từ thiết bị gốc." | Token hết TTL (>10p) hoặc không tồn tại Redis |
| `404` | "Không tìm thấy tài khoản." | Account JWT.AccountId không có trong DB (rare) |
| `409` | "2FA đã được bật trước khi confirm. Token đã được xoá." | Race với flow `/2fa/init`+`/2fa/confirm` |
| `422` | "Mã TOTP không đúng. Vui lòng thử lại." | TOTP wrong, có thể retry |

---

### `POST /api/auth/revoke` — **(#AUTH-54, RFC 7009)**

**Mục đích:** **Token Revocation theo RFC 7009** — Authenticated user gọi để revoke 1 access token cụ thể (qua `jti`). Khác `POST /api/auth/logout` (revoke refresh token + force re-login) ở chỗ chỉ blacklist `jti` của 1 access token; refresh token và các access token khác của cùng account vẫn hoạt động. Dùng khi client biết 1 access token đã bị leak nhưng vẫn muốn giữ các session khác.

**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}` (caller verify ownership).

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `token` | `string` | Bắt buộc | Không rỗng | Access token (JWT) muốn revoke. Có thể là cùng token đang authenticate request, hoặc một access token khác mà caller sở hữu. |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Token revoke request processed.",
  "data": null,
  "listErrors": null
}
```

**Lưu ý đặc biệt theo RFC 7009 §2.2:**
- Endpoint **luôn trả `200 OK`** kể cả khi token đã expired, signature invalid, hoặc thuộc account khác — để **không leak thông tin token có hợp lệ hay không** cho attacker.
- Server xử lý nội bộ:
  - Token signature/lifetime fail → no-op, trả 200.
  - Token thuộc account khác (subject claim `AccountId` ≠ `callerAccountId` từ JWT) → no-op, trả 200.
  - Token thiếu claim `jti` hoặc TTL ≤ 0 → no-op, trả 200.
  - Token hợp lệ và thuộc caller → blacklist `jti` vào Redis với TTL = thời gian còn lại của token; revocation reason = `"user_revoke"`.

**Lỗi:**
- `401` — JWT thiếu/sai (auth middleware, trước khi vào handler).

> **Phân biệt với Logout:** `Logout` cần `refreshToken` trong body, revoke refresh token + có thể bulk revoke access tokens; `Revoke` cần `accessToken` (JWT) trong body, chỉ blacklist 1 `jti`. Logout dùng khi user click "Đăng xuất", `Revoke` dùng khi user phát hiện 1 access token bị leak.

---

### `POST /api/auth/introspect` — **(#AUTH-40, RFC 7662)**

**Mục đích:** **OAuth 2.0 Token Introspection theo RFC 7662** — Resource server / API gateway / downstream service gọi để verify access token + check revocation trước khi cho qua. Trả `{active: true/false}` + metadata cơ bản. Dùng cho **service-to-service**, không phải user-facing.

**Auth:** Không yêu cầu auth header (RFC 7662 cho phép unauthenticated introspection trong scope nội bộ — production có thể wrap thêm `[Authorize(Roles="Service")]` hoặc mTLS).

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `token` | `string` | Bắt buộc | Không rỗng | Access token (JWT) cần introspect |

**Response thành công `200` — Case A: token active:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "active": true,
    "exp": 1748102400,
    "iat": 1748098800,
    "sub": "8f3a5b9d-2c1e-4d6a-9c0b-7e8f1a2b3c4d",
    "tokenType": "Bearer"
  },
  "listErrors": null
}
```

**Response thành công `200` — Case B: token inactive:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "active": false,
    "exp": null,
    "iat": null,
    "sub": null,
    "tokenType": null
  },
  "listErrors": null
}
```

**Field reference (`TokenIntrospectionDto`):**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.active` | `bool` | Không | `true` nếu token: (1) signature hợp lệ, (2) chưa expire, (3) `jti` không nằm trong revocation blacklist, (4) account không bị bulk-revoke sau thời điểm token được issue. `false` trong mọi trường hợp khác. |
| `data.exp` | `long?` | Null nếu `active=false` | Unix timestamp (giây) thời điểm token hết hạn. **Chỉ trả khi `active=true`** — không leak info khi inactive. |
| `data.iat` | `long?` | Null nếu `active=false` | Unix timestamp (giây) thời điểm token được issue. **Chỉ trả khi `active=true`**. |
| `data.sub` | `string?` | Null nếu `active=false` | Subject — `AccountId` lấy từ claim `AccountId` của JWT (dạng Guid string). **Chỉ trả khi `active=true`**. |
| `data.tokenType` | `string?` | Null nếu `active=false` | Token-type indicator. Hiện chỉ support `"Bearer"`. **Chỉ trả khi `active=true`**. |

**Quy tắc bảo mật theo RFC 7662:**
- Nếu token **inactive**: response **CHỈ** trả `active: false`, các field còn lại đều `null` để không leak metadata.
- Endpoint **luôn trả HTTP 200** (kể cả token sai format, expired, revoked) — phân biệt qua field `data.active`.

**Use case:** API gateway nhận request có header `Authorization: Bearer xxx` → gọi introspect → nếu `active=true` thì forward, nếu `false` thì reject 401.

---

### `POST /api/auth/reactivate-request` — **(#AUTH-50)**

**Mục đích:** Bước 1/2 của luồng **khôi phục tài khoản đã soft-delete** — User submit email của account đã bị `DELETE /api/accounts/me` (hoặc Admin xoá) trong vòng **90 ngày**, server gửi OTP về email. Window 90 ngày tính từ `Account.DeletedAt`.

**Auth:** Không yêu cầu.

**Rate limit:** `AnonOtp` (5 req / phút / IP).

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Đúng định dạng email | Email của account đã soft-delete cần khôi phục |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Nếu tài khoản trong window restore 90 ngày, OTP đã được gửi tới email.",
  "data": "user@example.com",
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data` | `string` | Không | Email đã normalize (lowercase) — phục vụ debug, không phải confirm email tồn tại |

**Quy tắc bảo mật (chống enumeration):**
- Endpoint **luôn trả `200` với cùng message** dù:
  - Email không tồn tại.
  - Email tồn tại nhưng account **không bị soft-delete** (vẫn active).
  - Email tồn tại, đã soft-delete nhưng **ngoài window 90 ngày** (`DeletedAt < now - 90d`).
- Chỉ khi tìm thấy account thoả mãn cả 2 điều kiện `IsDeleted = true` **AND** `DeletedAt >= UtcNow - 90d`, server mới sinh OTP và publish `SendPasswordResetOtpEvent` → EmailService gửi mail.

**Side effect khi tìm thấy account hợp lệ:**
- `Account.OtpCode` = OTP 6 số ngẫu nhiên.
- `Account.OtpExpiredAt = UtcNow + 5 phút` (#AUTH-14, 2026-06-19, giảm từ 10p).
- `Account.OtpPurpose = PasswordReset` (tái dùng enum sẵn — semantics: "verify by email").

**Lỗi thường gặp:**
- `429` — Vượt rate limit `AnonOtp` 5 req/phút/IP.

> **Window 90 ngày là gì:** Khi user tự xoá account (`DELETE /api/accounts/me`) hoặc Admin xoá (`DELETE /api/admin/accounts/{id}`), backend set `IsDeleted=true` + `DeletedAt=UtcNow`. Trong 90 ngày sau đó, data vẫn còn để phục vụ audit/khôi phục. Sau 90 ngày, cleanup job sẽ hard-delete; user **không còn khôi phục được** qua endpoint này.

---

### `POST /api/auth/reactivate-verify` — **(#AUTH-50)**

**Mục đích:** Bước 2/2 của luồng khôi phục — verify OTP từ bước 1 → clear `IsDeleted` flag, chuyển account về `Active`, reset failed login attempts và lockout. Sau khi success, user phải gọi `POST /api/auth/login` để đăng nhập (endpoint **không** cấp token trực tiếp).

**Auth:** Không yêu cầu.

**Rate limit:** `AnonOtp` (5 req / phút / IP).

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Đúng định dạng email | Email đã request reactivate ở bước 1 |
| `otp` | `string` | Bắt buộc | Không rỗng (handler trim trước khi compare) | OTP 6 chữ số nhận qua email |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Khôi phục tài khoản thành công. Vui lòng đăng nhập lại.",
  "data": "8f3a5b9d-2c1e-4d6a-9c0b-7e8f1a2b3c4d",
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data` | `string (Guid)` | Không | AccountId vừa được restore |

**Side effect khi success:**
- `Account.IsDeleted = false`, `Account.DeletedAt = null`.
- `Account.Status = Active`.
- Clear OTP state: `OtpCode = null`, `OtpExpiredAt = null`, `OtpPurpose = null`.
- Reset auth lockout: `FailedLoginAttempts = 0`, `LockoutEndAt = null`.

**Lỗi thường gặp:**
- `401` — OTP **hết hạn** HOẶC OTP **sai giá trị** (constant-time compare qua `SecureCompareHelper.FixedTimeEquals`, không leak timing).
- `404` — Không tìm thấy account trong window restore 90 ngày, hoặc email không tồn tại / chưa từng request reactivate.
- `429` — Vượt rate limit `AnonOtp`.

> **Endpoint này không cấp token.** Sau success, FE phải redirect user về `/login` và để user nhập password để gọi `POST /api/auth/login` như bình thường.

---

### `POST /api/auth/accept-invite`

**Mục đích:** Chấp nhận lời mời từ Admin, đặt mật khẩu lần đầu. Account chuyển sang `Active` và trả về token để login ngay.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `invitationToken` | `string` | Bắt buộc | Không được rỗng | Token trong email mời từ Admin |
| `password` | `string` | Bắt buộc | 8–100 ký tự, có chữ hoa/thường/số/ký tự đặc biệt | Mật khẩu mới |
| `confirmPassword` | `string` | Bắt buộc | Phải trùng với `password` | Xác nhận mật khẩu |

**Response thành công `200`:** Dùng cùng shape `LoginResultDto` như `POST /api/auth/login` Case A (Accept invite hoàn tất = login luôn, bypass 2FA cho lần đầu — user enroll 2FA sau nếu muốn). `data.tokens.accessToken` + `data.tokens.refreshToken`, `data.challenge = null`.

**Lỗi thường gặp:**
- `400` — Field validation đơn (password rỗng, invitationToken rỗng)
- `422` — `confirmPassword` không khớp `password` (cross-field validation, không phải 400)
- `401` — `invitationToken` không tồn tại / đã bị vô hiệu hoá / **đã hết hạn** (cùng status 401 cho cả 3 case)
- `409` — Token đã được dùng rồi (account đã active, không thể accept lại)

**Lưu ý status:** Token hết hạn trả **`401`** (không phải 410 — backend không có nhánh 410 cho endpoint này). `confirmPassword` mismatch trả **`422`** (cross-field), còn field rỗng trả `400`.

**Lưu ý TTL:** `invitationToken` hết hạn sau **72 giờ** (`InvitationLifetimeHours = 72` trong `InviteAccountCommandHandler`). Edge case: nếu `InvitationExpiredAt = null` trong DB (legacy row hoặc admin tạo manual) → handler reject 401 *"Invitation token đã hết hạn. Yêu cầu admin gửi lại invite."* (#AUTH-26 — không bao giờ chấp nhận invite "vô hạn").

**Side effect khi success (inline issuance — không dùng `IssueAsync`):**
- `Account.PasswordHash` set theo password mới (bcrypt).
- `Account.EmailConfirmed = true`, `Status = Active`.
- `Account.InvitationToken = null`, `InvitationExpiredAt = null` (single-use).
- `Account.FailedLoginAttempts = 0`, `LockoutEndAt = null`.
- `Account.LastLoginAt = UtcNow`, `LastLoginIp = ipAddress`.
- Insert `RefreshToken` row với `OriginalIssuedAt = now` (#AUTH-28 first issue), `ExpiredAt = now + 7d` (config), `Token = Hash(plaintext)` (#AUTH-01).
- Publish `SessionCreatedNotification` → session limit enforcement (revoke session cũ nhất nếu vượt `Session:MaxConcurrentSessions`).
- Publish outbox `AccountActivatedEvent` với `CreationSource = "AdminInvite"` → NotificationService/UserDirectory sync.
- Audit `AccountInviteAccepted` (`TargetAccountId = ActorAccountId = account.Id`, `TargetEmail = account.Email`, `IsSuccess = true`).

**Khác với `/login` (`IssueAsync` path):**
- **KHÔNG** chạy `DetectAndPublishSuspiciousLoginAsync` (vì đây là first login từ invitation, mọi IP/UA đều "mới" theo định nghĩa).
- **KHÔNG** publish `LoginAttempt` row (login-history sẽ không có entry cho accept-invite).

Sub-error messages:
| Status | Message | Trigger |
|---|---|---|
| 401 | "Invitation token không hợp lệ hoặc đã được sử dụng." | Token không match DB row nào |
| 401 | "Invitation token đã hết hạn. Yêu cầu admin gửi lại invite." | `InvitationExpiredAt <= now` hoặc null |
| 409 | "Tài khoản đã được kích hoạt trước đó." | Status ≠ PendingVerification |

---

## Nhóm 2 — Tài khoản cá nhân (Yêu cầu access token)

Base route: `/api/accounts`
Header: `Authorization: Bearer {accessToken}`

> **Phân biệt Nhóm 2 vs Nhóm 3:**
> - **Nhóm 2** (`/api/accounts`) — AccountsController: quản lý account cốt lõi (password, email change, phone verify, 2FA, Google link, deactivate/delete, login history). **Có thêm 3 endpoint profile** (`GET /me/profile`, `PUT /me/profile`, `PUT /{id}`) — xem cuối nhóm này.
> - **Nhóm 3** (`/api/auth`) — AuthProfilesController: **canonical route cho profile operations** (đọc profile, cập nhật fullName/address/birthDate/timezone, avatar). FE **nên dùng** `GET /api/auth/me` và `PUT /api/auth/me/profile` cho mọi thao tác profile.
>
> **Lưu ý route trùng:** 3 endpoint profile ở Nhóm 2 (`/api/accounts/me/profile`, `/api/accounts/{id}`) tồn tại trong code và hoạt động, nhưng **trùng chức năng** với Nhóm 3. Canonical vẫn là Nhóm 3 (`/api/auth/me`); FE mới nên dùng Nhóm 3, các route Nhóm 2 giữ để tương thích.

**Lỗi thường gặp cho nhóm này:**
- `401` — Token không hợp lệ, hết hạn hoặc JWT thiếu account id
- `404` — Account/session/resource không tồn tại
- `409` — Dữ liệu cập nhật xung đột với account khác hoặc rule nghiệp vụ

---

### Avatar

> **Endpoint này thuộc `/api/auth`, không phải `/api/accounts`.** Xem [Nhóm 3 → `POST /api/auth/me/avatar`](#post-apiauthmeavatar) để biết đầy đủ request body, response, và lưu ý contract.

---

### `PATCH /api/accounts/me/password`

**Mục đích:** Đổi mật khẩu khi đang đăng nhập.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `currentPassword` | `string` | Bắt buộc | Không rỗng | Mật khẩu hiện tại |
| `newPassword` | `string` | Bắt buộc | 8–100 ký tự, có chữ hoa/thường/số/ký tự đặc biệt | Mật khẩu mới |
| `confirmPassword` | `string` | Bắt buộc | Phải trùng với `newPassword` | Xác nhận mật khẩu mới |

**Response thành công `200`:** `isSuccess = true`. Toàn bộ refresh token bị revoke, user cần đăng nhập lại.

**Lưu ý bảo mật:** Rule mật khẩu mới đồng bộ với register/reset/accept-invite. Khi đổi mật khẩu thành công, tất cả refresh token của account bị revoke. Access token hiện tại vẫn valid đến khi hết hạn; FE phải clear token và redirect về login sau khi nhận response thành công.

**Error responses (verified với code handler — round 3 docs ghi sai về 422):**

| Status | Trường hợp | Tại đâu |
|---|---|---|
| `400` | Field validation đơn: `currentPassword` rỗng, `newPassword` rỗng/không đạt độ phức tạp | Validation pipeline (`hasCrossFieldError = false`) |
| `400` | **`currentPassword` không khớp hash** trong DB | Handler line 65-78. Message: `"Mật khẩu hiện tại không chính xác."` |
| `400` | **`newPassword == currentPassword`** (#AUTH-23 anti-trick — tránh user "force revoke session" bằng cách "đổi" password thành chính nó) | Handler line 50-63 (defensive). **Lưu ý**: validation pipeline cũng có cùng check → thường set 422 trước. Nếu somehow validation skip thì handler trả 400. |
| `401` | JWT thiếu/sai `AccountId` (middleware) | Auth middleware |
| `404` | Account không tồn tại | Handler |
| `409` | **(#AUTH-34, 2026-06-19) Concurrency conflict** — admin update account đồng thời (vd disable account, đổi role) → sau 3 lần retry vẫn fail HOẶC account becomes invalid (deleted/inactive) sau reload | Handler `ConcurrencyRetryHelper.ExecuteAsync`. Message: `"Tài khoản đã bị thay đổi bởi tiến trình khác. Vui lòng thử lại."` |
| `422` | **Cross-field validation pipeline**: `confirmPassword != newPassword` HOẶC `newPassword == currentPassword` | Validation pipeline (`hasCrossFieldError = true`) |

**Phân biệt `400` vs `422` (sửa lại từ round 3):**
- `400` — Field validation đơn HOẶC **wrong currentPassword** (handler check) HOẶC handler-level defensive check.
- `422` — **Cross-field** validation pipeline (`confirmPassword` mismatch, `newPassword == currentPassword`). Validation pipeline catch trước khi reach handler.
- `401` — Chỉ JWT middleware fail.
- `409` — Concurrent admin update conflict (#AUTH-34) → FE retry là an toàn (idempotent ở handler-side với `auditPublished` flag).

> Thực tế FE nên handle 400 + 422 cùng kiểu (cả 2 đều là input error) — chỉ khác ở `listErrors` shape.

**Side effect khi success (#AUTH-23, #AUTH-54, #AUTH-48):**
- Toàn bộ `RefreshToken` active của account → `Status = Revoked` với `RevokedReason = "Password changed"`.
- (#AUTH-54) **Bulk revoke ALL access tokens** đã issue trước thời điểm đổi password: call `_revocationStore.RevokeAllByAccountAsync(accountId, TTL=1h)` → access token còn hiệu lực bị blacklist trong tối đa 1h (= max access token life), sau đó tự dọn vì token đã expire tự nhiên.
- **(#AUTH-48, 2026-06-19)** Auto-revoke **TẤT CẢ trusted device** của account qua internal `RevokeAllTrustedDevicesCommand` (reason: `"Password changed"`) — security best practice: attacker biết password cũ + đã trust device thì đổi password phải reset trust để cắt access. Audit `TrustedDeviceAllRevoked=112`.
- Audit `PasswordChanged` (success) với metadata `revokedSessions = <count>`.
- Audit `PasswordChanged` (fail) cũng được publish cho 2 case fail: wrong currentPassword + new == old (với reason cụ thể trong metadata).
- **(#AUTH-34) Concurrency retry**: handler wrap business logic + SaveChanges trong `ConcurrencyRetryHelper.ExecuteAsync` (3 attempts max, reload entity sau conflict). `auditPublished` flag đảm bảo audit `PasswordChanged` chỉ publish 1 lần dù retry — tránh duplicate audit row.

---

### `POST /api/accounts/me/change-email`

**Mục đích:** Yêu cầu đổi email. Hệ thống gửi OTP 6 số về **email mới** để xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `newEmail` | `string` | Bắt buộc | Đúng định dạng email, max 256 ký tự | Email mới cần chuyển sang |
| `currentPassword` | `string` | Bắt buộc | Không rỗng | Mật khẩu hiện tại để xác nhận danh tính |

**Rate limit:** `AuthOtp` (#AUTH-46) — **3 req / phút / userId**. Vượt limit → `429` + header `Retry-After`.

**Response thành công `200`:** `isSuccess = true`, `data` là accountId (Guid). OTP đã gửi về email mới (purpose `EmailChange`, **TTL 5 phút** — #AUTH-14, 2026-06-19, giảm từ 10p).

**Lỗi thường gặp:**
- `400` — Field validation (email sai định dạng, password rỗng)
- `401` — Chưa đăng nhập HOẶC `currentPassword` không chính xác
- `404` — Không tìm thấy tài khoản
- `409` — Email mới đã được tài khoản khác sử dụng (DB unique check)
- `409` — **Email mới đang được tài khoản khác xử lý** (#AUTH-24 Redis reservation, message: *"Email mới đang được tài khoản khác xử lý. Vui lòng chọn email khác hoặc thử lại sau."*)
- `422` — Email mới trùng email hiện tại (message: *"Email mới phải khác email hiện tại."*)

**#AUTH-24 — Redis email reservation (chi tiết verify với handler):**

Ngay sau khi pass DB uniqueness check, handler set Redis key `email_reserve:{sha256(normalizedEmail)[..16]}` (privacy-preserving — không lưu raw email trong Redis) với value = `accountId.ToString("N")`, TTL **5 phút** (#AUTH-14, align với OTP TTL), SET NX (atomic check-and-set).

**Owner-aware behavior:**
- **Key chưa tồn tại** → SET thành công → tiếp tục flow.
- **Key tồn tại, owner ≠ accountId hiện tại** → user khác đang trong flow đổi sang cùng email → **409** với message ở trên. User phải chọn email khác hoặc đợi 5 phút.
- **Key tồn tại, owner = accountId hiện tại** → **idempotent**: user re-request OTP (vd OTP cũ hết hạn) — refresh TTL về 5 phút, tiếp tục flow bình thường. KHÔNG trả 409.

Key được delete ở `/confirm-email-change` (best-effort, tự expire 5 phút nếu fail).

**Side effect khi success:**
- `Account.PendingEmail = newEmail`, `OtpCode`, `OtpExpiredAt = now + 5p` (#AUTH-14, 2026-06-19), `OtpPurpose = EmailChange`.
- `FailedLoginAttempts = 0` (reset shared counter — note: KHÔNG clear `LockoutEndAt`, tức nếu account đang lockout, user phải đợi hết lockout trước khi confirm OTP được).
- Publish outbox `SendEmailChangeOtpEvent(newEmail, otp)` → EmailService gửi OTP tới email **mới** (không phải email hiện tại).

---

### `POST /api/accounts/me/confirm-email-change`

**Mục đích:** Xác thực OTP để hoàn tất đổi email. Email mới chính thức có hiệu lực.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `otp` | `string` | Bắt buộc | OTP 6 chữ số gửi về email mới |

**Lưu ý — email mới lấy từ đâu:** FE không cần gửi lại email mới trong request này. Khi gọi `POST /api/accounts/me/change-email`, server lưu email mới vào field `PendingEmail` của account trong DB. Handler `confirm-email-change` đọc `PendingEmail` từ DB, verify OTP, rồi copy sang `Email` chính thức. Không có race condition khi mở nhiều tab vì `PendingEmail` là per-account.

**Response thành công `200`:** `isSuccess = true`, `data` là accountId (Guid). Email đã cập nhật.

**Lỗi thường gặp:**
- `400` — OTP sai định dạng (phải đủ 6 chữ số)
- `401` — Chưa đăng nhập HOẶC OTP sai/hết hạn
- `404` — Không tìm thấy tài khoản
- `409` — Không có yêu cầu đổi email đang chờ verify, hoặc email mới bị tài khoản khác chiếm trong lúc chờ
- `423` — Tài khoản bị khóa tạm thời do sai OTP nhiều lần

**Side effect khi success (verified với handler code):**

| Bước | Action |
|---|---|
| 1 | Copy `PendingEmail` → `Email`; set `EmailConfirmed = true`. |
| 2 | Clear `PendingEmail = null`, `OtpCode = null`, `OtpPurpose = null`, `OtpExpiredAt = null`. |
| 3 | Reset shared lockout counter: `FailedLoginAttempts = 0`, `LockoutEndAt = null`. |
| 4 | Revoke toàn bộ `RefreshToken` active của account với `RevokedReason = "Email changed"`. |
| 5 | (#AUTH-24) **Release Redis email reservation key** `email_reserve:{sha256(email)[..16]}` (best-effort try-catch; key tự expire 10p nếu delete fail). Reservation key được set lúc `/me/change-email` để ngăn 2 user cùng pending tới 1 email. |
| 6 | Trả 200 với `data = accountId`, message *"Đổi email thành công. Vui lòng đăng nhập lại bằng email mới."* |

**Pre-confirm race check**: ngay TRƯỚC khi commit, handler double-check email vẫn chưa bị account khác chiếm trong khoảng chờ (xảy ra nếu user khác register/change-email tới cùng email giữa chừng). Nếu bị chiếm → clear `PendingEmail` của account hiện tại + return **409** *"Email mới đã bị tài khoản khác đăng ký trong lúc chờ verify."* User phải khởi tạo lại flow change-email.

> ⚠️ **Gap so với `/me/password`/`/reset-password`**: handler này **KHÔNG** gọi `_revocationStore.RevokeAllByAccountAsync(...)` (#AUTH-54) → access token còn hạn của account **vẫn dùng được** đến khi expire (max 1h). Tức user "logged out" ở mức refresh token nhưng access token chưa expire vẫn access được API. FE nên gọi `/auth/logout` hoặc `/auth/revoke` để force blacklist nếu cần immediate revocation.

**Shared lockout counter (#AUTH-19) — nhắc lại:** Wrong OTP ở endpoint này tăng `Account.FailedLoginAttempts` (shared với /login, /verify-otp, /verify-reset-otp, /verify-phone-otp). 5 fail tổng → 423 Lockout 15 phút. OTP expired (vs wrong) **KHÔNG** tăng counter — trả 401 *"OTP đã hết hạn. Vui lòng yêu cầu đổi email lại."*

---

### `POST /api/accounts/me/send-phone-otp`

**Mục đích:** Gửi OTP qua SMS đến số điện thoại đang lưu trong profile để xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có (AccountId lấy từ JWT)

**Response thành công `200`:** `isSuccess = true`, OTP đã gửi.

**Rate limit:** `AuthOtp` — **3 req / phút / userId**. Vượt limit → `429` + header `Retry-After`.

**Cooldown nội bộ (handler-level):** Có cooldown 60 giây giữa các lần gửi. Gọi quá sớm trả `429` với message "Vui lòng đợi N giây trước khi yêu cầu gửi lại OTP." OTP SMS có TTL 5 phút.

**Lỗi thường gặp (verified với code — docs cũ ghi sai status):**

| Status | Message | Trigger |
|---|---|---|
| 401 | (middleware) | JWT thiếu/sai |
| 404 | "Không tìm thấy tài khoản." | Account không tồn tại / soft-deleted |
| **422** | "Tài khoản chưa có số điện thoại. Vui lòng cập nhật profile trước." | `Account.PhoneNumber` rỗng/null — **state violation, không phải field validation** |
| **409** | "Số điện thoại đã được xác thực." | `Account.PhoneConfirmed = true` — conflict state |
| 429 | (cooldown message) | Gọi trong vòng 60s sau lần gửi trước HOẶC vượt rate limit `AuthOtp` (3/min/userId) |

> **Sửa từ docs cũ**: 2 case "chưa có phoneNumber" và "đã verified" được docs ghi 400 — **sai**. Code trả 422 và 409 tương ứng (business state violation, không phải field validation).

---

### `POST /api/accounts/me/verify-phone-otp`

**Mục đích:** Xác thực OTP SMS để đánh dấu số điện thoại là đã xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `otp` | `string` | Bắt buộc | OTP 6 chữ số nhận qua SMS |

**Response thành công `200`:** `isSuccess = true`, `phoneConfirmed = true`.

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | Validation: OTP sai định dạng (không đúng 6 chữ số) |
| `401` | Chưa đăng nhập (JWT thiếu/sai) HOẶC OTP sai giá trị (credential invalid) |
| `404` | Không tìm thấy account |
| `409` | Số điện thoại đã được xác thực trước đó (`phoneConfirmed = true`) |
| `422` | OTP không phải dành cho mục đích `PhoneVerify`, HOẶC OTP đã hết hạn, HOẶC account chưa được gửi OTP nào (state/business rule violation) |
| `423` | Lockout do sai OTP quá số lần cho phép |

**Phân biệt `401` vs `422`:**
- `401` — OTP **sai giá trị** (credential invalid), cùng cơ chế với password mismatch.
- `422` — Vi phạm state/business rule: OTP đúng giá trị nhưng `OtpPurpose != PhoneVerify`, hoặc OTP đã hết hạn, hoặc account chưa từng request gửi OTP. Đây là vi phạm trạng thái, không phải credential sai.

---

### `POST /api/accounts/me/2fa/enable` — **DEPRECATED (GH-295)**

**Status:** Endpoint cũ — luôn trả `410 Gone`. Dùng flow 2 bước mới: `POST /me/2fa/init` → `POST /me/2fa/confirm`.

**Response `410 Gone`:**
```json
{
  "isSuccess": false,
  "statusCode": 410,
  "message": "Endpoint này đã bị thay thế. Dùng POST /api/accounts/me/2fa/init rồi POST /api/accounts/me/2fa/confirm.",
  "data": null,
  "listErrors": null
}
```

> **Lý do reverse:** Behavior cũ (Option B) activate 2FA ngay khi gọi `/enable` mà chưa verify user đã quét QR thành công → user có thể tự lock-out chính mình. GH-295 tách thành 2 bước để bắt buộc verify TOTP trước khi activate. Xem `overall.md` §0.5 + ADR-019.

---

### `POST /api/accounts/me/2fa/init` — **(GH-295)**

**Mục đích:** Bước 1/2 của enable 2FA flow — sinh secret + QR URI, cache pending state vào Redis (TTL 10 phút). **CHƯA activate 2FA** ở bước này.

**Auth:** Bắt buộc (mọi role)

**Rate limit:** `AuthOtp` (3 req / phút / userId)

**Request body:** Không có (AccountId lấy từ JWT)

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã sinh secret. Quét QR bằng Authenticator rồi gọi /2fa/confirm với mã 6 số để kích hoạt.",
  "data": {
    "secret": "JBSWY3DPEHPK3PXP",
    "otpAuthUri": "otpauth://totp/GSU26SE55%20Auth:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GSU26SE55%20Auth&algorithm=SHA1&digits=6&period=30",
    "pendingToken": "a1b2c3d4e5f60718293a4b5c6d7e8f90"
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.secret` | `string` | Không | Base32 secret (20 bytes ≈ 32 ký tự) — user nhập tay nếu không quét được QR |
| `data.otpAuthUri` | `string` | Không | URI `otpauth://totp/...` — render thành QR code bằng `qrcode.js` |
| `data.pendingToken` | `string` | Không | Token (32 hex) gắn với pending state Redis — gửi kèm bước confirm |

**Side effect:**
- Sinh secret bằng `Otp.NET` (RFC 6238 SHA1/6digits/30s)
- Cache `2fa:pending:{accountId}` Redis TTL 10’ chứa `{secret, pendingToken, createdAtUtc}`
- **KHÔNG** set `Account.TwoFactorEnabled = true` — đợi confirm
- Gọi init lần 2 → overwrite pending cũ (idempotent — pendingToken mới invalidate token cũ)

**Lỗi thường gặp (verified với handler):**
- `401` — JWT empty/expired
- `404` — Account không tồn tại — message: *"Không tìm thấy tài khoản."*
- `409` — 2FA đã được bật trên account — message: *"2FA đã được bật. Hãy disable trước khi enroll lại."* (phải gọi `/2fa/disable` trước)
- `429` — Rate limit (`AuthOtp` 3/min/userId)

**Issuer name trong QR code:** Label = email của account; issuer = `JwtSettings:Issuer` config (fallback `"GSU26SE55 Auth"`). User sẽ thấy entry `"GSU26SE55 Auth:user@example.com"` trong Google Authenticator/Authy.

---

### `POST /api/accounts/me/2fa/confirm` — **(GH-295)**

**Mục đích:** Bước 2/2 của enable 2FA flow — verify mã TOTP từ Authenticator → activate 2FA, encrypt secret, sinh 8 backup codes.

**Auth:** Bắt buộc (mọi role)

**Rate limit:** `AuthOtp` (3 req / phút / userId)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `pendingToken` | `string` | Bắt buộc | Không rỗng | Token nhận từ `/2fa/init` |
| `code` | `string` | Bắt buộc | Đúng 6 chữ số | Mã TOTP hiện tại từ Authenticator |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Bật 2FA thành công. LƯU LẠI 8 backup codes — chúng chỉ hiển thị 1 lần.",
  "data": {
    "enabled": true,
    "backupCodes": [
      "abcd-2345", "efgh-6789", "jkmn-pqrs", "tuvw-xyz2",
      "3456-789a", "bcde-fghj", "kmnp-qrst", "uvwx-yz23"
    ]
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.enabled` | `bool` | Không | Luôn `true` khi success |
| `data.backupCodes` | `string[]` | Không | **8 plaintext codes** format `xxxx-xxxx` (8 ký tự alphanum bỏ `0/o/l/1` + 1 dash). DB lưu BCrypt hash. **Trả 1 lần duy nhất** — FE bắt buộc cho user lưu/print/copy trước khi đóng modal. |

**Side effect:**
- `Account.TwoFactorSecret = enc:v1:{base64}` (encrypt qua ASP.NET Data Protection)
- `Account.TwoFactorSecretEncryptedAt = UtcNow`
- `Account.TwoFactorEnabled = true`
- Insert 8 rows `backup_codes` (CodeHash = BCrypt cost 11, RedeemedAt = null)
- Xóa Redis pending state
- Audit log `TwoFactorEnabled` (metadata.backupCodesIssued = 8)

**Lỗi thường gặp (verified với handler — 3 sub-messages khác nhau cho 422):**

| Status | Message | Trigger |
|---|---|---|
| 400 | (listErrors detail) | Field validation: `pendingToken/code` rỗng, `code` không phải 6 chữ số |
| 401 | (middleware) | JWT empty/expired |
| 404 | `"Không tìm thấy tài khoản."` | AccountId từ JWT không match account nào |
| 409 | `"2FA đã được bật."` | Race: account đã enable 2FA giữa lúc user gọi init + confirm |
| **422** | `"Phiên setup đã hết hạn hoặc chưa init. Hãy gọi /2fa/init lại."` | Redis pending state expired (>10p) hoặc không tồn tại |
| **422** | `"PendingToken không khớp."` | Pending state có nhưng `pendingToken` body khác giá trị Redis cached (vd user gọi init lần 2 → pendingToken cũ invalidate) |
| **422** | `"Mã xác thực không đúng. Hãy kiểm tra thời gian thiết bị + thử lại."` | TOTP code sai — thường do device clock skew ngoài cửa sổ ±1 step (30s) |
| 429 | (rate limit) | Vượt `AuthOtp` 3/min/userId |

> **Quan trọng**: 422 với message thứ 3 ("Mã xác thực không đúng") **vẫn giữ pending state** — user retry với mã đúng (sau ~30s đợi step mới) vẫn được. 422 với 2 message đầu thì pending state đã mất → user phải gọi `/2fa/init` lại.

> **FE UX recommendation**: khi nhận 422, parse message để hiển thị guidance khác nhau:
> - "Mã xác thực không đúng" → cho retry inline (có thể là clock skew, user thử lại với mã mới)
> - "Phiên setup đã hết hạn" hoặc "PendingToken không khớp" → reset wizard về step 1, gọi lại `/2fa/init`

---

### `POST /api/accounts/me/2fa/disable` — **(updated GH-295)**

**Mục đích:** Tắt 2FA — yêu cầu re-auth bằng **cả** password **và** TOTP để chống session hijack (attacker chiếm JWT vẫn không disable được vì không biết password) + chống stolen device (attacker có device vẫn không biết password).

**Auth:** Bắt buộc (mọi role)

**Rate limit:** `TwoFactorDisable` (3 req / 5 phút / userId)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `password` | `string` | Bắt buộc | Không rỗng | Mật khẩu hiện tại |
| `totpCode` | `string` | Bắt buộc | Đúng 6 chữ số | Mã TOTP hiện tại từ Authenticator |

> **Không hỗ trợ backup code** để disable — chỉ TOTP (để tránh attacker có 1 backup code đoán được + chiếm session là disable luôn 2FA của user).

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Tắt 2FA thành công.",
  "data": "8f3a5b9d-...",
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `string (Guid)` | AccountId vừa disable |

**Idempotent:** Nếu 2FA vốn đã OFF → trả `200` ngay với message `"2FA vốn đã chưa bật."`, không yêu cầu verify password/TOTP.

**Side effect khi success:**
- `Account.TwoFactorSecret = null`, `TwoFactorEnabled = false`, `TwoFactorSecretEncryptedAt = null`
- Xóa toàn bộ `backup_codes` rows của account (soft delete via interceptor)
- Audit log `TwoFactorDisabled` (success/fail)

**Lỗi thường gặp:**
- `400` — Field validation (password/totpCode rỗng, totpCode không phải 6 chữ số) — có `listErrors`
- `401` — JWT empty/expired
- `404` — Account không tồn tại
- `422` — Password sai hoặc TOTP sai. Response message **generic** `"Mật khẩu hoặc mã không đúng."` (chống attacker dò xem field nào sai)
- `429` — Rate limit

---

### `POST /api/accounts/me/2fa/backup-codes/regenerate` — **(GH-295)**

**Mục đích:** Sinh lại 8 backup codes mới — vô hiệu hóa codes cũ. Dùng khi user lo codes cũ bị lộ hoặc đã dùng gần hết.

**Auth:** Bắt buộc (mọi role)

**Rate limit:** `BackupCodeRegenerate` (3 req / giờ / userId)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `totpCode` | `string` | Bắt buộc | Đúng 6 chữ số | Mã TOTP hiện tại (chứng minh user còn giữ device) |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã sinh 8 backup codes mới. Codes cũ đã bị vô hiệu hóa.",
  "data": {
    "backupCodes": [
      "wxyz-3456", "..."
    ]
  },
  "listErrors": null
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.backupCodes` | `string[8]` | Không | Plain codes mới, format `xxxx-xxxx`. **Trả 1 lần duy nhất.** |

**Side effect:**
- Xóa toàn bộ `backup_codes` rows cũ của account (soft delete)
- Insert 8 rows mới (CodeHash = BCrypt)
- Audit log `BackupCodesRegenerated` (metadata.oldCodesInvalidated, newCodesIssued=8)

**Lỗi thường gặp:**
- `400` — Field validation (totpCode rỗng / không phải 6 chữ số) — có `listErrors`
- `401` — JWT empty/expired
- `404` — Account không tồn tại
- `409` — 2FA chưa được bật (phải enroll trước)
- `422` — TOTP code sai
- `429` — Rate limit (3/giờ rất chặt vì đây là endpoint nhạy cảm)

---

### `POST /api/accounts/me/link-google`

**Mục đích:** Liên kết tài khoản hiện tại với tài khoản Google (để đăng nhập bằng Google sau).

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `idToken` | `string` | Bắt buộc | Google ID token từ Google Sign-In SDK |

**Response thành công `200`:** `isSuccess = true`, `data` là Guid của account.

---

### `POST /api/accounts/me/unlink-google`

**Mục đích:** Hủy liên kết với tài khoản Google.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `isSuccess = true`.

---

### `POST /api/accounts/me/deactivate`

**Mục đích:** Tự vô hiệu hóa tài khoản của mình (soft). Tài khoản chuyển sang `Inactive`.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có

**Response thành công `200`:** `isSuccess = true`, message *"Đã vô hiệu hóa tài khoản. Liên hệ admin để khôi phục."*, `data = accountId`.

**Side effect (verified với handler):**
- `Account.Status = Inactive`.
- Toàn bộ `RefreshToken` active → `Revoked` với reason `"Self deactivated"`.
- **KHÔNG publish event** (khác với `/me` DELETE có publish `AccountDeletedEvent`).
- **KHÔNG audit log** publish.

> **⚠️ Gap so với password/email change endpoints (#AUTH-54 missing)**: Handler **KHÔNG** gọi `_revocationStore.RevokeAllByAccountAsync(...)` → access token còn hạn vẫn dùng được cho đến khi expire (max 1h). User vừa deactivate có thể vẫn truy cập API trong khoảng đó. FE nên thêm explicit `/auth/logout` call sau deactivate nếu cần immediate access token revocation.

> **Reactivate**: User KHÔNG tự reactivate được — message rõ ràng *"Liên hệ admin để khôi phục."* Admin dùng `PATCH /api/admin/accounts/{id}/status` set `Active`. Khác với `Locked` (auto-recover sau 15 phút) và `IsDeleted` (qua `/reactivate-request` window 90 ngày).

---

### `DELETE /api/accounts/me`

**Mục đích:** Tự xóa tài khoản của mình theo cơ chế soft delete (`IsDeleted = true`, `DeletedAt = UtcNow`).

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `isSuccess = true`, message *"Đã xóa tài khoản."*, `data = accountId`.

**Side effect (verified với handler):**

| Bước | Action |
|---|---|
| 1 | Toàn bộ `RefreshToken` active → `Revoked` với reason `"Self deleted account"`. |
| 2 | `Account.IsDeleted = true`, `DeletedAt = UtcNow` (qua `AuditableEntityInterceptor` khi handler gọi `DeleteAsync`). |
| 3 | **Publish outbox `AccountDeletedEvent`**: `{accountId, email, DeletionSource: "SelfDelete"}` → NotificationService consume để gửi email confirm xóa, UserDirectory unsync. |
| 4 | **KHÔNG** audit log publish (audit trail là chính event publish). |

**90-day reactivation window:**
Sau khi soft delete, account có thể được khôi phục qua `POST /api/auth/reactivate-request` trong **90 ngày**. Sau 90 ngày, `AccountHardDeleteBackgroundService` xóa hard cùng cascade (refresh tokens, profiles, backup codes, audit log entries của account).

> **⚠️ Gap so với password endpoints (#AUTH-54 missing)**: Handler **KHÔNG** bulk blacklist access tokens → user vừa delete vẫn dùng được access token cho đến khi expire. FE force `/auth/logout` sau delete nếu cần immediate revocation.

**Lưu ý trạng thái:** User tự xóa không nên được FE hiển thị như "bị banned". `Banned` là trạng thái quản trị/nghiệp vụ riêng; self-delete phân biệt bằng:
- `Account.IsDeleted = true` (chỉ self-delete và admin-delete set flag này)
- `DeletionSource: "SelfDelete"` (vs `"AdminDelete"`) trong `AccountDeletedEvent` outbox payload

---

### `GET /api/accounts/me/export` — **(#AUTH-62, GDPR Article 20)**

**Mục đích:** **Data Portability theo GDPR Article 20** — User export toàn bộ data cá nhân của mình dưới dạng JSON structured (machine-readable). Endpoint trả response với header `Content-Disposition: attachment` để browser tự download thành file `account-export-{accountId}-{yyyyMMdd}.json`.

**Auth:** Bắt buộc (mọi role, lấy `AccountId` từ JWT).

**Query/Body:** Không có.

**Response thành công `200`:**

Response header:
```
Content-Disposition: attachment; filename="account-export-8f3a5b9d2c1e4d6a9c0b7e8f1a2b3c4d-20260619.json"
Content-Type: application/json
```

Response body wrapped trong `CommonResponse<AccountDataExportDto>`:
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "account": {
      "id": "8f3a5b9d-...",
      "email": "user@example.com",
      "phoneNumber": "0901234567",
      "fullName": "Nguyễn Văn A",
      "avatarUrl": "https://...",
      "dateOfBirth": "1995-05-16T00:00:00Z",
      "address": "123 Đường ABC, Q.1, HCM",
      "emailConfirmed": true,
      "phoneConfirmed": true,
      "twoFactorEnabled": true,
      "status": "Active",
      "googleId": "10987654321...",
      "provider": "Google",
      "lastLoginAt": "2026-06-18T08:30:00Z",
      "lastLoginIp": "192.168.1.1",
      "role": "Customer",
      "createdAt": "2026-01-15T03:20:00Z"
    },
    "profile": {
      "externalAvatarUrl": "https://lh3.googleusercontent.com/...",
      "avatarSource": "Uploaded",
      "address": "123 Đường ABC, Q.1, HCM",
      "birthDate": "1995-05-16T00:00:00Z",
      "timeZone": "Asia/Ho_Chi_Minh"
    },
    "staffProfile": null,
    "sessions": [
      {
        "id": "guid",
        "issuedAt": "2026-06-18T08:30:00Z",
        "expiredAt": "2026-06-25T08:30:00Z",
        "status": "Active",
        "ipAddress": "192.168.1.1",
        "userAgent": "Mozilla/5.0...",
        "deviceId": "ios-abc-123",
        "revokedAt": null,
        "revokedReason": null
      }
    ],
    "auditLogs": [
      {
        "id": "guid",
        "action": "LoginSuccess",
        "occurredAt": "2026-06-18T08:30:00Z",
        "isSuccess": true,
        "ipAddress": "192.168.1.1",
        "userAgent": "Mozilla/5.0...",
        "reason": null
      }
    ],
    "backupCodes": [
      {
        "id": "guid",
        "createdAt": "2026-06-01T00:00:00Z",
        "redeemedAt": null
      }
    ],
    "exportedAt": "2026-06-19T10:00:00Z",
    "format": "json",
    "version": "1.0"
  },
  "listErrors": null
}
```

**Field reference (`AccountDataExportDto`):**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.account` | `AccountSnapshot` | Không | Snapshot Account chính — xem bảng `AccountSnapshot` bên dưới |
| `data.profile` | `AccountProfileSnapshot?` | **Có** — null nếu user chưa từng cập nhật profile mở rộng | Profile mở rộng (avatar Google, timezone, …) |
| `data.staffProfile` | `StaffProfileSnapshot?` | **Có** — null nếu user không phải staff | Staff profile (employeeCode, department, tier) |
| `data.sessions` | `SessionSnapshot[]` | Không | **Tất cả** refresh token rows của account (cả active + revoked + expired). Sort `IssuedAt DESC`. |
| `data.auditLogs` | `AuditLogSnapshot[]` | Không | Tối đa **1000 row** audit log mới nhất nơi account là `TargetAccountId` HOẶC `ActorAccountId`. Sort `CreatedAt DESC`. **Giới hạn 1000 để response không vượt mức cho phép.** |
| `data.backupCodes` | `BackupCodeSnapshot[]` | Không | Backup codes 2FA (chỉ metadata id/createdAt/redeemedAt, **KHÔNG có code hash** vì security) |
| `data.exportedAt` | `DateTime` | Không | UTC timestamp lúc export |
| `data.format` | `string` | Không | Luôn `"json"` |
| `data.version` | `string` | Không | Version schema export — hiện tại `"1.0"` |

**Chi tiết `AccountSnapshot`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | AccountId |
| `email` | `string` | Không | Email |
| `phoneNumber` | `string?` | Null nếu chưa nhập | Phone |
| `fullName` | `string` | Không | Họ tên |
| `avatarUrl` | `string?` | Null nếu không có avatar legacy | Avatar URL trực tiếp lưu trên Account |
| `dateOfBirth` | `DateTime?` | Null nếu chưa nhập | Ngày sinh UTC |
| `address` | `string?` | Null nếu chưa nhập | Địa chỉ |
| `emailConfirmed` | `bool` | Không | Email đã verify chưa |
| `phoneConfirmed` | `bool` | Không | Phone đã verify chưa |
| `twoFactorEnabled` | `bool` | Không | 2FA đang bật không |
| `status` | `string` | Không | `AccountStatusEnum.ToString()` (vd `"Active"`, `"PendingVerification"`) — **không phải int** |
| `googleId` | `string?` | Null nếu chưa link Google | Google subject ID |
| `provider` | `string?` | Null nếu local register | Provider gốc (`"Google"`, `"Local"`) |
| `lastLoginAt` | `DateTime?` | Null nếu chưa từng login | Lần login cuối UTC |
| `lastLoginIp` | `string?` | Null nếu không capture | IP lần login cuối |
| `role` | `string` | Không | Role name (`"Admin"`, `"Manager"`, `"Staff"`, `"Customer"`) hoặc empty string nếu chưa gán |
| `createdAt` | `DateTime` | Không | Account tạo lúc nào UTC |

**Chi tiết `AccountProfileSnapshot`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `externalAvatarUrl` | `string?` | Null nếu không có Google avatar | URL avatar từ Google |
| `avatarSource` | `string?` | Null nếu chưa set | `AvatarSourceEnum.ToString()` (`"None"`, `"Uploaded"`, `"Google"`) |
| `address` | `string?` | Null nếu chưa nhập | Address ở profile mở rộng (có thể khác `account.address`) |
| `birthDate` | `DateTime?` | Null nếu chưa nhập | Birth date ở profile mở rộng |
| `timeZone` | `string?` | Null nếu chưa set | Timezone code (`"Asia/Ho_Chi_Minh"`) |

**Chi tiết `StaffProfileSnapshot`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `employeeCode` | `string?` | Null nếu chưa gán | Mã nhân viên |
| `department` | `string?` | Null nếu chưa gán | Phòng ban |
| `skillTier` | `string?` | Null nếu chưa gán | `StaffSkillTierEnum.ToString()` (`"Tier1"`, `"Tier2"`, `"Tier3"`) |
| `notes` | `string?` | Null nếu không có | Ghi chú nội bộ |

**Chi tiết `SessionSnapshot`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | RefreshToken Id (session id) |
| `issuedAt` | `DateTime` | Không | Lúc cấp token UTC |
| `expiredAt` | `DateTime` | Không | Lúc hết hạn token UTC |
| `status` | `string` | Không | `RefreshTokenStatus.ToString()` (`"Active"`, `"Used"`, `"Revoked"`, `"Expired"`, `"Compromised"`) |
| `ipAddress` | `string?` | Null nếu không capture | IP login |
| `userAgent` | `string?` | Null nếu không capture | User-Agent header |
| `deviceId` | `string?` | Null nếu client không gửi | Device ID |
| `revokedAt` | `DateTime?` | Null nếu chưa revoke | Lúc revoke UTC |
| `revokedReason` | `string?` | Null nếu chưa revoke | Lý do revoke |

**Chi tiết `AuditLogSnapshot`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | AuditLog Id |
| `action` | `string` | Không | `AuditActionEnum.ToString()` (vd `"LoginSuccess"`, `"PasswordChanged"`) — **không phải int** |
| `occurredAt` | `DateTime` | Không | Lúc audit log được ghi UTC |
| `isSuccess` | `bool` | Không | Action thành công không |
| `ipAddress` | `string?` | Null nếu không capture | IP |
| `userAgent` | `string?` | Null nếu không capture | User-Agent |
| `reason` | `string?` | Null nếu success | Lý do fail / metadata thêm |

**Chi tiết `BackupCodeSnapshot`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | BackupCode Id |
| `createdAt` | `DateTime` | Không | Lúc sinh code UTC |
| `redeemedAt` | `DateTime?` | Null nếu chưa dùng | Lúc user redeem code (single-use marker) |

> **Bảo mật:** Backup code **plain text** không được export (chỉ id + timestamp) — codes plain chỉ hiển thị **1 lần duy nhất** lúc sinh ở `POST /me/2fa/confirm` hoặc `POST /me/2fa/backup-codes/regenerate`. Tương tự, `PasswordHash`, `TwoFactorSecret`, `OtpCode`, `InvitationToken` **không xuất hiện** trong export — đây là design có chủ ý để export không thành attack vector nếu file bị leak.

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập (JWT thiếu/sai).
- `404` — Account trong JWT không tồn tại (hiếm — JWT valid nhưng account đã bị hard-delete).

> **Use case:** User submit yêu cầu GDPR data subject access request → FE call endpoint → browser auto-download JSON file → user lưu offline. AuditLog cap 1000 row để file không vượt size limit; nếu user cần full history, Admin export riêng qua endpoint khác (chưa có public).

---

### `GET /api/accounts/me/login-history`

**Mục đích:** Xem lịch sử đăng nhập của tài khoản hiện tại, có phân trang.

**Auth:** Bắt buộc (mọi role)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 20) | Số item mỗi trang |
| `result` | `LoginAttemptResult?` | Không | Lọc theo kết quả |
| `onlyFailed` | `bool?` | Không | Chỉ lấy lần thất bại |
| `fromUtc` | `DateTime?` | Không | Từ thời điểm (UTC) |
| `toUtc` | `DateTime?` | Không | Đến thời điểm (UTC) |
| `SortBy` | `string?` | Không | Cột sort server-side. Whitelist: `createdAt`, `result`, `method`, `ipAddress`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | Không (mặc định `desc`) | `asc` \| `desc`; giá trị lạ → `desc` |

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). `ipAddress` nullable. Chi tiết enum `LoginAttemptResult`: xem **Server-side Sort** đầu tài liệu.

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "items": [
      {
        "id": "...",
        "accountId": "guid",
        "attemptedEmail": "user@example.com",
        "result": 1,
        "resultName": "Success",
        "method": "Password",
        "ipAddress": "192.168.1.1",
        "userAgent": "Mozilla/5.0...",
        "deviceId": null,
        "note": null,
        "createdAt": "2026-05-16T08:00:00Z"
      }
    ],
    "totalItems": 42,
    "pageNumber": 1,
    "pageSize": 10,
    "totalPages": 5,
    "hasNextPage": true,
    "hasPreviousPage": false
  }
}
```

> **Pagination shape (`PaginationResponse<T>`):** `items`, `totalItems` (KHÔNG phải `totalCount`), `pageNumber`, `pageSize`, `totalPages` (computed = ceil(totalItems/pageSize)), `hasNextPage`, `hasPreviousPage`. Áp dụng cho mọi endpoint trả `PaginationResponse<T>` (login-history, accounts list, roles list, audit-logs...).

**Chi tiết `LoginAttemptDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID của login attempt |
| `accountId` | `Guid?` | Null nếu email không tồn tại | ID tài khoản |
| `attemptedEmail` | `string` | Không | Email đã submit |
| `result` | `LoginAttemptResult` | Không | Kết quả (xem enum) |
| `resultName` | `string` | Không | Tên kết quả dạng string |
| `method` | `string` | Không | Phương thức login. Giá trị thực tế từ code: `"Password"` (từ `POST /api/auth/login`), `"TOTP"` (từ `POST /api/auth/login/verify-2fa` với non-backup, **bao gồm cả SMS path** do code branch chung), `"BackupCode"` (từ verify-2fa với `isBackupCode=true`). Google OAuth callback và accept-invite **không publish** LoginAttempt → không xuất hiện trong list này. |
| `ipAddress` | `string?` | Null nếu không capture | IP address |
| `userAgent` | `string?` | Null nếu không có | User agent |
| `deviceId` | `string?` | Null nếu không gửi | Device ID từ client |
| `note` | `string?` | Null nếu không có | Ghi chú bổ sung |
| `createdAt` | `DateTime` | Không | Thời điểm xảy ra (UTC) |

---

### `GET /api/accounts/me/trusted-devices` — **(#AUTH-48)**

**Mục đích:** Liệt kê tất cả trusted device active của user hiện tại — để user xem + revoke device cụ thể qua Settings page.

**Auth:** Bắt buộc (mọi role)

**"Active" definition:** `RevokedAt IS NULL AND ExpiresAt > UtcNow AND IsDeleted = false`. Sắp xếp theo `TrustedAt DESC` (mới nhất lên đầu).

**Request:** Không có body / query param. Server đọc fingerprint từ HTTP context để đánh dấu `isCurrentDevice`.

**Headers gợi ý FE gửi:**
- `X-Device-Id` — Device id ổn định FE generate lần đầu, lưu localStorage/keychain
- `User-Agent` — auto-set bởi browser/native

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "",
  "data": [
    {
      "id": "8f3a5b9d-2c4e-4a1f-9b8d-1e2f3a4b5c6d",
      "label": "Chrome on macOS",
      "ipPrefix": "203.0.113.0/24",
      "userAgentSnapshot": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/...",
      "trustedAt": "2026-06-19T08:15:30Z",
      "expiresAt": "2026-07-19T08:15:30Z",
      "lastUsedAt": "2026-06-19T14:22:11Z",
      "usageCount": 7,
      "isCurrentDevice": true
    },
    {
      "id": "1a2b3c4d-...",
      "label": "Safari on iPhone",
      "ipPrefix": "203.0.113.0/24",
      "userAgentSnapshot": "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0...)",
      "trustedAt": "2026-06-15T10:00:00Z",
      "expiresAt": "2026-07-15T10:00:00Z",
      "lastUsedAt": null,
      "usageCount": 0,
      "isCurrentDevice": false
    }
  ],
  "listErrors": null
}
```

**Response shape:** `data` là `List<TrustedDeviceDto>` (có thể empty array nếu user chưa trust device nào). Xem `TrustedDeviceDto` ở section DTOs.

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập (JWT thiếu/sai/hết hạn)

---

### `DELETE /api/accounts/me/trusted-devices/{id}` — **(#AUTH-48)**

**Mục đích:** User revoke 1 trusted device cụ thể (vd device mất, đã bán). Sau revoke, device đó login lại sẽ phải pass 2FA challenge bình thường.

**Auth:** Bắt buộc (mọi role)

**Path param:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | Id row `TrustedDevice` cần revoke (lấy từ `GET /me/trusted-devices`) |

**Request body:** Không có.

**Behavior:** Soft-revoke — set `RevokedAt = UtcNow`, `RevokedReason = "User revoked"`. **KHÔNG hard-delete** row (giữ làm audit).

**Idempotent:** Gọi trên device đã revoke từ trước → vẫn trả 200 với message `"Thiết bị đã được thu hồi trước đó."` — KHÔNG báo lỗi.

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã thu hồi thiết bị 'Chrome on macOS'.",
  "data": "8f3a5b9d-2c4e-4a1f-9b8d-1e2f3a4b5c6d",
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `string (Guid)` | Id device vừa revoke (= path id) |

**Side effect khi success:**
- `TrustedDevice.RevokedAt = UtcNow`, `RevokedReason = "User revoked"`.
- Audit `TrustedDeviceRevoked=111` với metadata: `trustedDeviceId`, `label`.

**Lỗi thường gặp:**
- `400` — `id` không hợp lệ (Guid.Empty hoặc `AccountId = Guid.Empty` từ JWT)
- `401` — Chưa đăng nhập
- `404` — Device không tồn tại HOẶC không thuộc về user hiện tại (cùng response để tránh leak)

---

### `DELETE /api/accounts/me/trusted-devices` — **(#AUTH-48)**

**Mục đích:** User revoke **TẤT CẢ** trusted device của tài khoản hiện tại — dùng khi nghi ngờ account compromise hoặc muốn force-2FA toàn bộ.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có. Reason luôn fixed = `"User manual revoke-all"`.

**Idempotent:** Nếu account không có trusted device active → vẫn trả 200 với message `"Không có thiết bị tin cậy nào đang hoạt động."` — KHÔNG báo lỗi.

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã thu hồi 3 thiết bị tin cậy.",
  "data": null,
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `Guid?` | Luôn `null` (KHÔNG trả count qua data — count nằm trong `message`) |

**Side effect khi success:**
- Mỗi `TrustedDevice` active của account → set `RevokedAt = UtcNow`, `RevokedReason = "User manual revoke-all"`.
- Audit `TrustedDeviceAllRevoked=112` với metadata `revokedCount = <số>`, reason `"User manual revoke-all"`.

**Note — auto-revoke trong các flow khác:** Command này cũng được internal call (qua `IMediator`) trong các handler:
- `ChangePasswordCommandHandler` — sau đổi password (#AUTH-48 security best practice), reason `"Password changed"`.
- `Disable2FACommandHandler` — sau disable 2FA, reason `"2FA disabled"`.
Trong cả 2 case, audit `TrustedDeviceAllRevoked` được publish với reason tương ứng → cho phép forensic phân biệt manual revoke vs system-triggered.

**Lỗi thường gặp:**
- `400` — `AccountId = Guid.Empty` từ JWT (lỗi JWT format)
- `401` — Chưa đăng nhập

---

### `GET /api/accounts/me/profile`

> **Route trùng (tương thích):** Trả về cùng dữ liệu với canonical `GET /api/auth/me` (Nhóm 3). FE mới nên dùng `/api/auth/me`.

**Mục đích:** Đọc profile tổng hợp của user hiện tại.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `data` là `AccountDto` (giống `GET /api/auth/me`).

**Lỗi thường gặp:** `401` (chưa đăng nhập), `404` (account không tồn tại).

---

### `PUT /api/accounts/me/profile`

> **Route trùng (tương thích):** Cập nhật profile của chính user — alias của `PUT /api/accounts/{id}` nhưng không cần truyền id (AccountId resolve từ JWT). Canonical là `PUT /api/auth/me/profile` (Nhóm 3).

**Auth:** Bắt buộc (mọi role)

**Request body** (`UpdateAccountCommand`):

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `fullName` | `string` | Bắt buộc | Không rỗng, max 150 ký tự | Họ và tên |
| `phoneNumber` | `string?` | Tùy chọn | Max 20 ký tự | Số điện thoại |
| `avatarUrl` | `string?` | Tùy chọn | Max 500 ký tự | URL avatar direct (legacy — ưu tiên `POST /api/auth/me/avatar`) |
| `dateOfBirth` | `DateTime?` | Tùy chọn | Không ở tương lai | Ngày sinh |
| `address` | `string?` | Tùy chọn | Max 500 ký tự | Địa chỉ |

**Response thành công `200`:** `data` là accountId (Guid).

**Lưu ý:** Endpoint này KHÔNG đổi email/role/status. `timeZone` không có trong body này — nếu cần set timezone dùng `PUT /api/auth/me/profile` (Nhóm 3).

---

### `PUT /api/accounts/{id}`

> **Route trùng (tương thích) — owner-only:** Giống `PUT /api/accounts/me/profile` nhưng truyền `id` qua route. Backend bắt buộc `id` == userId trong JWT, nếu khác trả **`403`** (user A KHÔNG update được account B qua đây — admin override dùng `PUT /api/admin/accounts/{id}`).

**Auth:** Bắt buộc (mọi role, chỉ chính chủ)

**Path param:** `id` — phải khớp accountId trong JWT.

**Request body:** Giống `PUT /api/accounts/me/profile`.

**Response thành công `200`:** `data` là accountId (Guid).

**Lỗi thường gặp:** `400` (validation), `401` (chưa đăng nhập), `403` (id ≠ JWT userId), `404` (account không tồn tại).

---

## Nhóm 3 — Auth Profile & Staff Assignment

Base route self profile: `/api/auth`
Base route staff assignment read: `/api/staff`
Base route admin staff profile: `/api/admin/staff`
Header: `Authorization: Bearer {accessToken}`

**Lưu ý:** Các route `/api/auth-profiles/*` và `/api/staff-profiles/*` không phải route hiện tại trong controller. FE dùng các route bên dưới để tránh 404.

> **Phân biệt với Nhóm 2:** Nhóm 3 là **canonical route cho profile & avatar operations** (AuthProfilesController). Nhóm 2 (`/api/accounts`) dùng cho account management (password, email change, 2FA...). Dùng `GET /api/auth/me` để đọc profile tổng hợp — đây là route duy nhất, không có route profile thứ hai ở Nhóm 2.

---

### `GET /api/auth/me`

**Mục đích:** Lấy profile tổng hợp của tài khoản hiện tại.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `data` là `AccountDto`, gồm `profile`, `staffProfile` nếu có, và `displayAvatarUrl`.

> **2026-07-07:** `AccountDto` bổ sung field `isGoogleLinked` (bool) — FE màn Cài đặt dùng để toggle nút "Liên kết Google"/"Hủy liên kết" thay cho hardcode `isLinked={false}`.

---

### `PUT /api/auth/me/profile`

**Mục đích:** Cập nhật profile mở rộng của user hiện tại. Endpoint không dùng để đổi email, mật khẩu, role, status hoặc dữ liệu staff-specific.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `fullName` | `string` | Bắt buộc | Không rỗng, max 150 ký tự | Họ và tên |
| `phoneNumber` | `string?` | Tùy chọn | Max 20 ký tự | Số điện thoại |
| `address` | `string?` | Tùy chọn | Max 500 ký tự | Địa chỉ |
| `birthDate` | `DateTime?` | Tùy chọn | Không ở tương lai, năm >= 1900 | Ngày sinh |
| `timeZone` | `string?` | Tùy chọn | Max 100 ký tự | Timezone, ví dụ `Asia/Ho_Chi_Minh` |

**Response thành công `200`:** `data` là `AccountDto` mới sau khi cập nhật.

**Lỗi thường gặp:**
- `400` — Dữ liệu không hợp lệ
- `401` — Token không hợp lệ hoặc hết hạn
- `404` — Account trong token không tồn tại
- `409` — Phone hoặc dữ liệu có ràng buộc bị trùng theo rule nghiệp vụ

---

### `POST /api/auth/me/avatar`

**Mục đích:** Gắn avatar upload vào `AccountProfile`.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `avatarFileId` | `Guid` | Bắt buộc | Guid khác rỗng | FileId từ FileStorageService |

**Response thành công `200`:** `data` là `AccountDto` mới, FE render avatar bằng `displayAvatarUrl`.

---

### `GET /api/auth/me/permissions`

**Mục đích:** Lấy toàn bộ permission của **role mà tài khoản hiện tại đang được gán**. Endpoint này được thiết kế để FE build feature-gate (ẩn/hiện button, route, menu) sau khi login mà không cần phải decode JWT thủ công.

**Tác dụng & khi nào dùng:**

- Sau khi login thành công → FE gọi 1 lần để cache `Permissions` vào client state (Zustand/TanStack Query) → dùng cho mọi check `checkPermission(user, P.X)` toàn app.
- Khi cần verify FE migration: đảm bảo `P.*` constants trong code khớp với DB.
- Khi admin vừa thay đổi permission của role → FE re-fetch endpoint này (thay vì đợi refresh token) để áp permission mới ngay lập tức.
- Khác với catalog endpoints — `GET /api/permissions` (public, mọi role) và `GET /api/admin/permissions` (Admin-only) đều trả **catalog** tất cả permission trong hệ thống; endpoint này trả **subset** thuộc về role của user hiện tại.

**Auth:** Bắt buộc (mọi role — Admin / Manager / Staff / Customer đều gọi được, chỉ cần JWT hợp lệ)

**Headers:**

```
Authorization: Bearer {accessToken}
```

**Request:** Không có body, không có query param. `AccountId` được server tự đọc từ claim `NameIdentifier` trong JWT (client không thể override).

**Ghi chú implementation (quan trọng):**

- Server **resolve qua DB** (`Account → Role → RolePermission → Permission`), **KHÔNG đọc** mảng `perm[]` đã embed trong JWT. Lý do: trả về snapshot mới nhất, phản ánh ngay khi admin sửa role/permission mà không cần đợi user refresh token.
- Filter `!IsDeleted` ở cả 4 bảng (Account, Role, RolePermission, Permission).
- Distinct theo `Permission.Id`, sort theo `Module` rồi `Code`.
- Không cache phía server (latency 1 query JOIN nhỏ, FE đã cache phía client).

**Response thành công `200`:** `data` là `MyPermissionsDto`.

**Chi tiết `MyPermissionsDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `roleId` | `Guid` | Không | Guid role mà account đang được gán |
| `roleName` | `string` | Không | Tên role (`"Admin"` / `"Manager"` / `"Staff"` / `"Customer"`) |
| `permissions` | `PermissionDto[]` | Không (có thể rỗng) | Flat list permission, sort theo `module` → `code`. Rỗng nếu role không active hoặc chưa được gán permission |

**Chi tiết `PermissionDto`** (cùng shape với `GET /api/admin/permissions`):

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | Guid identifier |
| `code` | `string` | Không | Dot-separated key (vd `"ticket.view"`, `"alert.acknowledge"`) — match với `P.*` constant ở FE |
| `module` | `string` | Không | Namespace logic (`"Ticket"` / `"Alert"` / `"Battery"` / `"Account"` / `"Admin"`) — FE có thể group accordion theo field này |
| `description` | `string?` | Có thể null | Mô tả tiếng Việt dùng cho UI admin gán permission |
| `isSystemPermission` | `bool` | Không | `true` = permission hệ thống tạo (không cho admin xóa) |
| `createdAt` | `DateTime` | Không | Thời điểm permission được seed/tạo |

**Ví dụ JSON response (happy path — role `Staff` có 5 permission):**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "",
  "data": {
    "roleId": "3e3a9c8f-2b1a-4d5e-8c7f-9b0a1d2e3f40",
    "roleName": "Staff",
    "permissions": [
      {
        "id": "11111111-1111-1111-1111-111111111111",
        "code": "alert.acknowledge",
        "module": "Alert",
        "description": "Acknowledge cảnh báo từ pin",
        "isSystemPermission": true,
        "createdAt": "2026-05-11T14:05:53.000Z"
      },
      {
        "id": "22222222-2222-2222-2222-222222222222",
        "code": "battery.view",
        "module": "Battery",
        "description": "Xem chi tiết pin và sensor reading",
        "isSystemPermission": true,
        "createdAt": "2026-05-11T14:05:53.000Z"
      },
      {
        "id": "33333333-3333-3333-3333-333333333333",
        "code": "ticket.view",
        "module": "Ticket",
        "description": "Xem danh sách ticket được assign",
        "isSystemPermission": true,
        "createdAt": "2026-05-11T14:05:53.000Z"
      },
      {
        "id": "44444444-4444-4444-4444-444444444444",
        "code": "ticket.update",
        "module": "Ticket",
        "description": "Cập nhật trạng thái ticket trong scope assigned",
        "isSystemPermission": true,
        "createdAt": "2026-05-11T14:05:53.000Z"
      },
      {
        "id": "55555555-5555-5555-5555-555555555555",
        "code": "ticket.comment",
        "module": "Ticket",
        "description": "Bình luận/log maintenance trên ticket",
        "isSystemPermission": true,
        "createdAt": "2026-05-11T14:05:53.000Z"
      }
    ],
    "listErrors": null
  }
}
```

**Ví dụ JSON response (role không active — `200` nhưng `permissions = []`):**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Role hiện không hoạt động — không có permission nào được áp dụng.",
  "data": {
    "roleId": "3e3a9c8f-2b1a-4d5e-8c7f-9b0a1d2e3f40",
    "roleName": "Staff",
    "permissions": []
  },
  "listErrors": null
}
```

**Lỗi thường gặp:**

| HTTP | `message` | Tình huống |
|------|-----------|-----------|
| `401` | `"Chưa đăng nhập."` | Header `Authorization` thiếu / sai format / không decode được claim `NameIdentifier` |
| `401` | `"Token không chứa AccountId hợp lệ."` | Defensive — JWT thiếu/sai format `AccountId` (không xảy ra với token do AuthService issue) |
| `401` | `"Tài khoản không tồn tại hoặc đã bị xóa."` | Account đã bị admin xóa nhưng JWT chưa hết hạn |
| `403` | `"Tài khoản chưa được gán role."` | Account tồn tại nhưng `RoleId` orphan (role đã bị xóa) hoặc chưa từng gán role nào |

**Ví dụ JSON response (`403`):**

```json
{
  "isSuccess": false,
  "statusCode": 403,
  "message": "Tài khoản chưa được gán role.",
  "data": null,
  "listErrors": null
}
```

**Khác biệt với các endpoint permission khác:**

| Endpoint | Auth | Scope | Use case |
|----------|------|-------|----------|
| `GET /api/auth/me/permissions` | Mọi role có JWT | Permission của role **user hiện tại** | FE feature-gate sau login |
| `GET /api/admin/permissions` | Admin only | **Catalog** toàn bộ permission trong hệ thống (có `?module=` filter) | Admin dashboard render dropdown khi tạo/edit role |
| `GET /api/admin/roles/{roleId}/permissions` | Admin only | Permission của 1 role bất kỳ (`roleId` truyền từ URL) | Admin edit role — load list current permissions để pre-check checkbox |

---

### `GET /api/staff`

**Mục đích:** Admin/Manager lấy danh sách staff phục vụ màn hình phân công ticket.

**Auth:** Bắt buộc (Role Admin hoặc Manager)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `skill` | `string?` | Không | Lọc staff có skill code tương ứng |

**Response thành công `200`:** `List<StaffAssignmentProfileDto>`.

**Chi tiết `StaffAssignmentProfileDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `accountId` | `Guid` | Không | ID tài khoản staff |
| `email` | `string` | Không | Email staff |
| `fullName` | `string` | Không | Họ tên staff |
| `phoneNumber` | `string?` | Null nếu không có | Số điện thoại |
| `department` | `string?` | Null nếu chưa gán | Phòng ban |
| `maxConcurrentTickets` | `int` | Không | Số ticket tối đa |
| `isAvailable` | `bool` | Không | Đang sẵn sàng không |
| `displayAvatarUrl` | `string?` | Null nếu không có avatar | URL avatar FE nên render |
| `skills` | `StaffSkillDto[]` | Không | Danh sách kỹ năng |

---

### `GET /api/staff/{id}/assignment-profile`

**Mục đích:** Admin/Manager xem hồ sơ phân công chi tiết của một staff.

**Auth:** Bắt buộc (Role Admin hoặc Manager)

**Path param:** `id` — AccountId của staff.

**Response thành công `200`:** `data` là `StaffAssignmentProfileDto` — cùng shape với từng item trong `GET /api/staff`.

**Chi tiết `StaffAssignmentProfileDto`:** Xem bảng field tại [GET /api/staff](#get-apistaff).

**Lỗi thường gặp:**
- `400` — `id` không hợp lệ (không phải Guid)
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin/Manager; hoặc Staff không được xem profile của Staff khác
- `404` — Account không tồn tại hoặc không có staff profile tương ứng

---

## Nhóm 4 — Quản lý Session

Base route: `/api/sessions`
Header: `Authorization: Bearer {accessToken}`

**Session limit (enforcement chi tiết):**

Mỗi lần issue access token + refresh token (cả 4 entry: `/login`, `/login/verify-2fa`, `/accept-invite`, `/google/callback`), handler publish `SessionCreatedNotification` → `SessionCreatedNotificationHandler` (xem code path):

1. Đọc `Session:MaxConcurrentSessions` config (default 5). Nếu ≤ 0 → tắt limit, return ngay.
2. Đếm `RefreshToken` với `Status = Active` đã persist của account (chưa tính row mới — row mới đã trong DbContext ChangeTracker nhưng chưa SaveChanges).
3. Tính `totalAfterCommit = persistedActive + 1`. Nếu ≤ `maxAllowed` → return.
4. **Excess = totalAfterCommit - maxAllowed**. Sort `persistedActive` theo `IssuedAt` **ASC** (cũ nhất trước) → revoke `Take(excess)`:
   - `Status = Revoked`, `RevokedAt = UtcNow`, `RevokedReason = "Concurrent session limit (N) exceeded — revoked oldest session."`
5. Publish audit `SessionLimitExceededOldestRevoked` (SUCCESS) với metadata `revokedCount`, `maxAllowed`, `revokedSessionIds: [...]`.

> **Best-effort**: handler bọc trong try-catch + log error. Nếu enforce fail (vd DB lỗi), handler **không throw** — login vẫn success, chỉ là limit không enforce lần đó. FE/QA test session limit phải đảm bảo log không có `LogError("Failed to enforce session limit...")`.

> **FIFO semantics**: session cũ nhất theo `IssuedAt` bị revoke trước. Trong rotation chain (#AUTH-28), `IssuedAt` của row mới là `now` (không phải `OriginalIssuedAt`), nên rotation thường xuyên giúp session "trẻ" được giữ.

**Lỗi thường gặp cho nhóm này:**
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Session không thuộc account hiện tại
- `404` — Không tìm thấy session
- `200 isSuccess=false` — Session đã không còn active hoặc không có session nào cần revoke

---

### `GET /api/sessions/me`

**Mục đích:** Lấy danh sách session (refresh token) hiện tại của tài khoản đang đăng nhập.

**Auth:** Bắt buộc (mọi role)

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `activeOnly` | `bool` | Mặc định `true` — chỉ lấy session còn Active |
| `deviceId` | `string?` | (#AUTH-44) Lọc session theo `DeviceId` cụ thể. Bỏ trống = lấy mọi device. Dùng cho UI 'Manage devices' khi user muốn xem riêng 1 thiết bị. |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": [
    {
      "id": "guid",
      "issuedAt": "2026-05-16T00:00:00Z",
      "expiredAt": "2026-05-23T00:00:00Z",
      "status": 1,
      "ipAddress": "192.168.1.1",
      "userAgent": "Mozilla/5.0...",
      "deviceId": null,
      "revokedAt": null,
      "revokedReason": null,
      "isCurrent": true
    }
  ]
}
```

**Chi tiết `SessionDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID refresh token (session ID) |
| `issuedAt` | `DateTime` | Không | Thời điểm cấp token (UTC) |
| `expiredAt` | `DateTime` | Không | Thời điểm hết hạn token (UTC) |
| `status` | `RefreshTokenStatus` | Không | Trạng thái token (xem enum) |
| `ipAddress` | `string?` | Null nếu không capture | IP address khi login |
| `userAgent` | `string?` | Null nếu không có | User agent khi login |
| `deviceId` | `string?` | Null nếu không gửi | Device ID từ client |
| `revokedAt` | `DateTime?` | Null nếu chưa revoke | Thời điểm thu hồi |
| `revokedReason` | `string?` | Null nếu chưa revoke | Lý do thu hồi |
| `isCurrent` | `bool` | Không | Đây có phải session hiện tại không |

---

### `DELETE /api/sessions/{sessionId}`

**Mục đích:** Thu hồi một session cụ thể (đăng xuất khỏi thiết bị đó).

**Auth:** Bắt buộc (mọi role)

**Path param:** `sessionId` — Guid của session cần thu hồi

**Response thành công `200`:**
- Active session revoked: `data = 1`, message *"Thu hồi session thành công."*
- **Idempotent**: session đã không Active → `data = 0`, message *"Session đã không còn hiệu lực."* (vẫn `isSuccess = true`)

**Lưu ý bảo mật:** Backend kiểm tra `sessionId` phải thuộc account hiện tại. Nếu session thuộc account khác, API trả `403 Forbidden` với message *"Không có quyền thu hồi session này."*

**Lỗi thường gặp:**
- 401: JWT thiếu/sai (message *"Chưa đăng nhập."*)
- 403: Session thuộc account khác
- 404: Session không tồn tại (message *"Không tìm thấy session."*)

> ⚠️ **GAP quan trọng (asymmetry với `/logout` và admin force-logout):**
> Endpoint này **CHỈ revoke refresh token** với reason `"User revoked"`. **KHÔNG**:
> - Bulk blacklist access tokens (#AUTH-54) — access token của device đó vẫn dùng được tới ~1h.
> - Invalidate pending 2FA challenge (#AUTH-08).
> - Publish audit log (`SessionRevoked` enum value 80 **không** được publish ở endpoint này, dù enum tồn tại).
>
> FE muốn user "đăng xuất ngay" khỏi 1 device → kết hợp `/auth/logout` (kill refresh + bulk blacklist access) hoặc force user gọi `/auth/revoke` riêng cho access token. Hoặc dùng endpoint admin `POST /api/admin/accounts/{id}/sessions/revoke-all` (Admin có #AUTH-54 + audit).

---

### `POST /api/sessions/revoke-all`

**Mục đích:** Thu hồi tất cả session, có thể giữ lại session hiện tại ("Đăng xuất tất cả thiết bị").

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Mô tả |
|---|---|---|
| `exceptCurrent` | `bool` | Mặc định `true` — giữ session hiện tại, chỉ logout các thiết bị khác |
| `currentRefreshToken` | `string?` | Refresh token hiện tại (dùng khi `exceptCurrent = true`). DB chỉ lưu hash, server tự `RefreshTokenHasher.Hash(plaintext)` để so sánh `Token != currentHash` (#AUTH-01). |

**Response thành công `200`:** `data = sessions.Count` (số session đã revoke), message *"Đã thu hồi N session."*

Reason ghi vào mỗi refresh token revoked: `"Revoke all sessions"`.

> ⚠️ **GAP nghiêm trọng — cùng vấn đề với `DELETE /sessions/{id}`:**
> User click "Đăng xuất tất cả thiết bị" tưởng rằng moi session bị kill ngay, **NHƯNG access token còn hạn của các device khác vẫn dùng được tối đa 1h** vì handler **KHÔNG** bulk blacklist access tokens qua `_revocationStore.RevokeAllByAccountAsync(...)`.
> So sánh:
>
> | Endpoint | Refresh revoke | Access token blacklist | 2FA challenge clear | Audit log |
> |---|---|---|---|---|
> | `POST /api/auth/logout` | ✅ 1 token | ✅ ALL account | ✅ | ❌ |
> | `DELETE /api/sessions/{id}` | ✅ 1 token | ❌ **gap** | ❌ **gap** | ❌ **gap** |
> | `POST /api/sessions/revoke-all` | ✅ multi | ❌ **gap** | ❌ **gap** | ❌ **gap** |
> | `POST /api/admin/accounts/{id}/sessions/revoke-all` | ✅ multi | ✅ ALL account (#AUTH-54) | ❌ | ✅ AdminForceLogout |
>
> **FE workaround**: sau khi gọi `/sessions/revoke-all`, gọi thêm `/auth/logout` với current refresh token để force #AUTH-54 bulk blacklist (vì `/auth/logout` route đó đi qua handler có blacklist). Hoặc inform user "thiết bị khác có thể dùng tới 1 giờ".

**Audit log gap:** `AllSessionsRevoked` enum value 81 **không** được publish ở user-facing endpoint này (chỉ publish ở admin force-logout path). FE/QA test audit không thấy entry → là expected behavior, không phải bug.

---

## Nhóm 5 — Admin: Quản lý Tài khoản

Base route: `/api/admin/accounts`
**Auth:** Bắt buộc (Role Admin hoặc Manager, tùy endpoint)

**Lỗi thường gặp cho nhóm này:**
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không đủ quyền theo role của endpoint
- `404` — Không tìm thấy account, role hoặc session
- `409` — Email/phone/unique field bị trùng hoặc trạng thái nghiệp vụ xung đột
- `200 isSuccess=false` — Thao tác hợp lệ về HTTP nhưng không thay đổi dữ liệu theo rule nghiệp vụ

---

### `GET /api/admin/accounts`

**Mục đích:** Danh sách tài khoản với phân trang và lọc nâng cao.

**Auth:** Admin hoặc Manager

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang, mặc định 1 |
| `pageSize` | `int` | Số item/trang, mặc định 10 |
| `keyword` | `string?` | Tìm theo email hoặc tên |
| `status` | `AccountStatusEnum?` | Lọc theo trạng thái |
| `roleId` | `Guid?` | Lọc account đang có role cụ thể |
| `emailConfirmed` | `bool?` | Lọc theo xác thực email |
| `SortBy` | `string?` | Cột sort server-side. Whitelist: `fullName`, `role`, `status`, `createdAt`. Ngoài whitelist → `createdAt` |
| `SortDir` | `string?` | `asc` \| `desc` (mặc định `desc`; giá trị lạ → `desc`) |

> **Sắp xếp:** mặc định `createdAt` desc. Đã hỗ trợ sort server-side qua `SortBy`/`SortDir` (order toàn dataset trước phân trang, tie-breaker `Id ASC`). Chi tiết enum `AccountStatusEnum` + nullable: xem **Server-side Sort** đầu tài liệu.

**Response:** `PaginationResponse<AccountDto>`

**Lưu ý:** Mỗi `AccountDto` trong list **bao gồm đầy đủ** `profile` (AccountProfileDto) và `staffProfile` (StaffProfileDto nếu là Staff) — được eager load bằng `.Include()`, không có N+1 query. FE có thể render avatar, department, skills ngay từ list response mà không cần gọi thêm `/api/admin/accounts/{id}`.

---

### `GET /api/admin/accounts/stats`

**Mục đích:** Snapshot thống kê account — donut "Người dùng theo vai trò" trên Dashboard Admin. Thay cho việc FE tự đếm role trên 1 trang list (cap 200 → sai số khi vượt pageSize).

**Auth:** Admin hoặc Manager

**Query params:** Không có — endpoint snapshot, **không nhận from/to**. FE nên cache ~1 phút (staleTime).

**Response thành công `200`:** `CommonResponse<AccountStatsDto>`

```json
{
  "isSuccess": true, "statusCode": 200, "message": "",
  "data": {
    "total": 87,
    "countByRole": { "Admin": 2, "Manager": 4, "Staff": 18, "Customer": 62 }
  },
  "listErrors": null
}
```

**Chi tiết `AccountStatsDto`** — mọi field đều **không null** (`data` chỉ null khi lỗi):

| Field | Type | Mô tả |
|---|---|---|
| `total` | `int` | Tổng account chưa xóa mềm (mọi status, kể cả PendingVerification/Locked) |
| `countByRole` | `Dictionary<string,int>` | Số account theo **tên role** — key lấy từ bảng Roles (`"Admin"`, `"Manager"`, `"Staff"`, `"Customer"`), **zero-fill đủ mọi role đang tồn tại** trong hệ thống (role chưa có account = `0`) |

> **Lưu ý:** account **chưa gán role** (`roleId = null`) chỉ được tính vào `total`, không xuất hiện trong `countByRole` — vì vậy tổng các value của `countByRole` có thể **nhỏ hơn** `total`. FE vẽ donut theo `countByRole`; nếu muốn hiện phần "chưa gán role" thì tính `total − sum(countByRole)`.

**Lỗi thường gặp:**
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin/Manager

---

### `GET /api/admin/accounts/{id}`

**Mục đích:** Xem chi tiết một tài khoản.

**Auth:** Admin hoặc Manager

**Path param:** `id` — Guid của tài khoản

**Response thành công `200`:** `data` là `AccountDto` đầy đủ (cùng shape với `GET /api/accounts/me`), bao gồm `profile`, `staffProfile` nếu có, và `displayAvatarUrl`.

**Lỗi thường gặp:**
- `400` — `id` không hợp lệ (không phải Guid)
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin/Manager
- `404` — Không tìm thấy account với `id` đó

---

### `POST /api/admin/accounts`

**Mục đích:** Admin tạo account mới (không invite, set password ngay).

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Max 256, đúng định dạng | Email tài khoản |
| `fullName` | `string` | Bắt buộc | Max 150 ký tự | Họ tên |
| `password` | `string` | Bắt buộc | 8–100 ký tự, mạnh | Mật khẩu ban đầu |
| `phoneNumber` | `string?` | Không | Max 20 ký tự | Số điện thoại |
| `dateOfBirth` | `DateTime?` | Không | — | Ngày sinh |
| `address` | `string?` | Không | Max 500 ký tự | Địa chỉ |
| `roleId` | `Guid` | Bắt buộc | Role đang Active | Role gán cho account (mỗi account chỉ có 1 role — quan hệ 1-N) |

**Response thành công `201`:** `CommonResponse<Guid>` — `data` là Guid của account vừa tạo.

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Lưu ý:** FE invalidate `KEY.admin.accounts` sau khi tạo thành công để list tự refetch; không cần re-fetch `AccountDto` từ response này.

**Lỗi thường gặp:**
- `400` — Field validation (email/password/fullName sai, `roleId = Guid.Empty`)
- `404` — `roleId` không tồn tại hoặc role đang bị disable (không phải 400)
- `409` — Email đã tồn tại

---

### `POST /api/admin/accounts/invite`

**Mục đích:** Admin mời user qua email. Hệ thống tạo account với trạng thái `PendingVerification` và gửi email mời chứa invitation token.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email cần mời |
| `fullName` | `string` | Bắt buộc | Họ tên |
| `phoneNumber` | `string?` | Không | Số điện thoại |
| `roleId` | `Guid` | Bắt buộc | Role gán cho user khi accept invite (1 role/account — quan hệ 1-N) |

**Response thành công `201`:** `AccountActionResponse` (= `CommonResponse<Guid>`) — `data` là Guid của account vừa tạo (trạng thái `PendingVerification`).

```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Đã gửi email invite. User cần accept để kích hoạt tài khoản.",
  "data": "ab67cb7c-e960-4d2d-ac45-bc1393581ca6",
  "listErrors": null
}
```

**Luồng:** Sau khi invite, user nhận email chứa link với `invitationToken`. User truy cập link và gọi `POST /api/auth/accept-invite` để đặt mật khẩu và kích hoạt.

**Lỗi thường gặp:**
- `400` — Field validation (email/fullName sai, `roleId = Guid.Empty`)
- `404` — `roleId` không tồn tại hoặc role đang bị disable (không phải 400)
- `409` — Email đã tồn tại

**Luồng gửi email:**
- AuthService tạo account `PendingVerification`, sinh `invitationToken` TTL **72 giờ**, ghi `SendAdminInviteEvent` vào outbox và commit cùng account.
- `OutboxRelayBackgroundService` publish event lên RabbitMQ.
- EmailService consumer `SendAdminInviteConsumer` nhận event và gửi email qua MailJet bằng template `AdminInvite.html`.
- Link trong email được build từ config `AdminInvite:AcceptUrlBase` hoặc `Frontend:AcceptInviteUrl`, sau đó append `?token={invitationToken}`. K8s Helm đang set mặc định `https://{global.domain}/auth/accept-invite`.

**Troubleshooting nếu invite trả `201` nhưng không có email:**
- Kiểm tra AuthService outbox: event `SendAdminInviteEvent` phải có `processed_at != null`; nếu còn pending hoặc `last_error` có lỗi thì kiểm tra RabbitMQ/outbox relay.
- Kiểm tra EmailService có queue/consumer `SendAdminInviteConsumer`; nếu consumer không chạy, event sẽ không được gửi MailJet.
- Kiểm tra cấu hình `MailJet:ApiKey`, `MailJet:ApiSecret`, `MailJet:FromEmail`, `RabbitMQ:*`, `Inbox:*`.

---

### `PUT /api/admin/accounts/{id}`

**Mục đích:** Admin cập nhật thông tin tài khoản.

**Auth:** Admin (chỉ Admin — không phải Manager)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `fullName` | `string` | Bắt buộc | Không rỗng, max 150 ký tự | Họ và tên |
| `phoneNumber` | `string?` | Tùy chọn | Max 20 ký tự | Số điện thoại |
| `avatarUrl` | `string?` | Tùy chọn | Max 500 ký tự | URL avatar legacy/direct — **xem lưu ý bên dưới** |
| `dateOfBirth` | `DateTime?` | Tùy chọn | Không ở tương lai | Ngày sinh |
| `address` | `string?` | Tùy chọn | Max 500 ký tự | Địa chỉ |

**Response thành công `200`:** `CommonResponse<Guid>` — `data` là Guid của account vừa update.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Lưu ý:** Endpoint này chỉ sửa profile/account fields ở trên. Admin không sửa role/status/emailConfirmed trong body này; role dùng endpoint `/roles`, status dùng `PATCH /status`, session dùng `/sessions/*`. FE invalidate `QUERY_KEY.admin.accounts.detail(id)` và `KEY.admin.accounts` sau mutation.

**Lưu ý `avatarUrl` (legacy field — deprecated):** Field này cho phép Admin set avatar bằng direct URL mà không cần upload qua FileStorageService. **Sẽ bị xóa khỏi endpoint này sau khi FileStorageService integration hoàn thành** (dự kiến Sprint 5). Với flow mới, ưu tiên dùng `POST /api/auth/me/avatar` + `avatarFileId`. FE luôn render avatar bằng `displayAvatarUrl` từ `AccountDto`, không dùng `avatarUrl` trực tiếp để hiển thị.

---

### `PATCH /api/admin/accounts/{id}/status`

**Mục đích:** Thay đổi trạng thái tài khoản (activate, lock, suspend, ban...).

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `status` | `AccountStatusEnum` | Bắt buộc | Trạng thái mới |
| `reason` | `string?` | Không | Lý do thay đổi (ghi vào audit log) |

**Status transition hiện tại:** Backend **không enforce transition matrix** — cho phép chuyển giữa bất kỳ 2 giá trị enum hợp lệ nào (vd `Banned → Active` được phép). Nếu cần ràng buộc chặt hơn cho production, bổ sung rule ở `ChangeAccountStatusCommandHandler`.

**Behavior chi tiết theo new status (verified với handler code):**

| Target status | LockoutEndAt | FailedLoginAttempts | Refresh tokens active |
|---|---|---|---|
| `Active` (1) | Clear → null | Reset → 0 | **Giữ nguyên** (không revoke) |
| `PendingVerification` (0) | Clear → null | — | **Giữ nguyên** |
| `Locked` (2) | — (giữ giá trị hiện tại) | — | **Revoke toàn bộ** với reason `"Account status changed to Locked. {Reason}"` |
| `Inactive` (3) | Clear → null | — | **Revoke toàn bộ** với reason `"Account status changed to Inactive. {Reason}"` |
| `Suspended` (4) | Clear → null | — | **Revoke toàn bộ** với reason `"Account status changed to Suspended. {Reason}"` |
| `Banned` (5) | Clear → null | — | **Revoke toàn bộ** với reason `"Account status changed to Banned. {Reason}"` |

**Idempotent:** Nếu `request.Status == account.Status` (không thay đổi gì) → trả `200` ngay với message `"Trạng thái không thay đổi."`, **KHÔNG revoke session, KHÔNG audit log**. FE dùng để check current status mà không cần lo về side effect.

**Audit metadata:** `AccountStatusChanged` event publish với `oldStatus` (int), `oldStatusName` (string), `newStatus` (int), `newStatusName` (string), `revokedSessions` (count).

**Lưu ý**: KHÔNG bulk revoke access token qua `_revocationStore` — chỉ revoke refresh tokens. Tức là user bị Banned vẫn dùng được access token cũ cho đến khi expire (max 1h). Đây là gap nếu cần immediate lockout cho banned account → bổ sung handler call `RevokeAllByAccountAsync` tương tự `ResetPassword`/`ChangePassword`.

---

### `POST /api/admin/accounts/{id}/unlock`

**Mục đích:** Mở khóa tài khoản đang bị khóa (trạng thái `Locked`).

**Auth:** Admin hoặc Manager

**Response thành công `200`:** `isSuccess = true`, message *"Đã unlock tài khoản."*, `data = accountId`. Backend reset `FailedLoginAttempts = 0`, `LockoutEndAt = null`, account chuyển từ `Locked → Active`. Audit `AccountUnlocked` với metadata `wasLocked`, `previousFailedAttempts`.

**Idempotent (verified với code `UnlockAccountCommandHandler.cs:44-50`):**

Nếu account không ở `Status = Locked` (vd đã `Active`, `Suspended`, `Inactive`) → trả **`200` với `isSuccess = FALSE`**, message *"Tài khoản không ở trạng thái Locked, không cần unlock."* (HTTP status vẫn 200, nhưng `isSuccess=false` báo no-op). Khi unlock thực sự xảy ra → `isSuccess = true`, message *"Đã unlock tài khoản."*, `data = accountId`. FE/QA test:
- `if (!response.isSuccess && response.statusCode === 200)` → idempotent no-op (account không Locked)
- `if (response.isSuccess && response.data)` → actual unlock happened, `data = accountId`

**Lỗi thường gặp:**
- `400` — `id` không phải Guid hợp lệ
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin/Manager
- `404` — Không tìm thấy account

---

### `DELETE /api/admin/accounts/{id}`

**Mục đích:** Xóa mềm tài khoản (soft delete). Đặt `IsDeleted = true`; account không thể đăng nhập sau đó.

**Auth:** Admin

**Response thành công `200`:** `isSuccess = true`. Đồng thời toàn bộ refresh token của account bị revoke.

**Lỗi thường gặp:**
- `400` — `id` không hợp lệ
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin
- `404` — Không tìm thấy account
- `409` — *(Planned)* Không thể xóa account đang có ticket ở trạng thái active (`OPEN`, `ASSIGNED`, `IN_PROGRESS`, `ESCALATED`) — business rule này dự kiến implement cùng TicketService integration; hiện tại backend chưa enforce.

---

### `DELETE /api/admin/accounts/{id}/2fa` — **(GH-295)**

**Mục đích:** Admin reset 2FA của user khác — clear secret + xóa toàn bộ backup codes + set `TwoFactorEnabled=false`. Dùng cho case user mất hoàn toàn device + hết backup codes, không thể self-recovery.

**Auth:** **Admin** (chỉ Admin role, không phải Manager)

**Path param:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | AccountId của user cần reset 2FA |

**Request body:** Không có

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã reset 2FA cho tài khoản. User phải enroll lại nếu muốn dùng 2FA.",
  "data": "8f3a5b9d-...",
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `string (Guid)` | AccountId vừa reset (= target id từ route) |

**Idempotent:** Gọi trên account chưa bật 2FA cũng trả `200` với message `"Tài khoản vốn chưa bật 2FA. Đã clear sạch dữ liệu liên quan để chắc chắn."`

**Side effect khi success:**
- `Account.TwoFactorSecret = null`, `TwoFactorEnabled = false`, `TwoFactorSecretEncryptedAt = null`
- Xóa toàn bộ `backup_codes` rows của target account (soft delete)
- Audit log `Admin2FAReset`: `ActorAccountId = admin`, `TargetAccountId = user`, `Reason` ghi rõ pre-state, `Metadata` chứa `wasEnabled`, `backupCodesCleared`

**Lỗi thường gặp:**
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin (Manager/Staff/Customer đều bị chặn)
- `404` — Không tìm thấy target account

> **Use case operational:** User báo support mất hoàn toàn device + hết backup codes → Admin verify danh tính qua channel khác (email/phone) → gọi endpoint này → notify user enroll lại. **Không có cách self-recovery** — đây là design có chủ ý để 2FA thực sự là factor thứ hai an toàn.

---

### `PUT /api/admin/accounts/{id}/role`

**Mục đích:** Đổi role hiện tại của account sang role khác.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `roleId` | `Guid` | Bắt buộc | Role mới sẽ thay role hiện tại; role phải đang Active |

**Lưu ý 1-N:** Quan hệ Role ↔ Account là **1-N** — mỗi account có duy nhất 1 role tại bất kỳ thời điểm nào.
- Vì account bắt buộc có 1 role, **KHÔNG có endpoint revoke role**; để "thu hồi quyền", Admin đổi sang role thấp hơn (vd Customer).
- Tính năng **temporary role** (ExpiredAt) và **multi-role per account** đã được loại bỏ kể từ refactor 1-N.
- Nếu `roleId` mới trùng `roleId` hiện tại → response `200 OK` nhưng không phát sinh thay đổi (idempotent).
- Audit log `RoleAssigned` được ghi với metadata `previousRoleId`, `newRoleId`, `newRoleName`.

**Response thành công `200`:** `isSuccess = true`, `data = accountId`.

**Lỗi thường gặp:**
- `400` — `roleId` không hợp lệ, role không tồn tại hoặc role bị disable
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin
- `404` — Không tìm thấy account HOẶC **role không tồn tại / không Active** (verified với code — không phải 400 như docs sớm ghi). Message: `"Role không tồn tại hoặc đã bị vô hiệu hóa."`

**Status code map (verified với handler):**

| Status | Trường hợp | Message |
|---|---|---|
| 200 | Thay đổi role thành công | `"Đã đổi role sang {role.Name}."` |
| 200 | RoleId mới == RoleId hiện tại (idempotent) | `"Role không thay đổi."` (KHÔNG audit, KHÔNG update fields) |
| 400 | Field validation đơn (`AccountId = Guid.Empty`, `RoleId = Guid.Empty`) | (validation pipeline) |
| 404 | Account không tồn tại / soft-deleted | `"Không tìm thấy tài khoản."` |
| 404 | **Role không tồn tại** HOẶC role `Status ≠ Active` HOẶC role soft-deleted | `"Role không tồn tại hoặc đã bị vô hiệu hóa."` |
| 409 | **(#AUTH-34, 2026-06-19) Concurrency conflict** — admin khác đang update cùng account, hoặc account becomes invalid sau reload (soft-deleted concurrent) | `"Tài khoản đã bị thay đổi bởi tiến trình khác. Vui lòng thử lại."` |

**Side effect khi success (verified):**
- `Account.RoleId = request.RoleId`, `RoleAssignedAt = UtcNow`, `RoleAssignedBy = <admin's accountId từ JWT>` (qua `IHttpContextAccessor`).
- Audit `RoleAssigned` (`TargetAccountId = account.Id`) với metadata `previousRoleId`, `newRoleId`, `newRoleName`.
- **(#AUTH-34) Concurrency retry**: handler wrap business logic + SaveChanges trong `ConcurrencyRetryHelper.ExecuteAsync` (3 attempts max, reload entity sau conflict). `auditPublished` flag đảm bảo audit `RoleAssigned` chỉ publish 1 lần dù retry. Trường hợp đặc biệt: nếu sau reload phát hiện `account.RoleId == newRoleId` (admin khác đã chuyển sang role này) → return 409 thay vì 200 idempotent (để FE biết không phải request này gây thay đổi).

**Lưu ý JWT cache (đã sửa từ docs cũ):**

1. **DB**: Role assignment commit ngay sau success.
2. **JWT claim**: Access token cũ vẫn giữ claim `role` cũ + `perm[]` cũ cho đến khi hết hạn HOẶC user gọi `/refresh-token` HOẶC user logout + login lại. **Worst-case = TTL access token (max 1h)**.
3. **PermissionResolver cache (#AUTH-16)**: Cache miss do `RoleId` đổi, nhưng cache cũ của RoleId cũ KHÔNG tự invalidate (handler **KHÔNG publish `PermissionsChangedEvent`** — cùng pattern như `SetRolePermissions`). User refresh token → resolver lookup theo RoleId mới → ra DB → cache RoleId mới. OK case này.
4. **Worst-case delay** user "cảm nhận" được role mới: ~1 giờ (access token TTL) trừ khi force user logout/refresh.

> ⚠️ Handler **KHÔNG** revoke refresh token sau khi đổi role. Tức user vẫn dùng refresh token cũ để lấy access token mới với role mới (đây là behavior đúng — role change không phải security incident). Nếu Admin muốn force user re-authenticate sau đổi role (vd downgrade Admin → Customer), Admin phải gọi thêm `POST /api/admin/accounts/{id}/sessions/revoke-all`.

---

### `POST /api/admin/accounts/{id}/merge` — **(#AUTH-47)**

**Mục đích:** Admin merge **secondary account** vào **primary account** — dùng cho case 1 người dùng có 2 account (vd Alice tạo local account với email/password trước, sau đó login Google bằng cùng email → tạo 2 account riêng do `#AUTH-20` policy). Endpoint consolidate data về primary, tombstone secondary.

**Auth:** **Admin only** (chỉ Admin role, không phải Manager).

**Path param:**

| Field | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | **Primary account id** — account giữ lại (audit/sessions/profile transfer VỀ đây). Path id KHÔNG được trùng `secondaryAccountId` trong body |

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `secondaryAccountId` | `Guid` | Bắt buộc | Không Guid.Empty, không trùng primary | Account bị merge (sẽ bị tombstone + email anonymize) |
| `reason` | `string` | Bắt buộc | 1–1000 ký tự (trim) | Lý do merge — **bắt buộc** cho audit/compliance. Ví dụ: `"User báo support tạo nhầm 2 account cùng email"`, `"Compliance request — duplicate identity merged"` |

**Conflict resolution rules — Primary thắng:**

| Field secondary | Primary đã có? | Hành động |
|---|---|---|
| `GoogleId` | Null | Transfer sang primary (move + clear secondary) |
| `GoogleId` | Has value | Giữ primary, secondary's GoogleId bị clear (drop) |
| `AccountProfile` | Null | Migrate profile: update `AccountProfile.AccountId = primary.Id` |
| `AccountProfile` | Has value | Soft-delete secondary's profile (`IsDeleted=true`) |
| `StaffProfile` | Null | Migrate giống AccountProfile |
| `StaffProfile` | Has value | Soft-delete secondary's StaffProfile |
| `Email`/`PhoneNumber`/`FullName` | Has value | **Luôn giữ primary** (KHÔNG move). Audit ghi lại để compliance trace |

**Response thành công `200`:**

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Đã merge account 1a2b3c4d-... vào 8f3a5b9d-.... Revoke 3 session.",
  "data": "8f3a5b9d-2c4e-4a1f-9b8d-1e2f3a4b5c6d",
  "listErrors": null
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `string (Guid)` | Primary account id (= path id, giữ làm reference) |

**Side effect khi success (TẤT CẢ trong 1 transaction):**

1. **Revoke RT secondary:** Tất cả `RefreshToken` active của secondary → `Status = Revoked` + `RevokedReason = "Account merged into {primaryId}"`.
2. **Transfer identity:** GoogleId/Provider theo conflict resolution rules ở trên.
3. **Transfer profile:** AccountProfile/StaffProfile theo rules.
4. **Tombstone secondary:**
   - `IsDeleted = true`, `DeletedAt = UtcNow`
   - `MergedIntoId = primary.Id`, `MergedAt = UtcNow`
   - `Email = "merged-{secondary.Id}@anonymized.local"` (anonymize để tránh unique index violation nếu admin tạo account mới cùng email gốc của secondary)
   - `PhoneNumber = null`, `GoogleId = null` (clear PII)
5. **Insert AccountMergeLog row** (table dedicated, append-only):
   - `PrimaryAccountId`, `SecondaryAccountId`, `PerformedBy = <admin từ JWT>`
   - `Reason` (free-text)
   - `SecondaryAccountSnapshotJson` — JSONB snapshot full state secondary TRƯỚC merge (cho rollback manual/compliance investigation)
   - `ConflictResolutionJson` — JSONB record các quyết định: `{"googleId": "moved_from_secondary", "accountProfile": "kept_primary_secondary_softdeleted", ...}`
   - `SessionsRevoked` — count
   - `AuditLogsLinked` — số audit row có `TargetAccountId == secondary.Id` (statistic, audit log NOT moved — giữ immutable per `#AUTH-29` trigger)
6. **Audit log:**
   - `AccountMerged=130` (TargetAccountId = primary) với metadata `primaryAccountId`, `secondaryAccountId`, `sessionsRevoked`, `auditLogsLinked`, `mergeLogId`
   - Meta-audit `AccountDeleted=66` (TargetAccountId = secondary) với reason `"Merged into {primaryId} by admin {performedBy}"`
7. **Post-commit (sau SaveChanges thành công):** Redis TRL bulk revoke ALL access tokens của secondary TTL 1h: `_revocationStore.RevokeAllByAccountAsync(secondary.Id, TimeSpan.FromHours(1))`.

**Idempotency:**
- KHÔNG idempotent. Gọi merge cùng `(primary, secondary)` 2 lần → lần 2 fail 409 vì `secondary.MergedIntoId` đã có value.

**Audit log của secondary — KHÔNG move:**

`AuditLog` rows có `TargetAccountId = secondary.Id` **vẫn giữ nguyên** (không re-link sang primary) vì:
- Append-only trigger từ `#AUTH-29` chặn UPDATE.
- Audit history là time-machine — không nên rewrite history.
- Compliance: ai làm gì với ID nào tại thời điểm nào phải giữ nguyên.

Investigation FE: query `AccountMergeLog.SecondaryAccountId` + follow `AccountMergeLog.PrimaryAccountId` để cross-reference history.

**Sub-error messages:**

| Status | Message | Trigger |
|---|---|---|
| `400` | "Dữ liệu không hợp lệ." | Field validation: `primaryAccountId/secondaryAccountId = Guid.Empty`, trùng nhau, reason rỗng / > 1000 ký tự, `performedBy = Guid.Empty`. Có `listErrors` |
| `401` | Auth middleware | JWT thiếu/sai |
| `403` | Auth middleware | Không phải Admin role |
| `404` | "Primary account không tồn tại hoặc đã bị xoá." | Primary account không có trong DB hoặc `IsDeleted=true` |
| `404` | "Secondary account không tồn tại hoặc đã bị xoá." | Tương tự cho secondary |
| `409` | "Primary account đã từng bị merge vào account khác." | `primary.MergedIntoId != null` — primary từng là secondary trong merge khác. Không cho merge chained |
| `409` | "Secondary account đã từng bị merge trước đó." | `secondary.MergedIntoId != null` — secondary đã bị merge rồi |

**Use case operational:**

```
User báo support: "Tôi có 2 account cùng email user@example.com — 1 local + 1 Google"
↓
Admin verify danh tính (vd OTP qua phone, video call)
↓
Admin xác định primary = local account (có lịch sử lâu hơn) + secondary = Google account
↓
Admin gọi POST /api/admin/accounts/{primaryId}/merge với reason rõ ràng
↓
Server merge: secondary tombstone, GoogleId transfer sang primary, all session secondary revoke
↓
User giờ login bằng email/password (primary) HOẶC Google (qua GoogleId vừa transfer) đều ra cùng account
↓
Audit log đầy đủ ở AccountMergeLog row + AccountMerged + AccountDeleted entries
```

---

### `GET /api/admin/accounts/{id}/sessions`

**Mục đích:** Admin xem tất cả session của một tài khoản.

**Auth:** Admin hoặc Manager

**Query params:** `activeOnly` (bool, mặc định true)

**Response:** Giống `GET /api/sessions/me`

---

### `POST /api/admin/accounts/{id}/sessions/revoke-all`

**Mục đích:** Admin thu hồi tất cả session của tài khoản (force logout).

**Auth:** Admin

**Request body:**

| Field | Type | Mô tả |
|---|---|---|
| `reason` | `string?` | Lý do force logout (ghi vào audit log) |

---

### `GET /api/admin/accounts/{id}/login-history`

**Mục đích:** Admin xem login history của bất kỳ tài khoản nào.

**Auth:** Admin hoặc Manager

**Query params:** Giống `GET /api/accounts/me/login-history`

---

## Nhóm 6 — Admin: Staff Profiles

Base route: `/api/admin/staff`
**Auth:** Admin

---

### `PUT /api/admin/staff/{id}/profile`

**Mục đích:** Admin tạo hoặc cập nhật staff profile cho một account.

**Auth:** Admin

**Request body:**

| Field | Type | Mô tả |
|---|---|---|
| `employeeCode` | `string?` | Mã nhân viên (max 50 ký tự) |
| `department` | `string?` | Phòng ban (max 100 ký tự) |
| `maxConcurrentTickets` | `int` | Số ticket tối đa đồng thời, 1–50 (mặc định 3) |
| `isAvailable` | `bool` | Trạng thái sẵn sàng (mặc định true) |
| `skillTier` | `int` | Tier kỹ năng staff, 1–3 (mặc định 1) — `StaffSkillTierEnum` |
| `notes` | `string?` | Ghi chú (max 1000 ký tự) |

---

### `POST /api/admin/staff/{id}/skills`

**Mục đích:** Admin thêm, cập nhật hoặc khôi phục kỹ năng của một staff.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `skillCode` | `string` | Bắt buộc | Mã kỹ năng, max 64 ký tự |
| `skillLevel` | `int` | Bắt buộc | Mức độ kỹ năng, 1–5 |
| `certifiedUntil` | `DateTime?` | Không | Ngày hết hạn chứng chỉ |

---

### `DELETE /api/admin/staff/{id}/skills/{skillCode}`

**Mục đích:** Admin xóa mềm một kỹ năng khỏi staff profile.

**Auth:** Admin

**Lỗi thường gặp:**
- `400` — `id`, `skillCode` hoặc body không hợp lệ
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin
- `404` — Không tìm thấy account hoặc skill code trên staff đó

**Lưu ý route:** Staff assignment read nằm ở `/api/staff` và `/api/staff/{id}/assignment-profile`, không nằm dưới `/api/admin/accounts/staff`. Các route dynamic trong admin accounts đều có constraint `{id:guid}`, nên literal route như `invite` không bị match nhầm làm id.

---

## Nhóm 7 — Admin: Roles

Base route: `/api/admin/roles`
**Auth:** Admin

---

### `GET /api/admin/roles`

**Mục đích:** Danh sách roles với phân trang và lọc.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `keyword` | `string?` | Tìm theo tên role |
| `status` | `RoleStatusEnum?` | Lọc theo trạng thái |
| `isSystemRole` | `bool?` | Lọc role hệ thống |

**Response:** `PaginationResponse<RoleDto>`

**Chi tiết `RoleDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID role |
| `name` | `string` | Không | Tên role |
| `normalizedName` | `string` | Không | Tên chuẩn hóa (uppercase) |
| `description` | `string?` | Null nếu không có | Mô tả |
| `status` | `RoleStatusEnum` | Không | Trạng thái (xem enum) |
| `isSystemRole` | `bool` | Không | `true` = role hệ thống, không cho xóa |
| `createdAt` | `DateTime` | Không | Thời điểm tạo |
| `updatedAt` | `DateTime?` | Null nếu chưa cập nhật | Thời điểm cập nhật |

---

### `GET /api/admin/roles/{id}`

**Mục đích:** Xem chi tiết một role.

**Response:** `RoleDto`

---

### `POST /api/admin/roles`

**Mục đích:** Tạo role mới.

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `name` | `string` | Bắt buộc | Max 100 ký tự | Tên role |
| `description` | `string?` | Không | Max 500 ký tự | Mô tả |

**Response:** `CommonResponse<Guid>` — trả về ID role mới.

---

### `PUT /api/admin/roles/{id}`

**Mục đích:** Cập nhật tên và mô tả role.

**Request body:** Giống POST nhưng có `id` từ route.

**Response thành công `200`:** `CommonResponse<Guid>` — `data` là Guid của role vừa update.

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

---

### `PATCH /api/admin/roles/{id}/status`

**Mục đích:** Thay đổi trạng thái role.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `status` | `RoleStatusEnum` | Bắt buộc | Trạng thái mới |

---

### `DELETE /api/admin/roles/{id}`

**Mục đích:** Xóa role. Không thể xóa system role.

---

## Nhóm 8 — Permissions

Module này có **2 lớp endpoint** chia sẻ cùng DTO `PermissionDto` nhưng khác nhau ở quyền truy cập:

| Endpoint | Controller | Auth | Mục đích |
|---|---|---|---|
| `GET /api/permissions` | `PermissionsController` | `[Authorize]` — **mọi role** | FE/Mobile load catalog đầy đủ để build picker / đối chiếu `P.*` constant, **không cần quyền admin** |
| `GET /api/admin/permissions` | `AdminPermissionsController` | `[Authorize(Roles = "Admin")]` | Catalog + thao tác gán role↔permission (admin panel) |
| `GET /api/admin/roles/{roleId}/permissions` | `AdminPermissionsController` | `[Authorize(Roles = "Admin")]` | Permission đang gán cho 1 role |
| `PUT /api/admin/roles/{roleId}/permissions` | `AdminPermissionsController` | `[Authorize(Roles = "Admin")]` | Set toàn bộ permission cho role (replace) |

> **Phân biệt 3 endpoint trả permission** (dễ nhầm):
> - `GET /api/permissions` — **catalog public** (mọi role), trả TẤT CẢ permission trong hệ thống.
> - `GET /api/admin/permissions` — **catalog admin-only**, cùng data nhưng yêu cầu role Admin (dùng cho admin gán permission).
> - `GET /api/auth/me/permissions` ([Nhóm 3](#get-apiauthmepermissions)) — trả **subset** permission của role mà user hiện tại đang được gán (dùng build feature-gate).

---

### `GET /api/permissions`

**Mục đích:** Lấy **catalog toàn bộ permission** trong hệ thống. Endpoint dùng chung — chỉ cần access token hợp lệ, **KHÔNG phân quyền theo role** (không có slug/role `admin`).

**Tác dụng & khi nào dùng:**

- FE/Mobile cần load danh mục permission đầy đủ (build picker, đối chiếu `P.*` constant, hiển thị mô tả) mà **không cần quyền admin**.
- Khác với `GET /api/admin/permissions` ở chỗ **không yêu cầu role Admin** — Manager/Staff/Customer đã đăng nhập đều gọi được.
- Khác với `GET /api/auth/me/permissions` ở chỗ trả **toàn bộ catalog** (không phải subset theo role của user).

**Auth:** Bắt buộc (mọi role — Admin / Manager / Staff / Customer đều gọi được, chỉ cần JWT hợp lệ)

**Headers:**

```
Authorization: Bearer {accessToken}
```

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `module` | `string?` | Lọc theo module (e.g., `Battery`, `Ticket`). Để trống = trả tất cả |

**Response:** `PermissionListResponse` — flat list `PermissionDto`, sort theo `Module` rồi `Code`.

**Chi tiết `PermissionDto`** (cùng shape với `GET /api/admin/permissions`):

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID permission |
| `code` | `string` | Không | Code dạng `module.action` (e.g., `battery.view`) — match với `P.*` constant ở FE |
| `module` | `string` | Không | Module thuộc về |
| `description` | `string?` | Null nếu không có | Mô tả tiếng Việt |
| `isSystemPermission` | `bool` | Không | `true` = không cho admin xóa |
| `createdAt` | `DateTime` | Không | Thời điểm tạo |

**Response codes:**

| Status | Trường hợp |
|---|---|
| 200 | Lấy danh sách permission thành công |
| 401 | Chưa đăng nhập / token hết hạn |

> **Lưu ý:** Endpoint này **KHÔNG** trả `403` — vì chỉ gắn `[Authorize]` (không filter role). Mọi user đã đăng nhập đều truy cập được. Catalog system permissions (43 permission) xem bảng đầy đủ ở [`GET /api/admin/permissions`](#get-apiadminpermissions) bên dưới.

---

## Nhóm 8b — Admin: Permissions

Base route: `/api/admin/permissions`
**Auth:** Admin

---

### `GET /api/admin/permissions`

**Mục đích:** Danh sách tất cả permission trong hệ thống.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `module` | `string?` | Lọc theo module (e.g., `Battery`, `Ticket`) |

**Response:** `List<PermissionDto>`

**Chi tiết `PermissionDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID permission |
| `code` | `string` | Không | Code dạng `module.action` (e.g., `battery.view`) |
| `module` | `string` | Không | Module thuộc về |
| `description` | `string?` | Null nếu không có | Mô tả |
| `isSystemPermission` | `bool` | Không | `true` = không cho admin xóa |
| `createdAt` | `DateTime` | Không | Thời điểm tạo |

**Catalog system permissions (43 permission seed-ed lúc DB init):**

> Source of truth: `AuthService.Application/Authorization/PermissionCodes.cs` + `Infrastructure/Persistence/Seeders/PermissionSeed.cs`. FE dùng các code này để mapping với `P.*` constants trong `shared/lib/authz.ts`. Khi thêm permission mới → BE update 2 file trên + seed migration; FE update `P.*` enum.

| Module | Code | Description | Default cho role |
|---|---|---|---|
| User | `user.view` | Xem danh sách + chi tiết account | Admin, Manager, Staff |
| User | `user.create` | Tạo account trực tiếp | Admin |
| User | `user.update` | Cập nhật profile account | Admin |
| User | `user.delete` | Xóa mềm account | Admin |
| User | `user.change_status` | Đổi trạng thái account (Lock/Suspend/Ban) | Admin, Manager |
| User | `user.unlock` | Mở khóa account | Admin, Manager |
| User | `user.assign_role` | Gán/thu hồi role cho account | Admin, Manager |
| User | `user.force_logout` | Buộc account đăng xuất khỏi mọi session | Admin, Manager |
| User | `user.invite` | Gửi invite email cho user mới | Admin |
| Role | `role.view` | Xem danh sách + chi tiết role | Admin, Manager |
| Role | `role.create` | Tạo role mới (non-system) | Admin |
| Role | `role.update` | Cập nhật role | Admin |
| Role | `role.delete` | Xóa role | Admin |
| Role | `role.assign_permission` | Gán permission cho role | Admin |
| Battery | `battery.view` | Xem battery + sensor reading | Admin, Manager, Staff, Customer |
| Battery | `battery.create` | Tạo battery mới | Admin |
| Battery | `battery.update` | Cập nhật battery | Admin, Staff |
| Battery | `battery.delete` | Xóa battery | Admin |
| Battery | `battery.assign` | Assign battery cho customer | Admin, Manager |
| Battery | `battery.configure` | Cấu hình ngưỡng cảnh báo | Admin, Manager |
| Ticket | `ticket.view` | Xem ticket của chính mình | Admin, Staff, Customer |
| Ticket | `ticket.view_all` | Xem mọi ticket trong hệ thống | Admin, Manager |
| Ticket | `ticket.create` | Tạo ticket | Admin, Customer |
| Ticket | `ticket.assign` | Assign ticket cho Staff | Admin, Manager |
| Ticket | `ticket.resolve` | Mark ticket Resolved | Admin, Staff |
| Ticket | `ticket.close` | Đóng ticket | Admin, Manager |
| Ticket | `ticket.escalate` | Escalate ticket lên Manager | Admin, Manager |
| Notification | `notification.view` | Xem thông báo | Admin, Manager, Staff, Customer |
| Notification | `notification.send` | Gửi thông báo vận hành | Admin, Manager |
| Notification | `notification.manage_template` | Quản lý template thông báo | Admin |
| KnowledgeBase | `knowledge_base.view` | Xem bài viết knowledge base | Admin, Manager, Staff, Customer |
| KnowledgeBase | `knowledge_base.create` | Tạo bài viết knowledge base | Admin, Manager |
| KnowledgeBase | `knowledge_base.update` | Cập nhật bài viết knowledge base | Admin, Manager |
| KnowledgeBase | `knowledge_base.delete` | Xóa bài viết knowledge base | Admin |
| KnowledgeBase | `knowledge_base.publish` | Publish/unpublish bài viết knowledge base | Admin, Manager |
| Reports | `reports.view` | Xem báo cáo vận hành | Admin, Manager |
| Reports | `reports.export` | Export báo cáo | Admin, Manager |
| Audit | `audit.view` | Xem audit log | Admin, Manager |
| TicketSaga | `ticket.saga.view` | (Sprint 5B #241) Xem danh sách Alert-Ticket Saga + state hiện tại | Admin, Manager |
| TicketSaga | `ticket.saga.reprocess` | (Sprint 5B #241) Reprocess Saga đang Failed (admin only) | Admin |

> **Quan trọng — Role vs Permission tách 2 lớp:**
> - **Permission** (`user.view`, `battery.view`, …) — fine-grained, được FE kiểm qua `checkPermission(user, P.X)` để ẩn/hiện button.
> - **Role** (`Admin`, `Manager`, `Staff`, `Customer`) — coarse-grained, dùng cho controller-level `[Authorize(Roles = "...")]` ở BE và `RoleRoute` ở FE.
> - **Một số endpoint enforce CẢ HAI**. Ví dụ `GET /api/admin/audit-logs` yêu cầu `[Authorize(Roles = "Admin")]` ở controller → Manager (dù có permission `audit.view`) **KHÔNG access được**. Đây là chủ ý design để controller-level chặn trước, permission-level là defense-in-depth thứ hai.

**Permission cache (#AUTH-16) — quan trọng cho hiểu propagation delay:**

`PermissionResolver` cache list permission codes theo `RoleId` trong **process-memory** với **TTL 5 phút**:
- User chỉ nhận permission update qua claim trong **lần issue JWT tiếp theo** (login / refresh-token).
- BE cần `PermissionsChangedEvent` (#AUTH-15) publish khi `PUT /api/admin/roles/{roleId}/permissions` được gọi → consumer ở mỗi instance gọi `PermissionResolver.InvalidateRole(roleId)`.
- Nếu event không được publish (config sai / RabbitMQ down): cache invalidate sau tối đa 5 phút theo TTL, không phải vĩnh viễn.
- Cache scope là **per-process**: deployment có nhiều replica → mỗi replica có cache riêng → event phải fan-out tới mọi replica để invalidate đồng bộ.

**Permission resolution rule:**

`PermissionResolver.GetPermissionCodesAsync` chỉ trả permission codes nếu:
1. Account tồn tại + `IsDeleted = false`
2. Role tồn tại + `Status = Active` + `IsDeleted = false`
3. RolePermission rows + Permission rows phải `IsDeleted = false`

Nếu Role chuyển sang `Inactive` hoặc `Deprecated` → resolver trả **empty list** → user effectively mất hết permission ở lần issue JWT tiếp theo, dù assignment Role-Account vẫn tồn tại trong DB. Để khôi phục: đổi role status về `Active` HOẶC đổi sang role khác qua `PUT /api/admin/accounts/{id}/role`.

---

### `GET /api/admin/roles/{roleId}/permissions`

**Mục đích:** Lấy danh sách permission đang gán cho một role.

**Response:** `List<PermissionDto>`

---

### `PUT /api/admin/roles/{roleId}/permissions`

**Mục đích:** Set toàn bộ permission cho role (replace semantics). Permission không trong list sẽ bị revoke.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `permissionIds` | `Guid[]` | Bắt buộc | Danh sách ID permission cần gán |
| `allowSystemRole` | `bool` | Không (mặc định `false`) | Cho phép modify system role |

**Cảnh báo replace semantics:** Gửi `permissionIds: []` sẽ xóa toàn bộ permission khỏi role đó. FE phải fetch danh sách hiện tại, merge với thay đổi, rồi gửi toàn bộ list mong muốn.

**Status code mapping (đã verify với code handler — round 1 docs ghi sai):**

| Status | Trường hợp | Message |
|---|---|---|
| 200 | Success | `"Đã set N permission cho role X (+toAdd / -toRemove)."` |
| 401 | JWT thiếu/sai | (middleware) |
| 403 | Không có role Admin | (middleware) |
| 403 | Cố modify system role mà `allowSystemRole = false` | `"Không thể thay đổi permission của system role mặc định. Set AllowSystemRole=true nếu muốn override."` |
| 404 | Role không tồn tại / đã bị soft-delete | `"Role không tồn tại."` |
| 404 | **PermissionId không tồn tại** (1 hoặc nhiều ID không có trong DB) | `"Có N permission không tồn tại."` |

> **Chú ý**: docs sớm hơn ghi "400 — Permission không tồn tại / Cố modify system role" — đó là sai. Code thực tế trả **404** cho permission ID invalid và **403** cho system role.

**Side effect khi success:**
- Diff `existingPermissionIds` vs `requestedSet`:
  - **toAdd**: insert `RolePermission` rows mới với `AssignedAt = UtcNow`
  - **toRemove**: soft-delete `RolePermission` rows hiện có (qua `DeleteAsync` → interceptor set `IsDeleted = true`)
- Audit `PermissionGranted` (1 entry duy nhất cho cả thao tác, không phải 1 entry/permission) với metadata: `roleId`, `roleName`, `addedCount`, `removedCount`, `totalAfter`.
- **KHÔNG có** event nào khác được publish.

**Propagation timing — quan trọng cho FE/QA testing (đã sửa từ round 6):**

1. **DB**: thay đổi `RolePermission` rows commit ngay khi handler success.
2. **JWT claim**: user đang đăng nhập **không** auto-update — chỉ nhận permission mới ở **lần issue JWT tiếp theo** (login lại HOẶC refresh token).
3. **PermissionResolver cache** (#AUTH-16): mỗi instance BE có in-memory cache 5 phút theo `RoleId`.
   - **Code comment trong `PermissionResolver.cs` mention `PermissionsChangedEvent` (#AUTH-15)** sẽ invalidate cache, **NHƯNG handler `SetRolePermissionsCommandHandler` hiện KHÔNG publish event này**. Đây là gap trong implementation hiện tại — cache chỉ invalidate qua **5-min TTL**, không có push-based invalidation.
   - **Hậu quả thực tế**: sau khi Admin set permissions, user mới đã login (cache miss) sẽ ra DB lấy đúng → OK. User đang đăng nhập với access token cũ + role đã cached → mất tối đa **5 phút TTL + thời gian access token còn lại (max 1h)** mới nhận permission mới ở lần refresh tiếp theo.
4. **Multi-replica deployment**: cache scope là per-process → mỗi replica cache độc lập, không sync. TTL 5 phút độc lập từng replica.
5. **Worst-case delay** user nhận permission mới = **~5 phút (cache TTL) + ~1 giờ (max access token life)**. FE muốn force apply ngay → buộc user logout và login lại.

**System role bypass:**
- 4 system role (`Admin`, `Manager`, `Staff`, `Customer`) mặc định bị chặn modify để tránh accidental lockout.
- Để override, set `allowSystemRole = true` trong body. Chỉ dùng khi thực sự cần adjust default permission (vd thêm permission mới vào catalog → cần re-grant cho Admin).

---

## Nhóm 9 — Admin: Audit Logs

Base route: `/api/admin/audit-logs`
**Auth:** Admin (chỉ Admin — không phải Manager)

---

### `GET /api/admin/audit-logs`

**Mục đích:** Xem audit log toàn hệ thống, phân trang và lọc.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang (mặc định 1; ≤0 tự về 1) |
| `pageSize` | `int` | Số item/trang (mặc định 20, trần 100; ≤0 tự về 10) |
| `action` | `AuditActionEnum?` | Lọc theo loại hành động |
| `targetAccountId` | `Guid?` | Xem tất cả hành động liên quan đến account này |
| `actorAccountId` | `Guid?` | Xem tất cả hành động actor này thực hiện |
| `isSuccess` | `bool?` | Lọc theo kết quả thành công/thất bại |
| `fromUtc` | `DateTime?` | Từ thời điểm (UTC inclusive — `CreatedAt >= fromUtc`) |
| `toUtc` | `DateTime?` | Đến thời điểm (UTC exclusive — `CreatedAt < toUtc`) |

**Response:** `PaginationResponse<AuditLogDto>` (sort `CreatedAt` giảm dần — mới nhất trước).

**Status code:**

| Code | Khi nào |
|---|---|
| `200` | Lấy danh sách thành công (kể cả rỗng) |
| `422` | `fromUtc >= toUtc` — `message = "FromUtc phải nhỏ hơn ToUtc."`, `listErrors = null` |
| `401` | Chưa đăng nhập / token hết hạn |
| `403` | Không có role Admin |

> ⚠️ Annotation `[ProducesResponseType(400)]` trên controller chỉ là metadata Swagger; runtime handler thực tế trả **`422`** cho lỗi khoảng thời gian.

**Chi tiết `AuditLogDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID audit log |
| `action` | `AuditActionEnum` | Không | Loại hành động (xem enum) |
| `actionName` | `string` | Không | Tên hành động dạng string |
| `targetAccountId` | `Guid?` | Null nếu không xác định được (login với email lạ) | ID tài khoản mục tiêu |
| `targetEmail` | `string?` | Null nếu không cần | Email mục tiêu |
| `actorAccountId` | `Guid?` | Null = anonymous; = targetAccountId = self | ID actor thực hiện |
| `isSuccess` | `bool` | Không | Thành công không |
| `reason` | `string?` | Null nếu thành công hoặc không có thêm context | Lý do thất bại hoặc ghi chú |
| `metadataJson` | `string?` | Null nếu không có | JSON tự do chứa thông tin chi tiết |
| `ipAddress` | `string?` | Null nếu không capture | IP address |
| `userAgent` | `string?` | Null nếu không có | User agent |
| `deviceId` | `string?` | Null nếu không gửi | Device ID |
| `correlationId` | `string?` | Null nếu không có | Correlation ID để link với request log |
| `createdAt` | `DateTime` | Không | Thời điểm ghi log (UTC) |

**Field truncation rules (`AuditTrailNotificationHandler`):**

Handler tự truncate các text field trước khi persist DB để tránh quá size column:

| Field | Max length | Behavior khi vượt |
|---|---|---|
| `reason` | 500 chars | Truncate đuôi (giữ 500 ký tự đầu) |
| `userAgent` | 500 chars | Truncate đuôi |
| `targetEmail` | 256 chars | Truncate đuôi |

> FE submit `reason = "rất dài..."` qua endpoint admin (vd `/admin/accounts/{id}/sessions/revoke-all` body `reason`) — chỉ 500 ký tự đầu được lưu vào audit. FE muốn full reason → đưa vào `metadata` JSON (không có truncation rule).

**Quan trọng — handler KHÔNG tự `SaveChangesAsync`:**

`AuditTrailNotificationHandler` chỉ gọi `_unitOfWork.AuditLogs.AddAsync(entry)` — KHÔNG `SaveChanges`. Lý do: command handler đang publish audit notification sẽ `SaveChanges` sau đó, để audit log và business data atomic trong **cùng 1 transaction**.

**Hậu quả**:
- Nếu command handler return SỚM (vd validation fail) trước khi reach `SaveChanges` → audit log sẽ KHÔNG persist (entry còn trong DbContext nhưng không commit). Đây là expected behavior — fail validation không cần audit.
- Nếu command handler throw exception giữa `Publish(audit)` và `SaveChanges` → audit log không persist (entire transaction rollback). Cũng là expected.
- Nếu `AuditTrailNotificationHandler` chính nó throw (vd JSON serialize metadata fail) → handler log error qua `ILogger` nhưng **KHÔNG throw lại** → business flow tiếp tục bình thường (audit failure không phá vỡ business).

**Actor resolution:**
- Ưu tiên `ActorAccountIdOverride` từ notification (vd Login handler set actorOverride = account.Id để self-action).
- Nếu null → fallback `ICurrentUserService.UserId` (từ JWT claim).
- Nếu cả 2 đều null → `ActorAccountId = null` (anonymous action, vd register).

---

### `GET /api/admin/audit-logs/by-account/{accountId}`

**Mục đích:** Xem toàn bộ audit log mà account này là **target** (tiện hơn so với base endpoint + filter `targetAccountId`).

**Auth:** Admin (chỉ Admin)

**Path param:** `accountId` — Guid của account mục tiêu (map vào `targetAccountId`).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang, mặc định 1 |
| `pageSize` | `int` | Số item/trang, mặc định 20 |
| `action` | `AuditActionEnum?` | Lọc theo loại hành động |
| `isSuccess` | `bool?` | Lọc theo kết quả thành công/thất bại |

**Response:** `PaginationResponse<AuditLogDto>` — cùng shape với `GET /api/admin/audit-logs`.

---

## Appendix A — Configuration toggles

Các tham số sau **ảnh hưởng trực tiếp** đến hành vi API. Ops/DevOps cần biết khi tune; FE/QA cần biết khi setup test environment khác production.

### `JwtSettings:*` (#AUTH-32, #AUTH-66)

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `SecretKey` | `string` (≥32 chars) | **required, app startup fail nếu thiếu** | HMAC secret cho HS256 sign JWT |
| `Issuer` | `string` | required | `iss` claim — phải match validation |
| `Audience` | `string` | required | `aud` claim — phải match validation |
| `AccessTokenExpirationMinutes` | `int` (1–1440) | `60` | TTL access token. Ảnh hưởng tới `exp` claim. |
| `RefreshTokenExpirationDays` | `int` (1–90) | `7` | TTL refresh token. **Anchor vào `OriginalIssuedAt`** (#AUTH-28) — không reset mỗi rotate |
| `SigningKeyId` | `string` | `"v1"` | (#AUTH-59) Key ID hiện tại đi vào header `kid` |
| `PreviousSecretKey` | `string?` | `null` | (#AUTH-59) Key cũ — set khi rotate để token đang lưu hành vẫn pass cho đến hết hạn |
| `PreviousSigningKeyId` | `string` | `"v0"` | (#AUTH-59) `kid` của previous key |

### `Session:*`

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `MaxConcurrentSessions` | `int` | `5` | Số session active tối đa/account. Vượt → revoke session cũ nhất FIFO + audit `SessionLimitExceededOldestRevoked`. **Đặt 0 hoặc âm = tắt limit**. |

### `AuthSecurity:*`

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `EnforceDeviceBinding` | `bool` | `false` | (#AUTH-12) Bật → refresh token chỉ chấp nhận khi IP + UA match lúc issue. Mặc định tắt để mobile chuyển 4G/wifi không bị break. |

### `PasswordPolicy:*` (#AUTH-53)

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `MinLength` | `int` | `8` | Độ dài tối thiểu password |
| `MaxLength` | `int` | `100` | Độ dài tối đa |
| `RequireUppercase` | `bool` | `true` | Bắt buộc 1 chữ hoa |
| `RequireLowercase` | `bool` | `true` | Bắt buộc 1 chữ thường |
| `RequireDigit` | `bool` | `true` | Bắt buộc 1 chữ số |
| `RequireSpecialChar` | `bool` | `true` | Bắt buộc 1 ký tự đặc biệt |

> **Lưu ý**: Đổi `PasswordPolicy` không re-validate password cũ đã hash; chỉ ảnh hưởng password mới được set qua `/register`, `/reset-password`, `/me/password`, `/admin/accounts`, `/accept-invite`.

### `GoogleOAuth:*`

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `RedirectUri` | `string` | required cho Google flow | Redirect URI cố định, phải match đã đăng ký với Google. Có thể override qua env `GOOGLE_REDIRECT_URI`. |

### `AdminInvite:*` / `Frontend:*`

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `AdminInvite:AcceptUrlBase` hoặc `Frontend:AcceptInviteUrl` | `string` | seeded từ Helm (`https://{global.domain}/auth/accept-invite`) | Base URL email invite. Server append `?token={invitationToken}` |
| `Frontend:WebBaseUrl` | `string` | seeded từ Helm (`https://{global.domain}`), fallback hard-code `"https://app.local"` nếu config rỗng | **(#AUTH-51, 2026-06-19)** Base URL FE web app. Dùng ở `RequestCrossDevice2FAConfirmCommandHandler` để build email confirm URL: `{webBaseUrl}/2fa/cross-device-confirm?token={confirmToken}`. **KHÔNG được có trailing slash** (handler tự `TrimEnd('/')`). Setup env: `Frontend__WebBaseUrl=http://localhost:5173` (dev `.env` + `.env.Docker`), `https://app.example.com` (template `.env.Docker.example`), `https://{global.domain}` (Helm `solar-config` ConfigMap). Có thể mở rộng thêm cho future flow (vd password reset link nếu chuyển sang send-link thay vì OTP) |

### `Outbox:*`

| Key | Type | Default | Mô tả |
|---|---|---|---|
| `PollIntervalSeconds` | `int` | `2` | Tần suất `OutboxRelay` quét outbox table. Ảnh hưởng độ trễ event publish (RabbitMQ) sau khi handler success. |
| `BatchSize` | `int` | depends config | Số row publish mỗi tick |
| `MaxRetries` | `int` | depends config | Số lần retry khi RabbitMQ fail |

---

## Appendix B — Background services

5 `BackgroundService` chạy trong AuthService process. FE/QA cần biết để hiểu timing behaviors.

| Service | Poll interval | Behavior | User-visible impact |
|---|---|---|---|
| `LockoutReconcileBackgroundService` | 5 phút | Quét `Account` với `LockoutEndAt <= now` AND `Status = Locked` → reset `Status = Active`, `LockoutEndAt = null`, `FailedLoginAttempts = 0`. | Sau lockout 15 phút, **không cần user thử login** — status tự về Active sau ≤ 5 phút (LoginCommandHandler cũng tự reconcile khi user retry login). |
| `OutboxRelayBackgroundService` | 2 giây (config `Outbox:PollIntervalSeconds`) | Quét bảng `outbox_messages` với `processed_at IS NULL` → publish lên RabbitMQ → mark processed. | Email/notification có **độ trễ tối đa ~2s** sau khi handler success (vd `/forgot-password` trả 200 → user nhận email sau 0–2s). |
| `ExpiredOtpCleanupBackgroundService` | 24 giờ (chạy lúc 02:00 UTC) | Quét `Account` với `OtpExpiredAt + 24h < now` → clear `OtpCode`, `OtpExpiredAt`, `OtpPurpose`. Grace period 24h. | Transparent với FE — chỉ để giảm noise trong DB. |
| `PendingEmailCleanupBackgroundService` | 24 giờ (chạy lúc 02:00 UTC) | Quét `Account` với `PendingEmail != null` AND `OtpExpiredAt + 24h < now` (OTP `EmailChange` đã hết hạn lâu) → clear `PendingEmail`. | **Quan trọng**: User request `/me/change-email` nhưng không confirm OTP trong **24h** sau khi OTP hết hạn → PendingEmail bị xóa → phải gọi lại `/me/change-email` để khởi tạo flow mới. |
| `AccountHardDeleteBackgroundService` | 24 giờ (chạy lúc 02:00 UTC) | Quét `Account` với `IsDeleted = true` AND `DeletedAt + 90 ngày < now` → **HARD DELETE** row + cascade delete refresh tokens, profiles, backup codes, audit log entries của account. | **Quan trọng**: Sau 90 ngày soft-delete, account bị xóa hoàn toàn → `POST /api/auth/reactivate-request` không tìm thấy → user phải đăng ký mới với email khác. Window 90 ngày được tính từ `DeletedAt`, không phải `CreatedAt`. |

> **Initial delay**: 3 service cleanup (`ExpiredOtpCleanup`, `PendingEmailCleanup`, `AccountHardDelete`) tính `ComputeInitialDelay` để chạy lúc **02:00 UTC** kế tiếp (giờ ít traffic), sau đó tick 24h. Có nghĩa nếu app restart lúc 03:00 UTC, service đợi ~23h trước tick đầu tiên.

---

## Appendix C — `TokenRevocationMiddleware` + `RedisTokenRevocationStore` (#AUTH-54)

Mọi request `[Authorize]` đi qua middleware này SAU JWT bearer validation. 2 cơ chế blacklist độc lập:

### Cơ chế 1 — Per-`jti` blacklist (token-level)

| Field | Mô tả |
|---|---|
| Redis key | `revoked_jti:{jti}` |
| Value | reason string (default `"revoked"`) |
| TTL | thời gian còn lại của token (`token.ValidTo - UtcNow`); skip nếu TTL ≤ 0 (token đã expired tự nhiên) |
| Set bởi | `POST /api/auth/revoke` (`RevokeTokenCommandHandler`) — single user-facing revoke |
| Check bởi | `TokenRevocationMiddleware.IsRevokedAsync(jti)` — key exists → 401 |

### Cơ chế 2 — Bulk per-account cutoff (account-level)

| Field | Mô tả |
|---|---|
| Redis key | `account_revoke_cutoff:{accountIdNoHyphen}` (Guid `N` format, không có dash) |
| Value | Unix timestamp (UTC seconds) lúc bulk revoke được trigger |
| TTL | 1 giờ (= max access token life) |
| Set bởi | `POST /api/auth/logout` (`LogoutCommandHandler`), `PATCH /me/password` (`ChangePasswordCommandHandler`), `POST /reset-password` (`ResetPasswordCommandHandler`), `POST /admin/accounts/{id}/sessions/revoke-all` (`AdminRevokeAccountSessionsCommandHandler`) |
| Check bởi | `TokenRevocationMiddleware.IsAccountFullyRevokedAsync(accountId, tokenIat)` — `tokenIat < cutoff` → 401 |

**Quan trọng — bulk revoke semantic:**

Cutoff lưu **timestamp lúc revoke**, không phải "revoke all forever". Cơ chế so sánh: `token.iat < cutoff` thì token bị revoked. **Token issued SAU thời điểm revoke vẫn valid** — đây là intentional design:

- User đổi password lúc `t=100`: cutoff được set = 100, TTL = 1h.
- Access token issued lúc `t=50` (trước) → `iat=50 < cutoff=100` → blacklist → 401 với `errorCode: TOKEN_REVOKED_ACCOUNT`.
- User login lại lúc `t=200` (sau): access token mới có `iat=200 > cutoff=100` → pass middleware → OK.
- Sau `t=100 + 3600s = 3700`, cutoff key tự expire (TTL 1h). Token mới hoàn toàn không bị check nữa.

**Skip check khi nào:**
- Anonymous endpoint (`context.User.Identity?.IsAuthenticated != true`) → skip cả 2 cơ chế.
- Token không có claim `jti` → skip cơ chế 1.
- Token không có claim `iat` HOẶC không có claim `AccountId`/`nameid` → skip cơ chế 2.

**Hậu quả nếu Redis down:**

`IsRevokedAsync` và `IsAccountFullyRevokedAsync` sẽ throw `RedisException`. Middleware không catch → 500 lan đến client. **Fail-secure**: nếu Redis down, request authenticated đều fail, không có fallback "treat as not-revoked" (chống tình huống Redis down → bypass blacklist).

### `RevokeAllByAccountAsync` không revoke tokens issued sau cutoff

Pattern thường gặp gây confused:
1. User login từ Device A và Device B (2 access tokens active, 2 refresh tokens active).
2. User đổi password ở Device A → handler revoke 2 refresh tokens + set cutoff (#AUTH-54).
3. User refresh ở Device B → **refresh token đã revoked → 401 từ `/refresh-token`** (không phải từ middleware, mà từ handler).
4. User login lại ở Device B với password mới → access token mới có `iat > cutoff` → pass middleware → OK.

Tóm lại: cutoff chỉ tác động access token đã issued **trước** thời điểm cutoff. Refresh chain mới (sau login lại) không bị ảnh hưởng.

---

## Appendix D — `LoginAttemptNotificationHandler` (persist `LoginAttempt` rows)

Tương tự `AuditTrailNotificationHandler`:

| Field | Behavior |
|---|---|
| `AttemptedEmail` | Truncate 256 chars |
| `UserAgent` | Truncate 500 chars (resolve từ `HttpContext` headers) |
| `Note` | Truncate 500 chars |
| `IpAddress`, `DeviceId` | Resolve từ `ClientInfoHelper` (HttpContext) |
| `SaveChanges` | **KHÔNG** — relies on outer command handler |
| Throw on fail | **KHÔNG** — log error qua `ILogger`, business flow tiếp tục |

**Publish bởi**: `LoginCommandHandler` (mọi result: Success, WrongPassword, AccountLocked, AccountSuspended, AccountBanned, AccountInactive, AccountNotVerified, AccountNotFound) + `Verify2FALoginCommandHandler` (Success only).

**KHÔNG publish bởi**: `GoogleAuthCommandHandler`, `AcceptInviteCommandHandler`, `RefreshTokenCommandHandler`, `LogoutCommandHandler`, `ReactivateVerifyCommandHandler`. Nên `GET /me/login-history` không có entry cho các flow này — design decision (xem chi tiết ở section [`LoginAttemptDto.method`](#chi-tiết-loginattemptdto)).

---

## Appendix E — `AuthDataSeeder` (seed data ở startup)

`AuthDataSeeder` chạy **mỗi lần app startup** (sau khi migrate DB). Idempotent — không tạo trùng, chỉ update metadata + restore soft-deleted entries của seed data. Quan trọng cho devs hiểu account/role nào auto-tồn tại trong mọi environment.

### 1. **4 System Roles** (idempotent — restore nếu bị soft-deleted)

| Role | NormalizedName | ID (production via EF migration) | Description |
|---|---|---|---|
| Admin | `ADMIN` | `11111111-1111-1111-1111-111111111111` | Quản trị viên hệ thống, có toàn quyền |
| Manager | `MANAGER` | `22222222-2222-2222-2222-222222222222` | Quản lý vận hành và điều phối nhân sự |
| Staff | `STAFF` | `33333333-3333-3333-3333-333333333333` | Nhân viên vận hành hệ thống |
| Customer | `CUSTOMER` | `44444444-4444-4444-4444-444444444444` | Khách hàng sử dụng dịch vụ |

> **Quan trọng — ID stability**: ID của 4 system role được **hardcode trong EF migrations** (`HasData` seed). `AuthDataSeeder` ở startup chỉ **update metadata** (Name, Description, Status, IsSystemRole = true) nhưng giữ nguyên ID từ migrations. `RegisterCommandHandler`, `VerifyOtpCommandHandler`, `GoogleAuthCommandHandler` đều hardcode `CustomerRoleId = 44444444-...` để gán role mặc định cho self-register/Google flow — chỉ work khi migrations đã apply (production). Dev environment chỉ chạy seeder mà không apply migrations → role có Guid random → register fail.

### 2. **Legacy `TECHNICIAN` role migration** (#AUTH-staff-rename)

Nếu DB có role với `NormalizedName = "TECHNICIAN"` (legacy trước rename Staff):
- Nếu chưa có "STAFF" role → rename Technician → Staff (giữ ID, đổi name/normalizedName/description/status).
- Nếu đã có "STAFF" → soft-delete Technician (`IsDeleted = true`, `Status = Deprecated`).

### 3. **43 System Permissions** (idempotent)

43 entries từ `PermissionSeed.All` — xem [GET /api/admin/permissions](#get-apiadminpermissions) catalog. Mỗi entry có `IsSystemPermission = true` → admin không xóa được.

Seeder update existing entries' `Module` + `Description` mỗi startup (cho phép admin/devops update description qua source code).

### 4. **RolePermissions** (idempotent)

Map từ `PermissionSeed.RoleDefaults` (xem catalog) — Admin: all 43; Manager: 22; Staff: 6; Customer: 4.

Nếu role-permission row bị soft-deleted → restore. Nếu chưa tồn tại → add mới.

### 5. **Admin account** (idempotent, config-driven)

| Field | Default | Override env / config |
|---|---|---|
| Email | `admin@gmail.com` | `ADMIN_EMAIL` env var HOẶC `AdminSeed:Email` config |
| Password | `Admin123@` | `ADMIN_PASSWORD` env var HOẶC `AdminSeed:Password` config |
| FullName | `"System Admin"` | (không override) |
| EmailConfirmed | `true` | — |
| Status | `Active` | — |
| RoleId | (Admin role ID) | — |

> ⚠️ **SECURITY**: Default credentials phải được đổi ngay sau first deploy production. Trong staging/dev acceptable. Build script CI nên check + warn nếu default password còn ở production env.

Nếu admin account đã tồn tại theo email:
- Re-set `EmailConfirmed = true`, `Status = Active`, `IsDeleted = false`, `DeletedAt = null` (chống admin tự xóa nhầm).
- Force `RoleId = Admin role ID` (chống admin tự đổi role thành Customer rồi locked-out).
- **KHÔNG** reset password — đã set ban đầu là final.

### 6. **5 Sample accounts** (`@solarbattery.local`, password `Password123@`)

| Email | FullName | Role |
|---|---|---|
| `manager.demo@solarbattery.local` | "Demo Manager" | Manager |
| `staff.tier1@solarbattery.local` | "Staff Tier1 Generalist" | Staff (Generalist tier) |
| `staff.tier2@solarbattery.local` | "Staff Tier2 Specialist" | Staff (ModuleSpecialist tier) |
| `staff.tier3@solarbattery.local` | "Staff Tier3 Senior" | Staff (SeniorSpecialist tier) |
| `customer.demo@solarbattery.local` | "Demo Customer" | Customer |

> Sample accounts giúp FE/QA test các role flow mà không cần register thủ công. **KHÔNG** seed lên production — chỉ seed nếu config flag enable hoặc env `Development`.

### 7. **Sample LoginAttempts** (dev fixture)

Seed 1 lần (nếu bảng rỗng): 3 attempts cho admin (2 success + 1 wrong password) + 3 attempts/sample account (Password success + Google success + wrong password) + 1 AccountNotFound cho email lạ.

Giúp FE test `/me/login-history` UI ngay sau deploy mà không cần thực sự login nhiều lần.

### 8. **Staff profiles + Account profiles**

Auto-tạo profile rows cho admin + sample accounts. Staff sample được set:
- `tier1` → `StaffSkillTierEnum.Generalist`
- `tier2` → `ModuleSpecialist`
- `tier3` → `SeniorSpecialist`

---

## Appendix F — Misc patterns FE cần biết

### `OutboxRelayBackgroundService` retry policy

- Poll interval: 2s (config `Outbox:PollIntervalSeconds`).
- Batch size: configurable (`Outbox:BatchSize`).
- Max retries: configurable (`Outbox:MaxRetries`). Sau khi vượt → mark row `last_error` + skip (admin can investigate).
- Shutdown flush timeout: 5s — đảm bảo pending outbox messages publish trước khi app shutdown.

### `GlobalExceptionMiddleware` — 500 response shape

Mọi unhandled exception đi qua `GlobalExceptionMiddleware` (từ `SharedInfrastructure`) → trả response:

```json
{
  "isSuccess": false,
  "statusCode": 500,
  "message": "Đã có lỗi xảy ra. Vui lòng thử lại sau.",
  "data": null,
  "listErrors": null
}
```

Stack trace KHÔNG leak ra client (production). Log đầy đủ ở server với `correlationId` để truy ngược. FE có thể request `correlationId` header để báo support.

### `correlationId` propagation

Mọi request gắn `X-Correlation-Id` header (hoặc tự gen Guid nếu thiếu). CorrelationId được:
- Persist vào mọi `AuditLog` row (field `correlationId`).
- Include trong structured log entries.
- Propagate qua outbox messages (FE → AuthService → RabbitMQ → consumer services).

FE có thể gửi header này để debug end-to-end qua nhiều service.

---

## Appendix G — Security helper specs

Implementation chi tiết của các helper crypto/normalize. Quan trọng cho **FE input handling**, **security review**, và **timing analysis**.

### `PasswordHasher` (BCrypt)

| Property | Value |
|---|---|
| Algorithm | **BCrypt** (`BCrypt.Net.BCrypt` library) |
| Work factor (cost) | **12** (2^12 = 4096 iterations) |
| Salt | Tự động sinh per-hash (BCrypt embedded) |
| Verify time | ~100-200ms trên CPU server tier (tham chiếu cho enumeration delay #AUTH-17) |
| Empty input | `Hash` throws `ArgumentException`; `Verify` returns `false` (catch-all) |

> Cost 12 là **mức security tier** cho production 2024-2026 (recommended OWASP). Nâng lên 13/14 sẽ tăng exponentially verify time → trade-off với UX (login latency).

### `BackupCodeGenerator`

| Property | Value |
|---|---|
| Alphabet | `abcdefghjkmnpqrstuvwxyz23456789` — 31 chars |
| Excluded chars | `0`, `o`, `O`, `1`, `l`, `L`, `i`, `I` (confusable, gây typo) |
| Format | `xxxx-xxxx` (8 alphanum chars + 1 dash ở vị trí 4) = 9 chars hiển thị |
| Entropy | log2(31^8) ≈ **39.6 bits** per code — đủ chống brute-force kết hợp với 5-attempts/15min rate limit (#AUTH-45) |
| RNG | `RandomNumberGenerator.GetInt32()` (cryptographic) |
| Hash algorithm | BCrypt cost 12 (align với `PasswordHasher`) |
| Normalize rule | Lowercase + strip `-` + strip whitespace **trước khi** hash/verify |
| Count per regen | 8 codes (`BackupCodeCount`) |

**Quan trọng cho FE input UX:**
- User nhập `XXXX-XXXX` hoặc `xxxxxxxx` hoặc `xxxx xxxx` (space) hoặc `XxXx-XxXx` đều verify được — server normalize trước verify.
- FE hiển thị với **monospace font** + nhóm `xxxx-xxxx` để giảm typo.
- Hint message: *"8 ký tự không phân biệt hoa thường, dấu gạch ngang tùy chọn"*.

### `TotpService` (RFC 6238)

| Property | Value |
|---|---|
| Algorithm | HMAC-SHA1 (chuẩn Google Authenticator/Authy compat) |
| Secret length | **20 bytes** (160 bits) — Base32 encoded → ~32 chars |
| Digits | 6 |
| Period | 30 seconds |
| Clock drift tolerance | **±1 step** (default `allowedDriftSteps = 1`) → cửa sổ valid = ±30s mỗi bên thực ra ±60s effective |
| Verification window | OtpNet `VerificationWindow(previous: 1, future: 1)` |
| OtpAuth URI format | `otpauth://totp/{URI-encoded issuer}:{URI-encoded label}?secret={base32}&issuer={URI-encoded issuer}&algorithm=SHA1&digits=6&period=30` |
| Library | `Otp.NET` |

**Quan trọng cho FE UX:**
- ±1 step = user có thể nhập code valid trong khoảng ±30s từ current step. Nếu device clock skew > 30s, vẫn fail.
- Message *"Hãy kiểm tra thời gian thiết bị"* ở `/2fa/confirm` 422 fail message — đúng vì khả năng cao là clock skew.
- KHÔNG render Issuer + Label thẳng vào QR — phải URI-encode (handler đã làm — FE chỉ render URI).

### `EmailNormalizer` (#AUTH-39)

| Step | Rule |
|---|---|
| Input null/whitespace | Trả empty string |
| Step 1 | `Trim()` whitespace 2 đầu |
| Step 2 | `ToLowerInvariant()` (PostgreSQL mặc định case-sensitive — lowercase trước query để khớp index) |
| Validation | **KHÔNG** validate format ở normalize — chỉ chuẩn hóa. Format validation tách riêng ở command validator. |

> FE **KHÔNG cần** lowercase email trước submit — server tự normalize. Nhưng FE NÊN trim() để UX (tránh user copy-paste với trailing space, thấy submit fail).

### `PhoneNormalizer` (#AUTH-39, VN-specific)

| Pattern | Normalize result |
|---|---|
| `null` / `""` / whitespace | `""` (empty) |
| `0901234567` | `+84901234567` (VN assumption) |
| `84901234567` (không `+`) | `+84901234567` |
| `+84901234567` | `+84901234567` (unchanged) |
| `+1234567890` (foreign) | `+1234567890` (unchanged) |
| `1234567890` (foreign, không `+`) | `1234567890` (unchanged, caller validate) |
| `0 901 234 567` | `+84901234567` (strip whitespace) |
| `0-901-234-567` | `+84901234567` (strip `-`) |
| `0.901.234.567` | `+84901234567` (strip `.`) |
| `(090) 1234-567` | `+84901234567` (strip `()`, `-`) |

**Pre-cleanup regex**: `[\s\-\.\(\)]` (whitespace + `-` + `.` + `(` + `)`).

> FE **có thể** hiển thị raw input format (`0901234567`) nhưng nên lưu storage qua DB là `+84901234567` (server tự normalize trước AnyAsync check). Tránh duplicate phone do format khác nhau (`0901234567` vs `+84901234567`).

### `OtpHelper`

| Property | Value |
|---|---|
| RNG | `RandomNumberGenerator.Fill()` (cryptographic, KHÔNG phải `Random` class) |
| Length | 6 chars default (configurable, min 1) |
| Charset | `'0'`-`'9'` (chỉ chữ số) |
| Bias | Modulo bias minimal vì `byte % 10` (256 / 10 = 25.6 → ~3% bias) — acceptable cho OTP 6-digit (entropy ~20 bits) |

> Modulo bias là **known minor issue** nhưng với entropy 6-digit OTP (1/10^6 = 1/1,000,000) và rate limit 5 attempts/lockout, bias không phải attack vector trong context này.

---

## Appendix H — System endpoints (k8s probes + metrics)

AuthService expose **4 endpoint không có prefix `/api`** dành cho ops/k8s/Prometheus — KHÔNG yêu cầu auth, KHÔNG có rate limit. **Quan trọng cho FE/DevOps:**

### `GET /live` — Liveness probe (#AUTH-60)

| Aspect | Mô tả |
|---|---|
| Mục đích | k8s liveness probe — check app process còn alive không |
| Dependencies | KHÔNG check (`Predicate = _ => false`) — chỉ verify app responding HTTP |
| Response | Plain text `"Healthy"` HTTP 200 — KHÔNG phải JSON `CommonResponse` |
| Auth | Không yêu cầu |
| Use case | k8s `livenessProbe`: nếu fail nhiều lần → k8s restart pod |

### `GET /ready` — Readiness probe (#AUTH-60)

| Aspect | Mô tả |
|---|---|
| Mục đích | k8s readiness probe — check app **sẵn sàng nhận traffic** không |
| Dependencies | Check 3 deps tag `"ready"`: **PostgreSQL** + **Redis** + **RabbitMQ** (MassTransit) |
| Response thành công | Plain text `"Healthy"` HTTP 200 |
| Response fail | Plain text `"Unhealthy"` HTTP 503 (1 trong 3 deps không reachable) |
| Auth | Không yêu cầu |
| Use case | k8s `readinessProbe`: nếu fail → k8s loại pod khỏi load balancer rotation |

**Probe implementation (`PostgresHealthCheck`/`RedisHealthCheck`/`RabbitMqHealthCheck`):**
- **Postgres**: `DatabaseFacade.CanConnectAsync()` (= `SELECT 1`, không load EF metadata)
- **Redis**: `IConnectionMultiplexer.IsConnected` + `PING` command
- **RabbitMQ**: `IBus.GetProbeResult()` qua MassTransit

### `GET /health` — Full health report

| Aspect | Mô tả |
|---|---|
| Mục đích | Detailed status report — alias của `/ready` nhưng response chi tiết hơn |
| Response | JSON với từng check + duration + error details |
| Auth | Không yêu cầu — **CẢNH BÁO**: response có thể leak deps info (PostgreSQL version, Redis ping ms, RabbitMQ host) → DevOps nên k8s NetworkPolicy cấm public expose. |

### `GET /metrics` — Prometheus metrics

| Aspect | Mô tả |
|---|---|
| Mục đích | Expose Prometheus-format metrics cho scrape |
| Format | Prometheus text exposition format |
| Auth | Không yêu cầu — **CẢNH BÁO**: cùng issue privacy như `/health`. |
| Metric prefixes | `auth_login_total{result="..."}`, `auth_2fa_challenge_total{result="..."}`, `auth_refresh_token_total{result="..."}`, `auth_otp_usage_total{purpose="...",result="..."}`, `idempotency_replay_hits_total`, `idempotency_reservations_total`, `idempotency_conflicts_total` (#AUTH-78) |

**Metric label values (catalog):**

| Metric | Label | Values |
|---|---|---|
| `auth_login_total` | `result` | `success`, `success_2fa`, `invalid_credentials` |
| `auth_2fa_challenge_total` | `result` | `totp_success`, `totp_wrong`, `backup_success`, `backup_wrong`, `sms_success`, `sms_wrong` |
| `auth_refresh_token_total` | `result` | `success`, `reuse_detected` |
| `auth_otp_usage_total` | `purpose` | `register`, `password_reset` |
| `auth_otp_usage_total` | `result` | `verified`, `wrong` |
| `idempotency_*_total` | (no label) | counter only |

> **DevOps note**: Trong production k8s setup, `/health`, `/metrics`, `/ready` đều dùng probe internal traffic; KHÔNG nên route public qua Ingress. Idiomatic: dùng `serviceMonitor` (Prometheus operator) hoặc sidecar pull thay vì expose public.

---

## Appendix I — Global middleware behaviors

Pipeline (`Program.cs` line 130-148):

```
HTTPS redirection (skip nếu env=Docker)
  → CORS "AllowAll"
  → Rate limiter (per-endpoint policies)
  → JwtBearer Authentication
  → TokenRevocationMiddleware (#AUTH-54)
  → Authorization
  → IdempotencyKey middleware
  → MapControllers
```

### `Idempotency-Key` middleware (chống duplicate writes)

**Áp dụng GLOBAL** cho mọi endpoint `POST` / `PUT` / `PATCH` — không chỉ /register như docs sớm hơn ghi.

| Aspect | Mô tả |
|---|---|
| Header | `Idempotency-Key: <UUID v4 hoặc string>` (FE tự gen, gửi kèm request) |
| Cache | Response cache trong Redis qua `RedisIdempotencyKeyStore`, TTL = `Idempotency:TtlHours` config (default **24 giờ**) |
| Behavior khi duplicate | Request thứ 2 với cùng `Idempotency-Key` → trả response cũ NGAY (status + headers + body identical), KHÔNG re-execute handler. Metric `IdempotencyReplayHits` increment. |
| Conflict khi processing | Request 2 trong khi request 1 chưa response (reserve fail) → trả **409 Conflict** với body **chính xác**: `{"isSuccess":false,"message":"Request đang được xử lý với cùng Idempotency-Key."}`. Metric `IdempotencyConflicts` increment. **Lưu ý**: Body 409 này KHÔNG có `statusCode`/`data`/`listErrors` fields (raw string format, không qua `CommonResponseWriter`). |
| Áp dụng | Optional — endpoint không cần header này vẫn work (just no idempotency guarantee). |
| Config disable | `Idempotency:Enabled = false` (default `true`) — tắt toàn bộ middleware. Useful cho integration test environment. |
| Recommended endpoints | `/register`, `/forgot-password`, `/resend-otp`, `/resend-reset-otp`, `/admin/accounts`, `/admin/accounts/invite` — các endpoint create new resource |
| Race protection | Sau khi reserve fail, middleware **retry check cached response 1 lần** (cover race khi request 1 vừa save xong giữa 2 lần đọc). Nếu vẫn fail → 409. |

**FE pattern:**
```js
import { v4 as uuidv4 } from 'uuid';
const idempotencyKey = uuidv4(); // Gen 1 lần ở client
await api.post('/api/auth/register', payload, {
  headers: { 'Idempotency-Key': idempotencyKey }
});
// Nếu user click submit lại (vd mạng chậm), retry với cùng key → server không tạo trùng account.
```

### `AuditableEntityInterceptor` (soft delete + audit timestamps)

Mọi entity extend `AuditableEntity` (Account, Role, Permission, RolePermission, RefreshToken, AccountProfile, StaffProfile, StaffSkill, BackupCode) đi qua interceptor này khi SaveChanges:

| EntityState | Behavior |
|---|---|
| `Added` | Set `CreatedAt = UtcNow`. Set `CreatedBy = JWT.AccountId` nếu có, `Guid.Empty` nếu anonymous request. |
| `Modified` | Set `UpdatedAt = UtcNow`. (Không track `LastModifiedBy` — commented out trong code) |
| `Deleted` (default) | **Convert sang `Modified`** + set `IsDeleted = true`, `DeletedAt = UtcNow`. Đây là cơ chế soft delete chuẩn cho mọi entity. |
| `Deleted` (entity implements `IHardDeleteEntity`) | Real DELETE — interceptor không can thiệp. Hiện tại **chưa có** entity nào implement interface này trong AuthService — mọi soft delete. |

**Hậu quả cho code/docs đã viết:**
- `_unitOfWork.X.DeleteAsync(entity)` → KHÔNG xóa row, chỉ set `IsDeleted = true`. Đây là lý do các handler không cần explicit set `IsDeleted` (interceptor handle).
- Repository `GetAllAsync()` mặc định KHÔNG filter `IsDeleted` (project không dùng global query filter, xem `CLAUDE.md`) → handler PHẢI explicit `.Where(x => !x.IsDeleted)`.
- Reactivate flow (`/reactivate-request`/`/reactivate-verify`) dùng `IgnoreQueryFilters()` để bypass — nhưng project không có global query filter nên `IgnoreQueryFilters()` ở reactivate handler thực ra **no-op**. Vẫn safe vì code logic check `IsDeleted = true` explicit.

### CORS policy `"AllowAll"`

Hiện tại config CORS = **AllowAll** — permissive cho mọi origin/method/header. **Production lock down** qua config:
- Whitelist origins (vd `https://app.solarbattery.com`, `https://admin.solarbattery.com`)
- Allow methods: `GET, POST, PUT, PATCH, DELETE`
- Allow headers: `Authorization, Content-Type, Idempotency-Key, X-Challenge-Token, X-Correlation-Id`
- Allow credentials: `true` (cho cookie-based auth nếu có)

> **SECURITY review checkpoint**: AllowAll cho mọi cross-origin browser request bypass được CORS, nhưng KHÔNG bypass được auth (vẫn cần `Authorization` header). Tuy vậy enable cross-origin malicious sites đọc response 401/403 → có thể infer user state.

### Debug console output trong production code

`AuditableEntityInterceptor.cs:42, 47` có `Console.WriteLine` debug output (`"[AuditableEntityInterceptor] User found: {uid}..."`) — sẽ flood stdout trong production. **Recommendation**: thay bằng `ILogger.LogDebug` hoặc remove.

---

## Appendix J — Domain entity field details

DTOs exposed qua API đã document đầy đủ trong `DTOs dùng chung` section, nhưng **DB-level entity fields** có thêm thông tin không exposed nhưng FE/QA cần biết khi đọc audit log / debug.

### `RefreshToken` entity (DB schema vs `SessionDto`)

| Field | Type | Trong `SessionDto`? | Mô tả |
|---|---|---|---|
| `Id` | `Guid` | ✅ | Session ID |
| `AccountId` | `Guid` | ❌ | Owner account |
| `Token` | `string` | ❌ | **Hash** của plaintext refresh token (#AUTH-01) — KHÔNG bao giờ expose qua API |
| `JwtId` | `string?` | ❌ | Link tới access token JWT's `jti` claim (cho audit/correlation). Có thể null cho row legacy. |
| `IssuedAt` | `DateTime` | ✅ | Lúc cấp token UTC |
| `OriginalIssuedAt` | `DateTime?` | ❌ | (#AUTH-28) Lúc issue chain rotation gốc. `ExpiredAt = OriginalIssuedAt + 7d`. Null cho row legacy → fallback `IssuedAt`. |
| `ExpiredAt` | `DateTime` | ✅ | Lúc hết hạn UTC |
| `Status` | `RefreshTokenStatus` | ✅ | Trạng thái |
| `UsedAt` | `DateTime?` | ❌ | Thời điểm token được rotate (status chuyển `Active → Used`). Null nếu chưa rotate. |
| `RevokedAt` | `DateTime?` | ✅ | Thời điểm revoke |
| `RevokedReason` | `string?` | ✅ | Lý do revoke |
| `ReplacedByToken` | `string?` | ❌ | **Hash** của token mới trong chain rotation. Cho phép trace rotation chain ngược lên. Null nếu là token cuối hoặc chưa rotate. |
| `IpAddress` | `string?` | ✅ | IP lúc issue |
| `UserAgent` | `string?` | ✅ | UA lúc issue |
| `DeviceId` | `string?` | ✅ | Device ID |
| `CreatedAt`/`UpdatedAt`/`IsDeleted`/`DeletedAt`/`CreatedBy` | inherit `AuditableEntity` | ❌ | Audit fields chung |

**Computed properties** (không lưu DB, tính từ fields khác):
- `IsExpired` = `UtcNow >= ExpiredAt`
- `IsActive` = `Status == Active && !IsExpired`

> FE muốn check session active không thay vì call `/sessions/me` rồi filter, dùng `data.status == 1 && data.expiredAt > now`.

### `OutboxMessage` entity (DB schema cho outbox pattern)

| Field | Type | Mô tả |
|---|---|---|
| `Id` | `Guid` | Message ID |
| `EventType` | `string` | Tên event (vd `"SendOtpRegisterEvent"`, `"AccountActivatedEvent"`) |
| `Payload` | `string` (JSON) | Serialized event data |
| `OccurredAt` | `DateTime` | Lúc handler tạo message |
| `ProcessedAt` | `DateTime?` | Lúc `OutboxRelay` publish thành công lên RabbitMQ. **Null = pending**. |
| `RetryCount` | `int` | Số lần `OutboxRelay` retry. Tăng khi RabbitMQ publish fail. Vượt `Outbox:MaxRetries` → skip + log warning. |
| `LastError` | `string?` | Exception message từ lần fail gần nhất. Null nếu chưa fail hoặc đã success. |
| `CreatedAt`/`UpdatedAt`/`IsDeleted`/`DeletedAt`/`CreatedBy` | inherit `AuditableEntity` | Audit fields chung |

> **Debug pattern**: Email không tới user → query `OutboxMessage WHERE EventType='SendOtpRegisterEvent' AND ProcessedAt IS NULL` → nếu có row → RabbitMQ down hoặc consumer not running. `WHERE LastError IS NOT NULL` → tìm exception cụ thể.

> **OutboxRelay cleanup**: Code hiện tại **KHÔNG có** cleanup background job cho `OutboxMessage` đã processed. DB sẽ grow theo thời gian. **Recommendation**: thêm cleanup job xóa rows `WHERE ProcessedAt IS NOT NULL AND ProcessedAt < now - 30 days` (similar pattern với 5 background services hiện có).

---

## Appendix K — Database constraints (partial unique indexes)

PostgreSQL **partial unique indexes** (qua EF `HasFilter()`) ảnh hưởng trực tiếp behavior API. **Quan trọng cho FE/QA test edge case.**

### `accounts` table

| Column | Unique? | Filter (partial) | Hậu quả nghiệp vụ |
|---|---|---|---|
| `email` | ✅ | `WHERE is_deleted = false` | **Email được "release" sau soft-delete** — user soft-deleted account, sau đó email đó có thể register lại bởi user khác hoặc reactivate. **Đây là gap với `/reactivate-request`**: nếu user A soft-delete email X lúc t=0, user B register email X lúc t=10, user A gọi `/reactivate-request` lúc t=20 → tìm thấy account A soft-deleted (90-day window) → reactivate sẽ **conflict với account B đang active** → `taken` check tại reactivate-verify sẽ fail. **FE UX**: hiện message *"Email đã được dùng bởi tài khoản khác trong lúc bạn chờ. Không thể khôi phục."* |
| `phone_number` | ✅ | `WHERE phone_number IS NOT NULL` | Multi null phone OK. Phone đã set thì unique cross active+deleted accounts (different from email!). |
| `google_id` | ✅ | `WHERE google_id IS NOT NULL` | Multi null OK. Google ID đã link thì 1 Google account = 1 Auth account globally (#AUTH-20). |
| `invitation_token` | (non-unique index) | `WHERE invitation_token IS NOT NULL` | Index for invitation lookup performance. Token không enforce unique ở DB level — handler check before insert. |
| `status`, `is_deleted`, `role_id` | (non-unique index) | — | Query performance cho admin filter. |
| `(email, is_deleted)` | (non-unique composite) | — | Optimize lookup pattern `WHERE email = X AND !IsDeleted`. |

### `refresh_tokens` table

| Column | Unique? | Mô tả |
|---|---|---|
| `token` (= hash) | ✅ | Hash của refresh token. Collision rate ~0 với Guid `N` format + hash. |
| `account_id` | (index) | Lookup pattern "all sessions of user". |
| `jwt_id` | (index) | Audit correlation — find refresh token tương ứng với access token's jti. |
| `status`, `expired_at` | (index) | Background cleanup + active session count. |
| `(account_id, status)` | (composite) | Session limit check pattern. |

### `permissions` table

| Column | Unique? | Mô tả |
|---|---|---|
| `code` | ✅ | Permission code (vd `user.view`) globally unique. |
| `module` | (index) | Filter by module (vd Battery, Ticket). |

### `account_profiles` table

| Column | Unique? | Mô tả |
|---|---|---|
| `account_id` | ✅ | **1-to-1** Account ↔ Profile relationship. Một account chỉ có 1 profile. Handler tự tạo profile mới nếu chưa có khi gọi `/me/profile` hoặc `/me/avatar`. |
| `avatar_file_id` | (index) | `WHERE avatar_file_id IS NOT NULL` — partial. Multi null OK (chưa upload avatar). |

### `audit_logs` table

| Column | Index | Mô tả |
|---|---|---|
| `target_account_id`, `actor_account_id`, `action`, `created_at` | individual | Filter pattern. |
| `(target_account_id, created_at)` | composite | Optimize `GET /admin/audit-logs/by-account/{id}` (sort desc). |

### `login_attempts` table

| Column | Index | Mô tả |
|---|---|---|
| `account_id`, `attempted_email`, `ip_address` | individual | Forensic lookup. |
| `(account_id, created_at)` | composite | Optimize `GET /me/login-history` (sort desc). |

### `backup_codes` table

| Column | Index | Mô tả |
|---|---|---|
| `(account_id, redeemed_at)` | composite | Pattern "find unredeemed codes for account". |
| `is_deleted` | individual | Cleanup pattern. |

---

## Appendix L — MediatR ValidationBehavior (pipeline)

`ValidationBehavior<TRequest, TResponse>` chạy **trước mọi command/query handler** trong MediatR pipeline.

### Generic constraints

```csharp
where TResponse : CommonResponseBase
where TRequest : IRequest<TResponse>
```

→ Chỉ áp dụng cho command/query có response inherit `CommonResponseBase`. Notification handlers (`AuditTrail`, `LoginAttempt`, `SessionCreated`) **không** đi qua behavior này.

### Flow

```
Request → ValidationBehavior:
  ├─ Request implements IValidatable<TResponse>?
  │   ├─ Yes: call ValidateAsync() →
  │   │     ├─ result.IsSuccess = false → SHORT-CIRCUIT, return result. Handler không chạy.
  │   │     └─ result.IsSuccess = true → tiếp tục pipeline.
  │   └─ No: skip validation, tiếp tục pipeline.
  └─ → next() (handler hoặc next behavior)
```

> **Important**: Validation pipeline short-circuit return chính `result` (`CommonResponseBase` subclass). Status code đã set trong `ValidateAsync()` (vd 400 hoặc 422) sẽ là HTTP status final. Controller chỉ wrap `StatusCode(result.StatusCode, result)`.

### Commands implement `IValidatable<TResponse>` (verified):

`RegisterCommand`, `LoginCommand`, `Verify2FALoginCommand`, `Disable2FACommand`, `Confirm2FACommand`, `RegenerateBackupCodesCommand`, `AcceptInviteCommand`, `VerifyResetOtpCommand`, `ResetPasswordCommand`, `LogoutCommand`, `Init2FACommand` (nhưng chỉ check AccountId), `ChangePasswordCommand`, `UpdateMyProfileCommand`, `UpdateAccountCommand`, `UpdateStaffProfileCommand`, `AddStaffSkillCommand`, `SetMyAvatarCommand`, `ChangeEmailCommand`, `ConfirmEmailChangeCommand`, `ChangeAccountStatusCommand`, `ChangeAccountRoleCommand`, `ChangeRoleStatusCommand`, `UpdateRoleCommand`, `CreateRoleCommand`, `InviteAccountCommand`, `SetRolePermissionsCommand`, `AdminRevokeAccountSessionsCommand`, …

### Commands KHÔNG implement (handler tự validate):

`RefreshTokenCommand` (chỉ kiểm tra string empty trong handler), `GoogleAuthCommand`, `GoogleCallbackCommand`, `LinkGoogleCommand` (chỉ check Guid.Empty), `UnlinkGoogleCommand`, `Request2FASmsCommand`, `RevokeTokenCommand`, `IntrospectTokenCommand`, `ReactivateRequestCommand`, `ReactivateVerifyCommand`, `Enable2FACommand` (deprecated), `DeactivateMeCommand`, `DeleteMeCommand`, `DeleteAccountCommand`, `UnlockAccountCommand`, `DeleteRoleCommand`, `DeleteStaffSkillCommand`, `RevokeSessionCommand`, `RevokeAllSessionsCommand`, `ForgotPasswordCommand`, `ResendOtpCommand`, `ResendResetOtpCommand`, `SendPhoneOtpCommand`, `VerifyPhoneOtpCommand`, `VerifyOtpCommand`.

> **Hậu quả**: với command **không có** `IValidatable`, field-level errors **KHÔNG có** `listErrors` array — handler tự set message-only. FE check `data?.listErrors` thấy `null` thì biết là handler-level error.

---

## Appendix M — `CommonResponseWriter` (middleware response writer)

Helper static method dùng bởi `JwtBearer`, `TokenRevocation` và bất kỳ middleware nào cần ghi error response:

### Signature

```csharp
public static async Task WriteAsync(
    HttpResponse response,
    int statusCode,
    string message,
    IEnumerable<Errors>? errors = null,
    object? data = null)
```

### Hành vi

| Property | Value |
|---|---|
| JSON serialization | `JsonNamingPolicy.CamelCase` (`isSuccess` không phải `IsSuccess`) |
| `DefaultIgnoreCondition` | `Never` → **mọi field xuất hiện trong JSON kể cả null** (vd `data: null, listErrors: null` luôn xuất hiện) |
| `ContentType` | `application/json; charset=utf-8` |
| `IsSuccess` | Luôn `false` (helper chỉ dùng cho error response) |
| Idempotent | Skip write nếu `response.HasStarted` (tránh "Response already started" exception) |
| `ListErrors` shape | `errors?.ToList() ?? new List<Errors>()` → **empty list** nếu null, KHÔNG phải `null`. `ErrorsListJsonConverter` sau đó convert empty list → JSON `null`. |

### Response shape cố định (cho 401/403 từ middleware)

```json
{
  "isSuccess": false,
  "statusCode": 401,
  "message": "<message>",
  "data": <object|null>,
  "listErrors": null
}
```

> **Lưu ý FE**: `data` có thể là `null` HOẶC `{errorCode: "..."}` (như `JwtBearerEvents` truyền `data: new { errorCode }`) → FE parse `response.data?.errorCode` an toàn với `?.` optional chaining.

---

## Appendix N — `ICurrentUserService` (actor resolution)

Interface duy nhất:

```csharp
public interface ICurrentUserService
{
    string? UserId { get; }
}
```

### Implementation

```csharp
public string? UserId => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
```

→ Đọc claim `nameid` (từ JWT). **Lưu ý**: JWT có cả `nameid` (`ClaimTypes.NameIdentifier`) và custom `AccountId` claim — cả 2 đều cùng giá trị `accountId.ToString()` (xem JWT structure section).

### Dùng bởi

1. **`AuditableEntityInterceptor`**: set `CreatedBy` khi entity Added. Nếu null → `Guid.Empty`.
2. **`AuditTrailNotificationHandler`**: fallback actor resolution khi `ActorAccountIdOverride` null.
3. **`ChangeAccountRoleCommandHandler`**: set `RoleAssignedBy` field (đọc trực tiếp claim `AccountId` thay vì qua `ICurrentUserService` — minor inconsistency).
4. **`RevokeSessionCommandHandler`** + **`RevokeAllSessionsCommandHandler`**: get current user để check ownership.

### Edge case

| Scenario | UserId value | Hậu quả |
|---|---|---|
| HttpContext null (vd background service, MediatR call ngoài request scope) | `null` | `AuditableEntityInterceptor` set `CreatedBy = Guid.Empty` |
| JWT có claim `nameid` valid | `<accountId guid string>` | Resolve correctly |
| Anonymous request (no JWT, vd `/register`, `/login`) | `null` | Tương tự HttpContext null case |
| JWT bị tampered nhưng pass signature (chỉ test scenario) | `<tampered guid>` | Resolver trust JWT — đây là expected, sau JWT validation thì claim được trust |

---

## Appendix O — Redis state catalog + security primitives

Mọi Redis key được dùng bởi AuthService, với TTL và purpose. **Quan trọng cho DevOps debug + security review.**

### Redis key namespace

| Key pattern | Type | TTL | Set bởi | Mô tả |
|---|---|---|---|---|
| `revoked_jti:{jti}` | String | Remaining token life | `/auth/revoke` handler | (#AUTH-54) Per-token blacklist. Value = reason string. |
| `account_revoke_cutoff:{accountIdNoHyphen}` | String | 1 giờ | `/auth/logout`, `/me/password`, `/reset-password`, admin force-logout | (#AUTH-54) Account-level bulk cutoff. Value = Unix timestamp giây. |
| `2fa:challenge:{token}` | **Hash** với fields `data` (JSON) + `attempts` (int) | 5 phút | `LoginCommandHandler` khi 2FA on | (#AUTH-58) Challenge state cho login step 2. Token = Guid `N` format (32 hex). |
| `2fa:account:{accountIdNoHyphen}:challenges` | **Set** chứa challenge tokens | challenge TTL + 1 phút buffer | Cùng challenge creation | Secondary index cho `InvalidateByAccountAsync` (logout invalidate flow). |
| `2fa:pending:{accountId}` | String (JSON `{Secret, PendingToken, CreatedAtUtc}`) | 10 phút | `/2fa/init` handler | Pending state enrollment 2FA. Dùng `IDistributedCache` (đơn giản, không cần atomic counter). |
| `2fa:sms_otp:{challengeToken}` | String | 3 phút | `/login/2fa/sms` handler | OTP SMS cho 2FA fallback. Plain OTP string (không hash — chống bypass via timing). |
| `pwd_reset_used:{jti}` | String | Remaining reset token life | `/reset-password` handler | (#AUTH-06) Single-use enforcement cho reset token. SET NX guarantee atomic. |
| `idempotency:{header_value}` | String (response cached) | 24 giờ (config) | `IdempotencyKeyMiddleware` | Cache response cho duplicate detect. |
| `email_reserve:{sha256(email)[..16]}` | String | 10 phút | `/me/change-email` handler | (#AUTH-24) Owner-aware email reservation. Value = `accountIdNoHyphen`. |
| `backup_code_attempts:{accountIdNoHyphen}` | String (counter) | 15 phút | `Verify2FALoginCommandHandler` | (#AUTH-45) Per-account backup code rate limit. INCR. |

> **DevOps debug pattern**: `redis-cli KEYS "2fa:challenge:*"` để list active challenges. `redis-cli HGETALL "2fa:challenge:{token}"` để inspect attempts counter + payload JSON.

### `RefreshTokenHasher` (#AUTH-01)

| Property | Value |
|---|---|
| Algorithm | **SHA-256** (no salt) |
| Output | Hex lowercase, 64 chars |
| Justification no-salt | Plaintext refresh token = Guid `N` format = **128 bit entropy** unguessable. Không có user input → không cần defense against rainbow tables. |
| Implementation | `Convert.ToHexString(SHA256.HashData(UTF8.GetBytes(plainToken))).ToLowerInvariant()` |

> **Security review**: SHA-256 no-salt là **acceptable** cho high-entropy random tokens, KHÔNG dùng cho password. PenTester có thể flag → docs này là defense.

### `TwoFactorSecretProtector` (ASP.NET Data Protection wrapper)

| Property | Value |
|---|---|
| Backend | `IDataProtector` từ ASP.NET Core Data Protection |
| Purpose string | `"AuthService.Account.TwoFactorSecret.v1"` (DataProtection scopes keys by purpose) |
| Format | `enc:v1:{base64(ciphertext)}` |
| Key rotation | Hỗ trợ via prefix versioning. Future `enc:v2:` dùng key mới, `Unprotect` detect prefix → routing. |
| Lazy plaintext handling | Nếu không có prefix `enc:v1:` → `Unprotect` return as-is (legacy plaintext) → caller (`Verify2FALoginCommandHandler`) tự re-encrypt sau verify thành công (#AUTH-22). |
| `IsProtected(value)` | `value.StartsWith("enc:v1:")` |

> ⚠️ **DataProtection key store**: ASP.NET DataProtection mặc định dùng filesystem (ephemeral trong container) → app restart có thể mất key → existing 2FA secrets không decrypt được → users phải reset 2FA. **Production**: persist keys qua Redis/file mount với `services.AddDataProtection().PersistKeysToXXX(...)`. Hiện code chưa thấy explicit persist config → cần verify deployment setup. **DevOps check**: nếu thấy users báo "2FA TOTP code luôn fail sau deploy" → check DataProtection key store config.

### Store implementations summary

| Store | Library | Underlying primitive | Atomic operations |
|---|---|---|---|
| `TokenRevocationStore` | StackExchange.Redis | `StringSetAsync` + `KeyExistsAsync` | SET (no NX), GET |
| `TwoFactorChallengeStore` | StackExchange.Redis | `HashSetAsync`, `HashGetAllAsync`, `HashIncrementAsync` (atomic INCR for attempts) | HINCRBY |
| `TwoFactorPendingStore` | `IDistributedCache` (abstraction over Redis) | `SetStringAsync`, `GetStringAsync`, `RemoveAsync` | None (overwrite OK — init lần 2 = invalidate token cũ) |
| `RedisTwoFactorSmsOtpStore` | StackExchange.Redis | `StringSetAsync`, `StringGetAsync`, `KeyDeleteAsync` | None |
| `RedisIdempotencyKeyStore` | StackExchange.Redis | SET NX (reserve), then SET (save response) | SETNX |

---

## Appendix P — Deployment health checklist

Items DevOps PHẢI verify ở mỗi production deploy (consolidate từ findings 21 round):

1. **`JwtSettings:SecretKey`** ≥ 32 chars (app startup fail nếu thiếu).
2. **`JwtSettings:Issuer`** + **`Audience`** set.
3. **`ADMIN_EMAIL`** + **`ADMIN_PASSWORD`** env vars set **khác default** (`admin@gmail.com`/`Admin123@`) cho production.
4. **`GoogleOAuth:RedirectUri`** + (optional) **`AllowedRedirectUris`** array set + match Google Console config.
5. **`GoogleOAuth:ClientId`** + **`ClientSecret`** set.
6. **`Redis:ConnectionString`** reachable từ pod — verify qua `/ready` probe.
7. **`RabbitMQ:*`** config + reachable — verify qua `/ready`.
8. **`Outbox:PollIntervalSeconds`** = 2 (production), có thể tăng cho dev.
9. **CORS** policy không phải `AllowAll` cho production — whitelist origins cụ thể.
10. **DataProtection key store** persist (Redis/file mount) — TRÁNH user bị mất 2FA sau restart.
11. **`Idempotency:Enabled`** = true cho production.
12. **`Session:MaxConcurrentSessions`** = 5 (default) hoặc theo policy.
13. **`AuthSecurity:EnforceDeviceBinding`** — quyết định bật/tắt theo mobile UX requirement.
14. **`PasswordPolicy:*`** — verify match security policy.
15. **Migrations** đã apply (Customer role ID `44444444-...` phải tồn tại — register sẽ fail nếu không).
16. **Health probes** k8s: `livenessProbe` → `/live`, `readinessProbe` → `/ready`.
17. **Prometheus scrape** `/metrics` qua serviceMonitor (KHÔNG public expose).
18. **Console.WriteLine debug** trong `AuditableEntityInterceptor` — flag để clean up (flood stdout).

---

## Appendix Q — Integration events catalog (RabbitMQ outbox + consumers)

### Events PUBLISHED bởi AuthService → RabbitMQ outbox

| Event | Payload fields | Published bởi handler | Consumed bởi (downstream) |
|---|---|---|---|
| `SendOtpRegisterEvent` | `ToEmail`, `Otp` | `RegisterCommandHandler`, `ResendOtpCommandHandler` | **EmailService** → `SendOtpRegisterConsumer` (template `OtpRegister.html`) |
| `SendPasswordResetOtpEvent` | `ToEmail`, `Otp` | `ForgotPasswordCommandHandler`, `ResendResetOtpCommandHandler`, **`ReactivateRequestCommandHandler`** (re-use enum) | **EmailService** → `SendPasswordResetOtpConsumer` (template **`OtpPasswordReset.html`** — mới có từ Sprint 6.2 NOTI-09) |
| `SendEmailChangeOtpEvent` | `ToNewEmail`, `Otp` | `ChangeEmailCommandHandler` | **EmailService** → `SendEmailChangeOtpConsumer`, gửi tới email **mới** (template **`OtpEmailChange.html`** — mới có từ Sprint 6.2 NOTI-09) |
| `SendSmsCommand` | `PhoneNumber`, `Message`, `SourceService`, `CorrelationId`, `Category` | `Request2FASmsCommandHandler` (`category="2fa_sms"`), `SendPhoneOtpCommandHandler` (`category="phone_verify"`) | SmsService |
| `SendAdminInviteEvent` | (admin invite details + token) | `InviteAccountCommandHandler` | **EmailService** → `SendAdminInviteConsumer` (template `AdminInvite.html`) |
| `AccountActivatedEvent` | `AccountId`, `Email`, `FullName`, `PhoneNumber`, `Role`, `CreationSource` | `VerifyOtpCommandHandler` (`SelfRegister`), `AcceptInviteCommandHandler` (`AdminInvite`), `GoogleAuthCommandHandler` (`GoogleOAuth`) | NotificationService (`AccountActivatedConsumer` → in-app; `AccountActivatedSyncConsumer` → read-model) |
| `AccountDeletedEvent` | `AccountId`, `Email`, `DeletionSource` | `DeleteMeCommandHandler` (`SelfDelete`), `DeleteAccountCommandHandler` (`AdminDelete`) | NotificationService (`AccountDeletedSyncConsumer` → xoá read-model) — ✅ **NOTI-17 (#688) đã verify 30/07/2026: publisher tồn tại thật ở cả 2 handler, consumer giữ nguyên, không phải orphan** |
| `RefreshTokenReuseDetectedEvent` | `AccountId`, **`Email`** ⬅ *Sprint 6.2*, `ReusedTokenId`, `IpAddress`, `UserAgent`, `DetectedAt`, `RevokedFamilyCount` | `RefreshTokenCommandHandler` (#AUTH-79) | **EmailService** → `RefreshTokenReuseDetectedConsumer` (Sprint 6.2 NOTI-04), Grafana |
| `SuspiciousLoginDetectedEvent` | `AccountId`, `Email`, `IpAddress`, `UserAgent`, `Reason`, `DetectedAt` | `AuthTokenIssuer` (#AUTH-52, từ `LoginCommandHandler` + `Verify2FALoginCommandHandler`) | **EmailService** → `SuspiciousLoginDetectedConsumer` (Sprint 6.2 NOTI-04) |
| `StaffProfileUpdatedEvent` | (staff profile fields) | `UpdateStaffProfileCommandHandler` | TicketService (sync staff routing config), ManagerDashboard |

> ⚠️ **Sửa 30/07/2026 — cột "Consumed bởi" của 5 event email trước đây ghi nhầm `NotificationService`.**
> Toàn bộ email **giao dịch** (OTP đăng ký / đặt lại mật khẩu / đổi email / mời admin) đi thẳng
> **AuthService → EmailService**, KHÔNG qua NotificationService. Chỉ email **thông báo nghiệp vụ**
> (SLA, pin, chat, saga…) mới đi qua NotificationService rồi publish `SendNotificationEmailEvent`.

#### Sprint 6.2 NOTI-09 (#680) — 2 template email thiếu, gây email SAI NGỮ CẢNH

`EmailTemplates.OtpPasswordReset` và `EmailTemplates.OtpEmailChange` đã được tham chiếu trong code từ
lâu, nhưng **2 file HTML tương ứng chưa tồn tại** trong
`EmailService.Api/wwwroot/email-templates/`. Renderer không tìm thấy file ⇒ rơi về
`OtpRegister.html`, nên:

- user bấm **"Quên mật khẩu"** nhận email có nội dung **"đăng ký tài khoản"**;
- user **đổi email** cũng nhận đúng email đó.

Sai ngữ cảnh kiểu này **dễ bị nghi là phishing** và làm người dùng bỏ dở luồng.

**Đã thêm:** `OtpPasswordReset.html`, `OtpEmailChange.html`.
`SendEmailChangeOtpConsumer` truyền thêm placeholder **`PendingEmail` = `ToNewEmail`** để email hiển
thị rõ địa chỉ đích — thiếu key này thì placeholder hiện nguyên văn trong thư gửi đi.

> Ảnh hưởng tới endpoint: `POST /api/auth/forgot-password`, `POST /api/auth/resend-reset-otp`,
> `POST /api/auth/reactivate-request` (dùng chung `SendPasswordResetOtpEvent`) và
> `POST /api/accounts/me/change-email` (`SendEmailChangeOtpEvent`). **Contract REST không đổi** —
> chỉ nội dung email user nhận được là đúng ngữ cảnh.

#### Sprint 6.3 NOTI3-05 (#705) — `IEmailProvider`

6 consumer email của EmailService nay phụ thuộc **`IEmailProvider`** thay vì lớp cụ thể
`EmailSenderService`. Hành vi **không đổi** (Mailjet vẫn là hiện thực duy nhất, `ProviderName = "mailjet"`);
mục đích là tách sẵn ranh giới để cắm provider thứ hai sau này chỉ cần đổi một dòng đăng ký DI, không
phải mở lại business logic. Đăng ký:
`builder.Services.AddTransient<IEmailProvider>(sp => sp.GetRequiredService<EmailSenderService>())`.

Giới hạn ghi nhận có chủ đích (R-44): **Mailjet vẫn là single point of failure** — không mua provider
thứ hai vì ngoài ngân sách đồ án.

Interface có **2 overload `SendAsync`**: bản cũ (không header) và bản mới nhận
`IReadOnlyDictionary<string,string>? headers` — dùng cho `List-Unsubscribe` (Sprint 6.3 NOTI3-15).
Tách overload thay vì đổi chữ ký cũ để **6 consumer email giao dịch không phải sửa gì**, và để việc
"email này có nút huỷ" là một quyết định hiện rõ ở chỗ gọi. Mailjet v3.1 nhận header tuỳ ý qua trường
`Headers`; khi không có header, payload gửi đi **giữ nguyên như cũ**.

> **Email giao dịch của AuthService (OTP, reset, đổi email, mời admin) KHÔNG có `List-Unsubscribe`** —
> người dùng không thể "huỷ đăng ký" khỏi mã xác thực do chính họ yêu cầu. Chỉ email thông báo nghiệp
> vụ đi qua NotificationService mới có.

#### Sprint 6.2 NOTI-04 (#675) — cảnh báo bảo mật nay THỰC SỰ tới hộp thư user

**Trước sprint này:** `AuthTokenIssuer` phát hiện login từ IP/User-Agent lạ (đối chiếu 50 session gần
nhất) và `RefreshTokenCommandHandler` phát hiện replay attack — cả hai publish event đúng, đã revoke
token đúng — nhưng **KHÔNG service nào consume**. Công detect bỏ đi: nạn nhân chỉ thấy mình "bị
logout" không rõ lý do và **mất cơ hội đổi mật khẩu kịp thời**.

| Event | Consumer mới (EmailService) | Template | Tiêu đề email |
|---|---|---|---|
| `SuspiciousLoginDetectedEvent` | `SuspiciousLoginDetectedConsumer` | `SuspiciousLogin.html` | `[Cảnh báo bảo mật] Đăng nhập mới trên tài khoản của bạn - {AppName}` |
| `RefreshTokenReuseDetectedEvent` | `RefreshTokenReuseDetectedConsumer` | `RefreshTokenReuse.html` | `[Cảnh báo bảo mật] Phiên đăng nhập đáng ngờ - {AppName}` |

**Vì sao đi thẳng EmailService (không qua NotificationService):** đây là email **bảo mật**, phải tới
ngay và **không được phụ thuộc preference / quiet hours / digest / hạn mức** của user. Cũng vì vậy
**cố ý KHÔNG thêm giá trị `NotificationTypeEnum`** cho 2 loại này — tránh đẻ thêm enum không producer.

**⚠️ Breaking — `RefreshTokenReuseDetectedEvent` thêm field `Email` (vị trí thứ 2):**
record positional nên chữ ký constructor đổi. Lý do: EmailService **không có DB account** nên không
tra ngược được địa chỉ từ `AccountId`. `RefreshTokenCommandHandler` nay truy vấn
`Accounts.Where(a => a.Id == existing.AccountId && !a.IsDeleted).Select(a => a.Email)` trước khi
publish; không tìm thấy → truyền `string.Empty` và consumer **bỏ qua + log Warning** (không gửi email
mù). `SuspiciousLoginDetectedEvent` đã mang sẵn `Email` từ trước theo cùng lý do.

**Dedup:** cả 2 consumer dùng Redis inbox `ProcessOnceAsync(IInboxStore, consumerName, …)` như 4
consumer OTP (key `inbox:{consumerName}:{messageId:N}`, TTL 7 ngày).

**Nội dung `Reason` được diễn giải sang tiếng Việt** trong email:

| `reason` (event) | Hiển thị |
|---|---|
| `new_ip` | "địa chỉ IP lạ" |
| `new_user_agent` | "thiết bị / trình duyệt lạ" |
| `new_ip_and_user_agent` | "cả địa chỉ IP lẫn thiết bị đều lạ" |
| khác / null | "dấu hiệu bất thường" |

`DetectedAt` format `dd/MM/yyyy HH:mm:ss 'UTC'`. Email `RefreshTokenReuse` hiển thị thêm
`RevokedSessions` = `RevokedFamilyCount` (số phiên bị thu hồi).

### Events CONSUMED bởi AuthService ← RabbitMQ

| Event | Consumer | Behavior |
|---|---|---|
| `PermissionsChangedEvent` | `PermissionsChangedConsumer` (#AUTH-15) | Invalidate `PermissionResolver` in-memory cache. **Per-instance**: mỗi BE replica consume riêng → mỗi replica invalidate cache local. Nếu `RoleCode` rỗng → `InvalidateAll()`; có RoleCode → `InvalidateRole(roleId)` (lookup theo NormalizedName). Nếu RoleCode không tồn tại → fallback `InvalidateAll()` as safety net. |

> ⚠️ **GAP nghiêm trọng — `PermissionsChangedEvent` consumer exists nhưng KHÔNG handler nào trong AuthService publish event này.**
>
> Round 9 docs claim đúng: cache chỉ invalidate qua 5-min TTL (không qua push event) — vì `SetRolePermissionsCommandHandler` không publish.
>
> Architectural intent (#AUTH-15) là: `SetRolePermissions` publish event → RabbitMQ broadcast → mỗi BE replica consumer invalidate cache → instant sync across replicas. **Implementation incomplete** — publish side missing.
>
> **Production impact**: Multi-replica deploy → mỗi replica cache **độc lập**, sync chỉ qua 5-min TTL từng replica. Admin đổi permission → user trên replica A có thể thấy permission mới sau 1 phút, user trên replica B sau 4 phút (cache age khác nhau).
>
> **Fix**: thêm `_messageProducer.PublishAsync(new PermissionsChangedEvent(...))` vào `SetRolePermissionsCommandHandler` và `ChangeAccountRoleCommandHandler`.

### Events tồn tại trong SharedContracts nhưng **KHÔNG được publish/consume** trong AuthService

| Event | Status | Note |
|---|---|---|
| `AccountStatusChangedEvent` | Defined nhưng unused | `ChangeAccountStatusCommandHandler` chỉ publish `AuditTrailNotification`, không publish event ra outbox. Downstream services muốn react tới status change phải poll DB hoặc dùng audit log subscription. |
| `SendPhoneOtpEvent` | **Deprecated — nay hoàn toàn CHẾT** | Code comment trong `SendPhoneOtpCommandHandler.cs:66` xác nhận: *"SendPhoneOtpEvent đã được xoá khỏi flow này — backward-compat"*. Hiện publish `SendSmsCommand` thay thế. **Sprint 6.2 NOTI-15 (#686) đã xoá nốt 2 consumer backward-compat cuối cùng** (`SendPhoneOtpConsumer` ở SmsService và stub cùng tên ở EmailService) ⇒ event này giờ **không còn consumer nào**, publish sẽ bị RabbitMQ drop im lặng. |
| `StaffSkillsUpdatedEvent` | Defined, không publish trong handler nào của AuthService hiện tại | Reserved cho future `AddStaffSkillCommandHandler` integration (chưa wire up). |
| `SmsDeliveryReportEvent`, `SmsFailedEvent` | Defined trong SharedContracts | AuthService **không consume** — đây là events SmsService publish cho ManagerDashboard/CustomerNotification consumer. AuthService chỉ publish `SendSmsCommand` request. |

### Outbox publish pattern (TRƯỚC vs SAU SaveChanges)

Phần lớn handlers publish event **TRƯỚC `SaveChangesAsync()`** để event row được commit cùng transaction với business data (idiomatic Outbox pattern). Examples:
- `RegisterCommandHandler.cs:119` (publish SendOtpRegisterEvent trước SaveChanges line 123)
- `VerifyOtpCommandHandler.cs:101` (publish AccountActivatedEvent trước SaveChanges line 109)
- `AcceptInviteCommandHandler.cs:105` (publish AccountActivatedEvent trước SaveChanges line 118)
- `ChangeEmailCommandHandler.cs:95` (publish SendEmailChangeOtpEvent trước SaveChanges line 97)
- `InviteAccountCommandHandler.cs:115` (publish SendAdminInviteEvent)
- `GoogleAuthCommandHandler.cs:169` (publish AccountActivatedEvent trước SaveChanges)

**Vài exception publish SAU SaveChanges:**
- `RefreshTokenCommandHandler.cs:84` (publish RefreshTokenReuseDetectedEvent SAU rollback transaction trong reuse case — vì revoke action đã commit ngay)

> **Hậu quả nếu handler throw giữa publish và SaveChanges**: Outbox row được Add vào DbContext nhưng chưa commit → exception rollback → outbox row KHÔNG persist → event không được publish (consistent with business state, đúng pattern).
