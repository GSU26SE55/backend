using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Token push thiết bị đăng ký với Expo / FCM. Mobile/Web đăng ký token sau login.
/// </summary>
public class DeviceToken : AuditableEntity
{
    public Guid UserId { get; set; }

    /// <summary>Expo push token (ExponentPushToken[xxx]) hoặc FCM token.</summary>
    public string Token { get; set; } = string.Empty;

    public DevicePlatformEnum Platform { get; set; }

    /// <summary>Mô tả thiết bị (model, OS version) — debug only.</summary>
    public string? DeviceInfo { get; set; }

    /// <summary>Token còn active để dispatcher gửi push không.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? LastUsedAt { get; set; }
}
