namespace EmailService.Infrastructure.Templates;

public static class EmailTemplates
{
    public const string OtpRegister = "OtpRegister";
    public const string OtpPasswordReset = "OtpPasswordReset";
    public const string OtpEmailChange = "OtpEmailChange";
    public const string AdminInvite = "AdminInvite";

    /// <summary>Sprint 6.2 NOTI-02 (#673) — khung email chung cho notification pipeline.</summary>
    public const string NotificationGeneric = "NotificationGeneric";

    /// <summary>Sprint 6.2 NOTI-04 (#675) — cảnh báo đăng nhập từ thiết bị/IP lạ.</summary>
    public const string SuspiciousLogin = "SuspiciousLogin";

    /// <summary>Sprint 6.2 NOTI-04 (#675) — cảnh báo refresh token bị dùng lại (nghi bị đánh cắp).</summary>
    public const string RefreshTokenReuse = "RefreshTokenReuse";
}
