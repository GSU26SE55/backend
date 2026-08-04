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

/// <summary>Sprint Chat Wave 4 (#544) — ChatReactionConsumer notify author của chat khi có reaction mới.</summary>
public class ChatReactionConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, Mock<IInboxStore>? inboxStore = null)
    {
        inboxStore ??= MakeInboxStore();

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<ChatReactionConsumer>();
                // Timeout tường minh — mặc định inactivity 1s của MassTransit v8 làm test đỏ
                // thất thường khi cả solution chạy song song. Xem ConsumerTestHarness.InactivityTimeout.
                x.SetTestTimeouts(Helpers.ConsumerTestHarness.TestTimeout, Helpers.ConsumerTestHarness.InactivityTimeout);
            })
            .AddSingleton(mediator)
            .AddSingleton(inboxStore.Object)
            .AddSingleton(NullLogger<ChatReactionConsumer>.Instance)
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

    private static ChatReactedEvent MakeEvent(Guid actorUserId, Guid chatAuthorUserId, bool isRemoved) => new(
        ChatId: Guid.NewGuid(),
        TicketId: Guid.NewGuid(),
        ActorUserId: actorUserId,
        ActorRole: 4,
        ReactionType: 1,
        IsRemoved: isRemoved,
        ChatAuthorUserId: chatAuthorUserId);

    [Fact]
    public async Task Consume_ReactionAdded_NotifiesChatAuthor()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var authorId = Guid.NewGuid();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent(Guid.NewGuid(), authorId, isRemoved: false));
        (await harness.Consumed.Any<ChatReactedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-01 (#701) — thêm row InApp để notification hiện trong feed (feed lọc Channel=InApp).
        captured.Should().HaveCount(2);
        captured.Select(c => c.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push
        });
        captured[0].UserId.Should().Be(authorId);
        captured[0].Type.Should().Be(NotificationTypeEnum.ChatReacted);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ReactionRemoved_SkipsNotification()
    {
        var mediator = new Mock<IMediator>();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent(Guid.NewGuid(), Guid.NewGuid(), isRemoved: true));
        (await harness.Consumed.Any<ChatReactedEvent>()).Should().BeTrue();

        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_SelfReaction_SkipsNotification()
    {
        var mediator = new Mock<IMediator>();
        var sameUser = Guid.NewGuid();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent(sameUser, sameUser, isRemoved: false));
        (await harness.Consumed.Any<ChatReactedEvent>()).Should().BeTrue();

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

        var evt = MakeEvent(Guid.NewGuid(), Guid.NewGuid(), isRemoved: false);
        var harness = await StartHarness(mediator.Object, inboxStore);
        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatReactedEvent>()).Should().BeTrue();

        // Inbox dedup vẫn chặn xử lý lặp — 2 command là InApp + Push của MỘT lần xử lý, không phải 2 lần.
        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        await harness.Stop();
    }
}
