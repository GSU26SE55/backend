namespace SmsService.Application.DTOs.Response.Admin;

/// <summary>
/// DTO list gateway device cho admin. KHÔNG trả <c>ApiKeyHash</c> (security).
/// </summary>
public record GatewayDeviceDto(
    Guid Id,
    string DeviceName,
    string DeviceCode,
    bool IsActive,
    DateTime? RevokedAt,
    int DailyLimit,
    int SentToday,
    DateOnly? SentTodayDate,
    DateTime? LastSeenAt,
    string? LastSeenIp,
    DateTime CreatedAt
);
