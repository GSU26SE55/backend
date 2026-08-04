using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Application.CQRS.Handler.Preference;
using NotificationService.Application.CQRS.Query.Preference;
using NotificationService.Application.DTOs.Response.Preference;
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
using DeviceTokenEntity = NotificationService.Domain.Entities.DeviceToken;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers.Preference;

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — ma trận tuỳ chọn nhóm × kênh.
///
/// Vấn đề gốc: trước sprint này người dùng chỉ bật/tắt được **cả kênh**. Tắt Email để khỏi bị làm
/// phiền bởi chat cũng đồng nghĩa mất luôn email SLA sắp vỡ — nên họ hoặc chịu ồn, hoặc bỏ lỡ việc
/// quan trọng.
/// </summary>
public class NotificationCategoryMapTests
{
    /// <summary>
    /// Test bao: thêm type mới mà quên khai báo nhóm thì đỏ ngay ở CI, thay vì âm thầm rơi vào
    /// nhóm mặc định và né mất tuỳ chọn của người dùng.
    /// </summary>
    [Fact]
    public void EveryNotificationType_HasExplicitCategory()
    {
        var missing = Enum.GetValues<NotificationTypeEnum>()
            .Where(t => !NotificationCategoryMap.All.ContainsKey(t))
            .ToList();

        missing.Should().BeEmpty(
            "mọi NotificationTypeEnum phải được khai báo nhóm trong NotificationCategoryMap");
    }

    /// <summary>
    /// GH-83 — chặn hai tên enum trùng giá trị.
    ///
    /// **Vì sao cần test riêng:** <see cref="EveryNotificationType_HasExplicitCategory"/> kiểm theo
    /// *giá trị* (<c>All.ContainsKey</c>). Khi <c>TicketMerged</c> và <c>ChatEscalatedToAdmin</c> cùng
    /// bằng 27 thì khoá 27 đã tồn tại ⇒ test đó vẫn **XANH**, trong khi thực tế <c>TicketMerged</c>
    /// không thể có nhóm riêng, bị xếp nhầm vào <c>Sla</c> và biến mất khỏi
    /// <c>GET /api/notification-preferences/categories</c>. Lỗi lọt CI vì không ai kiểm điều kiện này.
    /// </summary>
    [Fact]
    public void NotificationTypeEnum_HasNoDuplicateValues()
    {
        var duplicates = Enum.GetNames<NotificationTypeEnum>()
            .GroupBy(name => (int)Enum.Parse<NotificationTypeEnum>(name))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} = {string.Join(" / ", group)}")
            .ToList();

        duplicates.Should().BeEmpty(
            "hai tên trùng giá trị làm NotificationCategoryMap không khai báo được nhóm riêng cho từng type");
    }

    [Fact]
    public void EveryCategory_HasAtLeastOneType()
    {
        var used = NotificationCategoryMap.All.Values.Distinct().ToList();

        used.Should().BeEquivalentTo(Enum.GetValues<NotificationCategoryEnum>(),
            "nhóm không có type nào là nhóm thừa — chỉ làm rối màn hình cài đặt");
    }

    /// <summary>
    /// Leo thang là hệ quả của rủi ro vỡ cam kết. Người dùng tắt "cập nhật ticket" vẫn phải
    /// nhận được nó, nên nó thuộc nhóm SLA chứ không phải Ticket.
    /// </summary>
    [Fact]
    public void TicketEscalated_BelongsToSlaCategory()
    {
        NotificationCategoryMap.Resolve(NotificationTypeEnum.TicketEscalated)
            .Should().Be(NotificationCategoryEnum.Sla);
    }

    [Theory]
    [InlineData(NotificationTypeEnum.TicketCreated, NotificationCategoryEnum.Ticket)]
    [InlineData(NotificationTypeEnum.SlaBreached, NotificationCategoryEnum.Sla)]
    [InlineData(NotificationTypeEnum.BatteryAnomalyWarning, NotificationCategoryEnum.Battery)]
    [InlineData(NotificationTypeEnum.EnvironmentalIncidentDetected, NotificationCategoryEnum.Environmental)]
    [InlineData(NotificationTypeEnum.ChatMentioned, NotificationCategoryEnum.Chat)]
    // 03/08/2026 — thay AdminInvite bằng AccountActivated: AdminInvite đã bị gỡ khỏi
    // NotificationTypeEnum vì thư mời đi thẳng AuthService → EmailService, không consumer nào ở
    // đây ghi nó. Vẫn cần một đại diện nhóm Account nên dùng loại có producer thật.
    [InlineData(NotificationTypeEnum.AccountActivated, NotificationCategoryEnum.Account)]
    public void Resolve_ReturnsExpectedCategory(NotificationTypeEnum type, NotificationCategoryEnum expected)
    {
        NotificationCategoryMap.Resolve(type).Should().Be(expected);
    }

    /// <summary>Type lạ (dữ liệu cũ, giá trị ngoài enum) không được ném lỗi làm chết đường gửi.</summary>
    [Fact]
    public void Resolve_UnknownType_FallsBackToAccount()
    {
        NotificationCategoryMap.Resolve((NotificationTypeEnum)9999)
            .Should().Be(NotificationCategoryEnum.Account);
    }
}

