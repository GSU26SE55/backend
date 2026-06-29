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

/// <summary>Sprint Chat Wave 4 (#544) — ChatMentionConsumer push + email cho user được mention.</summary>
public class ChatMentionConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, Mock<IInboxStore>? inboxStore = null)
    {
        inboxStore ??= MakeInboxStore();

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<ChatMentionConsumer>())
            .AddSingleton(mediator)
            .AddSingleton(inboxStore.Object)
            .AddSingleton(NullLogger<ChatMentionConsumer>.Instance)
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

    private static ChatMentionedEvent MakeEvent(Guid mentionedUserId) => new(
        ChatId: Guid.NewGuid(),
        TicketId: Guid.NewGuid(),
        MentionedUserId: mentionedUserId,
        MentionedUserRole: 3,
        MentionedDisplayName: "Nguyễn Văn A",
        ActorUserId: Guid.NewGuid(),
        IsGroupMention: false);

    [Fact]
    public async Task Consume_SendsBothPushAndEmail_ToMentionedUser()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var mentionedUserId = Guid.NewGuid();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent(mentionedUserId));
        (await harness.Consumed.Any<ChatMentionedEvent>()).Should().BeTrue();

        captured.Should().HaveCount(2);
        captured.Should().AllSatisfy(c =>
        {
            c.UserId.Should().Be(mentionedUserId);
            c.Type.Should().Be(NotificationTypeEnum.ChatMentioned);
        });
        captured.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        captured.Should().Contain(c => c.Channel == NotificationChannelEnum.Email);

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

        var evt = MakeEvent(Guid.NewGuid());
        var harness = await StartHarness(mediator.Object, inboxStore);
        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<ChatMentionedEvent>()).Should().BeTrue();

        // Mỗi lần process thành công gửi 2 notification (Push+Email) — duplicate bị skip nên vẫn đúng 2.
        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        await harness.Stop();
    }
}
