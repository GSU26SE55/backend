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
| `listErrors` | `Errors[]` | Danh sách lỗi validation — mỗi phần tử có `field` và `detail` |

**Lỗi HTTP chung:**
- `400` — Validation hoặc input không hợp lệ, body vẫn theo `CommonResponse<T>` nếu lỗi đi qua application validation
- `401` — Token thiếu/hết hạn/không hợp lệ hoặc credential sai
- `403` — Có token nhưng không đủ quyền hoặc resource không thuộc user hiện tại
- `404` — Không tìm thấy resource
- `409` — Xung đột dữ liệu/nghiệp vụ
- `423` — Account bị lockout tạm thời
- `429` — Bị rate limit
- `500` — Lỗi server ngoài dự kiến

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
| `TwoFactorEnabled` | 40 | 2FA |
| `TwoFactorDisabled` | 41 | 2FA |
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

**Mục đích:** Đăng nhập bằng email + mật khẩu, nhận cặp access token / refresh token.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `email` | `string` | Bắt buộc | Max 256 ký tự, đúng định dạng email | Email đăng ký tài khoản |
| `password` | `string` | Bắt buộc | Không rỗng | Mật khẩu |

**Lưu ý:** Login chỉ validate password ở mức sanity check để tránh gửi field rỗng. Đây không phải security gate; server vẫn verify password bằng hash hiện có và không áp dụng regex strong-password tại endpoint login.

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "abc123..."
  }
}
```

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `data.accessToken` | `string` | Có thể null khi lỗi | JWT access token, thời hạn 1 giờ |
| `data.refreshToken` | `string` | Có thể null khi lỗi | Refresh token, thời hạn 7 ngày, lưu trong Redis |

**Lỗi thường gặp:**
- `400` — Dữ liệu không hợp lệ (email sai định dạng, password rỗng)
- `401` — Email hoặc mật khẩu không chính xác
- `403` — Tài khoản chưa verify, inactive, suspended hoặc banned
- `423` — Tài khoản bị khóa tạm thời do sai mật khẩu quá số lần cho phép

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

**Rate limit / retry / lockout:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Sai OTP tối đa 5 lần. Khi vượt quá giới hạn, account bị lock 15 phút và API trả `423 Locked`. Nếu verify thành công, account chuyển sang `Active` nhưng không trả token; FE cần gọi `POST /api/auth/login`.

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

**Rate limit / retry / lockout:** Endpoint có policy `AnonOtp` 5 request/phút theo IP. Sai OTP reset tối đa 5 lần. Khi đạt giới hạn, account bị lock 15 phút; các request trong thời gian lockout trả `423 Locked`.

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

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "newtoken..."
  }
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

**Response thành công `200`:** Giống `POST /api/auth/login`.

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

**Response thành công `200`:** Giống `POST /api/auth/login` — trả về `accessToken` + `refreshToken`.

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
> - **Nhóm 2** (`/api/accounts`) — AccountsController: quản lý account cốt lõi (password, email change, phone, 2FA, Google link, session, login history). **Đây là route canonical cho các thao tác account.**
> - **Nhóm 3** (`/api/auth`) — AuthProfilesController: cập nhật profile mở rộng (fullName, address, birthDate, timezone, avatar). **Dùng Nhóm 3 cho profile/avatar operations.**
> - `GET /api/accounts/me` và `GET /api/auth/me` trả cùng shape `AccountDto` — FE chọn một route nhất quán, khuyên dùng `GET /api/auth/me` vì thuộc AuthProfilesController quản lý profile.

**Lỗi thường gặp cho nhóm này:**
- `401` — Token không hợp lệ, hết hạn hoặc JWT thiếu account id
- `403` — Route id không thuộc account hiện tại hoặc không đủ quyền
- `404` — Account/session/resource không tồn tại
- `409` — Dữ liệu cập nhật xung đột với account khác hoặc rule nghiệp vụ

---

### `GET /api/accounts/me`

**Mục đích:** Lấy thông tin profile đầy đủ của tài khoản đang đăng nhập.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "email": "user@example.com",
    "phoneNumber": null,
    "fullName": "Nguyen Van A",
    "avatarUrl": null,
    "dateOfBirth": null,
    "address": null,
    "emailConfirmed": true,
    "phoneConfirmed": false,
    "twoFactorEnabled": false,
    "status": 1,
    "lastLoginAt": "2026-05-16T08:00:00Z",
    "createdAt": "2026-01-01T00:00:00Z",
    "updatedAt": null,
    "roles": ["Staff"],
    "profile": { ... },
    "staffProfile": null,
    "displayAvatarUrl": "/api/files/{fileId}/download"
  }
}
```

