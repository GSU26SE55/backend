using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
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
    /// mọi type × channel trong dispatch matrix (trước đây chỉ một phần nhỏ có template, phần còn lại phải sửa code mới đổi
    /// được câu chữ).
    ///
    /// Idempotent theo cặp (Type × Channel): đã có bản nào cho cặp đó thì KHÔNG thêm.
    /// Cố ý không ghi đè — người vận hành có thể đã sửa nội dung trong DB, seeder không được xoá công
    /// sức đó mỗi lần khởi động.
    ///
    /// <para><b>03/08/2026 — thêm bước hội tụ.</b> Chỉ "có bản nào thì thôi" là chưa đủ: bản đang có
    /// trong DB đã trôi khỏi danh mục theo hai đường, và cả hai đều âm thầm.</para>
    ///
    /// <list type="number">
    ///   <item><description><b>Enum đánh số lại.</b> Khi <c>BlogGenerationCompleted(25)</c> và
    ///   <c>BlogGenerationFailed(26)</c> được chèn vào giữa, mọi type từ 27 trở đi dịch lên 2. Các
    ///   dòng template seed trước đó giữ nguyên số cũ, thành ra nội dung của type này nằm dưới số
    ///   của type khác — <c>TicketReopened(30)</c> mang câu "Cảnh báo pin", <c>BlogGenerationCompleted(25)</c>
    ///   mang câu "Trao đổi được leo thang lên Admin".</description></item>
    ///   <item><description><b>Tên biến sai.</b> Bộ template cũ soạn theo hợp đồng payload tưởng
    ///   tượng (<c>{{ticketCode}}</c> trong khi consumer ghi <c>code</c>). Handlebars render biến lạ
    ///   ra rỗng chứ không báo lỗi.</description></item>
    /// </list>
    ///
    /// <para><b>Luật hội tụ — cố ý hẹp, để không giẫm lên công sức người vận hành:</b></para>
    /// <list type="bullet">
    ///   <item><description>Chưa có template cho cặp đó ⇒ thêm bản v1.</description></item>
    ///   <item><description>Bản đang dùng <b>do seeder tạo</b> mà nội dung khác danh mục ⇒ sinh
    ///   phiên bản mới theo danh mục, hạ cờ bản cũ. Nội dung seed là dữ liệu suy ra được từ code,
    ///   không phải công sức của ai.</description></item>
    ///   <item><description>Bản đang dùng <b>do người vận hành soạn</b> mà gọi biến không tồn tại
    ///   ⇒ cũng sinh phiên bản mới theo danh mục. Bản hỏng vẫn nằm nguyên trong lịch sử phiên bản.
    ///   Sửa vì template hỏng biến là hỏng thật, không phải lựa chọn biên tập.</description></item>
    ///   <item><description>Bản do người vận hành soạn và biến đều hợp lệ ⇒ <b>không đụng tới</b>.</description></item>
    /// </list>
    ///
    /// <para>Tự dừng: sau khi hội tụ, bản đang dùng khớp danh mục nên lần khởi động sau không sinh
    /// thêm phiên bản nào.</para>
    /// </summary>
    private async Task SeedTemplatesAsync(CancellationToken ct)
    {
        var existing = await _dbContext.NotificationTemplates.ToListAsync(ct);
        var byPair = existing.ToLookup(t => (t.Type, t.Channel));

        var now = DateTime.UtcNow;
        var added = new List<NotificationTemplate>();
        var repaired = 0;

        var catalog = NotificationTemplateCatalog.Build(
            NotificationDispatchOptions.DefaultTypeChannelMatrix);

        // Hạ cờ template "mồ côi": cặp (type × channel) đã RỜI khỏi ma trận nhưng dòng seed cũ còn
        // nằm lại và vẫn bật. Vòng hội tụ bên dưới duyệt theo danh mục nên không bao giờ đi qua
        // chúng — sót lại 5 bản như vậy sau lần chạy đầu, gồm cả (TicketCreated × Email) mang
        // {{ticketCode}}. Hôm nay vô hại vì cặp đó không sinh thông báo nào, nhưng ngày nào cặp ấy
        // được bật lại thì một template hỏng biến sẽ có hiệu lực ngay.
        //
        // Chỉ hạ cờ bản DO SEEDER TẠO và chỉ HẠ CỜ, không xoá: bản người vận hành soạn cho cặp ngoài
        // ma trận là lựa chọn có chủ đích của họ, còn hạ cờ thì bật lại được bằng endpoint activate.
        var trongDanhMuc = catalog.Select(e => (e.Type, e.Channel)).ToHashSet();
        var moCoi = existing
            .Where(t => t.IsActive && !t.IsDeleted)
            .Where(t => !trongDanhMuc.Contains((t.Type, t.Channel)))
            .Where(t => t.CreatedBy is null || t.CreatedBy == Guid.Empty)
            .ToList();

        foreach (var t in moCoi)
            t.IsActive = false;

        foreach (var entry in catalog)
        {
            var siblings = byPair[(entry.Type, entry.Channel)].ToList();

            if (siblings.Count == 0)
            {
                added.Add(NewTemplate(entry, version: 1, now));
                continue;
            }

            var active = siblings.FirstOrDefault(t => t.IsActive && !t.IsDeleted);
            if (active is null)
                continue;

            if (!NeedsConvergence(active, entry))
                continue;

            // Version phải tính trên CẢ bản đã xoá mềm: unique index (type, channel, version)
            // không lọc is_deleted nên dùng lại số cũ là vi phạm khoá.
            var nextVersion = siblings.Max(t => t.Version) + 1;

            active.IsActive = false;
            added.Add(NewTemplate(entry, nextVersion, now));
            repaired++;
        }

        if (added.Count == 0 && moCoi.Count == 0)
            return;

        _dbContext.NotificationTemplates.AddRange(added);
        await _dbContext.SaveChangesAsync(ct);

        _logger?.LogInformation(
            "Seeded {New} notification templates, converged {Repaired} bản đã trôi khỏi danh mục, "
          + "hạ cờ {Orphan} bản mồ côi (cặp đã rời ma trận).",
            added.Count - repaired, repaired, moCoi.Count);
    }

    /// <summary>
    /// Bản đang dùng có cần thay bằng nội dung danh mục không. Xem chú thích luật hội tụ ở
    /// <see cref="SeedTemplatesAsync"/>.
    /// </summary>
    private static bool NeedsConvergence(NotificationTemplate active, NotificationTemplateCatalog.Entry entry)
    {
        var matchesCatalog = active.TitleTemplate == entry.Title && active.BodyTemplate == entry.Body;
        if (matchesCatalog)
            return false;

        // CreatedBy rỗng = do seeder tạo (seeder chạy ngoài ngữ cảnh người dùng nên interceptor để
        // trống). Nội dung seed suy ra được từ code nên thay thoải mái.
        var isSeederOwned = active.CreatedBy is null || active.CreatedBy == Guid.Empty;
        if (isSeederOwned)
            return true;

        // Người vận hành soạn: chỉ can thiệp khi template hỏng thật — gọi biến không tồn tại.
        return TemplateVariableGuard.FindUnknownVariables(
            active.Type, active.TitleTemplate, active.BodyTemplate) is not null;
    }

    private static NotificationTemplate NewTemplate(
        NotificationTemplateCatalog.Entry entry, int version, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = entry.Type,
            Channel = entry.Channel,
            TitleTemplate = entry.Title,
            BodyTemplate = entry.Body,
            Version = version,
            IsActive = true,
            CreatedAt = now,
        };

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
                Title = "You have been assigned ticket TKT-2602-0001",
                Body = "Battery not charging — Priority P1Critical",
                // Khoá phải khớp TicketAssignedConsumer: `code`, KHÔNG phải `ticketCode`. Dữ liệu mẫu
                // sai hợp đồng thì dạy người soạn template một tên biến không tồn tại.
                PayloadJson = "{\"code\":\"TKT-2602-0001\",\"priority\":\"P1Critical\"}",
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
                Title = "SLA warning: TKT-2602-0001",
                Body = "30 minutes remaining before the P1Critical SLA breach.",
                // SlaWarningConsumer ghi `percentage`, không có `ticketCode` lẫn `minutesRemaining`.
                PayloadJson = "{\"percentage\":85,\"screen\":\"TicketDetail\"}",
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
                Title = "[TKT-2602-0004] Resolved",
                Body = "Ticket TKT-2602-0004 has been resolved. Please confirm and rate it.",
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
                Title = "Battery anomaly BAT-2026-002",
                Body = "Type: Overheat — Severity: Critical",
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
                Title = "Environmental alert at Solar Farm Long An",
                Body = "Type: Smoke — Severity: Critical",
                EntityType = "EnvironmentalIncident",
                EntityId = Guid.NewGuid(),
                CreatedAt = now.AddMinutes(-2)
            });

        await _dbContext.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded sample notifications.");
    }
}
