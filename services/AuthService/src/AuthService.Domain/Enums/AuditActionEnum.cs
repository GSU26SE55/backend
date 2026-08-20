namespace AuthService.Domain.Enums;

/// <summary>
/// Phân loại hành động được ghi vào audit log.
/// Giá trị enum được persist vào DB (int) — KHÔNG đổi giá trị đã gán.
/// Khi thêm mới, dùng giá trị tiếp theo, KHÔNG reuse.
/// </summary>
public enum AuditActionEnum
{
    // Authentication (1-19)
    LoginSuccess = 1,
    LoginFailedWrongPassword = 2,
    LoginFailedAccountLocked = 3,
    LoginFailedAccountSuspended = 4,
    LoginFailedAccountBanned = 5,
    LoginFailedAccountInactive = 6,
    LoginFailedNotVerified = 7,
    AccountAutoLocked = 8,
    Logout = 9,
    GoogleLoginSuccess = 10,
    GoogleLoginFailed = 11,
    TokenRefreshed = 12,
    TokenReuseDetected = 13,

    // Password / OTP (20-39)
    PasswordChanged = 20,
    PasswordReset = 21,
    OtpVerifySuccess = 22,
    OtpVerifyFailed = 23,
    EmailChangeRequested = 24,
    EmailChangeConfirmed = 25,
    PhoneVerified = 26,

    // 2FA (40-49)
    TwoFactorEnabled = 40,
    TwoFactorDisabled = 41,
    TwoFactorReset = 42,
    BackupCodeRedeemed = 43,
    BackupCodesRegenerated = 44,
    Admin2FAReset = 45,
    LoginWith2FA = 46,
    LoginPending2FA = 47,

    // Google Linking (50-59)
    GoogleLinked = 50,
    GoogleUnlinked = 51,

    // Account Lifecycle (60-79)
    AccountRegistered = 60,
    AccountCreatedByAdmin = 61,
    AccountUpdated = 62,
    AccountStatusChanged = 63,
    AccountUnlocked = 64,
    AccountDeactivated = 65,
    AccountDeleted = 66,
    AccountInviteSent = 67,
    AccountInviteAccepted = 68,

    // Session (80-89)
    SessionRevoked = 80,
    AllSessionsRevoked = 81,
    AdminForceLogout = 82,
    SessionLimitExceededOldestRevoked = 83,

    // Role / Permission (90-109)
    RoleAssigned = 90,
    RoleRevoked = 91,
    RoleTemporaryAssigned = 92,
    RoleCreated = 93,
    RoleUpdated = 94,
    RoleStatusChanged = 95,
    RoleDeleted = 96,
    PermissionGranted = 97,
    PermissionRevoked = 98,

    // Trusted Device (110-119) — #AUTH-48
    TrustedDeviceAdded = 110,
    TrustedDeviceRevoked = 111,
    TrustedDeviceAllRevoked = 112,
    LoginWithTrustedDevice = 113,

    // Cross-device 2FA (120-129) — #AUTH-51
    TwoFactorSetupCrossDeviceRequested = 120,
    TwoFactorSetupCrossDeviceConfirmed = 121,
    TwoFactorSetupCrossDeviceExpired = 122,

    // Account Merge (130-139) — #AUTH-47
    AccountMerged = 130,
    AccountMergeRejected = 131,

    // Import dữ liệu bên thứ ba
    /// <summary>Tài khoản khách hàng được cấp tự động từ dữ liệu nhập của bên thứ ba.</summary>
    AccountProvisionedFromPartnerImport = 140,
}