**Chi tiết fields `AccountDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `Guid` | Không | ID tài khoản |
| `email` | `string` | Không | Email đăng nhập |
| `phoneNumber` | `string?` | Null nếu chưa cung cấp | Số điện thoại |
| `fullName` | `string` | Không | Họ và tên đầy đủ |
| `avatarUrl` | `string?` | Null nếu không có hoặc dùng uploaded file | URL avatar legacy (Google hoặc direct URL) |
| `dateOfBirth` | `DateTime?` | Null nếu chưa cung cấp | Ngày sinh |
| `address` | `string?` | Null nếu chưa cung cấp | Địa chỉ |
| `emailConfirmed` | `bool` | Không | Email đã xác thực chưa |
| `phoneConfirmed` | `bool` | Không | Số điện thoại đã xác thực chưa |
| `twoFactorEnabled` | `bool` | Không | 2FA đang bật không |
| `status` | `AccountStatusEnum` | Không | Trạng thái tài khoản (xem enum) |
| `lastLoginAt` | `DateTime?` | Null nếu chưa login lần nào | Thời điểm đăng nhập gần nhất (UTC) |
| `createdAt` | `DateTime` | Không | Thời điểm tạo tài khoản (UTC) |
| `updatedAt` | `DateTime?` | Null nếu chưa cập nhật | Thời điểm cập nhật gần nhất (UTC) |
| `roles` | `string[]` | Không (có thể rỗng) | Danh sách tên role đang gán |
| `profile` | `AccountProfileDto?` | Null nếu chưa có profile extended | Thông tin profile mở rộng |
| `staffProfile` | `StaffProfileDto?` | Null nếu không phải Staff | Thông tin staff (chỉ có với role Staff) |
| `displayAvatarUrl` | `string?` | Null nếu không có avatar | URL để hiển thị avatar — có thể là URL Google hoặc `/api/files/{fileId}/download` |

**Chi tiết `AccountProfileDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `accountId` | `Guid` | Không | ID tài khoản |
| `avatarFileId` | `Guid?` | Null nếu avatar không phải uploaded | FileId trong FileStorageService |
| `externalAvatarUrl` | `string?` | Null nếu không có Google avatar | URL avatar từ Google OAuth |
| `avatarSource` | `AvatarSourceEnum` | Không | Nguồn avatar (xem enum) |
| `address` | `string?` | Null nếu chưa cung cấp | Địa chỉ trong profile |
| `birthDate` | `DateTime?` | Null nếu chưa cung cấp | Ngày sinh |
| `timeZone` | `string?` | Null nếu chưa cài | Timezone (e.g., `Asia/Ho_Chi_Minh`) |

**Chi tiết `StaffProfileDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `accountId` | `Guid` | Không | ID tài khoản Staff |
| `employeeCode` | `string?` | Null nếu chưa gán | Mã nhân viên |
| `department` | `string?` | Null nếu chưa gán | Phòng ban |
| `maxConcurrentTickets` | `int` | Không | Số ticket tối đa xử lý đồng thời (mặc định 3) |
| `isAvailable` | `bool` | Không | Đang sẵn sàng nhận ticket không |
| `notes` | `string?` | Null nếu không có | Ghi chú về staff |
| `skills` | `StaffSkillDto[]` | Không (có thể rỗng) | Danh sách kỹ năng |

**Chi tiết `StaffSkillDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `skillCode` | `string` | Không | Mã kỹ năng (ví dụ: `BATTERY_REPAIR`, `ELECTRICAL`) |
| `skillLevel` | `int` | Không | Mức độ kỹ năng (1–5 theo quy ước dự án) |
| `certifiedUntil` | `DateTime?` | Null nếu không có chứng chỉ có hạn | Ngày hết hạn chứng chỉ |

