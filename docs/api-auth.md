# API Documentation — AuthService

> Base URL: `http://localhost:{port}/api`
> Content-Type mặc định: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem phần [Cấu trúc Response chung](#cấu-trúc-response-chung)

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": []
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
- `401` — Token thiếu/hết hạn — chỉ cho endpoint có `[Authorize]`
- `403` — Có token nhưng không đủ permission / sai role
- `404` — Resource không có trong DB
- `409` — Conflict với state hiện tại (vd 2FA đã enable khi user gọi `/enable` lại)
- `410` — Endpoint đã deprecated (vd `/2fa/enable` cũ sau GH-295)
- `422` — Business rule violation: format đúng nhưng value/state sai (vd wrong TOTP, wrong password khi disable, expired challenge token)
- `423` — Account bị lockout tạm thời (sai password quá số lần)
- `429` — Rate limit
- `500` — Lỗi server ngoài dự kiến

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
| `/api/accounts/me/2fa/init` | POST | Bước 1 enroll — sinh QR + pendingToken |
| `/api/accounts/me/2fa/confirm` | POST | Bước 2 enroll — verify TOTP, trả 8 backup codes (1 lần) |
| `/api/accounts/me/2fa/backup-codes/regenerate` | POST | User sinh lại 8 codes mới (cần TOTP) |
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
| `Revoked` | 3 | Token đã bị thu hồi thủ công (logout, đổi mật khẩu, admin revoke) |
| `Expired` | 4 | Token đã hết hạn theo thời gian |
| `Compromised` | 5 | Token bị nghi replay attack — toàn bộ chain bị invalidate |

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

---

## Nhóm 1 — Xác thực (Public, không cần token)

Base route: `/api/auth`

---

### `POST /api/auth/login`

**Mục đích:** Đăng nhập bằng email + mật khẩu. **Response shape là discriminated union** — tuỳ theo `Account.TwoFactorEnabled`:
- 2FA **OFF** → trả tokens ngay (`data.tokens` set, `data.challenge` null)
- 2FA **ON** → trả challenge token để bước 2 verify TOTP/backup code (`data.tokens` null, `data.challenge` set)

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Max 256 ký tự, đúng định dạng email | Email đăng ký tài khoản |
| `password` | `string` | Bắt buộc | Không rỗng | Mật khẩu |

**Lưu ý:** Login chỉ validate password ở mức sanity check để tránh gửi field rỗng. Đây không phải security gate; server vẫn verify password bằng hash hiện có và không áp dụng regex strong-password tại endpoint login.

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

**Rate limit:** 5 attempts / 5 phút / `challengeToken` (vượt → 429 + challenge bị invalidate).

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `challengeToken` | `string` | Bắt buộc | Không rỗng | Lấy từ `data.challenge.challengeToken` của `/api/auth/login` Case B |
| `code` | `string` | Bắt buộc | Nếu `isBackupCode=false`: đúng 6 chữ số. Nếu `isBackupCode=true`: không validate format (server tự normalize). | Mã TOTP 6 số từ Authenticator hoặc backup code (`xxxx-xxxx`, không phân biệt hoa thường, dash optional) |
| `isBackupCode` | `bool` | Mặc định `false` | — | `true` khi user dùng backup code thay vì TOTP |

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
- Nếu `isBackupCode=true`: row `BackupCode` đó set `RedeemedAt = UtcNow` (single-use)
- Nếu `TwoFactorSecret` còn dạng plaintext legacy (pre-GH-295): tự động lazy re-encrypt sau khi verify thành công
- Audit log: `LoginWith2FA` (metadata.method=`totp`/`backupCode`), `BackupCodeRedeemed` (nếu backup code), `LoginAttempt` row Success

**Lỗi thường gặp:**
- `400` — Field validation (challengeToken/code rỗng, TOTP code không phải 6 chữ số) — có `listErrors`
- `403` — Account suspended/banned/inactive giữa lúc challenge còn sống → challenge bị invalidate
- `404` — Account bị xóa giữa lúc challenge còn sống → challenge bị invalidate
- `409` — Account đã disable 2FA giữa lúc challenge còn sống → challenge bị invalidate
- `422` — Challenge token expired/invalid, hoặc mã TOTP/backup code sai (business rule, không phải field format)
- `429` — Quá 5 attempts cho cùng 1 challenge → challenge bị xóa, user phải login lại

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

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
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

**Lưu ý:** Sau khi đăng ký, account ở trạng thái `PendingVerification`. Cần gọi `POST /api/auth/verify-otp` để kích hoạt.

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
  "message": "Xác thực thành công.",
  "data": null
}
```

**Rate limit / retry / lockout:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Sai OTP tối đa 5 lần. Khi vượt quá giới hạn, API trả `423 Locked` trong 15 phút. **Lock tự hết sau 15 phút — không cần admin can thiệp.** Sau 15 phút gọi lại bình thường. Nếu verify thành công, account chuyển sang `Active` nhưng không trả token; FE cần gọi `POST /api/auth/login`.

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
    "expiresInSeconds": 600
  }
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.resetToken` | `string` | Không | Token ngắn hạn dùng để đặt lại mật khẩu (bước sau) |
| `data.expiresInSeconds` | `int` | Không | Thời gian hết hạn của resetToken (900 giây = 15 phút) |

