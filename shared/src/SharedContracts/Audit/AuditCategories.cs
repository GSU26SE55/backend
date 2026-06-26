namespace SharedContracts.Audit;

/// <summary>
/// 9 category cố định nhóm hành động audit (Sprint audit <c>#AUDIT-02</c>, Phụ lục B §B.2.1).
/// Cross-cutting (KHÔNG theo service) — dùng cho <c>AuditCreatedEventV1.ActionCategory</c> + filter ở Audit Explorer.
/// </summary>
public static class AuditCategories
{
    /// <summary>Đăng nhập/đăng xuất/OTP/2FA/refresh token (vd LoginSucceeded, TwoFactorEnabled).</summary>
    public const string Authentication = "Authentication";

    /// <summary>Cấp/thu hồi quyền, role, permission (vd PermissionGranted, RoleAssigned).</summary>
    public const string Authorization = "Authorization";

    /// <summary>Quản lý vòng đời account (vd AccountCreated, AccountLocked, AccountMerged).</summary>
    public const string AccountManagement = "AccountManagement";

    /// <summary>Tạo/sửa/xóa dữ liệu nghiệp vụ (vd BatteryUpdated, TicketStateTransitioned).</summary>
    public const string DataModification = "DataModification";

    /// <summary>Truy cập/đọc/tải dữ liệu nhạy cảm (vd FileDownloaded, AuditExported).</summary>
    public const string DataAccess = "DataAccess";

    /// <summary>Thay đổi cấu hình/threshold/rule (vd ThresholdConfigChanged, AlertRuleChanged).</summary>
    public const string Configuration = "Configuration";

    /// <summary>Sự kiện an ninh/forensic/compliance (vd PriorityOverridden, AccountDataRedacted).</summary>
    public const string Security = "Security";

    /// <summary>Gửi thông báo ra ngoài (vd EmailSent, PushSent, SmsForwarded).</summary>
    public const string Communication = "Communication";

    /// <summary>Hành động hệ thống/tự động/AI (vd AutoCreatedFromAnomaly, ModelInferenceCalled).</summary>
    public const string System = "System";

    /// <summary>Toàn bộ category hợp lệ — dùng để validate ở consumer/analyzer.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Authentication, Authorization, AccountManagement, DataModification, DataAccess,
        Configuration, Security, Communication, System,
    };
}