---

### `PUT /api/accounts/me`

**Mục đích:** Cập nhật thông tin cá nhân (họ tên, ngày sinh, địa chỉ, timezone).

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `fullName` | `string` | Bắt buộc | Không rỗng, max 150 ký tự | Họ và tên |
| `phoneNumber` | `string?` | Tùy chọn | Max 20 ký tự | Số điện thoại |
| `dateOfBirth` | `DateTime?` | Tùy chọn | Không ở tương lai, năm >= 1900 | Ngày sinh |
| `address` | `string?` | Tùy chọn | Max 500 ký tự | Địa chỉ |
| `timeZone` | `string?` | Tùy chọn | — | Timezone string |

**Response thành công `200`:** `isSuccess = true`, data là Guid của account.

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

**Response thành công `200`:** `isSuccess = true`.

**Lưu ý bảo mật:** Rule mật khẩu mới đồng bộ với register/reset/accept-invite. Khi đổi mật khẩu thành công, tất cả refresh token của account bị revoke. Access token hiện tại vẫn valid đến khi hết hạn; FE phải clear token và redirect về login sau khi nhận response thành công.

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

**Response thành công `200`:** `isSuccess = true`, email đã cập nhật.

---

### `POST /api/accounts/me/send-phone-otp`

**Mục đích:** Gửi OTP qua SMS đến số điện thoại đang lưu trong profile để xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có (AccountId lấy từ JWT)

**Response thành công `200`:** `isSuccess = true`, OTP đã gửi.

---

### `POST /api/accounts/me/verify-phone-otp`

**Mục đích:** Xác thực OTP SMS để đánh dấu số điện thoại là đã xác thực.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `otp` | `string` | Bắt buộc | OTP 6 chữ số nhận qua SMS |

**Response thành công `200`:** `isSuccess = true`, `phoneConfirmed = true`.

---

### `POST /api/accounts/me/2fa/enable`

**Mục đích:** Bật xác thực hai yếu tố (TOTP). Trả về secret và URI để quét QR code với Google Authenticator.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": {
    "secret": "BASE32SECRETKEY",
    "otpAuthUri": "otpauth://totp/SolarBattery:user@example.com?secret=...&issuer=..."
  }
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data.secret` | `string` | Secret key (Base32) để nhập thủ công vào app authenticator |
| `data.otpAuthUri` | `string` | URI để tạo QR code, quét bằng Google Authenticator / Authy |

**Lưu ý — 2FA activation behavior:**
- 2FA **kích hoạt ngay** sau khi endpoint này thành công — không có bước confirm TOTP riêng biệt.
- FE phải hiển thị QR code / secret và yêu cầu user scan + lưu **trước khi** cho phép rời màn hình, vì sau đó secret không được trả lại nữa.
- **Recovery path nếu user mất access TOTP authenticator:** User tự gọi `POST /api/accounts/me/2fa/disable` (cần access token hợp lệ). Nếu user không thể login được, Admin cần can thiệp trực tiếp ở DB — hiện tại chưa có admin endpoint để disable 2FA cho account khác; backup codes cũng chưa được implement.
- **Trạng thái triển khai hiện tại:** Backend lưu secret và đánh dấu `twoFactorEnabled = true`, nhưng **TOTP chưa được enforce tại bước login** (sẽ implement ở sprint sau). FE hiện tại không cần xử lý TOTP challenge khi login — chỉ cần hiển thị setup screen để user sẵn sàng cho sprint sau. Khi TOTP enforcement được bật, tài liệu này sẽ được cập nhật.

---

### `POST /api/accounts/me/2fa/disable`

**Mục đích:** Tắt xác thực hai yếu tố.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có

**Response thành công `200`:** `isSuccess = true`.

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
    "totalCount": 42,
    "pageNumber": 1,
    "pageSize": 10
  }
}
```

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