**Rate limit / retry / lockout:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Sai OTP reset tối đa 5 lần. Khi đạt giới hạn, các request trong 15 phút tiếp theo trả `423 Locked`. **Lock tự hết sau 15 phút — không cần admin can thiệp** (cùng cơ chế với `verify-otp`: chỉ dùng `LockoutEndAt`, không set `Status = Locked`).

---

### `POST /api/auth/resend-reset-otp`

**Mục đích:** Gửi lại OTP reset mật khẩu khi OTP cũ hết hạn.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email đang trong luồng reset password |

**Rate limit / cooldown:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Nếu account đang trong luồng reset password, resend reset OTP có cooldown 60 giây; gọi quá sớm trả `429`. OTP reset password có TTL 10 phút. Response vẫn tránh tiết lộ email tồn tại hay không.

---

### `POST /api/auth/reset-password`

**Mục đích:** Đặt lại mật khẩu mới sau khi đã xác thực OTP thành công (có `resetToken`).

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `resetToken` | `string` | Bắt buộc | Không được rỗng | Token lấy từ bước verify-reset-otp |
| `newPassword` | `string` | Bắt buộc | 8–100 ký tự, có chữ hoa/thường/số/ký tự đặc biệt | Mật khẩu mới |

**Response thành công `200`:** `isSuccess = true`, mật khẩu đã được cập nhật.

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

**Lưu ý:** Nếu phát hiện refresh token đã được dùng lại (replay attack), toàn bộ session chain bị invalidate và trạng thái token chuyển sang `Compromised`.

---

### `POST /api/auth/logout`

**Mục đích:** Đăng xuất, thu hồi refresh token hiện tại. Access token vẫn còn hiệu lực đến khi hết hạn (không dùng blacklist).

