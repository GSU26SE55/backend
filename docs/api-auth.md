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
| `Banned` | 5 | Bị xóa/banned (soft delete cho mục đích nghiệp vụ) |

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
| `password` | `string` | Bắt buộc | 6–100 ký tự, không chứa khoảng trắng | Mật khẩu |

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
- `400` — Dữ liệu không hợp lệ (email sai định dạng, mật khẩu thiếu ký tự)
- `200 isSuccess=false` — Sai mật khẩu, tài khoản bị khóa/banned/chưa verify

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

---

### `POST /api/auth/resend-otp`

**Mục đích:** Gửi lại OTP đăng ký khi OTP cũ hết hạn. Chỉ dùng cho luồng `Register`.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email đã đăng ký nhưng chưa verify |

**Response thành công `200`:** `isSuccess = true`, message xác nhận.

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
| `data.expiresInSeconds` | `int` | Không | Thời gian hết hạn của resetToken (thường 10 phút) |

---

### `POST /api/auth/resend-reset-otp`

**Mục đích:** Gửi lại OTP reset mật khẩu khi OTP cũ hết hạn.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `email` | `string` | Bắt buộc | Email đang trong luồng reset password |

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

**Auth:** Không yêu cầu (chỉ cần refresh token)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `refreshToken` | `string` | Bắt buộc | Refresh token cần thu hồi |

**Response thành công `200`:** `isSuccess = true`, token đã bị revoke.

---

### `GET /api/auth/google`

**Mục đích:** Khởi tạo OAuth flow với Google. Trả về URL redirect đến trang đăng nhập Google.

**Auth:** Không yêu cầu