> **Phân biệt với Nhóm 2:** Nhóm 3 là **canonical route cho profile & avatar operations** (AuthProfilesController). Nhóm 2 (`/api/accounts`) dùng cho account management (password, email change, 2FA...). Khi cần đọc profile tổng hợp, `GET /api/auth/me` và `GET /api/accounts/me` trả cùng shape — chọn một và dùng nhất quán trong toàn bộ FE.

---

### `GET /api/auth/me`

**Mục đích:** Lấy profile tổng hợp của tài khoản hiện tại, cùng shape với `GET /api/accounts/me`.

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
| `roleIds` | `Guid[]?` | Không | — | Danh sách role ID gán ngay |

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
| `roleIds` | `Guid[]` | Bắt buộc | Role gán sẵn, ít nhất 1 role |

**Luồng:** Sau khi invite, user nhận email chứa link với `invitationToken`. User truy cập link và gọi `POST /api/auth/accept-invite` để đặt mật khẩu và kích hoạt.

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

**Lưu ý:** Endpoint này chỉ sửa profile/account fields ở trên. Admin không sửa role/status/emailConfirmed trong body này; role dùng endpoint `/roles`, status dùng `PATCH /status`, session dùng `/sessions/*`.

**Lưu ý `avatarUrl` (legacy field):** Field này cho phép Admin set avatar bằng direct URL (ví dụ URL ảnh từ Google hoặc CDN bên ngoài) mà không cần upload qua FileStorageService. Với flow mới, ưu tiên dùng `POST /api/auth/me/avatar` + `avatarFileId`. FE luôn render avatar bằng `displayAvatarUrl` từ `AccountDto`, không dùng `avatarUrl` trực tiếp để hiển thị.

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

### `POST /api/admin/accounts/{id}/roles`

**Mục đích:** Gán thêm hoặc cập nhật danh sách role cho tài khoản.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `roleIds` | `Guid[]` | Bắt buộc | Danh sách role ID cần gán hoặc cập nhật |
| `expiredAt` | `DateTime?` | Không | Thời điểm hết hạn chung cho các role được gán |

**Lưu ý semantics:** Endpoint này là additive/upsert, không phải replace toàn bộ. Role hiện có nhưng không nằm trong request vẫn được giữ nguyên; muốn thu hồi role, FE gọi `DELETE /api/admin/accounts/{id}/roles/{roleId}`.

---

### `DELETE /api/admin/accounts/{id}/roles/{roleId}`

**Mục đích:** Thu hồi một role cụ thể khỏi tài khoản.

**Auth:** Admin

---

### `POST /api/admin/accounts/{id}/roles/temporary`

**Mục đích:** Gán role tạm thời cho tài khoản trong khoảng thời gian xác định.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `roleId` | `Guid` | Bắt buộc | Role ID cần gán |
| `expiredAt` | `DateTime` | Bắt buộc | Thời điểm hết hạn role (UTC) |

**Lưu ý lifecycle của temporary role:**
- Role expire theo cơ chế **lazy expiry** — không có background job chủ động revoke. Khi user login hoặc refresh token, hệ thống filter `ExpiredAt == null || ExpiredAt > UtcNow` để loại role đã hết hạn khỏi JWT claims.
- Role record vẫn tồn tại trong DB sau khi expire, chỉ không được đưa vào JWT — không có audit log `RoleRevoked` tự động khi hết hạn.
- Nếu cần revoke sớm trước `expiredAt`, Admin dùng `DELETE /api/admin/accounts/{id}/roles/{roleId}`.

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
- `404` — Không tìm thấy account/staff skill
- `409` — `employeeCode` trùng với staff khác

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
