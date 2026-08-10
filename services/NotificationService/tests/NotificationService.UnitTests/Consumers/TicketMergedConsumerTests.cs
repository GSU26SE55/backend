using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace NotificationService.UnitTests.Consumers;

public class TicketMergedConsumerTests
{
    [Fact]
    public async Task Consume_CreatesInAppNotificationForSourceCustomer()
    {
        var mediator = new Mock<IMediator>();
        CreateNotificationCommand? captured = null;
        mediator.Setup(x => x.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((command, _) => captured = (CreateNotificationCommand)command)
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        var evt = new TicketMergedEvent(Guid.NewGuid(), "TKT-001", Guid.NewGuid(), Guid.NewGuid(), "TKT-002", Guid.NewGuid());

        await harness.Bus.Publish(evt);

        (await harness.Consumed.Any<TicketMergedEvent>()).Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(evt.SourceCustomerId);
        captured.Type.Should().Be(NotificationTypeEnum.TicketMerged);
        captured.Channel.Should().Be(NotificationChannelEnum.InApp);
        captured.PayloadJson.Should().Contain(evt.SourceTicketId.ToString()).And.Contain(evt.MasterTicketId.ToString());
        await harness.Stop();
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_CreatesOnlyOneNotification()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });
        var inbox = new Mock<IInboxStore>();
        var inboxClaims = 0;
        inbox.Setup(x => x.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref inboxClaims) == 1
                ? new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token")
                : InboxClaim.Completed);
        var harness = await StartHarness(mediator.Object, inbox);
        var evt = new TicketMergedEvent(Guid.NewGuid(), "TKT-001", Guid.NewGuid(), Guid.NewGuid(), "TKT-002", Guid.NewGuid());

        await harness.Bus.Publish(evt);
        await harness.Bus.Publish(evt);

        (await harness.Consumed.SelectAsync<TicketMergedEvent>().Take(2).Count()).Should().Be(2);
        mediator.Verify(x => x.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        await harness.Stop();
    }

    private static async Task<ITestHarness> StartHarness(IMediator mediator, Mock<IInboxStore>? inbox = null)
    {
        if (inbox is null)
        {
            inbox = new Mock<IInboxStore>();
            inbox.Setup(x => x.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));
        }
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
                x.AddConsumer<TicketMergedConsumer>();
            })
            .AddSingleton(mediator)
            .AddSingleton(inbox.Object)
            .AddSingleton(NullLogger<TicketMergedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }
}
