using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// Sprint Chat Wave 4 (#544) — ChatCreatedConsumer notify Customer khi Staff post public,
/// notify Staff khi Customer post; skip nếu IsInternal.
/// </summary>
public class ChatCreatedConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, Mock<IInboxStore>? inboxStore = null)
    {
        inboxStore ??= MakeInboxStore();

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<ChatCreatedConsumer>())
            .AddSingleton(mediator)
            .AddSingleton(inboxStore.Object)
            .AddSingleton(NullLogger<ChatCreatedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    private static Mock<IInboxStore> MakeInboxStore()
    {
        var store = new Mock<IInboxStore>();
        store.Setup(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        return store;
    }

    private const int StaffRole = 3;
    private const int CustomerRole = 4;

    private static ChatCreatedEvent MakeEvent(int authorRole, bool isInternal, Guid customerId, Guid? staffId) => new(
        ChatId: Guid.NewGuid(),
        TicketId: Guid.NewGuid(),
        AuthorUserId: Guid.NewGuid(),
        AuthorRole: authorRole,
        AuthorDisplayName: "Tác giả",
        Body: "Nội dung chat test",
        IsInternal: isInternal,
        AttachmentFileIds: new List<Guid>(),
        CustomerId: customerId,
        AssignedStaffId: staffId);

    [Fact]
    public async Task Consume_StaffPostsPublic_NotifiesCustomer()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var customerId = Guid.NewGuid();
        var evt = MakeEvent(StaffRole, isInternal: false, customerId, Guid.NewGuid());

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatCreatedEvent>()).Should().BeTrue();

        captured.Should().ContainSingle();
        captured[0].UserId.Should().Be(customerId);
        captured[0].Type.Should().Be(NotificationTypeEnum.ChatCreated);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_CustomerPosts_NotifiesAssignedStaff()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var staffId = Guid.NewGuid();
        var evt = MakeEvent(CustomerRole, isInternal: false, Guid.NewGuid(), staffId);

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatCreatedEvent>()).Should().BeTrue();

        captured.Should().ContainSingle();
        captured[0].UserId.Should().Be(staffId);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_IsInternal_SkipsNotification()
    {
        var mediator = new Mock<IMediator>();
        var evt = MakeEvent(StaffRole, isInternal: true, Guid.NewGuid(), Guid.NewGuid());

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatCreatedEvent>()).Should().BeTrue();

        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_DuplicateEvent_OnlyProcessedOnce()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var inboxStore = new Mock<IInboxStore>();
        inboxStore.SetupSequence(s => s.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var evt = MakeEvent(StaffRole, isInternal: false, Guid.NewGuid(), Guid.NewGuid());

        var harness = await StartHarness(mediator.Object, inboxStore);
        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatCreatedEvent>()).Should().BeTrue();

        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
