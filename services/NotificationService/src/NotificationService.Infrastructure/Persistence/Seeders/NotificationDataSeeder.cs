using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    private async Task SeedTemplatesAsync(CancellationToken ct)
    {
        var existing = await _dbContext.NotificationTemplates
            .Select(t => new { t.Type, t.Channel, t.Locale })
            .ToListAsync(ct);
        var existingKeys = existing.Select(k => (k.Type, k.Channel, k.Locale)).ToHashSet();

        var now = DateTime.UtcNow;
        var templates = new List<NotificationTemplate>();

        void Add(NotificationTypeEnum type, NotificationChannelEnum channel, string locale, string title, string body)
        {
            if (existingKeys.Contains((type, channel, locale)))
                return;
            templates.Add(new NotificationTemplate
            {
                Id = Guid.NewGuid(),
                Type = type,
                Channel = channel,
                Locale = locale,
                TitleTemplate = title,
                BodyTemplate = body,
                IsActive = true,
                CreatedAt = now
            });
        }

        // Ticket lifecycle
        Add(NotificationTypeEnum.TicketCreated, NotificationChannelEnum.InApp, "vi-VN",
            "Ticket mới {{ticketCode}}", "Khách hàng {{customerName}} vừa tạo ticket {{ticketCode}}: {{title}}");
        Add(NotificationTypeEnum.TicketCreated, NotificationChannelEnum.Email, "vi-VN",
            "[{{ticketCode}}] Ticket mới được tạo", "Ticket {{ticketCode}} - {{title}} đã được tạo bởi {{customerName}}. Truy cập hệ thống để xử lý.");
        Add(NotificationTypeEnum.TicketAssigned, NotificationChannelEnum.Push, "vi-VN",
            "Bạn được giao ticket {{ticketCode}}", "{{title}} — Priority {{priority}}");
        Add(NotificationTypeEnum.TicketStatusChanged, NotificationChannelEnum.InApp, "vi-VN",
            "Ticket {{ticketCode}} đổi trạng thái", "Từ {{oldStatus}} → {{newStatus}}");
        Add(NotificationTypeEnum.TicketResolved, NotificationChannelEnum.Email, "vi-VN",
            "[{{ticketCode}}] Đã xử lý xong", "Ticket {{ticketCode}} đã được xử lý. Vui lòng xác nhận và đánh giá.");
        Add(NotificationTypeEnum.TicketClosed, NotificationChannelEnum.InApp, "vi-VN",
            "Ticket {{ticketCode}} đã đóng", "Cảm ơn bạn đã sử dụng dịch vụ.");
        Add(NotificationTypeEnum.TicketEscalated, NotificationChannelEnum.Push, "vi-VN",
            "Ticket {{ticketCode}} đã escalate", "Lý do: {{reason}}");

        // SLA
        Add(NotificationTypeEnum.SlaWarning, NotificationChannelEnum.Push, "vi-VN",
            "Cảnh báo SLA: {{ticketCode}}", "Còn {{minutesRemaining}} phút trước khi breach SLA {{priority}}.");
        Add(NotificationTypeEnum.SlaBreached, NotificationChannelEnum.Push, "vi-VN",
            "SLA breach: {{ticketCode}}", "Ticket đã quá hạn SLA. Cần escalate ngay.");

        // Battery / Environmental
        Add(NotificationTypeEnum.BatteryAnomalyDetected, NotificationChannelEnum.Push, "vi-VN",
            "Bất thường pin {{serialNumber}}", "Loại: {{anomalyType}} — Severity: {{severity}}");
        Add(NotificationTypeEnum.EnvironmentalIncidentDetected, NotificationChannelEnum.Push, "vi-VN",
            "Cảnh báo môi trường tại {{siteName}}", "Loại: {{incidentType}} — Severity: {{severity}}");
        // Sprint IoT-2 #IoT2-31 — Email + SMS template (Critical channel, bypass quiet hours).
        Add(NotificationTypeEnum.EnvironmentalIncidentDetected, NotificationChannelEnum.Email, "vi-VN",
            "[CRITICAL] Sự cố môi trường tại {{siteName}}",
            "Phát hiện sự cố môi trường (IncidentType={{incidentType}}, Severity={{severity}}) tại site \"{{siteName}}\". Detected at {{detectedAt}}. Yêu cầu xử lý NGAY.\n\n{{description}}");
        Add(NotificationTypeEnum.EnvironmentalIncidentDetected, NotificationChannelEnum.Sms, "vi-VN",
            "[CRITICAL]",
            "ENV INCIDENT {{incidentType}} tại {{siteName}}. Severity {{severity}}. Vào hệ thống xử lý NGAY.");
        Add(NotificationTypeEnum.EnvironmentalIncidentResolved, NotificationChannelEnum.InApp, "vi-VN",
            "Sự cố môi trường đã xử lý", "Site {{siteName}} — incident {{incidentType}} đã được resolved.");

        // Account
        Add(NotificationTypeEnum.AccountActivated, NotificationChannelEnum.Email, "vi-VN",
            "Tài khoản đã kích hoạt", "Chào {{fullName}}, tài khoản của bạn đã được kích hoạt thành công.");
        Add(NotificationTypeEnum.AdminInvite, NotificationChannelEnum.Email, "vi-VN",
            "Lời mời tham gia hệ thống", "Bạn được {{inviter}} mời làm {{role}}. Bấm link để kích hoạt: {{activationLink}}");

        // Sprint IoT-1 (#249) — IoT device offline.
        Add(NotificationTypeEnum.IotDeviceWentOffline, NotificationChannelEnum.Push, "vi-VN",
            "Device offline: {{deviceCode}}",
            "Device \"{{displayName}}\" tại site {{siteName}} mất heartbeat {{durationMinutes}} phút. Ảnh hưởng {{affectedBatteryCount}} pin.");
        Add(NotificationTypeEnum.IotDeviceWentOffline, NotificationChannelEnum.InApp, "vi-VN",
            "IoT device offline: {{deviceCode}}",
            "Last seen {{lastSeenAt}}. Kiểm tra nguồn điện / mạng cho device tại site {{siteName}}.");
        Add(NotificationTypeEnum.IotDeviceWentOffline, NotificationChannelEnum.Push, "en-US",
            "Device offline: {{deviceCode}}",
            "Device \"{{displayName}}\" at site {{siteName}} lost heartbeat for {{durationMinutes}} min. {{affectedBatteryCount}} battery affected.");

        // English fallbacks for high-priority types
        Add(NotificationTypeEnum.TicketAssigned, NotificationChannelEnum.Push, "en-US",
            "Ticket {{ticketCode}} assigned to you", "{{title}} — Priority {{priority}}");
        Add(NotificationTypeEnum.SlaBreached, NotificationChannelEnum.Push, "en-US",
            "SLA breached: {{ticketCode}}", "Ticket exceeded SLA. Immediate escalation required.");

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
