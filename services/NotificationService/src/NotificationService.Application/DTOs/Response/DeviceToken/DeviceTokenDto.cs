using NotificationService.Domain.Enums;

namespace NotificationService.Application.DTOs.Response.DeviceToken;

/// <summary>
/// Thông tin thiết bị đã đăng ký push của user. KHÔNG chứa raw push token string —
/// chỉ metadata để user nhận diện thiết bị trong danh sách.
/// </summary>
public class DeviceTokenDto
{
    public Guid Id { get; set; }
    public DevicePlatformEnum Platform { get; set; }
    public string? DeviceInfo { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
