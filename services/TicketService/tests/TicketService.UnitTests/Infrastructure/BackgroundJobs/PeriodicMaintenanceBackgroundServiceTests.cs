using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Infrastructure.BackgroundJobs;

public class PeriodicMaintenanceBackgroundServiceTests
{
    private static readonly TimeZoneInfo Vietnam =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    [Fact]
    public void CalculateDueAtUtc_UsesCalendarMonthsAcrossMonthEnd()
    {
        var closedAt = new DateTime(2026, 8, 31, 3, 0, 0, DateTimeKind.Utc);

        var dueAt = PeriodicMaintenanceBackgroundService.CalculateDueAtUtc(closedAt, 6);

        dueAt.Should().Be(new DateTime(2027, 2, 28, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void CalculateCreationLocalDate_SubtractsSevenLocalCalendarDays()
    {
        var dueAt = new DateTime(2026, 9, 8, 1, 0, 0, DateTimeKind.Utc);

        var creationDate = PeriodicMaintenanceBackgroundService.CalculateCreationLocalDate(
            dueAt,
            Vietnam,
            7);

        creationDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void AddLocalCalendarDays_PreservesLocalTime()
    {
        var createdAt = new DateTime(2026, 8, 20, 4, 30, 0, DateTimeKind.Utc);

        var deadline = PeriodicMaintenanceBackgroundService.AddLocalCalendarDays(
            createdAt,
            Vietnam,
            7);

        deadline.Should().Be(new DateTime(2026, 8, 27, 4, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetDueReminderStage_AdvancesOncePerPersistedStage()
    {
        var createdAt = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
        var ticket = PeriodicTicket(createdAt);
        var now = new DateTime(2026, 8, 22, 2, 0, 0, DateTimeKind.Utc);

        PeriodicMaintenanceBackgroundService.GetDueReminderStage(
                ticket, now, Vietnam, TimeSpan.FromHours(8))
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerFirstReminder);

        ticket.PeriodicMaintenanceReminder1SentAtUtc = now;
        PeriodicMaintenanceBackgroundService.GetDueReminderStage(
                ticket, now, Vietnam, TimeSpan.FromHours(8))
            .Should().Be(PeriodicMaintenanceReminderStage.CustomerSecondReminder);

        ticket.PeriodicMaintenanceReminder2SentAtUtc = now;
        PeriodicMaintenanceBackgroundService.GetDueReminderStage(
                ticket, now, Vietnam, TimeSpan.FromHours(8))
            .Should().Be(PeriodicMaintenanceReminderStage.ManagerEscalation);
    }

    [Fact]
    public void GetDueReminderStage_WithSelectedSchedule_ReturnsNull()
    {
        var ticket = PeriodicTicket(DateTime.UtcNow.AddDays(-3));
        ticket.ScheduledStartAtUtc = DateTime.UtcNow.AddDays(1);

        var stage = PeriodicMaintenanceBackgroundService.GetDueReminderStage(
            ticket,
            DateTime.UtcNow,
            Vietnam,
            TimeSpan.FromHours(8));

        stage.Should().BeNull();
    }

    [Fact]
    public void BuildCreationAnchorQuery_UsesLatestClosedTicketAndExcludesExistingCycle()
    {
        var batteryWithNewCycle = Guid.NewGuid();
        var batteryAlreadyGenerated = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var latestClosedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var latest = ClosedTicket(batteryWithNewCycle, customerId, latestClosedAt);
        var existingAnchor = ClosedTicket(
            batteryAlreadyGenerated,
            customerId,
            latestClosedAt.AddDays(-1));
        var tickets = new List<Ticket>
        {
            ClosedTicket(batteryWithNewCycle, customerId, latestClosedAt.AddDays(-10)),
            latest,
            existingAnchor,
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-EXISTING",
                Title = "Periodic",
                Description = "Periodic",
                BatteryAssetId = batteryAlreadyGenerated,
                CustomerId = customerId,
                Status = TicketStatusEnum.Open,
                PeriodicMaintenanceSourceTicketId = existingAnchor.Id,
                PeriodicMaintenanceDueAtUtc = existingAnchor.ClosedAt!.Value.AddMonths(6)
            }
        }.AsQueryable();

        var anchors = PeriodicMaintenanceBackgroundService.BuildCreationAnchorQuery(
                tickets,
                new PeriodicMaintenanceOptions { CycleMonths = 6, BatchSize = 100 },
                latestClosedAt.AddMonths(6).AddDays(1))
            .ToList();

        anchors.Should().ContainSingle();
        anchors[0].SourceTicketId.Should().Be(latest.Id);
        anchors[0].BatteryAssetId.Should().Be(batteryWithNewCycle);
    }

    [Fact]
    public async Task RunOnceAsync_CreatesNormalAndOverdueTicketsWithExpectedMetadata()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        var normalBatteryId = Guid.NewGuid();
        var overdueBatteryId = Guid.NewGuid();
        var normalCustomerId = Guid.NewGuid();
        var overdueCustomerId = Guid.NewGuid();
        var normalDueAtUtc = nowUtc.UtcDateTime.AddDays(7);
        var overdueDueAtUtc = nowUtc.UtcDateTime.AddDays(-2);
        var normalAnchor = ClosedTicket(
            normalBatteryId,
            normalCustomerId,
            normalDueAtUtc.AddMonths(-6));
        var overdueAnchor = ClosedTicket(
            overdueBatteryId,
            overdueCustomerId,
            overdueDueAtUtc.AddMonths(-6));
        var persistedTickets = new List<Ticket> { normalAnchor, overdueAnchor };
        var setup = MockTicketUnitOfWork.Build(ticketSeed: persistedTickets);

        setup.tickets.Setup(repository => repository.GetAllAsync())
            .Returns(() => persistedTickets.AsQueryable().BuildMock());
        setup.tickets.Setup(repository => repository.AddAsync(It.IsAny<Ticket>()))
            .Callback<Ticket>(persistedTickets.Add)
            .Returns(Task.CompletedTask);
        Mock.Get(setup.uow.Object.TicketBatteryAssets)
            .Setup(repository => repository.AddAsync(It.IsAny<TicketBatteryAsset>()))
            .Returns(Task.CompletedTask);
        Mock.Get(setup.uow.Object.TicketParticipants)
            .Setup(repository => repository.AddAsync(It.IsAny<TicketParticipant>()))
            .Returns(Task.CompletedTask);

        var codeGenerator = new Mock<ITicketCodeGenerator>();
        codeGenerator.SetupSequence(generator => generator.GenerateAsync())
            .ReturnsAsync("TKT-PERIODIC-1")
            .ReturnsAsync("TKT-PERIODIC-2");
        var services = new ServiceCollection()
            .AddSingleton(setup.uow.Object)
            .AddSingleton(codeGenerator.Object)
            .AddSingleton(Mock.Of<IIntegrationEventOutboxWriter>())
            .BuildServiceProvider();
        var options = Options.Create(new PeriodicMaintenanceOptions
        {
            Enabled = true,
            TimeZoneId = "Asia/Ho_Chi_Minh",
            CycleMonths = 6,
            LeadDays = 7,
            OverdueScheduleWindowDays = 7,
            ReminderTime = TimeSpan.FromHours(8),
            PollIntervalSeconds = 60,
            BatchSize = 100
        });
        var service = new PeriodicMaintenanceBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(),
            options,
            Mock.Of<ILogger<PeriodicMaintenanceBackgroundService>>(),
            new FixedTimeProvider(nowUtc));

        await service.RunOnceAsync(CancellationToken.None);

        var generated = persistedTickets
            .Where(ticket => ticket.PeriodicMaintenanceSourceTicketId.HasValue)
            .ToList();
        generated.Should().HaveCount(2);

        var normal = generated.Single(ticket => ticket.BatteryAssetId == normalBatteryId);
        normal.CustomerId.Should().Be(normalCustomerId);
        normal.PeriodicMaintenanceSourceTicketId.Should().Be(normalAnchor.Id);
        normal.PeriodicMaintenanceDueAtUtc.Should().Be(normalDueAtUtc);
        normal.PeriodicMaintenanceScheduleDeadlineAtUtc.Should().Be(normalDueAtUtc);
        normal.Status.Should().Be(TicketStatusEnum.Open);

        var overdue = generated.Single(ticket => ticket.BatteryAssetId == overdueBatteryId);
        overdue.CustomerId.Should().Be(overdueCustomerId);
        overdue.PeriodicMaintenanceSourceTicketId.Should().Be(overdueAnchor.Id);
        overdue.PeriodicMaintenanceDueAtUtc.Should().Be(overdueDueAtUtc);
        overdue.PeriodicMaintenanceScheduleDeadlineAtUtc.Should().Be(
            PeriodicMaintenanceBackgroundService.AddLocalCalendarDays(
                nowUtc.UtcDateTime,
                Vietnam,
                7));
    }

    private static Ticket PeriodicTicket(DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        Code = "TKT-PERIODIC",
        Title = "Periodic",
        Description = "Periodic",
        CustomerId = Guid.NewGuid(),
        PeriodicMaintenanceSourceTicketId = Guid.NewGuid(),
        PeriodicMaintenanceDueAtUtc = createdAt.AddDays(7),
        PeriodicMaintenanceScheduleDeadlineAtUtc = createdAt.AddDays(7),
        CreatedAt = createdAt
    };

    private static Ticket ClosedTicket(Guid batteryId, Guid customerId, DateTime closedAt) => new()
    {
        Id = Guid.NewGuid(),
        Code = $"TKT-{Guid.NewGuid():N}",
        Title = "Closed",
        Description = "Closed",
        BatteryAssetId = batteryId,
        CustomerId = customerId,
        Status = TicketStatusEnum.Closed,
        ClosedAt = closedAt
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
