using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.Infrastructure.Persistence;

namespace TicketService.UnitTests.Infrastructure.BackgroundJobs;

public class SlaTimerBackgroundServiceTests
{
    private class NullUserService : ICurrentUserService
    {
        public string? UserId => Guid.Empty.ToString();
    }

    private class TestableSlaTimerService : SlaTimerBackgroundService
    {
        public TestableSlaTimerService(ILogger<SlaTimerBackgroundService> logger, IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
            : base(logger, scopeFactory, timeProvider) { }

        public async Task TriggerCheck(CancellationToken ct) => await CheckSlaViolations(ct);
    }

    // Mốc thời gian cố định: 2026-06-03 09:00 UTC = 16:00 Asia/Ho_Chi_Minh.
    private static readonly DateTime FixedNow = new DateTime(2026, 6, 3, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task When_SLA_is_breached_Should_update_timer_status_to_Breached()
    {
        // Arrange
        var ticketId = NewId.NextGuid();
        var slaTimerId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        // Giả lập thời gian hiện tại là FixedNow
        var mockTime = new Mock<TimeProvider>();
        mockTime.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(FixedNow));

        await using var provider = CreateProvider(dbName);

        // Data: Đã bắt đầu từ lâu và DueAt là 11:59:59 (đã quá hạn 1 giây so với FixedNow)
        var dueAt = FixedNow.AddSeconds(-1);
        await SetupData(provider, ticketId, slaTimerId, FixedNow.AddHours(-5), dueAt);

        var service = new TestableSlaTimerService(new Mock<ILogger<SlaTimerBackgroundService>>().Object, provider.GetRequiredService<IServiceScopeFactory>(), mockTime.Object);

        // Act
        await service.TriggerCheck(CancellationToken.None);

        // Assert
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var updatedTimer = await dbContext.SlaTimers.FindAsync(slaTimerId);

        updatedTimer!.Status.Should().Be(SlaTimerStatusEnum.Breached);
        updatedTimer.BreachAt.Should().Be(FixedNow, "Thời điểm vi phạm phải khớp chính xác với đồng hồ hệ thống giả lập");
    }

    [Fact]
    public async Task When_SLA_is_in_warning_zone_Should_update_WarningSentAt()
    {
        // Arrange
        var ticketId = NewId.NextGuid();
        var slaTimerId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var mockTime = new Mock<TimeProvider>();
        mockTime.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(FixedNow));

        await using var provider = CreateProvider(dbName);

        // Data: P1 budget = 1 ngày làm việc (600 phút). 80% là 480 phút.
        // Còn 119 phút làm việc -> đã dùng 80.2% -> Phải Warning.
        var startTime = FixedNow.AddDays(-12);
        var dueAt = new SlaCalculator().AddWorkingMinutes(FixedNow, 119);

        await SetupData(provider, ticketId, slaTimerId, startTime, dueAt);

        var service = new TestableSlaTimerService(new Mock<ILogger<SlaTimerBackgroundService>>().Object, provider.GetRequiredService<IServiceScopeFactory>(), mockTime.Object);

        // Act
        await service.TriggerCheck(CancellationToken.None);

        // Assert
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var updatedTimer = await dbContext.SlaTimers.FindAsync(slaTimerId);

        updatedTimer!.WarningSentAt.Should().Be(FixedNow);
    }

