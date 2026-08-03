using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Persistence.Seeders;

/// <summary>
/// Seed data cho NotificationService:
/// <list type="bullet">
///   <item><description><see cref="NotificationTemplate"/> cho các loại event chính (Ticket, SLA, Battery, Environmental).</description></item>
///   <item><description><see cref="NotificationPreference"/> mặc định cho sample user.</description></item>
///   <item><description><see cref="DeviceToken"/> mẫu (Android/iOS/Web) cho test push dispatcher.</description></item>
///   <item><description><see cref="Notification"/> mẫu — đủ status để test query (Pending/Sent/Read/Failed).</description></item>
/// </list>
/// Tất cả Id sinh runtime bằng <c>Guid.NewGuid()</c>. Idempotent.
/// </summary>
public class NotificationDataSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<NotificationDataSeeder>? _logger;

    public NotificationDataSeeder(ApplicationDbContext dbContext, ILogger<NotificationDataSeeder>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedTemplatesAsync(cancellationToken);
        var sampleUserIds = await SeedPreferencesAsync(cancellationToken);
        await SeedDeviceTokensAsync(sampleUserIds, cancellationToken);
        await SeedSampleNotificationsAsync(sampleUserIds, cancellationToken);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-12 (#712) — seed từ <see cref="NotificationTemplateCatalog"/>, phủ đủ
    /// 32 type × mọi kênh (trước đây chỉ 5/32 type có template, phần còn lại phải sửa code mới đổi
    /// được câu chữ).
    ///
    /// Idempotent theo bộ ba (Type × Channel × Locale): đã có bản nào cho bộ ba đó thì KHÔNG thêm.
    /// Cố ý không ghi đè — người vận hành có thể đã sửa nội dung trong DB, seeder không được xoá công
    /// sức đó mỗi lần khởi động.
    /// </summary>
    private async Task SeedTemplatesAsync(CancellationToken ct)
    {
        var existingKeys = (await _dbContext.NotificationTemplates
                .Select(t => new { t.Type, t.Channel, t.Locale })
                .ToListAsync(ct))
            .Select(k => (k.Type, k.Channel, k.Locale))
            .ToHashSet();

        var now = DateTime.UtcNow;

        var templates = NotificationTemplateCatalog
            .Build(NotificationDispatchOptions.DefaultTypeChannelMatrix)
            .Where(e => !existingKeys.Contains((e.Type, e.Channel, e.Locale)))
            .Select(e => new NotificationTemplate
            {
                Id = Guid.NewGuid(),
                Type = e.Type,
                Channel = e.Channel,
                Locale = e.Locale,
                TitleTemplate = e.Title,
                BodyTemplate = e.Body,
                Version = 1,
                IsActive = true,
                CreatedAt = now,
            })
            .ToList();

        if (templates.Count == 0)
            return;

        _dbContext.NotificationTemplates.AddRange(templates);
        await _dbContext.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded {Count} notification templates.", templates.Count);
    }

    private async Task<List<Guid>> SeedPreferencesAsync(CancellationToken ct)
    {
        var existing = await _dbContext.NotificationPreferences.Select(p => p.UserId).ToListAsync(ct);
        if (existing.Count > 0)
            return existing;

        // 3 sample userIds — đại diện Admin/Staff/Customer. Tạo runtime.
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var now = DateTime.UtcNow;

        _dbContext.NotificationPreferences.AddRange(
            new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = ids[0],
                PushEnabled = true,
                EmailEnabled = true,
                SmsEnabled = false,
                InAppEnabled = true,
                Frequency = NotificationFrequencyEnum.Immediate,
                TimeZone = "Asia/Ho_Chi_Minh",
                CreatedAt = now
            },
            new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = ids[1],
                PushEnabled = true,
                EmailEnabled = false,
                SmsEnabled = false,
                InAppEnabled = true,
                Frequency = NotificationFrequencyEnum.Immediate,
                QuietHoursStart = new TimeOnly(22, 0),
                QuietHoursEnd = new TimeOnly(6, 0),
                TimeZone = "Asia/Ho_Chi_Minh",
                CreatedAt = now
            },
            new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = ids[2],
                PushEnabled = false,
                EmailEnabled = true,
                SmsEnabled = false,
                InAppEnabled = true,
                Frequency = NotificationFrequencyEnum.Daily,
                TimeZone = "Asia/Ho_Chi_Minh",
                CreatedAt = now
            });

        await _dbContext.SaveChangesAsync(ct);
        return ids;
    }

    private async Task SeedDeviceTokensAsync(List<Guid> userIds, CancellationToken ct)
    {
        var hasTokens = await _dbContext.DeviceTokens.AnyAsync(ct);
        if (hasTokens || userIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        _dbContext.DeviceTokens.AddRange(
            new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userIds[0],
                Token = "ExponentPushToken[seed-admin-android-001]",
                Platform = DevicePlatformEnum.Android,
                DeviceInfo = "Samsung Galaxy S24 - Android 14",
                IsActive = true,
                LastUsedAt = now,
                CreatedAt = now
            },
            new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userIds[1 % userIds.Count],
                Token = "ExponentPushToken[seed-staff-ios-001]",
                Platform = DevicePlatformEnum.Ios,
                DeviceInfo = "iPhone 15 Pro - iOS 17.4",
                IsActive = true,
                LastUsedAt = now,
                CreatedAt = now
            },
            new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = userIds[2 % userIds.Count],
                Token = "fcm-web-seed-customer-001",
                Platform = DevicePlatformEnum.Web,
                DeviceInfo = "Chrome 124 / macOS",
                IsActive = true,
                LastUsedAt = now,
                CreatedAt = now
            });

        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task SeedSampleNotificationsAsync(List<Guid> userIds, CancellationToken ct)
    {
        var hasNotifications = await _dbContext.Notifications.AnyAsync(ct);
        if (hasNotifications || userIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var user0 = userIds[0];
        var user1 = userIds[1 % userIds.Count];
        var user2 = userIds[2 % userIds.Count];

        _dbContext.Notifications.AddRange(
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                Type = NotificationTypeEnum.TicketAssigned,
                Channel = NotificationChannelEnum.InApp,
                Status = NotificationStatusEnum.Sent,
                Title = "Bạn được giao ticket TKT-2602-0001",
                Body = "Battery not charging — Priority P1Critical",
                PayloadJson = "{\"ticketCode\":\"TKT-2602-0001\",\"priority\":\"P1Critical\"}",
                EntityType = "Ticket",
                EntityId = Guid.NewGuid(),
                SentAt = now.AddMinutes(-30),
                CreatedAt = now.AddMinutes(-30)
            },
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                Type = NotificationTypeEnum.SlaWarning,
                Channel = NotificationChannelEnum.Push,
                Status = NotificationStatusEnum.Read,
                Title = "Cảnh báo SLA: TKT-2602-0001",
                Body = "Còn 30 phút trước khi breach SLA P1Critical.",
                PayloadJson = "{\"ticketCode\":\"TKT-2602-0001\",\"minutesRemaining\":30}",
                EntityType = "Ticket",
                EntityId = Guid.NewGuid(),
                SentAt = now.AddMinutes(-20),
                ReadAt = now.AddMinutes(-15),
                CreatedAt = now.AddMinutes(-20)
            },
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = user2,
                Type = NotificationTypeEnum.TicketResolved,
                Channel = NotificationChannelEnum.Email,
                Status = NotificationStatusEnum.Sent,
                Title = "[TKT-2602-0004] Đã xử lý xong",
                Body = "Ticket TKT-2602-0004 đã được xử lý. Vui lòng xác nhận và đánh giá.",
                EntityType = "Ticket",
                EntityId = Guid.NewGuid(),
                SentAt = now.AddHours(-2),
                CreatedAt = now.AddHours(-2)
            },
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = user0,
                Type = NotificationTypeEnum.BatteryAnomalyDetected,
                Channel = NotificationChannelEnum.Push,
                Status = NotificationStatusEnum.Failed,
                Title = "Bất thường pin BAT-2026-002",
                Body = "Loại: Overheat — Severity: Critical",
                FailureReason = "Expo push token expired",
                EntityType = "Alert",
                EntityId = Guid.NewGuid(),
                CreatedAt = now.AddMinutes(-5)
            },
            new Notification
            {
                Id = Guid.NewGuid(),
                UserId = user0,
                Type = NotificationTypeEnum.EnvironmentalIncidentDetected,
                Channel = NotificationChannelEnum.InApp,
                Status = NotificationStatusEnum.Pending,
                Title = "Cảnh báo môi trường tại Solar Farm Long An",
                Body = "Loại: Smoke — Severity: Critical",
                EntityType = "EnvironmentalIncident",
                EntityId = Guid.NewGuid(),
                CreatedAt = now.AddMinutes(-2)
            });

        await _dbContext.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded sample notifications.");
    }
}
