using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers.Notification;

/// <summary>
/// Sprint 6.3 NOTI3-01 (#701) — feed in-app phải là <b>1 dòng / sự kiện</b>.
///
/// Mô hình dữ liệu: 1 sự kiện nghiệp vụ → nhiều record, mỗi channel một record. Record của
/// Push/Email/Sms là bản ghi GIAO NHẬN, không phải mục hiển thị. Trước sprint này endpoint trả hết
/// nên user thấy cùng một thông báo lặp 2–4 lần và badge chưa đọc phồng đúng bấy nhiêu lần.
/// </summary>
public class FeedChannelFilterTests
{
    private static readonly Guid UserId = Guid.Parse("cccccccc-1111-2222-3333-444444444444");
    private static readonly Guid TicketId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    /// <summary>1 sự kiện SLA breach ghi ra 4 record (InApp + Push + Email + Sms) — như SlaBreachedConsumer P1.</summary>
    private static NotificationEntity[] OneEventAcrossFourChannels(
        NotificationStatusEnum status = NotificationStatusEnum.Sent, DateTime? createdAt = null)
    {
        var at = createdAt ?? new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);
        return new[]
        {
            NotificationChannelEnum.InApp,
            NotificationChannelEnum.Push,
            NotificationChannelEnum.Email,
            NotificationChannelEnum.Sms,
        }.Select(c => new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationTypeEnum.SlaBreached,
            Channel = c,
            Status = status,
            Title = "SLA P1 breached",
            Body = "Ticket breach SLA.",
            EntityType = "Ticket",
            EntityId = TicketId,
            CreatedAt = at,
        }).ToArray();
    }

    // ── danh sách feed ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_ByDefault_ReturnsOnlyInAppRow()
    {
        var seed = OneEventAcrossFourChannels();
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new GetNotificationsQueryHandler(uow.Object);

        var resp = await handler.Handle(
            new GetNotificationsQuery { UserId = UserId, PageNumber = 1, PageSize = 20 }, CancellationToken.None);

        resp.Data!.Items.Should().ContainSingle("1 sự kiện chỉ được hiện 1 dòng trong feed");
        resp.Data.Items[0].Channel.Should().Be(NotificationChannelEnum.InApp);
        resp.Data.TotalItems.Should().Be(1, "tổng số bản ghi cũng phải đếm theo feed, không đếm bản giao nhận");
    }

    [Fact]
    public async Task GetNotifications_WithExplicitChannel_ReturnsThatChannelOnly()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: OneEventAcrossFourChannels());
        var handler = new GetNotificationsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetNotificationsQuery
        {
            UserId = UserId,
            PageNumber = 1,
            PageSize = 20,
            Channel = NotificationChannelEnum.Sms
        }, CancellationToken.None);

        resp.Data!.Items.Should().ContainSingle();
        resp.Data.Items[0].Channel.Should().Be(NotificationChannelEnum.Sms);
    }

    [Fact]
    public async Task GetNotifications_WithIncludeAllChannels_ReturnsEveryDeliveryRow()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: OneEventAcrossFourChannels());
        var handler = new GetNotificationsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetNotificationsQuery
        {
            UserId = UserId,
            PageNumber = 1,
            PageSize = 20,
            IncludeAllChannels = true
        }, CancellationToken.None);

        resp.Data!.Items.Should().HaveCount(4, "màn hình chẩn đoán vẫn cần xem đủ mọi bản ghi giao nhận");
    }

    // ── badge chưa đọc ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCount_CountsOnlyInAppRows()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: OneEventAcrossFourChannels(NotificationStatusEnum.Sent));
        var handler = new GetUnreadCountQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetUnreadCountQuery { UserId = UserId }, CancellationToken.None);

        resp.Data.Should().Be(1, "badge phải khớp số dòng user nhìn thấy, không phải số bản ghi giao nhận");
    }

    [Fact]
    public async Task GetUnreadCount_TwoEvents_ReturnsTwo()
    {
        var seed = OneEventAcrossFourChannels(createdAt: new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc))
            .Concat(OneEventAcrossFourChannels(createdAt: new DateTime(2026, 7, 30, 11, 0, 0, DateTimeKind.Utc)))
            .ToArray();
        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new GetUnreadCountQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetUnreadCountQuery { UserId = UserId }, CancellationToken.None);

        resp.Data.Should().Be(2);
    }

    // ── lan mark-read sang record anh em ──────────────────────────────────────

    [Fact]
    public async Task MarkRead_AlsoMarksSiblingRows_SoUserIsNotPushedForSomethingAlreadyRead()
    {
        var seed = OneEventAcrossFourChannels(NotificationStatusEnum.Pending);
        var inApp = seed.Single(n => n.Channel == NotificationChannelEnum.InApp);
        inApp.Status = NotificationStatusEnum.Sent;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new MarkNotificationReadCommandHandler(uow.Object, new NoopAuditWriter());

        var resp = await handler.Handle(
            new MarkNotificationReadCommand { Id = inApp.Id, UserId = UserId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        inApp.Status.Should().Be(NotificationStatusEnum.Read);

        seed.Where(n => n.Channel != NotificationChannelEnum.InApp)
            .Should().AllSatisfy(n =>
            {
                n.Status.Should().Be(NotificationStatusEnum.Read,
                    "record Pending còn lại sẽ bị dispatch worker gửi đi ⇒ user đã đọc rồi vẫn lãnh push");
                n.NextAttemptAt.Should().BeNull();
            });
    }

    [Fact]
    public async Task MarkRead_LeavesFailedSiblingsUntouched_ForDiagnostics()
    {
        var seed = OneEventAcrossFourChannels(NotificationStatusEnum.Pending);
        var inApp = seed.Single(n => n.Channel == NotificationChannelEnum.InApp);
        var failed = seed.Single(n => n.Channel == NotificationChannelEnum.Sms);
        failed.Status = NotificationStatusEnum.Failed;
        failed.FailureReason = "SIM out of credit";

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: seed);
        var handler = new MarkNotificationReadCommandHandler(uow.Object, new NoopAuditWriter());

        await handler.Handle(new MarkNotificationReadCommand { Id = inApp.Id, UserId = UserId }, CancellationToken.None);

        failed.Status.Should().Be(NotificationStatusEnum.Failed, "ghi đè sẽ xoá mất dấu vết lỗi");
        failed.FailureReason.Should().Be("SIM out of credit");
    }

    [Fact]
    public async Task MarkRead_DoesNotTouchOtherEventsOfSameUser()
    {
        var target = OneEventAcrossFourChannels(
            NotificationStatusEnum.Pending, new DateTime(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc));
        var unrelated = OneEventAcrossFourChannels(
            NotificationStatusEnum.Pending, new DateTime(2026, 7, 30, 15, 0, 0, DateTimeKind.Utc));

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: target.Concat(unrelated).ToArray());
        var handler = new MarkNotificationReadCommandHandler(uow.Object, new NoopAuditWriter());

        var inApp = target.Single(n => n.Channel == NotificationChannelEnum.InApp);
        await handler.Handle(new MarkNotificationReadCommand { Id = inApp.Id, UserId = UserId }, CancellationToken.None);

        unrelated.Should().AllSatisfy(n => n.Status.Should().Be(NotificationStatusEnum.Pending,
            "sự kiện khác thời điểm không được coi là anh em"));
    }

    /// <summary>
    /// Record dữ liệu cũ có thể mang <c>CreatedAt = DateTime.MinValue</c>; phép trừ cửa sổ anh em
    /// từng ném <c>ArgumentOutOfRangeException</c> làm hỏng cả endpoint mark-read.
    /// </summary>
    [Fact]
    public async Task MarkRead_WithMinValueCreatedAt_DoesNotOverflow()
    {
        var entity = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationTypeEnum.TicketCreated,
            Channel = NotificationChannelEnum.InApp,
            Status = NotificationStatusEnum.Sent,
            Title = "T",
            Body = "B",
            CreatedAt = DateTime.MinValue,
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(notificationSeed: new[] { entity });
        var handler = new MarkNotificationReadCommandHandler(uow.Object, new NoopAuditWriter());

        var act = async () => await handler.Handle(
            new MarkNotificationReadCommand { Id = entity.Id, UserId = UserId }, CancellationToken.None);

        await act.Should().NotThrowAsync();
        entity.Status.Should().Be(NotificationStatusEnum.Read);
    }
}