    [Fact]
    public async Task When_PendingTimerIsPaused_Should_NotCountOrAutoResume()
    {
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();
        await using var provider = CreateProvider(Guid.NewGuid().ToString());
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var ticket = new Ticket
            {
                Id = ticketId,
                Code = "T-AUTO",
                CustomerId = Guid.NewGuid(),
                Title = "Test",
                Description = "Test",
                Category = TicketCategoryEnum.Other,
                Status = TicketStatusEnum.Pending,
                PendingContext = PendingContextEnum.Held,
                Origin = TicketOriginEnum.ManualByCustomer
            };
            db.Tickets.Add(ticket);
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Ticket = ticket,
                Priority = TicketPriorityEnum.P3Normal,
                StartedAt = FixedNow.AddDays(-3),
                DueAt = FixedNow.AddHours(2),
                OriginalDueAt = FixedNow.AddHours(2),
                CurrentPauseStartedAt = FixedNow.AddHours(-49),
                Status = SlaTimerStatusEnum.Paused
            });
            db.SlaPauseEvents.Add(new SlaPauseEvent
            {
                Id = Guid.NewGuid(),
                SlaTimerId = timerId,
                Reason = PauseReasonEnum.CustomerUnavailable,
                PausedAt = FixedNow.AddHours(-49),
                PausedByUserId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        }
        var clock = new Mock<TimeProvider>();
        clock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedNow));
        var service = new TestableSlaTimerService(new Mock<ILogger<SlaTimerBackgroundService>>().Object,
            provider.GetRequiredService<IServiceScopeFactory>(), clock.Object);

        await service.TriggerCheck(CancellationToken.None);

        using var verificationScope = provider.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var timer = await verificationDb.SlaTimers.FindAsync(timerId);
        var pauseEvent = await verificationDb.SlaPauseEvents.SingleAsync();
        var ticketAfter = await verificationDb.Tickets.FindAsync(ticketId);
        timer!.Status.Should().Be(SlaTimerStatusEnum.Paused);
        timer.LastAutoResumeAt.Should().BeNull();
        timer.DueAt.Should().Be(FixedNow.AddHours(2));
        pauseEvent.ResumedAt.Should().BeNull();
        ticketAfter!.Status.Should().Be(TicketStatusEnum.Pending);
    }

    [Theory]
    [InlineData(TicketStatusEnum.Pending, TicketPriorityEnum.P1Critical)]
    [InlineData(TicketStatusEnum.Request, TicketPriorityEnum.P1Critical)]
    [InlineData(TicketStatusEnum.ReAssign, TicketPriorityEnum.P1Critical)]
    [InlineData(TicketStatusEnum.InProgress, TicketPriorityEnum.Urgent)]
    public async Task RunningTimer_OutsideEligibleWork_DoesNotWarnOrBreach(
        TicketStatusEnum status,
        TicketPriorityEnum priority)
    {
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();
        await using var provider = CreateProvider(Guid.NewGuid().ToString());

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var ticket = new Ticket
            {
                Id = ticketId,
                Code = "T-INELIGIBLE-SLA",
                CustomerId = Guid.NewGuid(),
                Title = "Test",
                Description = "Test",
                Category = TicketCategoryEnum.Other,
                Status = status,
                Priority = priority,
                Origin = TicketOriginEnum.ManualByCustomer
            };
            db.Tickets.Add(ticket);
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Ticket = ticket,
                Priority = priority,
                StartedAt = FixedNow.AddHours(-5),
                DueAt = FixedNow.AddSeconds(-1),
                OriginalDueAt = FixedNow.AddSeconds(-1),
                Status = SlaTimerStatusEnum.Running
            });
            await db.SaveChangesAsync();
        }

        var clock = new Mock<TimeProvider>();
        clock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedNow));
        var service = new TestableSlaTimerService(
            new Mock<ILogger<SlaTimerBackgroundService>>().Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock.Object);

        await service.TriggerCheck(CancellationToken.None);

        using var verificationScope = provider.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var timer = await verificationDb.SlaTimers.FindAsync(timerId);
        timer!.Status.Should().Be(SlaTimerStatusEnum.Running);
        timer.WarningSentAt.Should().BeNull();
        timer.BreachAt.Should().BeNull();

        var outbox = Mock.Get(verificationScope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>());
        outbox.Verify(
            x => x.WriteAsync(It.IsAny<SlaBreachedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ServiceProvider CreateProvider(string dbName)
    {
        var mockProducer = new Mock<IIntegrationEventOutboxWriter>();

        return new ServiceCollection()
            .AddScoped<ICurrentUserService, NullUserService>()
            .AddScoped<AuditableEntityInterceptor>()
            .AddScoped<ISlaCalculator, SlaCalculator>()
            .AddDbContext<TicketDbContext>(options => options.UseInMemoryDatabase(dbName))
            .AddSingleton<IIntegrationEventOutboxWriter>(mockProducer.Object)
            .AddMassTransitTestHarness(x =>
            {
                // Flaky guard 2026-07-31: inactivity mặc định của MassTransit v8 = 1s ⇒ Consumed.Any<T>()
                // trả false khi cả solution chạy song song. Khuôn: NotificationService/Helpers/ConsumerTestHarness.cs
                x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15));
            })
            .BuildServiceProvider(true);
    }

    private async Task SetupData(ServiceProvider provider, Guid ticketId, Guid slaTimerId, DateTime start, DateTime due)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

        var ticket = new Ticket { Id = ticketId, Code = "T-FIXED", Title = "Test", Description = "Test", Category = TicketCategoryEnum.Other, Status = TicketStatusEnum.InProgress, Origin = TicketOriginEnum.ManualByCustomer, IsDeleted = false };
        var slaTimer = new SlaTimer { Id = slaTimerId, TicketId = ticketId, Priority = TicketPriorityEnum.P1Critical, StartedAt = start, DueAt = due, Status = SlaTimerStatusEnum.Running, IsDeleted = false };

        dbContext.Tickets.Add(ticket);
        dbContext.SlaTimers.Add(slaTimer);
        await dbContext.SaveChangesAsync();
    }
}
