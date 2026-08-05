using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Consumers;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// GH-111 — NotificationEnvironmentalIncidentDetectedConsumer:
/// Push + Email + SMS (§3.4), BypassQuietHours=true, push format §15.2.
/// </summary>
public class EnvironmentalIncidentDetectedConsumerTests
{
    private static async Task<ITestHarness> StartHarness(IMediator mediator, ICacheService? cache = null)
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<NotificationEnvironmentalIncidentDetectedConsumer>();
                // Timeout tường minh — mặc định inactivity 1s của MassTransit v8 làm test đỏ
                // thất thường khi cả solution chạy song song. Xem ConsumerTestHarness.InactivityTimeout.
                x.SetTestTimeouts(Helpers.ConsumerTestHarness.TestTimeout, Helpers.ConsumerTestHarness.InactivityTimeout);
            })
            .AddSingleton(mediator)
            .AddSingleton<ITemplateRenderer, HandlebarsTemplateRenderer>()
            .AddSingleton(Resolver())
            .AddSingleton(cache ?? ConsumerTestHarness.ProceedCache())
            .AddSingleton(NullLogger<NotificationEnvironmentalIncidentDetectedConsumer>.Instance)
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    /// <summary>Resolver mock: trả về đúng 1 recipient cho mọi role (3 channel × 1 = 3 notification).</summary>
    private static IRecipientResolver Resolver()
    {
        var r = new Mock<IRecipientResolver>();
        r.Setup(x => x.GetActiveByRoleAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()))
            .ReturnsAsync(new[] { Guid.NewGuid() });
        return r.Object;
    }

    private static EnvironmentalIncidentDetectedEvent MakeEvent() => new(
        IncidentId: Guid.NewGuid(),
        SiteId: Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        SiteName: "Site Hanoi",
        IncidentType: 1,
        Severity: 3,
        DetectedAt: new DateTime(2026, 6, 21, 14, 30, 0, DateTimeKind.Utc),
        AlertId: Guid.NewGuid(),
        Description: "Smoke detected near battery rack"
    );

    [Fact]
    public async Task Consume_ShouldDispatch_PushEmailSms_Channels()
    {
        var mediator = new Mock<IMediator>();
        var calls = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                calls.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<EnvironmentalIncidentDetectedEvent>()).Should().BeTrue();

        // Sprint 6.3 NOTI3-01 (#701) — thêm row InApp để notification hiện trong feed (feed lọc Channel=InApp).
        calls.Should().HaveCount(4);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.InApp);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Push);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Email);
        calls.Should().Contain(c => c.Channel == NotificationChannelEnum.Sms);
        calls.Should().AllSatisfy(c =>
            c.Type.Should().Be(NotificationTypeEnum.EnvironmentalIncidentDetected));

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_PushTitle_ShouldMatchSection15_2Format()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<EnvironmentalIncidentDetectedEvent>()).Should().BeTrue();

        // §15.2: Title = "🚨 Sự cố môi trường: {IncidentType} tại {SiteName}"
        captured.Should().AllSatisfy(c =>
        {
            c.Title.Should().Contain("Site Hanoi");
            c.Title.Should().StartWith("🚨");
        });

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_PushBody_ShouldContain_SeverityAndTime()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<EnvironmentalIncidentDetectedEvent>()).Should().BeTrue();

        // Push/SMS body should contain severity and time; Email body is HTML
        var pushCmd = captured.First(c => c.Channel == NotificationChannelEnum.Push);
        pushCmd.Body.Should().Contain("Severity");
        pushCmd.Body.Should().Contain("14:30");
        pushCmd.Body.Should().Contain("Xử lý ngay");

        var emailCmd = captured.First(c => c.Channel == NotificationChannelEnum.Email);
        emailCmd.Body.Should().Contain("<html");

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ShouldSetBypassQuietHours_True()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(MakeEvent());
        (await harness.Consumed.Any<EnvironmentalIncidentDetectedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c => c.BypassQuietHours.Should().BeTrue());

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_ShouldSetEntityType_EnvironmentalIncident()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var evt = MakeEvent();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<EnvironmentalIncidentDetectedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.EntityType.Should().Be("EnvironmentalIncident");
            c.EntityId.Should().Be(evt.IncidentId);
        });

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_PayloadShouldContain_IncidentIdAndSiteId()
    {
        var mediator = new Mock<IMediator>();
        var captured = new List<CreateNotificationCommand>();
        mediator.Setup(m => m.Send(It.IsAny<CreateNotificationCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<NotificationActionResponse>, CancellationToken>((c, _) =>
                captured.Add((CreateNotificationCommand)c))
            .ReturnsAsync(new NotificationActionResponse { IsSuccess = true });

        var evt = MakeEvent();
        var harness = await StartHarness(mediator.Object);
        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<EnvironmentalIncidentDetectedEvent>()).Should().BeTrue();

        captured.Should().AllSatisfy(c =>
        {
            c.PayloadJson.Should().Contain(evt.IncidentId.ToString());
            c.PayloadJson.Should().Contain(evt.SiteId.ToString());
            c.PayloadJson.Should().Contain("IncidentDetail");
        });

        await harness.Stop();
    }
}