**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}`

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `refreshToken` | `string` | Bắt buộc | Refresh token cần thu hồi |

**Response thành công `200`:** `isSuccess = true`, token đã bị revoke.

**Lưu ý bảo mật:** Backend lấy `accountId` từ access token trong header và chỉ revoke refresh token thuộc account đó. Nếu refresh token thuộc account khác, API trả `403 Forbidden`. Access token đã cấp vẫn valid đến khi hết hạn vì hệ thống không dùng blacklist; FE phải clear cả access token và refresh token khỏi cookie/local state ngay khi logout thành công.

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
- `400` — Body không hợp lệ (password rỗng, confirmPassword không khớp, invitationToken rỗng)
- `401` — `invitationToken` không tồn tại hoặc đã bị vô hiệu hoá
- `410` — `invitationToken` đã hết hạn (token có TTL **72 giờ** kể từ lúc Admin gửi invite)
- `409` — Token đã được dùng rồi (account đã active, không thể accept lại)

**Lưu ý TTL:** `invitationToken` hết hạn sau **72 giờ**. Nếu hết hạn, Admin cần gửi lại invite qua `POST /api/admin/accounts/invite` với cùng email.

---

## Nhóm 2 — Tài khoản cá nhân (Yêu cầu access token)

Base route: `/api/accounts`
Header: `Authorization: Bearer {accessToken}`

> **Phân biệt Nhóm 2 vs Nhóm 3:**
> - **Nhóm 2** (`/api/accounts`) — AccountsController: quản lý account cốt lõi (password, email change, phone verify, 2FA, Google link, deactivate/delete, login history). **Không có endpoint đọc/cập nhật profile ở nhóm này.**
> - **Nhóm 3** (`/api/auth`) — AuthProfilesController: **canonical route cho profile operations** (đọc profile, cập nhật fullName/address/birthDate/timezone, avatar). FE dùng `GET /api/auth/me` và `PUT /api/auth/me/profile` cho mọi thao tác profile.

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

**Error responses:**

| Status | Trường hợp |
|---|---|
| `400` | Validation lỗi (`NewPassword` không đạt độ phức tạp, `ConfirmPassword` không khớp) HOẶC `currentPassword` không đúng |
| `401` | Chỉ khi JWT thiếu/sai `AccountId` (auth middleware-level fail) |
| `404` | Account không tồn tại |

**Phân biệt `400` vs `401`:** `currentPassword` sai trả `400`, KHÔNG phải `401`. Đây là input error của một user đã authenticated (JWT hợp lệ) — coi như validation business rule, không phải auth fail. `401` được dành riêng cho trường hợp JWT thiếu/sai do auth middleware xử lý trước khi handler được gọi.

---

### `POST /api/accounts/me/change-email`

**Mục đích:** Yêu cầu đổi email. Hệ thống gửi OTP 6 số về **email mới** để xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `newEmail` | `string` | Bắt buộc | Đúng định dạng email, max 256 ký tự | Email mới cần chuyển sang |
| `currentPassword` | `string` | Bắt buộc | Không rỗng | Mật khẩu hiện tại để xác nhận danh tính |

**Response thành công `200`:** `isSuccess = true`, OTP đã gửi về email mới.

---

### `POST /api/accounts/me/confirm-email-change`

**Mục đích:** Xác thực OTP để hoàn tất đổi email. Email mới chính thức có hiệu lực.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `otp` | `string` | Bắt buộc | OTP 6 chữ số gửi về email mới |

**Lưu ý — email mới lấy từ đâu:** FE không cần gửi lại email mới trong request này. Khi gọi `POST /api/accounts/me/change-email`, server lưu email mới vào field `PendingEmail` của account trong DB. Handler `confirm-email-change` đọc `PendingEmail` từ DB, verify OTP, rồi copy sang `Email` chính thức. Không có race condition khi mở nhiều tab vì `PendingEmail` là per-account.

**Response thành công `200`:** `isSuccess = true`, email đã cập nhật.

**Lưu ý sau confirm:** Tất cả refresh token của account bị revoke. FE phải clear token và redirect về login ngay sau khi nhận response thành công.

---

### `POST /api/accounts/me/send-phone-otp`

**Mục đích:** Gửi OTP qua SMS đến số điện thoại đang lưu trong profile để xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có (AccountId lấy từ JWT)

**Response thành công `200`:** `isSuccess = true`, OTP đã gửi.

**Cooldown / Rate limit:** Có cooldown 60 giây giữa các lần gửi. Gọi quá sớm trả `429` với message "Vui lòng đợi N giây trước khi yêu cầu gửi lại OTP." OTP SMS có TTL 5 phút.

**Lỗi thường gặp:**
- `400` — Account chưa có `phoneNumber` trong profile (phải cập nhật profile trước)
- `400` — Số điện thoại đã được xác thực (`phoneConfirmed = true`)
- `429` — Cooldown chưa hết (gửi lại trong vòng 60 giây)

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

**Lỗi thường gặp:**
- `401` — JWT empty/expired
- `404` — Account không tồn tại
- `409` — 2FA đã được bật trên account (phải disable trước nếu muốn enroll lại)
- `429` — Rate limit

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

**Lỗi thường gặp:**
- `400` — Field validation (pendingToken/code rỗng, code không phải 6 chữ số) — có `listErrors`
- `401` — JWT empty/expired
- `404` — Account không tồn tại
- `409` — 2FA đã được bật (race condition)
- `422` — Pending session expired/init lại / pendingToken không khớp / mã TOTP sai — business rule, không touch ListErrors. Pending state vẫn còn (retry với code đúng vẫn được).
- `429` — Rate limit

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

**Response thành công `200`:** `isSuccess = true`.

**Session sau khi deactivate:** Tất cả refresh token của tài khoản bị revoke ngay lập tức. Access token hiện tại vẫn valid đến khi hết hạn; FE phải clear token và redirect về login ngay sau khi gọi thành công.

---

### `DELETE /api/accounts/me`

**Mục đích:** Tự xóa tài khoản của mình theo cơ chế soft delete (`IsDeleted = true`).

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `isSuccess = true`.

**Session sau khi delete:** Tất cả refresh token của tài khoản bị revoke ngay lập tức. Access token hiện tại vẫn valid đến khi hết hạn; FE phải clear token và redirect về login ngay sau khi gọi thành công.

**Lưu ý trạng thái:** User tự xóa không nên được FE hiển thị như "bị banned". `Banned` là trạng thái quản trị/nghiệp vụ riêng; self-delete phân biệt bằng context endpoint `DELETE /api/accounts/me` và soft-delete flag ở backend.

---

### `GET /api/accounts/me/login-history`

**Mục đích:** Xem lịch sử đăng nhập của tài khoản hiện tại, có phân trang.

**Auth:** Bắt buộc (mọi role)

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 10) | Số item mỗi trang |
| `result` | `LoginAttemptResult?` | Không | Lọc theo kết quả |
| `onlyFailed` | `bool?` | Không | Chỉ lấy lần thất bại |
| `fromUtc` | `DateTime?` | Không | Từ thời điểm (UTC) |
| `toUtc` | `DateTime?` | Không | Đến thời điểm (UTC) |

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
| `method` | `string` | Không | Phương thức: `Password`, `Google`, `VerifyOtp` |
| `ipAddress` | `string?` | Null nếu không capture | IP address |
| `userAgent` | `string?` | Null nếu không có | User agent |
| `deviceId` | `string?` | Null nếu không gửi | Device ID từ client |
| `note` | `string?` | Null nếu không có | Ghi chú bổ sung |
| `createdAt` | `DateTime` | Không | Thời điểm xảy ra (UTC) |

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

**Session limit:** Mặc định tối đa 5 session active/account (`Session:MaxConcurrentSessions`). Khi vượt quá giới hạn, session active cũ nhất bị revoke tự động với audit action `SessionLimitExceededOldestRevoked`. Nếu cấu hình `MaxConcurrentSessions <= 0`, giới hạn này bị tắt.

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

**Response thành công `200`:** `data` là số session đã revoke.

**Lưu ý bảo mật:** Backend kiểm tra `sessionId` phải thuộc account hiện tại. Nếu session thuộc account khác, API trả `403 Forbidden`.

---

### `POST /api/sessions/revoke-all`

**Mục đích:** Thu hồi tất cả session, có thể giữ lại session hiện tại.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Mô tả |
|---|---|---|
| `exceptCurrent` | `bool` | Mặc định `true` — giữ session hiện tại, chỉ logout các thiết bị khác |
| `currentRefreshToken` | `string?` | Refresh token hiện tại (dùng khi `exceptCurrent = true`) |

**Response thành công `200`:** `data` là số session đã revoke.

**Lưu ý:** Chỉ refresh token bị revoke. Access token đã cấp vẫn valid đến khi hết TTL.

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

**Response:** `PaginationResponse<AccountDto>`

**Lưu ý:** Mỗi `AccountDto` trong list **bao gồm đầy đủ** `profile` (AccountProfileDto) và `staffProfile` (StaffProfileDto nếu là Staff) — được eager load bằng `.Include()`, không có N+1 query. FE có thể render avatar, department, skills ngay từ list response mà không cần gọi thêm `/api/admin/accounts/{id}`.

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
  "listErrors": []
}
```

