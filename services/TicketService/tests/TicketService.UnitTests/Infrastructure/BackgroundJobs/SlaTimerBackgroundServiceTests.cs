using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.Persistence;
using Xunit;

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

    // Mốc thời gian cố định cho toàn bộ Test: 2026-06-03 12:00:00 UTC
    private static readonly DateTime FixedNow = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);

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

        // Data: P1 (4h = 240m). 80% là 192m.
        // Nếu bắt đầu lúc FixedNow - 193m -> Hiện tại là 80.4% -> Phải Warning.
        var startTime = FixedNow.AddMinutes(-193);
        var dueAt = startTime.AddHours(4);

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

    private ServiceProvider CreateProvider(string dbName)
    {
        var mockProducer = new Mock<IMessageProducerService>();

        return new ServiceCollection()
            .AddScoped<ICurrentUserService, NullUserService>()
            .AddScoped<AuditableEntityInterceptor>()
            .AddScoped<ISlaCalculator, SlaCalculator>()
            .AddDbContext<TicketDbContext>(options => options.UseInMemoryDatabase(dbName))
            .AddSingleton<IMessageProducerService>(mockProducer.Object)
            .AddMassTransitTestHarness()
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
