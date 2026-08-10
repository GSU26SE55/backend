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
/// Sprint Chat Wave 4 (#544) — ParticipantChangeConsumer welcome/farewell notify
/// (gộp 2 event type ParticipantAddedEvent + ParticipantRemovedEvent trong 1 consumer class).
/// </summary>
public class ParticipantChangeConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, Mock<IInboxStore>? inboxStore = null)
    {
        inboxStore ??= MakeInboxStore();

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<ParticipantChangeConsumer>();
                // Timeout tường minh — mặc định inactivity 1s của MassTransit v8 làm test đỏ
                // thất thường khi cả solution chạy song song. Xem ConsumerTestHarness.InactivityTimeout.
                x.SetTestTimeouts(Helpers.ConsumerTestHarness.TestTimeout, Helpers.ConsumerTestHarness.InactivityTimeout);
            })
            .AddSingleton(mediator)
            .AddSingleton(inboxStore.Object)
            .AddSingleton(NullLogger<ParticipantChangeConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    private static Mock<IInboxStore> MakeInboxStore()
    {
        var store = new Mock<IInboxStore>();
        store.Setup(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));
        return store;
    }

    [Fact]
    public async Task Consume_ParticipantAdded_SendsWelcomeNotification()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var participantUserId = Guid.NewGuid();
        var evt = new ParticipantAddedEvent(Guid.NewGuid(), participantUserId, 3, 3, Guid.NewGuid());

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ParticipantAddedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-01 (#701) — thêm row InApp để notification hiện trong feed (feed lọc Channel=InApp).
        captured.Should().HaveCount(2);
        captured.Select(c => c.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push
        });
        captured[0].UserId.Should().Be(participantUserId);
        captured[0].Type.Should().Be(NotificationTypeEnum.ParticipantAdded);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ParticipantRemoved_SendsFarewellNotification()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var participantUserId = Guid.NewGuid();
        var evt = new ParticipantRemovedEvent(Guid.NewGuid(), participantUserId, Guid.NewGuid(), "Hết nhu cầu theo dõi");

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ParticipantRemovedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-01 (#701) — thêm row InApp để notification hiện trong feed (feed lọc Channel=InApp).
        captured.Should().HaveCount(2);
        captured.Select(c => c.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push
        });
        captured[0].UserId.Should().Be(participantUserId);
        captured[0].Type.Should().Be(NotificationTypeEnum.ParticipantRemoved);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ParticipantRoleChanged_SendsNotification()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var participantUserId = Guid.NewGuid();
        var evt = new ParticipantRoleChangedEvent(Guid.NewGuid(), participantUserId, 3, 4, Guid.NewGuid());

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ParticipantRoleChangedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-01 (#701) — thêm row InApp để notification hiện trong feed (feed lọc Channel=InApp).
        captured.Should().HaveCount(2);
        captured.Select(c => c.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push
        });
        captured[0].UserId.Should().Be(participantUserId);
        captured[0].Type.Should().Be(NotificationTypeEnum.ParticipantRoleChanged);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_DuplicateAddedEvent_OnlyProcessedOnce()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var inboxStore = new Mock<IInboxStore>();
        inboxStore.SetupSequence(s => s.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"))
            .ReturnsAsync(InboxClaim.Completed);

        var evt = new ParticipantAddedEvent(Guid.NewGuid(), Guid.NewGuid(), 3, 3, Guid.NewGuid());
        var harness = await StartHarness(mediator.Object, inboxStore);
        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ParticipantAddedEvent>()).Should().BeTrue();

        // Inbox dedup vẫn chặn xử lý lặp — 2 command là InApp + Push của MỘT lần xử lý.
        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        await harness.Stop();
    }
}
