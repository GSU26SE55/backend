using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;
using SmsService.Infrastructure.BackgroundJobs;
using SmsService.Infrastructure.Persistence;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.BackgroundJobs;

/// <summary>
/// Hai job nền mà trước đây phủ 0% — và đáng lo hơn con số phủ là <b>việc chúng hỏng thì không có
/// triệu chứng gì</b>:
///
/// <list type="bullet">
///   <item><see cref="StaleSmsReaperBackgroundService"/> hỏng ⇒ tin nhắn mà thiết bị đã nhận rồi
///   chết giữa chừng sẽ kẹt ở <c>Sending</c> vĩnh viễn. Không ai gửi lại, không lỗi nào nổi lên,
///   người dùng chỉ đơn giản là không nhận được tin.</item>
///   <item><see cref="SmsMessageRedactorBackgroundService"/> hỏng ⇒ nội dung tin nhắn (có thể chứa
///   mã OTP, thông tin cá nhân) nằm lại trong cơ sở dữ liệu vô thời hạn.</item>
/// </list>
///
/// <para><b>Cách chạy được:</b> cả hai lớp khai <c>protected virtual TickInterval</c>; ở đây dùng
/// lớp con rút nhịp xuống nửa giây. Đó là khe duy nhất được thêm vào mã production, và giá trị mặc
/// định (1 phút / 15 phút) không đổi.</para>
/// </summary>
[Collection(nameof(SmsDatabaseCollection))]
public class StaleSmsReaperAndRedactorTests : IAsyncLifetime
{
    private readonly SmsPostgresFixture _db;
    public StaleSmsReaperAndRedactorTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class FastReaper(IServiceScopeFactory f, ILogger<StaleSmsReaperBackgroundService> l)
        : StaleSmsReaperBackgroundService(f, l)
    {
        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(500);
    }

    private sealed class FastRedactor(IServiceScopeFactory f, ILogger<SmsMessageRedactorBackgroundService> l)
        : SmsMessageRedactorBackgroundService(f, l)
    {
        protected override TimeSpan TickInterval => TimeSpan.FromMilliseconds(500);
    }

    private ServiceProvider BuildProvider()
    {
        var provider = new ServiceCollection()
            .AddDbContext<SmsDbContext>(o => o.UseNpgsql(_db.ConnectionString))
            .AddScoped<ICurrentUserService, NoUserCurrentUserService>()
            .AddScoped<AuditableEntityInterceptor>()
            .AddLogging()
            .BuildServiceProvider(true);

        using var probe = provider.CreateScope();
        probe.ServiceProvider.GetRequiredService<SmsDbContext>().Should().NotBeNull();
        return provider;
    }