**Luồng:** Sau khi invite, user nhận email chứa link với `invitationToken`. User truy cập link và gọi `POST /api/auth/accept-invite` để đặt mật khẩu và kích hoạt.

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

**Auth:** Admin hoặc Manager

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

**Status transition hiện tại:** Backend cho phép chuyển giữa các giá trị enum hợp lệ. Nếu chuyển sang `Inactive`, `Suspended`, `Banned` hoặc `Locked`, toàn bộ refresh token active của account bị revoke. Nếu chuyển sang `Active`, backend reset failed login attempts và lockout. Nếu cần matrix chặt hơn cho production, BE cần bổ sung rule ở `ChangeAccountStatusCommandHandler`.

---

### `POST /api/admin/accounts/{id}/unlock`

**Mục đích:** Mở khóa tài khoản đang bị khóa (trạng thái `Locked`).

**Auth:** Admin hoặc Manager

**Response thành công `200`:** `isSuccess = true`. Backend reset failed login attempts và lockout counter; account chuyển sang `Active`.

**Lỗi thường gặp:**
- `400` — `id` không hợp lệ
- `401` — Token không hợp lệ hoặc hết hạn
- `403` — Không có role Admin/Manager
- `404` — Không tìm thấy account
- `200 isSuccess=false` — Account không ở trạng thái `Locked` (không cần unlock)

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
- `404` — Không tìm thấy account

