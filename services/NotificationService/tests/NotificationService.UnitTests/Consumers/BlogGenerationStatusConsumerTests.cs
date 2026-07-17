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
using SharedContracts.Events.Blog;
using SharedInfrastructure.Idempotency;

namespace NotificationService.UnitTests.Consumers;

public class BlogGenerationStatusConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, Mock<IInboxStore>? inboxStore = null)
    {
        inboxStore ??= MakeInboxStore();

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<BlogGenerationStatusConsumer>())
            .AddSingleton(mediator)
            .AddSingleton(inboxStore.Object)
            .AddSingleton(NullLogger<BlogGenerationStatusConsumer>.Instance)
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

    [Fact]
    public async Task Consume_StatusDraft_SendsBlogGenerationCompletedNotification()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var blogPostId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = new BlogGenerationStatusChangedEvent(blogPostId, userId, "Draft", "My AI Blog", null);

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BlogGenerationStatusChangedEvent>()).Should().BeTrue();

        captured.Should().ContainSingle();
        captured[0].UserId.Should().Be(userId);
        captured[0].Type.Should().Be(NotificationTypeEnum.BlogGenerationCompleted);
        captured[0].Channel.Should().Be(NotificationChannelEnum.InApp);
        captured[0].EntityType.Should().Be("BlogPost");
        captured[0].EntityId.Should().Be(blogPostId);
        captured[0].Body.Should().Contain("My AI Blog");

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_StatusGenerationFailed_SendsBlogGenerationFailedNotification()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var blogPostId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = new BlogGenerationStatusChangedEvent(blogPostId, userId, "GenerationFailed", "My Blog", "DeepSeek timeout");

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BlogGenerationStatusChangedEvent>()).Should().BeTrue();

        captured.Should().ContainSingle();
        captured[0].UserId.Should().Be(userId);
        captured[0].Type.Should().Be(NotificationTypeEnum.BlogGenerationFailed);
        captured[0].Body.Should().Contain("thất bại");

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

        var evt = new BlogGenerationStatusChangedEvent(Guid.NewGuid(), Guid.NewGuid(), "Draft", "Blog", null);

        var harness = await StartHarness(mediator.Object, inboxStore);
        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<BlogGenerationStatusChangedEvent>()).Should().BeTrue();

        mediator.Verify(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Once);

        await harness.Stop();
    }
}