    private static async Task RunUntilAsync(Microsoft.Extensions.Hosting.BackgroundService svc,
        Func<Task<bool>> until, int timeoutSeconds = 20)
    {
        await svc.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (await until())
                    return;
                await Task.Delay(150);
            }
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
            svc.Dispose();
        }
    }

    private static SmsMessage Msg(
        SmsStatus status, DateTime? pickedAt = null, DateTime? sentAt = null,
        string? message = "noi dung mat", bool deleted = false) => new()
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "0901234567",
            Message = message,
            SourceService = "TicketService",
            CorrelationId = Guid.NewGuid(),
            Status = status,
            PickedAt = pickedAt,
            SentAt = sentAt,
            GatewayDeviceCode = pickedAt is null ? null : "GW-001",
            GatewayDeviceId = pickedAt is null ? null : Guid.NewGuid(),
            IsDeleted = deleted,
        };

    private async Task SeedAsync(params SmsMessage[] rows)
    {
        await using var db = _db.NewContext();
        db.SmsMessages.AddRange(rows);
        await db.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────── StaleSmsReaper

    /// <summary>
    /// Tin bị giữ quá 5 phút phải quay về <c>Pending</c> để thiết bị khác nhận, và <b>không</b> bị
    /// tăng <c>RetryCount</c> — thiết bị chết không phải là một lần gửi thất bại, tính vào retry sẽ
    /// đẩy tin tới trần retry oan.
    /// </summary>
    [Fact]
    public async Task Reaper_RevertsStaleClaim_WithoutBumpingRetryCount()
    {
        var stale = Msg(SmsStatus.Sending, pickedAt: DateTime.UtcNow.AddMinutes(-10));
        await SeedAsync(stale);

        await using var provider = BuildProvider();
        var reaper = new FastReaper(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleSmsReaperBackgroundService>.Instance);

        await RunUntilAsync(reaper, async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsMessages.AnyAsync(x => x.Id == stale.Id && x.Status == SmsStatus.Pending);
        });

        await using var verify = _db.NewContext();
        var row = await verify.SmsMessages.SingleAsync(x => x.Id == stale.Id);
        row.Status.Should().Be(SmsStatus.Pending);
        row.PickedAt.Should().BeNull();
        row.GatewayDeviceCode.Should().BeNull();
        row.GatewayDeviceId.Should().BeNull();
        row.RetryCount.Should().Be(0, "thiết bị chết KHÔNG phải một lần gửi thất bại — tính vào retry là oan");

        var audit = await verify.SmsAuditLogs.SingleAsync(a => a.SmsMessageId == stale.Id);
        audit.Event.Should().Be(SmsAuditEvent.Reaped, "phải để lại dấu vết vì sao tin quay về hàng đợi");
    }

    /// <summary>
    /// Tin vừa được nhận (chưa quá ngưỡng) phải để yên. Thu hồi sớm sẽ khiến hai thiết bị cùng gửi
    /// một tin — người dùng nhận tin trùng.
    /// </summary>
    [Fact]
    public async Task Reaper_LeavesFreshClaimAlone()
    {
        var fresh = Msg(SmsStatus.Sending, pickedAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedAsync(fresh);

        await using var provider = BuildProvider();
        var reaper = new FastReaper(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleSmsReaperBackgroundService>.Instance);

        await reaper.StartAsync(CancellationToken.None);
        await Task.Delay(2000); // vài nhịp
        await reaper.StopAsync(CancellationToken.None);
        reaper.Dispose();

        await using var verify = _db.NewContext();
        var row = await verify.SmsMessages.SingleAsync(x => x.Id == fresh.Id);
        row.Status.Should().Be(SmsStatus.Sending, "thu hồi sớm sẽ làm hai thiết bị cùng gửi một tin");
        row.PickedAt.Should().NotBeNull();
    }

    /// <summary>Chỉ tin ở trạng thái <c>Sending</c> mới thuộc phạm vi — các trạng thái khác phải nguyên vẹn.</summary>
    [Fact]
    public async Task Reaper_IgnoresOtherStatuses_AndSoftDeletedRows()
    {
        var sent = Msg(SmsStatus.Sent, pickedAt: DateTime.UtcNow.AddHours(-2), sentAt: DateTime.UtcNow.AddHours(-2));
        var cancelled = Msg(SmsStatus.Cancelled, pickedAt: DateTime.UtcNow.AddHours(-2));
        var softDeleted = Msg(SmsStatus.Sending, pickedAt: DateTime.UtcNow.AddHours(-2), deleted: true);
        var target = Msg(SmsStatus.Sending, pickedAt: DateTime.UtcNow.AddHours(-2));
        await SeedAsync(sent, cancelled, softDeleted, target);

        await using var provider = BuildProvider();
        var reaper = new FastReaper(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleSmsReaperBackgroundService>.Instance);

        await RunUntilAsync(reaper, async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsMessages.AnyAsync(x => x.Id == target.Id && x.Status == SmsStatus.Pending);
        });

        await using var verify = _db.NewContext();
        (await verify.SmsMessages.SingleAsync(x => x.Id == sent.Id)).Status.Should().Be(SmsStatus.Sent);
        (await verify.SmsMessages.SingleAsync(x => x.Id == cancelled.Id)).Status.Should().Be(SmsStatus.Cancelled);
        (await verify.SmsMessages.SingleAsync(x => x.Id == softDeleted.Id)).Status.Should().Be(SmsStatus.Sending,
            "dòng đã xoá mềm nằm ngoài phạm vi");
    }

    [Fact]
    public async Task Reaper_WithNothingToDo_TicksQuietly_AndStopsGracefully()
    {
        await using var provider = BuildProvider();
        var reaper = new FastReaper(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StaleSmsReaperBackgroundService>.Instance);

        await reaper.StartAsync(CancellationToken.None);
        await Task.Delay(1500);

        var stop = async () => await reaper.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
        reaper.Dispose();
    }

    // ────────────────────────────────────────────────── SmsMessageRedactor

    /// <summary>
    /// Tin đã gửi quá 24 giờ phải bị xoá <b>nội dung</b> nhưng giữ nguyên bản ghi (số điện thoại,
    /// mốc thời gian, trạng thái) — vẫn cần cho đối soát, chỉ bỏ đi phần nhạy cảm.
    /// </summary>
    [Fact]
    public async Task Redactor_ClearsMessageBody_ButKeepsTheRecord()
    {
        var old = Msg(SmsStatus.Sent, sentAt: DateTime.UtcNow.AddHours(-30), message: "Ma OTP cua ban la 123456");
        await SeedAsync(old);

        await using var provider = BuildProvider();
        var redactor = new FastRedactor(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SmsMessageRedactorBackgroundService>.Instance);

        await RunUntilAsync(redactor, async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsMessages.AnyAsync(x => x.Id == old.Id && x.RedactedAt != null);
        });

        await using var verify = _db.NewContext();
        var row = await verify.SmsMessages.SingleAsync(x => x.Id == old.Id);
        row.Message.Should().BeNull("nội dung phải biến mất khỏi cơ sở dữ liệu");
        row.RedactedAt.Should().NotBeNull();
        row.PhoneNumber.Should().Be("0901234567", "bản ghi vẫn phải còn để đối soát");
        row.Status.Should().Be(SmsStatus.Sent);

        var audit = await verify.SmsAuditLogs.SingleAsync(a => a.SmsMessageId == old.Id);
        audit.Event.Should().Be(SmsAuditEvent.Redacted);
    }

    /// <summary>
    /// Tin gửi trong vòng 24 giờ phải giữ nguyên nội dung — ứng dụng Android còn cần hiển thị.
    /// Xoá sớm là làm hỏng tính năng đang dùng.
    /// </summary>
    [Fact]
    public async Task Redactor_KeepsRecentMessagesIntact()
    {
        var recent = Msg(SmsStatus.Sent, sentAt: DateTime.UtcNow.AddHours(-2), message: "van con trong han");
        await SeedAsync(recent);

        await using var provider = BuildProvider();
        var redactor = new FastRedactor(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SmsMessageRedactorBackgroundService>.Instance);

        await redactor.StartAsync(CancellationToken.None);
        await Task.Delay(2000);
        await redactor.StopAsync(CancellationToken.None);
        redactor.Dispose();

        await using var verify = _db.NewContext();
        var row = await verify.SmsMessages.SingleAsync(x => x.Id == recent.Id);
        row.Message.Should().Be("van con trong han", "trong 24h đầu ứng dụng còn cần hiển thị nội dung");
        row.RedactedAt.Should().BeNull();
    }

    /// <summary>
    /// Chỉ tin <c>Sent</c> mới bị xoá nội dung. Tin <c>Failed</c>/<c>Pending</c> giữ nội dung để còn
    /// gửi lại hoặc điều tra; tin đã xoá nội dung rồi thì không xử lý lại (tránh đẻ ra bản ghi kiểm
    /// toán trùng ở mỗi nhịp).
    /// </summary>
    [Fact]
    public async Task Redactor_OnlyTargetsSentMessagesWithContent()
    {
        var failed = Msg(SmsStatus.Failed, sentAt: DateTime.UtcNow.AddDays(-3), message: "that bai");
        var pending = Msg(SmsStatus.Pending, message: "cho gui");
        var alreadyRedacted = Msg(SmsStatus.Sent, sentAt: DateTime.UtcNow.AddDays(-3), message: null);
        var target = Msg(SmsStatus.Sent, sentAt: DateTime.UtcNow.AddDays(-3), message: "can xoa");
        await SeedAsync(failed, pending, alreadyRedacted, target);

        await using var provider = BuildProvider();
        var redactor = new FastRedactor(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SmsMessageRedactorBackgroundService>.Instance);

        await RunUntilAsync(redactor, async () =>
        {
            await using var db = _db.NewContext();
            return await db.SmsMessages.AnyAsync(x => x.Id == target.Id && x.Message == null);
        });

        await using var verify = _db.NewContext();
        (await verify.SmsMessages.SingleAsync(x => x.Id == failed.Id)).Message.Should().Be("that bai",
            "tin thất bại còn cần nội dung để gửi lại hoặc điều tra");
        (await verify.SmsMessages.SingleAsync(x => x.Id == pending.Id)).Message.Should().Be("cho gui");

        (await verify.SmsAuditLogs.CountAsync(a => a.SmsMessageId == alreadyRedacted.Id)).Should().Be(0,
            "tin đã xoá nội dung rồi không được xử lý lại — nếu không mỗi nhịp sẽ đẻ thêm một bản ghi kiểm toán");
    }

    [Fact]
    public async Task Redactor_WithNothingToDo_TicksQuietly_AndStopsGracefully()
    {
        await using var provider = BuildProvider();
        var redactor = new FastRedactor(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SmsMessageRedactorBackgroundService>.Instance);

        await redactor.StartAsync(CancellationToken.None);
        await Task.Delay(1500);

        var stop = async () => await redactor.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
        redactor.Dispose();
    }

    private sealed class NoUserCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
    }
}
