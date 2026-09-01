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
    [InlineData(TicketStatusEnum.Closed, TicketPriorityEnum.P1Critical)]
    [InlineData(TicketStatusEnum.ClosedRejected, TicketPriorityEnum.P1Critical)]
    [InlineData(TicketStatusEnum.Completed, TicketPriorityEnum.P1Critical)]
    [InlineData(TicketStatusEnum.InProgress, TicketPriorityEnum.Urgent)]
    [InlineData(TicketStatusEnum.Open, TicketPriorityEnum.Urgent)]
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

    [Fact]
    public async Task When_OpenTicket_SlaInWarningZone_Should_SendWarning_With_NullStaffId()
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
                Code = "T-OPEN-WARN",
                CustomerId = Guid.NewGuid(),
                Title = "Test Open",
                Description = "Test",
                Category = TicketCategoryEnum.Other,
                Status = TicketStatusEnum.Open,
                Priority = TicketPriorityEnum.P1Critical,
                Origin = TicketOriginEnum.ManualByCustomer
            };
            db.Tickets.Add(ticket);
            // P1 Response SLA: 4h (240 min). 80% = 192 min elapsed (48 min remaining).
            // StartedAt = FixedNow - 193 min, DueAt = FixedNow + 47 min.
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Ticket = ticket,
                Priority = TicketPriorityEnum.P1Critical,
                StartedAt = FixedNow.AddMinutes(-193),
                DueAt = FixedNow.AddMinutes(47),
                OriginalDueAt = FixedNow.AddMinutes(47),
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
        timer!.WarningSentAt.Should().Be(FixedNow);

        var outbox = Mock.Get(verificationScope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>());
        outbox.Verify(
            x => x.WriteAsync(
                It.Is<SlaWarningEvent>(e => e.TicketId == ticketId && e.StaffId == null && e.Percentage >= 80d),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task When_OpenTicket_SlaBreached_Should_UpdateStatus_To_Breached()
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
                Code = "T-OPEN-BREACH",
                CustomerId = Guid.NewGuid(),
                Title = "Test Open Breach",
                Description = "Test",
                Category = TicketCategoryEnum.Other,
                Status = TicketStatusEnum.Open,
                Priority = TicketPriorityEnum.P1Critical,
                Origin = TicketOriginEnum.ManualByCustomer
            };
            db.Tickets.Add(ticket);
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Ticket = ticket,
                Priority = TicketPriorityEnum.P1Critical,
                StartedAt = FixedNow.AddHours(-5),
                DueAt = FixedNow.AddSeconds(-10),
                OriginalDueAt = FixedNow.AddSeconds(-10),
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
        timer!.Status.Should().Be(SlaTimerStatusEnum.Breached);
        timer.BreachAt.Should().Be(FixedNow);

        var outbox = Mock.Get(verificationScope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>());
        outbox.Verify(
            x => x.WriteAsync(
                It.Is<SlaBreachedEvent>(e => e.TicketId == ticketId && e.Code == "T-OPEN-BREACH"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── QA Bug #20 — rescue window monitoring ────────────────────────────────────

    [Fact]
    public async Task RescueWindow_Expired_StopsTimerAndFiresSecondBreachEvent()
    {
        // Arrange: ticket InProgress, timer Breached, assignment 3 days old (> 1440 working min)
        // Note: AuditableEntityInterceptor overrides CreatedAt=UtcNow on EntityState.Added.
        // We work around this with a two-step save: first Add, then Modify CreatedAt.
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var assignmentCreatedAt = FixedNow.AddDays(-3);  // 3 days = 1800 working min > 1440

        await using var provider = CreateProvider(Guid.NewGuid().ToString());
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var ticket = new Ticket
            {
                Id = ticketId,
                Code = "T-RESCUE-EXP",
                Title = "Rescue expired",
                Description = "d",
                Category = TicketCategoryEnum.Other,
                Status = TicketStatusEnum.InProgress,
                Priority = TicketPriorityEnum.P2High,
                Origin = TicketOriginEnum.ManualByCustomer
            };
            db.Tickets.Add(ticket);
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Ticket = ticket,
                Type = SlaTimerTypeEnum.Resolution,
                Priority = TicketPriorityEnum.P2High,
                StartedAt = FixedNow.AddDays(-10),
                DueAt = FixedNow.AddDays(-3),
                OriginalDueAt = FixedNow.AddDays(-3),
                BreachAt = FixedNow.AddDays(-3),
                Status = SlaTimerStatusEnum.Breached
            });
            db.TicketAssignments.Add(new TicketAssignment
            {
                Id = assignmentId,
                TicketId = ticketId,
                Ticket = ticket,
                StaffId = staffId,
                Role = AssignmentRoleEnum.PrimaryHandler
                // CreatedAt will be set by interceptor → overwrite in second save below
            });
            await db.SaveChangesAsync();

            // Interceptor set CreatedAt = real UtcNow; overwrite via Modified (interceptor only
            // touches UpdatedAt on Modified, not CreatedAt — so the value sticks).
            var saved = db.TicketAssignments.Local.Single(a => a.Id == assignmentId);
            saved.CreatedAt = assignmentCreatedAt;
            await db.SaveChangesAsync();
        }

        var clock = new Mock<TimeProvider>();
        clock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedNow));
        var service = new TestableSlaTimerService(
            new Mock<ILogger<SlaTimerBackgroundService>>().Object,
            provider.GetRequiredService<IServiceScopeFactory>(), clock.Object);

        // Act
        await service.TriggerCheck(CancellationToken.None);

        // Assert — timer stopped, second SlaBreachedEvent fired
        using var verifyScope = provider.CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var timer = await db2.SlaTimers.FindAsync(timerId);
        timer!.Status.Should().Be(SlaTimerStatusEnum.Stopped, "rescue window expired → timer must be stopped");

        var outbox = Mock.Get(verifyScope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>());
        outbox.Verify(
            x => x.WriteAsync(
                It.Is<SlaBreachedEvent>(e => e.TicketId == ticketId && e.Code == "T-RESCUE-EXP"),
                It.IsAny<CancellationToken>()),
            Times.Once, "second SlaBreachedEvent must be fired when rescue window expires");
    }

    [Fact]
    public async Task RescueWindow_NotYetExpired_DoesNotStopTimer()
    {
        // Arrange: assignment only 30 working minutes old (< 1440) → timer must remain Breached
        var ticketId = Guid.NewGuid();
        var timerId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var assignmentCreatedAt = FixedNow.AddMinutes(-30);  // 30 working min << 1440

        await using var provider = CreateProvider(Guid.NewGuid().ToString());
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
            var ticket = new Ticket
            {
                Id = ticketId,
                Code = "T-RESCUE-OK",
                Title = "Rescue active",
                Description = "d",
                Category = TicketCategoryEnum.Other,
                Status = TicketStatusEnum.InProgress,
                Priority = TicketPriorityEnum.P2High,
                Origin = TicketOriginEnum.ManualByCustomer
            };
            db.Tickets.Add(ticket);
            db.SlaTimers.Add(new SlaTimer
            {
                Id = timerId,
                TicketId = ticketId,
                Ticket = ticket,
                Type = SlaTimerTypeEnum.Resolution,
                Priority = TicketPriorityEnum.P2High,
                StartedAt = FixedNow.AddDays(-10),
                DueAt = FixedNow.AddDays(-1),
                OriginalDueAt = FixedNow.AddDays(-1),
                BreachAt = FixedNow.AddDays(-1),
                Status = SlaTimerStatusEnum.Breached
            });
            db.TicketAssignments.Add(new TicketAssignment
            {
                Id = assignmentId,
                TicketId = ticketId,
                Ticket = ticket,
                StaffId = staffId,
                Role = AssignmentRoleEnum.PrimaryHandler
                // CreatedAt overwritten below after interceptor runs
            });
            await db.SaveChangesAsync();

            var saved = db.TicketAssignments.Local.Single(a => a.Id == assignmentId);
            saved.CreatedAt = assignmentCreatedAt;
            await db.SaveChangesAsync();
        }

        var clock = new Mock<TimeProvider>();
        clock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(FixedNow));
        var service = new TestableSlaTimerService(
            new Mock<ILogger<SlaTimerBackgroundService>>().Object,
            provider.GetRequiredService<IServiceScopeFactory>(), clock.Object);

        await service.TriggerCheck(CancellationToken.None);

        using var verifyScope = provider.CreateScope();
        var db2 = verifyScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var timer = await db2.SlaTimers.FindAsync(timerId);
        timer!.Status.Should().Be(SlaTimerStatusEnum.Breached, "rescue window still active — timer must stay Breached");

        var outbox = Mock.Get(verifyScope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>());
        outbox.Verify(
            x => x.WriteAsync(It.IsAny<SlaBreachedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never, "no second breach event while rescue window is still active");
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
        var slaTimer = new SlaTimer { Id = slaTimerId, TicketId = ticketId, Type = SlaTimerTypeEnum.Resolution, Priority = TicketPriorityEnum.P1Critical, StartedAt = start, DueAt = due, Status = SlaTimerStatusEnum.Running, IsDeleted = false };

        dbContext.Tickets.Add(ticket);
        dbContext.SlaTimers.Add(slaTimer);
        await dbContext.SaveChangesAsync();
    }
}