/// <summary>Sprint 6.3 NOTI3-04 (#704) — API đọc/ghi ma trận.</summary>
public class CategoryPreferenceHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");

    private static Mock<INotificationUnitOfWork> Build(
        NotificationPreference? channelPref = null,
        NotificationCategoryPreference[]? categoryPrefs = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            categoryPreferenceSeed: categoryPrefs ?? []);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        var data = channelPref is null ? Array.Empty<NotificationPreference>() : [channelPref];
        prefRepo.Setup(r => r.GetAllAsync()).Returns(data.AsQueryable().BuildMock());
        prefRepo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(data.AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        return uow;
    }

    [Fact]
    public async Task GetMatrix_ReturnsAllSixCategories()
    {
        var handler = new GetNotificationPreferenceMatrixQueryHandler(Build().Object);

        var resp = await handler.Handle(
            new GetNotificationPreferenceMatrixQuery { UserId = UserId }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Categories.Should().HaveCount(6);
        resp.Data.Categories.Select(c => c.Category)
            .Should().BeEquivalentTo(Enum.GetValues<NotificationCategoryEnum>());
    }

    /// <summary>Chưa tuỳ chỉnh thì FE phải thấy trạng thái "kế thừa", không phải "đã đặt".</summary>
    [Fact]
    public async Task GetMatrix_UncustomizedCategories_InheritChannelDefaults()
    {
        var channelPref = new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            PushEnabled = false,
            EmailEnabled = true,
            SmsEnabled = true,
            InAppEnabled = true,
        };

        var handler = new GetNotificationPreferenceMatrixQueryHandler(Build(channelPref).Object);

        var resp = await handler.Handle(
            new GetNotificationPreferenceMatrixQuery { UserId = UserId }, CancellationToken.None);

        resp.Data!.Categories.Should().OnlyContain(c => !c.IsCustomized);
        resp.Data.Categories.Should().OnlyContain(c => c.PushEnabled == false && c.SmsEnabled == true);
    }

    [Fact]
    public async Task GetMatrix_CustomizedCategory_IsMarkedAndReturnsSavedValues()
    {
        var saved = new NotificationCategoryPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Category = NotificationCategoryEnum.Chat,
            PushEnabled = false,
            EmailEnabled = false,
            SmsEnabled = false,
            InAppEnabled = true,
        };

        var handler = new GetNotificationPreferenceMatrixQueryHandler(Build(categoryPrefs: [saved]).Object);

        var resp = await handler.Handle(
            new GetNotificationPreferenceMatrixQuery { UserId = UserId }, CancellationToken.None);

        var chat = resp.Data!.Categories.Single(c => c.Category == NotificationCategoryEnum.Chat);
        chat.IsCustomized.Should().BeTrue();
        chat.PushEnabled.Should().BeFalse();
        chat.InAppEnabled.Should().BeTrue();

        resp.Data.Categories.Where(c => c.Category != NotificationCategoryEnum.Chat)
            .Should().OnlyContain(c => !c.IsCustomized);
    }

    [Fact]
    public async Task Update_CreatesRowForNewCategory_AndInvalidatesCache()
    {
        var uow = Build();
        var cache = new Mock<ICacheService>();
        var mediator = new Mock<MediatR.IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetNotificationPreferenceMatrixQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationPreferenceMatrixResponse { IsSuccess = true });

        var handler = new UpdateNotificationCategoryPreferenceCommandHandler(uow.Object, cache.Object, mediator.Object);

        await handler.Handle(new UpdateNotificationCategoryPreferenceCommand
        {
            UserId = UserId,
            Items = [new CategoryPreferenceItem { Category = NotificationCategoryEnum.Chat, InAppEnabled = true }],
        }, CancellationToken.None);

        uow.Object.NotificationCategoryPreferences.GetAllAsync().ToList()
            .Should().ContainSingle(p => p.Category == NotificationCategoryEnum.Chat);

        // Không xoá cache thì dispatcher còn dùng bản cũ tới 5 phút, user tưởng thiết lập không ăn.
        cache.Verify(c => c.RemoveAsync(
            $"notif_cat_pref:{UserId}:{(int)NotificationCategoryEnum.Chat}", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Ghi đè toàn bộ sẽ khiến hai tab mở song song xoá thiết lập của nhau.</summary>
    [Fact]
    public async Task Update_OnlyTouchesRequestedCategories()
    {
        var chat = new NotificationCategoryPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Category = NotificationCategoryEnum.Chat,
            PushEnabled = true,
            EmailEnabled = true,
        };
        var sla = new NotificationCategoryPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Category = NotificationCategoryEnum.Sla,
            PushEnabled = true,
            EmailEnabled = true,
        };

        var uow = Build(categoryPrefs: [chat, sla]);
        var mediator = new Mock<MediatR.IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetNotificationPreferenceMatrixQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NotificationPreferenceMatrixResponse { IsSuccess = true });

        var handler = new UpdateNotificationCategoryPreferenceCommandHandler(
            uow.Object, new Mock<ICacheService>().Object, mediator.Object);

        await handler.Handle(new UpdateNotificationCategoryPreferenceCommand
        {
            UserId = UserId,
            Items = [new CategoryPreferenceItem { Category = NotificationCategoryEnum.Chat }],
        }, CancellationToken.None);

        chat.PushEnabled.Should().BeFalse("dòng Chat được vá theo request");
        sla.PushEnabled.Should().BeTrue("dòng SLA không nằm trong request nên giữ nguyên");
        sla.EmailEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RejectsDuplicateCategory()
    {
        var command = new UpdateNotificationCategoryPreferenceCommand
        {
            UserId = UserId,
            Items =
            [
                new CategoryPreferenceItem { Category = NotificationCategoryEnum.Chat },
                new CategoryPreferenceItem { Category = NotificationCategoryEnum.Chat },
            ],
        };

        var response = await command.ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.ListErrors.Should().Contain(e => e.Detail!.Contains("nhiều lần"));
    }

    [Fact]
    public async Task Validate_RejectsEmptyItems()
    {
        var response = await new UpdateNotificationCategoryPreferenceCommand { UserId = UserId }.ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.ListErrors.Should().Contain(e => e.Field == "Items");
    }

    [Fact]
    public async Task Validate_RejectsUnknownCategory()
    {
        var command = new UpdateNotificationCategoryPreferenceCommand
        {
            UserId = UserId,
            Items = [new CategoryPreferenceItem { Category = (NotificationCategoryEnum)99 }],
        };

        var response = await command.ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.ListErrors.Should().Contain(e => e.Field == "Items.Category");
    }
}

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — dispatcher tôn trọng ma trận.
/// Đây là phần quyết định: API có đẹp mấy mà dispatcher không đọc thì tính năng chỉ là trang trí.
/// </summary>
public class DispatcherRespectsCategoryPreferenceTests
{
    private static readonly Guid UserId = Guid.Parse("eeeeeeee-1111-2222-3333-444444444444");