**Query params:** Không có

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "data": "https://accounts.google.com/o/oauth2/auth?..."
}
```

| Field | Mô tả |
|---|---|
| `data` | URL redirect đến Google OAuth consent screen |

---

### `POST /api/auth/google/token`

**Mục đích:** Đăng nhập / đăng ký bằng Google ID token (Mobile/SPA flow). Tự động tạo account nếu chưa có.

**Auth:** Không yêu cầu

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `idToken` | `string` | Bắt buộc | Google ID token lấy từ Google Sign-In SDK |

**Response thành công `200`:** Giống `POST /api/auth/login` — trả về `accessToken` + `refreshToken`.

---

### `GET /api/auth/google/callback`

**Mục đích:** Server-side callback sau khi Google redirect về. Exchange authorization code lấy token.

**Auth:** Không yêu cầu

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `code` | `string` | Bắt buộc | Authorization code từ Google |
| `redirectUri` | `string` | Bắt buộc | Redirect URI đã đăng ký với Google |

**Response thành công `200`:** Giống `POST /api/auth/login`.

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

---

## Nhóm 2 — Tài khoản cá nhân (Yêu cầu access token)

Base route: `/api/accounts`
Header: `Authorization: Bearer {accessToken}`

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

### `PUT /api/accounts/me/avatar`

**Mục đích:** Đặt avatar bằng `fileId` đã upload lên FileStorageService.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `fileId` | `Guid` | Bắt buộc | FileId từ FileStorageService (phải là file type `Avatar`) |

**Response thành công `200`:** `isSuccess = true`.

**Lưu ý:** Sau khi set avatar, `displayAvatarUrl` của account sẽ trỏ về `/api/files/{fileId}/download`.

---

### `POST /api/accounts/me/change-password`

**Mục đích:** Đổi mật khẩu khi đang đăng nhập.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `currentPassword` | `string` | Bắt buộc | Không rỗng | Mật khẩu hiện tại |
| `newPassword` | `string` | Bắt buộc | 6–100 ký tự, không chứa khoảng trắng | Mật khẩu mới |
| `confirmPassword` | `string` | Bắt buộc | Phải trùng với `newPassword` | Xác nhận mật khẩu mới |

**Response thành công `200`:** `isSuccess = true`.

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

### `POST /api/accounts/me/enable-2fa`

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

---

### `POST /api/accounts/me/disable-2fa`

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

### `DELETE /api/accounts/me/unlink-google`

**Mục đích:** Hủy liên kết với tài khoản Google.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `isSuccess = true`.

---

### `POST /api/accounts/me/deactivate`

**Mục đích:** Tự vô hiệu hóa tài khoản của mình (soft). Tài khoản chuyển sang `Inactive`.

**Auth:** Bắt buộc (mọi role)

**Request body:** Không có

**Response thành công `200`:** `isSuccess = true`.

---

### `DELETE /api/accounts/me`

**Mục đích:** Tự xóa tài khoản của mình (soft delete). Tài khoản chuyển sang `Banned`.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `isSuccess = true`.

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

## Nhóm 3 — Profile Staff & Auth Profile

### `GET /api/auth-profiles/me`

**Mục đích:** Lấy thông tin profile mở rộng (AccountProfile) của tài khoản hiện tại.

**Auth:** Bắt buộc (mọi role)

**Response thành công `200`:** `data` là `AccountProfileDto` (xem phần GET `/api/accounts/me`).

---

### `PUT /api/auth-profiles/me`

**Mục đích:** Cập nhật profile mở rộng (địa chỉ, ngày sinh, timezone).

**Auth:** Bắt buộc (mọi role)

**Request body:** Các field của `AccountProfileDto` (không bao gồm avatar — dùng endpoint riêng).

---

### `GET /api/staff-profiles/me`

**Mục đích:** Lấy thông tin staff profile của bản thân (chỉ Staff).

**Auth:** Bắt buộc (Role Staff)

**Response thành công `200`:** `data` là `StaffProfileDto`.

---

### `PUT /api/staff-profiles/me`

**Mục đích:** Cập nhật staff profile của bản thân (availability, notes).

**Auth:** Bắt buộc (Role Staff)

**Request body:**

| Field | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `isAvailable` | `bool` | Không | — | Có sẵn sàng nhận ticket không |
| `notes` | `string?` | Không | Max 500 ký tự | Ghi chú |

---

### `POST /api/staff-profiles/me/skills`

**Mục đích:** Thêm kỹ năng vào profile Staff.

**Auth:** Bắt buộc (Role Staff)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `skillCode` | `string` | Bắt buộc | Mã kỹ năng |
| `skillLevel` | `int` | Bắt buộc | Mức độ kỹ năng (1–5) |
| `certifiedUntil` | `DateTime?` | Không | Ngày hết hạn chứng chỉ |

---

### `DELETE /api/staff-profiles/me/skills/{skillCode}`

**Mục đích:** Xóa kỹ năng khỏi profile Staff.

**Auth:** Bắt buộc (Role Staff)

---

## Nhóm 4 — Quản lý Session

Base route: `/api/sessions`

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

---

### `DELETE /api/sessions/me/all`

**Mục đích:** Thu hồi tất cả session, có thể giữ lại session hiện tại.

**Auth:** Bắt buộc (mọi role)

**Request body:**

| Field | Type | Mô tả |
|---|---|---|
| `exceptCurrent` | `bool` | Mặc định `true` — giữ session hiện tại, chỉ logout các thiết bị khác |
| `currentRefreshToken` | `string?` | Refresh token hiện tại (dùng khi `exceptCurrent = true`) |

---

## Nhóm 5 — Admin: Quản lý Tài khoản

Base route: `/api/admin/accounts`
**Auth:** Bắt buộc (Role Admin hoặc Manager, tùy endpoint)

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

**Response:** `AccountDto`

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
| `roleIds` | `Guid[]?` | Không | Role gán sẵn |

**Luồng:** Sau khi invite, user nhận email chứa link với `invitationToken`. User truy cập link và gọi `POST /api/auth/accept-invite` để đặt mật khẩu và kích hoạt.

---

### `PUT /api/admin/accounts/{id}`

**Mục đích:** Admin cập nhật thông tin tài khoản.

**Auth:** Admin hoặc Manager

**Request body:** Tương tự `PUT /api/accounts/me` nhưng Admin có thể sửa thêm một số field.

---

### `PATCH /api/admin/accounts/{id}/status`

**Mục đích:** Thay đổi trạng thái tài khoản (activate, lock, suspend, ban...).

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `status` | `AccountStatusEnum` | Bắt buộc | Trạng thái mới |
| `reason` | `string?` | Không | Lý do thay đổi (ghi vào audit log) |

---

### `POST /api/admin/accounts/{id}/unlock`

**Mục đích:** Mở khóa tài khoản đang bị khóa (trạng thái `Locked`).

**Auth:** Admin hoặc Manager

---

### `DELETE /api/admin/accounts/{id}`

**Mục đích:** Xóa mềm tài khoản (soft delete).

**Auth:** Admin

---

### `POST /api/admin/accounts/{id}/assign-roles`

**Mục đích:** Gán danh sách role cho tài khoản (replace semantics — thay toàn bộ).

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `roleIds` | `Guid[]` | Bắt buộc | Danh sách role ID mới |

---

### `DELETE /api/admin/accounts/{id}/roles/{roleId}`

**Mục đích:** Thu hồi một role cụ thể khỏi tài khoản.

**Auth:** Admin

---

### `POST /api/admin/accounts/{id}/assign-role-temporary`

**Mục đích:** Gán role tạm thời cho tài khoản trong khoảng thời gian xác định.

**Auth:** Admin

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `roleId` | `Guid` | Bắt buộc | Role ID cần gán |
| `expiredAt` | `DateTime` | Bắt buộc | Thời điểm hết hạn role (UTC) |

---

### `GET /api/admin/accounts/{id}/sessions`

**Mục đích:** Admin xem tất cả session của một tài khoản.

**Auth:** Admin

**Query params:** `activeOnly` (bool, mặc định true)

**Response:** Giống `GET /api/sessions/me`

---

### `DELETE /api/admin/accounts/{id}/sessions`

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

### `GET /api/admin/staff-profiles/{accountId}`

**Mục đích:** Admin xem staff profile của một nhân viên.

**Auth:** Admin hoặc Manager

---

### `PUT /api/admin/staff-profiles/{accountId}`

**Mục đích:** Admin cập nhật staff profile (employee code, department, max tickets).

**Auth:** Admin hoặc Manager

**Request body:**

| Field | Type | Mô tả |
|---|---|---|
| `employeeCode` | `string?` | Mã nhân viên |
| `department` | `string?` | Phòng ban |
| `maxConcurrentTickets` | `int` | Số ticket tối đa đồng thời |
| `isAvailable` | `bool` | Trạng thái sẵn sàng |
| `notes` | `string?` | Ghi chú |

---

### `GET /api/admin/accounts/staff`

**Mục đích:** Lấy danh sách staff kèm profile assignment (dùng cho dropdown giao việc).

**Auth:** Admin hoặc Manager

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `skill` | `string?` | Lọc theo kỹ năng cụ thể |

**Response:** `List<StaffAssignmentProfileDto>`

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
| `displayAvatarUrl` | `string?` | Null nếu không có avatar | URL avatar |
| `skills` | `StaffSkillDto[]` | Không | Danh sách kỹ năng |

---

### `GET /api/admin/accounts/staff/{staffAccountId}/assignment-profile`

**Mục đích:** Lấy thông tin chi tiết staff phục vụ bài toán giao ticket (assignment).

**Auth:** Admin hoặc Manager

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
| `id` | `string` | Không | ID permission |
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
