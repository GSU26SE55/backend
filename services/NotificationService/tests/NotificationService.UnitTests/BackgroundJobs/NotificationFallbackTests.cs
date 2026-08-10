using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.BackgroundJobs;
using NotificationService.Infrastructure.Persistence;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.BackgroundJobs;

/// <summary>
/// Sprint 6.3 NOTI3-05 (#705) — chuỗi dự phòng push → SMS cho notification critical.
///
/// Kiểm chứng đúng ranh giới đã chốt ở nhánh B: chỉ bù khi push critical **không có** receipt
/// sau ngưỡng chờ, mỗi push bù đúng một lần, và không đụng tới notification thường.
/// </summary>
public class NotificationFallbackTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _provider;

    public NotificationFallbackTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"fallback-{Guid.NewGuid()}")
            .Options;

        _db = new ApplicationDbContext(options, null!);

        var services = new ServiceCollection();
        services.AddSingleton(_db);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _db.Dispose();
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    private NotificationFallbackBackgroundService Sut(NotificationFallbackOptions? options = null) =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryLease(),
            Options.Create(options ?? new NotificationFallbackOptions { PushReceiptTimeoutMinutes = 10 }),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationFallbackBackgroundService>.Instance);

    /// <summary><c>SlaBreached</c> nằm trong <c>DefaultCriticalTypes</c>.</summary>
    private NotificationEntity CriticalPush(DateTime sentAt, NotificationStatusEnum status = NotificationStatusEnum.Sent)
    {
        var entity = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Type = NotificationTypeEnum.SlaBreached,
            Channel = NotificationChannelEnum.Push,
            Status = status,
            Title = "SLA breach",
            Body = "Ticket sắp quá hạn",
            EntityType = "Ticket",
            EntityId = Guid.NewGuid(),
            SentAt = sentAt,
            CreatedAt = sentAt,
            PayloadJson = """{"ticketId":"abc"}""",
        };

        _db.Notifications.Add(entity);
        _db.SaveChanges();
        return entity;
    }

    private async Task<List<NotificationEntity>> SmsRowsAsync() =>
        await _db.Notifications.Where(n => n.Channel == NotificationChannelEnum.Sms).ToListAsync();

    [Fact]
    public async Task CriticalPush_WithoutReceipt_AfterTimeout_CreatesSmsFallback()
    {
        var push = CriticalPush(DateTime.UtcNow.AddMinutes(-30));

        var created = await Sut().ProcessOnceAsync(CancellationToken.None);

        created.Should().Be(1);

        var sms = (await SmsRowsAsync()).Should().ContainSingle().Subject;
        sms.UserId.Should().Be(push.UserId);
        sms.Type.Should().Be(push.Type);
        sms.Status.Should().Be(NotificationStatusEnum.Pending, "worker dispatch sẽ gửi ở vòng sau");
        sms.EntityId.Should().Be(push.EntityId);
    }

    /// <summary>Marker này là cách báo cáo phân biệt "một sự kiện" với "hai notification".</summary>
    [Fact]
    public async Task SmsFallback_CarriesFallbackFromMarker_AndKeepsOriginalPayload()
    {
        var push = CriticalPush(DateTime.UtcNow.AddMinutes(-30));

        await Sut().ProcessOnceAsync(CancellationToken.None);

        var sms = (await SmsRowsAsync()).Single();
        using var doc = JsonDocument.Parse(sms.PayloadJson!);

        doc.RootElement.GetProperty("fallbackFrom").GetString().Should().Be(push.Id.ToString());
        doc.RootElement.GetProperty("fallbackChannel").GetString().Should().Be("Push");
        doc.RootElement.GetProperty("ticketId").GetString().Should().Be("abc", "payload gốc phải giữ nguyên");
    }

    /// <summary>Receipt ok = đã tới thiết bị. Bù SMS lúc này là làm phiền người dùng vô cớ.</summary>
    [Fact]
    public async Task PushWithOkReceipt_DoesNotFallback_AndIsPromotedToDelivered()
    {
        var push = CriticalPush(DateTime.UtcNow.AddMinutes(-30));

        _db.PushReceipts.Add(new PushReceipt
        {
            Id = Guid.NewGuid(),
            NotificationId = push.Id,
            UserId = push.UserId,
            TicketId = "t1",
            DeviceToken = "ExponentPushToken[a]",
            Status = PushReceiptStatusEnum.Ok,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
        });
        await _db.SaveChangesAsync();

        var created = await Sut().ProcessOnceAsync(CancellationToken.None);

        created.Should().Be(0);
        (await SmsRowsAsync()).Should().BeEmpty();

        var reloaded = await _db.Notifications.FirstAsync(n => n.Id == push.Id);
        reloaded.Status.Should().Be(NotificationStatusEnum.Delivered);
    }

    /// <summary>Chưa hết ngưỡng chờ thì receipt còn có thể về — bù sớm là gửi thừa.</summary>
    [Fact]
    public async Task PushInsideTimeoutWindow_IsNotFallenBack()
    {
        CriticalPush(DateTime.UtcNow.AddMinutes(-2));

        var created = await Sut(new NotificationFallbackOptions { PushReceiptTimeoutMinutes = 10 })
            .ProcessOnceAsync(CancellationToken.None);

        created.Should().Be(0);
        (await SmsRowsAsync()).Should().BeEmpty();
    }

    /// <summary>Nếu không chống lặp thì mỗi vòng quét (2 phút) lại bắn thêm một SMS.</summary>
    [Fact]
    public async Task RepeatedRuns_CreateFallbackOnlyOnce()
    {
        CriticalPush(DateTime.UtcNow.AddMinutes(-30));
        var sut = Sut();

        (await sut.ProcessOnceAsync(CancellationToken.None)).Should().Be(1);
        (await sut.ProcessOnceAsync(CancellationToken.None)).Should().Be(0);
        (await sut.ProcessOnceAsync(CancellationToken.None)).Should().Be(0);

        (await SmsRowsAsync()).Should().HaveCount(1);
    }

    /// <summary>Fallback chỉ dành cho critical — notification thường không được phép đốt SMS.</summary>
    [Fact]
    public async Task NonCriticalPush_IsNeverFallenBack()
    {
        var push = CriticalPush(DateTime.UtcNow.AddMinutes(-30));
        push.Type = NotificationTypeEnum.TicketCreated;
        await _db.SaveChangesAsync();

        (await Sut().ProcessOnceAsync(CancellationToken.None)).Should().Be(0);
        (await SmsRowsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(NotificationStatusEnum.Delivered)]
    [InlineData(NotificationStatusEnum.Read)]
    [InlineData(NotificationStatusEnum.Opened)]
    [InlineData(NotificationStatusEnum.Failed)]
    [InlineData(NotificationStatusEnum.Pending)]
    public async Task PushNotInSentState_IsNotFallenBack(NotificationStatusEnum status)
    {
        CriticalPush(DateTime.UtcNow.AddMinutes(-30), status);

        (await Sut().ProcessOnceAsync(CancellationToken.None)).Should().Be(0);
        (await SmsRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_ByConfiguration_DoesNothing()
    {
        CriticalPush(DateTime.UtcNow.AddMinutes(-30));

        var sut = Sut(new NotificationFallbackOptions { Enabled = false });
        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        (await SmsRowsAsync()).Should().BeEmpty();
    }

    /// <summary>Payload gốc hỏng không được làm chết cả bản SMS bù.</summary>
    [Fact]
    public async Task MalformedOriginalPayload_StillProducesValidFallbackPayload()
    {
        var push = CriticalPush(DateTime.UtcNow.AddMinutes(-30));
        push.PayloadJson = "{ đây không phải json";
        await _db.SaveChangesAsync();

        (await Sut().ProcessOnceAsync(CancellationToken.None)).Should().Be(1);

        var sms = (await SmsRowsAsync()).Single();
        using var doc = JsonDocument.Parse(sms.PayloadJson!);
        doc.RootElement.GetProperty("fallbackFrom").GetString().Should().Be(push.Id.ToString());
    }

    /// <summary>
    /// Ràng buộc thời gian giữa NOTI3-05 và NOTI3-02: ngưỡng chờ fallback phải lớn hơn thời điểm
    /// sớm nhất worker đối soát có thể biết kết quả.
    ///
    /// Đặt thấp hơn thì fallback bắn SMS trong khi receipt còn chưa được phép hỏi Expo — nghĩa là
    /// **mọi** push critical đều lãnh một SMS thừa, mà không có lỗi kỹ thuật nào để lần ra.
    /// Test này chốt mặc định an toàn để không ai vô tình hạ xuống.
    /// </summary>
    [Fact]
    public void DefaultTimeout_IsSafeAgainstReceiptReconcileWindow()
    {
        var fallback = new NotificationFallbackOptions();
        var receipt = new ExpoReceiptOptions();

        var minimumSafe = receipt.MinAgeMinutes
                          + (int)Math.Ceiling(receipt.PollIntervalSeconds / 60.0)
                          + 5;   // biên dự phòng cho độ trễ HTTP

        fallback.PushReceiptTimeoutMinutes.Should().BeGreaterThanOrEqualTo(minimumSafe,
            "fallback phải đợi qua ít nhất một chu kỳ đối soát đầy đủ trước khi kết luận push hỏng");
    }

    /// <summary>Cache no-op — test không dựng Redis, leader election phải tự xoay xở được.</summary>
}
