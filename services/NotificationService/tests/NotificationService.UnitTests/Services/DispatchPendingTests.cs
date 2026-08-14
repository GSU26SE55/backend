using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// Sprint 6.2 NOTI-01 (#672) — test cho <c>DispatchPendingAsync</c>, đường chạy thật của worker
/// giao record Pending xuống channel. Đây là phần vá lỗi gốc "dispatcher là dead code".
/// Bao luôn NOTI-12 (digest defer), NOTI-13 (audit) và NOTI-14 (DB template thắng inline).
/// </summary>
public class DispatchPendingTests
{
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Mock<ICacheService> NoCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<NotificationPreference>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((NotificationPreference?)null);
        return m;
    }

    private static Mock<INotificationChannel> Channel(
        NotificationChannelEnum type, bool success = true, string? error = null)
    {
        var m = new Mock<INotificationChannel>();
        m.SetupGet(c => c.ChannelType).Returns(type);
        m.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync(new ChannelResult(success, error));
        return m;
    }

    private static Notification Pending(
        NotificationChannelEnum channel,
        NotificationTypeEnum type = NotificationTypeEnum.TicketCreated,
        int attempts = 0,
        string? entityType = "Ticket",
        string? payloadJson = null) => new()
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = type,
            Channel = channel,
            Status = NotificationStatusEnum.Pending,
            Title = "Original title",
            Body = "Original body",
            EntityType = entityType,
            PayloadJson = payloadJson,
            DispatchAttemptCount = attempts,
        };

    private static AccountReadModel Account(string? email = "user@x.com", string? phone = "0901234567") => new()
    {
        Id = UserId,
        Email = email ?? string.Empty,
        FullName = "User",
        PhoneNumber = phone,
        Role = "Customer",
        IsActive = true,
    };

    private static (NotificationDispatcher sut, Mock<INotificationUnitOfWork> uow, NoopAuditWriter audit) Build(
        Notification notification,
        INotificationChannel channel,
        NotificationPreference? pref = null,
        AccountReadModel? account = null,
        DeviceToken[]? tokens = null,
        NotificationDispatchOptions? options = null,
        NotificationTemplate[]? templates = null,
        ITemplateRenderer? renderer = null,
        NotificationBatch[]? batches = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: tokens ?? [],
            notificationSeed: [notification],
            accountSeed: account is null ? [] : [account],
            templateSeed: templates ?? [],
            batchSeed: batches ?? []);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns((pref is null ? Array.Empty<NotificationPreference>() : [pref]).AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        var audit = new NoopAuditWriter();

        var sut = new NotificationDispatcher(
            uow.Object,
            NoCache().Object,
            [channel],
            renderer ?? new Mock<ITemplateRenderer>().Object,
            audit,
            Microsoft.Extensions.Options.Options.Create(options ?? new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance);

        return (sut, uow, audit);
    }

    // ── happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchPending_InApp_MarksSent_AndWritesAudit()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        var (sut, uow, audit) = Build(n, Channel(NotificationChannelEnum.InApp).Object, account: Account());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        n.Status.Should().Be(NotificationStatusEnum.Sent);
        n.SentAt.Should().NotBeNull();
        n.FailureReason.Should().BeNull();
        n.DispatchAttemptCount.Should().Be(1);

        audit.Written.Should().ContainSingle();
        audit.Written[0].Action.Should().Be(NotificationAuditActionEnum.InAppCreated);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task DispatchPending_Push_MarksSent_AndAuditsPushSent_WithoutExternalToken()
    {
        var n = Pending(NotificationChannelEnum.Push);
        n.EntityId = Guid.NewGuid();
        n.CreatedAt = new DateTime(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
        var token = new DeviceToken { Id = Guid.NewGuid(), UserId = UserId, Token = "ExponentPushToken[x]", IsActive = true };
        var channel = Channel(NotificationChannelEnum.Push);
        var (sut, _, audit) = Build(n, channel.Object, account: Account(), tokens: [token]);

        var outcome = await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(request => request.EntityType == n.EntityType
                                          && request.EntityId == n.EntityId
                                          && request.CreatedAt == n.CreatedAt),
            It.IsAny<CancellationToken>()), Times.Once);

        outcome.Should().Be(DispatchOutcome.Sent);
        audit.Written[0].Action.Should().Be(NotificationAuditActionEnum.PushSent);

        // NOTI-16 — toàn bộ token của user được đưa vào 1 lần gửi.
        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.ExpoToken == null && r.ExpoTokens == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPending_Email_PassesRecipientEmailFromReadModel()
    {
        var n = Pending(NotificationChannelEnum.Email);
        var channel = Channel(NotificationChannelEnum.Email);
        var (sut, _, _) = Build(n, channel.Object, account: Account(email: "target@x.com"));

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Email == "target@x.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── lỗi vĩnh viễn (không retry vô hạn) ───────────────────────────────────

    [Fact]
    public async Task DispatchPending_ChannelDisabledByPreference_FailsImmediately()
    {
        var n = Pending(NotificationChannelEnum.Push);
        var pref = new NotificationPreference { UserId = UserId, PushEnabled = false, InAppEnabled = true };
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Push).Object, pref: pref, account: Account());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Failed);
        n.Status.Should().Be(NotificationStatusEnum.Failed);
        n.FailureReason.Should().Contain("disabled");
    }

    [Fact]
    public async Task DispatchPending_PushWithoutDeviceToken_UsesSelfHostedChannel()
    {
        var n = Pending(NotificationChannelEnum.Push);
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Push).Object, account: Account(), tokens: []);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        n.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task DispatchPending_EmailWithoutAddress_Fails()
    {
        var n = Pending(NotificationChannelEnum.Email);
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Email).Object, account: Account(email: ""));

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Failed);
        n.FailureReason.Should().Contain("email");
    }

    [Fact]
    public async Task DispatchPending_SmsWithoutPhone_Fails()
    {
        var n = Pending(NotificationChannelEnum.Sms);
        var pref = new NotificationPreference { UserId = UserId, SmsEnabled = true };
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Sms).Object, pref: pref, account: Account(phone: null));

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Failed);
        n.FailureReason.Should().Contain("phone number");
    }

    [Fact]
    public async Task DispatchPending_EmptyUserId_Fails_WithoutCallingChannel()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        n.UserId = Guid.Empty;
        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Failed);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPending_AlreadySent_IsNoOp()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        n.Status = NotificationStatusEnum.Sent;
        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── retry + backoff ──────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchPending_TransientFailure_SchedulesRetryWithBackoff()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        var options = new NotificationDispatchOptions { MaxAttempts = 3, BaseBackoffSeconds = 30 };
        var (sut, _, _) = Build(
            n, Channel(NotificationChannelEnum.InApp, success: false, error: "broker down").Object,
            account: Account(), options: options);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Retrying);
        n.Status.Should().Be(NotificationStatusEnum.Pending);
        n.DispatchAttemptCount.Should().Be(1);
        n.FailureReason.Should().Be("broker down");
        n.NextAttemptAt.Should().NotBeNull().And.BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task DispatchPending_LastAttemptFailure_MarksFailedPermanently()
    {
        var options = new NotificationDispatchOptions { MaxAttempts = 3 };
        var n = Pending(NotificationChannelEnum.InApp, attempts: 2);   // lần thử này là lần thứ 3
        var (sut, _, audit) = Build(
            n, Channel(NotificationChannelEnum.InApp, success: false, error: "vẫn lỗi").Object,
            account: Account(), options: options);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Failed);
        n.Status.Should().Be(NotificationStatusEnum.Failed);
        n.DispatchAttemptCount.Should().Be(3);
        n.NextAttemptAt.Should().BeNull();
        audit.Written.Should().BeEmpty("InApp thất bại không nằm trong 7 action audit của #AUDIT-34");
    }

    [Fact]
    public async Task DispatchPending_ChannelThrows_IsCaught_AndRetried()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.InApp);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new HttpRequestException("network"));

        var (sut, _, _) = Build(n, channel.Object, account: Account());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Retrying);
        n.FailureReason.Should().Contain("network");
    }

    // ── quiet hours (NOTI-01) ────────────────────────────────────────────────

    [Fact]
    public async Task DispatchPending_QuietHours_DefersPushWithoutConsumingAttempt()
    {
        var now = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")));

        var pref = new NotificationPreference
        {
            UserId = UserId,
            PushEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            QuietHoursStart = now.AddHours(-1),
            QuietHoursEnd = now.AddHours(1),
        };

        var n = Pending(NotificationChannelEnum.Push);
        var token = new DeviceToken { Id = Guid.NewGuid(), UserId = UserId, Token = "t", IsActive = true };
        var channel = Channel(NotificationChannelEnum.Push);
        var (sut, _, _) = Build(n, channel.Object, pref: pref, account: Account(), tokens: [token]);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Deferred);
        n.Status.Should().Be(NotificationStatusEnum.Pending);
        n.DispatchAttemptCount.Should().Be(0, "hoãn không được tiêu tốn lượt thử");
        n.NextAttemptAt.Should().NotBeNull();
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPending_QuietHours_InAppStillDelivered()
    {
        var now = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")));

        var pref = new NotificationPreference
        {
            UserId = UserId,
            InAppEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            QuietHoursStart = now.AddHours(-1),
            QuietHoursEnd = now.AddHours(1),
        };

        var n = Pending(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.InApp).Object, pref: pref, account: Account());

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
    }

    [Fact]
    public async Task DispatchPending_CriticalType_BypassesQuietHours()
    {
        var now = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")));

        var pref = new NotificationPreference
        {
            UserId = UserId,
            PushEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            QuietHoursStart = now.AddHours(-1),
            QuietHoursEnd = now.AddHours(1),
        };

        var n = Pending(NotificationChannelEnum.Push, NotificationTypeEnum.SlaBreached);
        var token = new DeviceToken { Id = Guid.NewGuid(), UserId = UserId, Token = "t", IsActive = true };
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Push).Object, pref: pref, account: Account(), tokens: [token]);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
    }

    [Fact]
    public async Task DispatchPending_PayloadBypassFlag_OverridesQuietHours()
    {
        var now = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")));

        var pref = new NotificationPreference
        {
            UserId = UserId,
            PushEnabled = true,
            TimeZone = "Asia/Ho_Chi_Minh",
            QuietHoursStart = now.AddHours(-1),
            QuietHoursEnd = now.AddHours(1),
        };

        var n = Pending(NotificationChannelEnum.Push, payloadJson: """{"bypassQuietHours":true}""");
        var token = new DeviceToken { Id = Guid.NewGuid(), UserId = UserId, Token = "t", IsActive = true };
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Push).Object, pref: pref, account: Account(), tokens: [token]);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
    }

    // ── digest (NOTI-12) ─────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchPending_DigestUser_DefersEmail()
    {
        var pref = new NotificationPreference { UserId = UserId, EmailEnabled = true, DigestWindowMinutes = 15 };
        var n = Pending(NotificationChannelEnum.Email);
        var channel = Channel(NotificationChannelEnum.Email);
        var (sut, _, _) = Build(n, channel.Object, pref: pref, account: Account());

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Deferred);
        n.NextAttemptAt.Should().NotBeNull().And.BeAfter(DateTime.UtcNow.AddMinutes(14));
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchPending_DigestUser_DoesNotDeferInApp()
    {
        var pref = new NotificationPreference { UserId = UserId, InAppEnabled = true, DigestWindowMinutes = 15 };
        var n = Pending(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.InApp).Object, pref: pref, account: Account());

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
    }

    [Fact]
    public async Task DispatchPending_DigestUser_DoesNotDeferCriticalType()
    {
        var pref = new NotificationPreference { UserId = UserId, EmailEnabled = true, Frequency = NotificationFrequencyEnum.Daily };
        var n = Pending(NotificationChannelEnum.Email, NotificationTypeEnum.SlaBreached);
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Email).Object, pref: pref, account: Account());

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
    }

    /// <summary>Bản digest tổng hợp KHÔNG được gom vào một digest khác (nếu không sẽ tự hoãn vĩnh viễn).</summary>
    [Fact]
    public async Task DispatchPending_DigestAggregateRecord_IsNotDeferredAgain()
    {
        var pref = new NotificationPreference { UserId = UserId, EmailEnabled = true, DigestWindowMinutes = 15 };
        var n = Pending(NotificationChannelEnum.Email, NotificationTypeEnum.System, entityType: NotificationDigest.EntityType);
        var (sut, _, _) = Build(n, Channel(NotificationChannelEnum.Email).Object, pref: pref, account: Account());

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
    }

    // ── template DB thắng inline (NOTI-14) ───────────────────────────────────

    [Fact]
    public async Task DispatchPending_WhenDbTemplateExists_RendersItInsteadOfInlineText()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,

            TitleTemplate = "TPL {{code}}",
            BodyTemplate = "BODY {{code}}",
            IsActive = true,
        };

        var renderer = new Mock<ITemplateRenderer>();
        renderer.Setup(r => r.RenderInline("TPL {{code}}", It.IsAny<object>())).Returns("TPL TKT-9");
        renderer.Setup(r => r.RenderInline("BODY {{code}}", It.IsAny<object>())).Returns("BODY TKT-9");

        var n = Pending(NotificationChannelEnum.InApp, payloadJson: """{"code":"TKT-9"}""");
        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account(), templates: [template], renderer: renderer.Object);

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "TPL TKT-9" && r.Body == "BODY TKT-9"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 03/08/2026 — nội dung của lần gửi hàng loạt THỦ CÔNG không được đem template đè lên.
    ///
    /// <para>Màn hình gửi hàng loạt cho admin chọn <b>bất kỳ</b> loại thông báo nào (loại quyết định
    /// nhóm tuỳ chọn nhận tin nên không ép về mỗi System được). Chọn "Ticket mới" rồi gõ tay tiêu đề
    /// thì template (TicketCreated × kênh) khớp và render — nhưng payload của một lần gửi tay KHÔNG
    /// có <c>code</c>, nên ra "Ticket mới " với chỗ trống và chữ admin vừa gõ biến mất sạch.</para>
    ///
    /// <para>Đã kiểm trên hệ thống thật: admin gõ "KTMPL Tiêu đề admin tự gõ", người nhận thấy
    /// "Ticket mới ". Lỗi có từ khi có tính năng gửi hàng loạt, âm thầm áp cho Email/Push/SMS; riêng
    /// InApp thì kết quả render vốn bị vứt đi nên không ai thấy, tới khi InApp ghi ngược nội dung
    /// vào dòng notification thì nó lộ ngay trên feed.</para>
    /// </summary>
    [Fact]
    public async Task DispatchPending_LanGuiThuCong_GiuNguyenChuAdminGo_KhongDungTemplate()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,
            TitleTemplate = "New ticket {{code}}",
            BodyTemplate = "Ticket {{code}} was just created.",
            IsActive = true,
        };

        var batch = new NotificationBatch
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Source = NotificationBatchSourceEnum.Manual,
            Title = "Title typed by admin",
            Body = "Content typed by admin",
        };

        // Renderer thật sẽ trả về chuỗi có chỗ trống; ở đây dựng sẵn để nếu guard hỏng thì thấy rõ.
        var renderer = new Mock<ITemplateRenderer>();
        renderer.Setup(r => r.RenderInline(It.IsAny<string>(), It.IsAny<object>())).Returns("New ticket ");

        var n = Pending(NotificationChannelEnum.InApp);
        n.BatchId = batch.Id;
        n.Title = "Title typed by admin";
        n.Body = "Content typed by admin";

        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account(),
            templates: [template], renderer: renderer.Object, batches: [batch]);

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "Title typed by admin" && r.Body == "Content typed by admin"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// 03/08/2026 — lần gửi THỦ CÔNG mà admin <b>bật "dùng mẫu"</b> thì phải render qua mẫu.
    ///
    /// <para>Đây là ranh giới tinh tế nhất của guard: cùng là <c>Source = Manual</c>, nhưng
    /// <c>UseTemplate = true</c> nghĩa là admin cố ý chọn và đã điền biến. Chặn nhầm ở đây là tính
    /// năng vừa làm thành vô dụng mà không có gì báo.</para>
    /// </summary>
    [Fact]
    public async Task DispatchPending_LanGuiThuCong_BatDungMau_ThiVanRenderQuaMau()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,
            TitleTemplate = "TPL {{code}}",
            BodyTemplate = "BODY {{code}}",
            IsActive = true,
        };

        var batch = new NotificationBatch
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Source = NotificationBatchSourceEnum.Manual,
            UseTemplate = true,
        };

        var renderer = new Mock<ITemplateRenderer>();
        renderer.Setup(r => r.RenderInline("TPL {{code}}", It.IsAny<object>())).Returns("TPL TKT-9");
        renderer.Setup(r => r.RenderInline("BODY {{code}}", It.IsAny<object>())).Returns("BODY TKT-9");

        var n = Pending(NotificationChannelEnum.InApp, payloadJson: """{"code":"TKT-9"}""");
        n.BatchId = batch.Id;

        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account(),
            templates: [template], renderer: renderer.Object, batches: [batch]);

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "TPL TKT-9" && r.Body == "BODY TKT-9"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Bật "dùng mẫu" nhưng kênh đó KHÔNG có mẫu khớp ⇒ rơi về nội dung admin gõ, không chặn gửi.
    /// </summary>
    [Fact]
    public async Task DispatchPending_BatDungMau_NhungKenhKhongCoMau_ThiRoiVeChuAdminGo()
    {
        var batch = new NotificationBatch
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Source = NotificationBatchSourceEnum.Manual,
            UseTemplate = true,
        };

        var n = Pending(NotificationChannelEnum.InApp);
        n.BatchId = batch.Id;
        n.Title = "Fallback title";
        n.Body = "Fallback body";

        var channel = Channel(NotificationChannelEnum.InApp);
        // templates rỗng — không cặp nào khớp
        var (sut, _, _) = Build(n, channel.Object, account: Account(), batches: [batch]);

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "Fallback title" && r.Body == "Fallback body"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Ngược lại: lần gửi sinh TỰ ĐỘNG từ sự kiện vẫn phải đi qua template như thường — guard trên
    /// chỉ được chặn đúng nhánh thủ công, không được tắt template cho mọi thứ có batch.
    /// </summary>
    [Fact]
    public async Task DispatchPending_LanGuiTuSuKien_VanDungTemplate()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,
            TitleTemplate = "TPL {{code}}",
            BodyTemplate = "BODY {{code}}",
            IsActive = true,
        };

        var batch = new NotificationBatch
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Source = NotificationBatchSourceEnum.Event,
        };

        var renderer = new Mock<ITemplateRenderer>();
        renderer.Setup(r => r.RenderInline("TPL {{code}}", It.IsAny<object>())).Returns("TPL TKT-9");
        renderer.Setup(r => r.RenderInline("BODY {{code}}", It.IsAny<object>())).Returns("BODY TKT-9");

        var n = Pending(NotificationChannelEnum.InApp, payloadJson: """{"code":"TKT-9"}""");
        n.BatchId = batch.Id;

        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account(),
            templates: [template], renderer: renderer.Object, batches: [batch]);

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "TPL TKT-9" && r.Body == "BODY TKT-9"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPending_WhenNoDbTemplate_UsesInlineTitleAndBody()
    {
        var n = Pending(NotificationChannelEnum.InApp);
        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account());

        await sut.DispatchPendingAsync(n);

        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "Original title" && r.Body == "Original body"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPending_WhenTemplateRenderThrows_FallsBackToInline()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,

            TitleTemplate = "{{#bad}}",
            BodyTemplate = "{{#bad}}",
            IsActive = true,
        };

        var renderer = new Mock<ITemplateRenderer>();
        renderer.Setup(r => r.RenderInline(It.IsAny<string>(), It.IsAny<object>()))
                .Throws(new InvalidOperationException("broken template"));

        var n = Pending(NotificationChannelEnum.InApp);
        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account(), templates: [template], renderer: renderer.Object);

        var outcome = await sut.DispatchPendingAsync(n);

        outcome.Should().Be(DispatchOutcome.Sent, "template hỏng không được chặn việc gửi");
        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "Original title"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchPending_WhenUseDbTemplatesDisabled_IgnoresDbTemplate()
    {
        var template = new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,

            TitleTemplate = "TPL",
            BodyTemplate = "BODY",
            IsActive = true,
        };

        var renderer = new Mock<ITemplateRenderer>();
        var n = Pending(NotificationChannelEnum.InApp);
        var channel = Channel(NotificationChannelEnum.InApp);
        var (sut, _, _) = Build(n, channel.Object, account: Account(), templates: [template],
            renderer: renderer.Object, options: new NotificationDispatchOptions { UseDbTemplates = false });

        await sut.DispatchPendingAsync(n);

        renderer.Verify(r => r.RenderInline(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
        channel.Verify(c => c.SendAsync(
            It.Is<SendRequest>(r => r.Title == "Original title"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── options (NOTI-14) ────────────────────────────────────────────────────

    [Fact]
    public void ResolveTypeChannelMatrix_ConfigOverridesDefault()
    {
        var options = new NotificationDispatchOptions
        {
            TypeChannelMatrix = new Dictionary<string, string[]>
            {
                ["SlaBreached"] = ["InApp"],
            }
        };

        var matrix = options.ResolveTypeChannelMatrix();

        matrix[NotificationTypeEnum.SlaBreached].Should().BeEquivalentTo(new[] { NotificationChannelEnum.InApp });
        matrix[NotificationTypeEnum.TicketCreated].Should().Contain(NotificationChannelEnum.Push,
            "key không override thì giữ nguyên default");
    }

    [Fact]
    public void ResolveTypeChannelMatrix_IgnoresUnparsableEntries()
    {
        var options = new NotificationDispatchOptions
        {
            TypeChannelMatrix = new Dictionary<string, string[]>
            {
                ["KhongTonTai"] = ["InApp"],
                ["SlaBreached"] = ["SaiKenh"],
            }
        };

        var matrix = options.ResolveTypeChannelMatrix();

        matrix[NotificationTypeEnum.SlaBreached].Should().Contain(NotificationChannelEnum.Sms,
            "giá trị channel không parse được thì giữ default, không làm rỗng danh sách");
    }

    [Fact]
    public void ResolveCriticalTypes_EmptyConfig_UsesDefault()
    {
        new NotificationDispatchOptions().ResolveCriticalTypes()
            .Should().Contain(NotificationTypeEnum.SlaBreached);
    }

    [Fact]
    public void ResolveCriticalTypes_ConfigReplacesDefault()
    {
        var options = new NotificationDispatchOptions { CriticalTypes = ["TicketCreated"] };
        var result = options.ResolveCriticalTypes();

        result.Should().Contain(NotificationTypeEnum.TicketCreated);
        result.Should().NotContain(NotificationTypeEnum.SlaBreached);
    }
}
