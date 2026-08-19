using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Scenarios;

public class SlaBreachScenarioTests : IClassFixture<TicketApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeTimeProvider _timeProvider;
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriterMock;

    public SlaBreachScenarioTests(TicketApiFactory factory)
    {
        _timeProvider = new FakeTimeProvider();
        _timeProvider.SetUtcNow(Utc(2026, 8, 19, 7, 48)); // Wednesday 14:48 local
        _outboxWriterMock = new Mock<IIntegrationEventOutboxWriter>();
        _outboxWriterMock
            .Setup(x => x.WriteAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _outboxWriterMock
            .Setup(x => x.WriteAsync(It.IsAny<SlaBreachedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.RemoveAll<IIntegrationEventOutboxWriter>();
                services.AddSingleton<TimeProvider>(_timeProvider);
                services.AddScoped<IIntegrationEventOutboxWriter>(_ => _outboxWriterMock.Object);
                services.AddScoped<SlaTimerBackgroundService>();
            });
        });
    }

    [Fact]
    public async Task P2Timer_ShouldWarnLateDay_RemindOnceNextSession_ThenBreachAtBusinessDeadline()
    {
        _ = _factory.CreateClient();
        var startedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var dueAt = new SlaCalculator().CalculateDueDate(startedAt, TicketPriorityEnum.P2High);
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var ticket = new Ticket
            {
                Id = ticketId,
                Code = "TKT-BUSINESS-HOURS",
                Title = "Business-hours SLA",
                Description = "Integration scenario",
                Category = TicketCategoryEnum.Other,
                CustomerId = Guid.NewGuid(),
                BatteryAssetId = Guid.NewGuid(),
                Status = TicketStatusEnum.InProgress,
                Priority = TicketPriorityEnum.P2High,
                Origin = TicketOriginEnum.ManualByCustomer,
                CreatedAt = startedAt,
                CreatedBy = Guid.NewGuid()
            };
            db.Tickets.Add(ticket);
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Priority = TicketPriorityEnum.P2High,
                Status = SlaTimerStatusEnum.Running,
                StartedAt = startedAt,
                DueAt = dueAt,
                OriginalDueAt = dueAt
            });
            await db.SaveChangesAsync();
        }

        // Exactly 80% consumed on Friday 16:00 local: first warning is late-day.
        _timeProvider.SetUtcNow(Utc(2026, 8, 21, 9));
        await RunWorkerOnce();
        (await LoadTimer(timerId)).WarningSentAt.Should().Be(Utc(2026, 8, 21, 9));
        _outboxWriterMock.Verify(
            x => x.WriteAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Night/weekend passage neither breaches nor emits the reminder early.
        _timeProvider.SetUtcNow(Utc(2026, 8, 21, 23, 59)); // Saturday 06:59 local
        await RunWorkerOnce();
        var beforeOpening = await LoadTimer(timerId);
        beforeOpening.Status.Should().Be(SlaTimerStatusEnum.Running);
        beforeOpening.WarningSentAt.Should().Be(Utc(2026, 8, 21, 9));
        _outboxWriterMock.Verify(
            x => x.WriteAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Reminder is emitted once when the next eligible SLA session starts.
        _timeProvider.SetUtcNow(Utc(2026, 8, 22, 0)); // Saturday 07:00 local
        await RunWorkerOnce();
        (await LoadTimer(timerId)).WarningSentAt.Should().Be(Utc(2026, 8, 22, 0));
        _outboxWriterMock.Verify(
            x => x.WriteAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _timeProvider.SetUtcNow(Utc(2026, 8, 22, 0, 1));
        await RunWorkerOnce();
        _outboxWriterMock.Verify(
            x => x.WriteAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        _timeProvider.SetUtcNow(dueAt);
        await RunWorkerOnce();
        var breached = await LoadTimer(timerId);
        breached.Status.Should().Be(SlaTimerStatusEnum.Breached);
        breached.BreachAt.Should().Be(dueAt);
        _outboxWriterMock.Verify(
            x => x.WriteAsync(It.IsAny<SlaBreachedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task RunWorkerOnce()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<SlaTimerBackgroundService>();
        await worker.CheckSlaViolations(CancellationToken.None);
    }

    private async Task<SlaTimer> LoadTimer(Guid timerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        return (await db.SlaTimers.FindAsync(timerId))!;
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