**Lưu ý JWT cache:** Role được đưa vào JWT ở lần issue token tiếp theo. Access token cũ vẫn giữ claim `role` cũ cho đến khi hết hạn hoặc user login/refresh lại.

---

### `GET /api/admin/accounts/{id}/sessions`

**Mục đích:** Admin xem tất cả session của một tài khoản.

**Auth:** Admin

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
| `employeeCode` | `string?` | Mã nhân viên |
| `department` | `string?` | Phòng ban |
| `maxConcurrentTickets` | `int` | Số ticket tối đa đồng thời, 1–50 |
| `isAvailable` | `bool` | Trạng thái sẵn sàng |
| `notes` | `string?` | Ghi chú |

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

## Nhóm 8 — Admin: Permissions

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

---

## Nhóm 9 — Admin: Audit Logs

Base route: `/api/admin/audit-logs`
**Auth:** Admin hoặc Manager

---

### `GET /api/admin/audit-logs`

**Mục đích:** Xem audit log toàn hệ thống, phân trang và lọc.

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `pageNumber` | `int` | Trang |
| `pageSize` | `int` | Số item/trang |
| `action` | `AuditActionEnum?` | Lọc theo loại hành động |
| `targetAccountId` | `Guid?` | Xem tất cả hành động liên quan đến account này |
| `actorAccountId` | `Guid?` | Xem tất cả hành động actor này thực hiện |
| `isSuccess` | `bool?` | Lọc theo kết quả thành công/thất bại |
| `fromUtc` | `DateTime?` | Từ thời điểm (UTC inclusive) |
| `toUtc` | `DateTime?` | Đến thời điểm (UTC exclusive) |

**Response:** `PaginationResponse<AuditLogDto>`

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
