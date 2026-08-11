using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// GH-604 — NotificationEnvironmentalIncidentResolvedConsumer: resolve Manager+Admin → InApp (clear banner).
/// Recipient rỗng → skip.
/// </summary>
public class EnvironmentalIncidentResolvedConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, IReadOnlyList<Guid>? recipients = null, ICacheService? cache = null)
    {
        var resolver = new Mock<IRecipientResolver>();
        resolver.Setup(x => x.GetActiveByRoleAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
            .ReturnsAsync(recipients ?? new[] { Guid.NewGuid() });

        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<NotificationEnvironmentalIncidentResolvedConsumer>();
                // Timeout tường minh — mặc định inactivity 1s của MassTransit v8 làm test đỏ
                // thất thường khi cả solution chạy song song. Xem ConsumerTestHarness.InactivityTimeout.
                x.SetTestTimeouts(Helpers.ConsumerTestHarness.TestTimeout, Helpers.ConsumerTestHarness.InactivityTimeout);
            })
            .AddSingleton(mediator)
            .AddSingleton(resolver.Object)
            .AddSingleton(cache ?? ConsumerTestHarness.ProceedCache())
            .AddSingleton(NullLogger<NotificationEnvironmentalIncidentResolvedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    private static EnvironmentalIncidentResolvedEvent MakeEvent(bool falseAlarm = false) => new(
        IncidentId: Guid.NewGuid(),
        SiteId: Guid.NewGuid(),
        ResolvedAt: new DateTime(2026, 6, 22, 12, 0, 0, DateTimeKind.Utc),
        ResolvedByUserId: Guid.NewGuid(),
        WasFalseAlarm: falseAlarm,
        ResolutionNote: "Checked, everything is safe");

    private static Mock<IMediator> CaptureMediator(List<CreateNotificationCommand> sink)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) => sink.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });
        return mediator;
    }

    [Fact]
    public async Task Consume_ShouldDispatch_InAppOnly()
    {
        var calls = new List<CreateNotificationCommand>();
        var harness = await StartHarness(CaptureMediator(calls).Object);

        var evt = MakeEvent();
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<EnvironmentalIncidentResolvedEvent>()).Should().BeTrue();

        calls.Should().ContainSingle();
        calls[0].Channel.Should().Be(NotificationChannelEnum.InApp);
        calls[0].Type.Should().Be(NotificationTypeEnum.EnvironmentalIncidentResolved);
        calls[0].EntityId.Should().Be(evt.IncidentId);

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_FalseAlarm_TitleReflectsLabel()
    {
        var calls = new List<CreateNotificationCommand>();
        var harness = await StartHarness(CaptureMediator(calls).Object);

        var evt = MakeEvent(falseAlarm: true);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<EnvironmentalIncidentResolvedEvent>()).Should().BeTrue();

        calls.Should().ContainSingle();
        // Tiêu đề nói bằng tiếng Việt cho người dùng, không dùng nhãn kỹ thuật "false-alarm".
        calls[0].Title.Should().Contain("false alarm");
        // Guid không được lọt vào câu chữ hiển thị — định danh nằm ở payload.
        calls[0].Title.Should().NotContain(evt.IncidentId.ToString());
        calls[0].PayloadJson.Should().Contain(evt.IncidentId.ToString());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_NoRecipientResolved_ShouldSkip()
    {
        var calls = new List<CreateNotificationCommand>();
        var harness = await StartHarness(CaptureMediator(calls).Object, Array.Empty<Guid>());

        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<EnvironmentalIncidentResolvedEvent>()).Should().BeTrue();

        calls.Should().BeEmpty();
        await harness.Stop();
    }
}