    private static NotificationEntity Pending(NotificationChannelEnum channel, NotificationTypeEnum type) => new()
    {
        Id = Guid.NewGuid(),
        UserId = UserId,
        Type = type,
        Channel = channel,
        Status = NotificationStatusEnum.Pending,
        Title = "T",
        Body = "B",
        EntityType = "Ticket",
    };

    private static (NotificationDispatcher sut, Mock<INotificationChannel> channel) Build(
        NotificationEntity notification,
        NotificationCategoryPreference[]? categoryPrefs = null)
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            deviceTokenSeed: [new DeviceTokenEntity { Id = Guid.NewGuid(), UserId = UserId, Token = "ExponentPushToken[x]", IsActive = true }],
            notificationSeed: [notification],
            accountSeed: [new AccountReadModel
            {
                Id = UserId, Email = "u@x.com", FullName = "U", PhoneNumber = "0901234567",
                Role = "Customer", IsActive = true,
            }],
            categoryPreferenceSeed: categoryPrefs ?? []);

        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync())
                .Returns(Array.Empty<NotificationPreference>().AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(notification.Channel);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(true));

        var cache = new Mock<ICacheService>();

        var sut = new NotificationDispatcher(
            uow.Object,
            cache.Object,
            [channel.Object],
            new Mock<ITemplateRenderer>().Object,
            new NoopAuditWriter(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance);

        return (sut, channel);
    }

    /// <summary>Tắt push cho nhóm Chat thì chat không push, nhưng SLA vẫn push — mục đích của cả task.</summary>
    [Fact]
    public async Task CategoryDisabled_BlocksThatCategoryOnly()
    {
        var chatOff = new NotificationCategoryPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Category = NotificationCategoryEnum.Chat,
            PushEnabled = false,
            EmailEnabled = false,
            SmsEnabled = false,
            InAppEnabled = true,
        };

        var chatPush = Pending(NotificationChannelEnum.Push, NotificationTypeEnum.ChatCreated);
        var (sutChat, chatChannel) = Build(chatPush, [chatOff]);

        var chatOutcome = await sutChat.DispatchPendingAsync(chatPush);

        chatOutcome.Should().Be(DispatchOutcome.Failed);
        chatPush.FailureReason.Should().Contain("Chat");
        chatChannel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        var slaPush = Pending(NotificationChannelEnum.Push, NotificationTypeEnum.SlaWarning);
        var (sutSla, slaChannel) = Build(slaPush, [chatOff]);

        (await sutSla.DispatchPendingAsync(slaPush)).Should().Be(DispatchOutcome.Sent);
        slaChannel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Nhóm bật nhưng công tắc kênh tắt ⇒ vẫn không gửi (và logic).</summary>
    [Fact]
    public async Task GlobalChannelSwitch_OverridesCategoryPreference()
    {
        var chatOn = new NotificationCategoryPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Category = NotificationCategoryEnum.Chat,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = true,
            InAppEnabled = true,
        };

        var n = Pending(NotificationChannelEnum.Sms, NotificationTypeEnum.ChatCreated);

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: [n],
            accountSeed: [new AccountReadModel
            {
                Id = UserId, Email = "u@x.com", FullName = "U", PhoneNumber = "0901234567",
                Role = "Customer", IsActive = true,
            }],
            categoryPreferenceSeed: [chatOn]);

        // SmsEnabled = false ở cấp kênh (mặc định) — công tắc lớn phải thắng.
        var globalPref = new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            PushEnabled = true,
            EmailEnabled = true,
            SmsEnabled = false,
            InAppEnabled = true,
        };
        var prefRepo = new Mock<IGenericRepository<NotificationPreference>>();
        prefRepo.Setup(r => r.GetAllAsync()).Returns(new[] { globalPref }.AsQueryable().BuildMock());
        uow.SetupGet(u => u.NotificationPreferences).Returns(prefRepo.Object);

        var channel = new Mock<INotificationChannel>();
        channel.SetupGet(c => c.ChannelType).Returns(NotificationChannelEnum.Sms);
        channel.Setup(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new ChannelResult(true));

        var sut = new NotificationDispatcher(
            uow.Object, new Mock<ICacheService>().Object, [channel.Object],
            new Mock<ITemplateRenderer>().Object, new NoopAuditWriter(),
            Options.Create(new NotificationDispatchOptions()),
            NullLogger<NotificationDispatcher>.Instance);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Failed);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Chưa tuỳ chỉnh nhóm nào ⇒ hành vi y hệt trước sprint này (không cần backfill dữ liệu).</summary>
    [Fact]
    public async Task NoCategoryPreference_BehavesAsBefore()
    {
        var n = Pending(NotificationChannelEnum.Push, NotificationTypeEnum.ChatCreated);
        var (sut, channel) = Build(n);

        (await sut.DispatchPendingAsync(n)).Should().Be(DispatchOutcome.Sent);
        channel.Verify(c => c.SendAsync(It.IsAny<SendRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
