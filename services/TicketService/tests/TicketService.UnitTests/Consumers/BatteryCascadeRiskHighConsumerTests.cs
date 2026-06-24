using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedKernels.Interfaces;
using TicketService.Application.Consumers;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Consumers;

/// <summary>
/// Sprint 7 B4 (§31.7) — BatteryCascadeRiskHighConsumer: auto-upgrade Priority P1 + audit.
/// </summary>
public class BatteryCascadeRiskHighConsumerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketRepo = new();
    private readonly Mock<IActivityLogger> _activityLogger = new();

    public BatteryCascadeRiskHighConsumerTests()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private BatteryCascadeRiskHighConsumer Build() =>
        new(_uow.Object, _activityLogger.Object, NullLogger<BatteryCascadeRiskHighConsumer>.Instance);

    private static ConsumeContext<BatteryCascadeRiskHighEvent> Ctx(BatteryCascadeRiskHighEvent evt)
    {
        var ctx = new Mock<ConsumeContext<BatteryCascadeRiskHighEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static BatteryCascadeRiskHighEvent Event(Guid assetId, Guid? relatedTicketId, decimal score = 0.8m) =>
        new(assetId, SiteId: Guid.NewGuid(), CustomerId: Guid.NewGuid(),
            AssetSerialNumber: "BAT-001", CascadeRiskScore: score,
            RelatedTicketId: relatedTicketId, DetectedAt: DateTime.UtcNow);

    [Fact]
    public async Task ActiveTicket_NotP1_UpgradesToP1_AndLogsActivity()
    {
        var assetId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId, Code = "T-1", Title = "t", Description = "d",
            BatteryAssetId = assetId, Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P3Normal
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, ticketId)));

        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _activityLogger.Verify(a => a.LogAsync(
            ticketId, null, ActorRoleEnum.System, It.IsAny<string>(),
            ActivityActionEnum.PriorityAssigned,
            "P3Normal", "P1Critical", It.Is<string>(s => s.Contains("CascadeRisk"))), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadyP1_Skips_NoLog_NoSave()
    {
        var assetId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId, Code = "T-1", Title = "t", Description = "d",
            BatteryAssetId = assetId, Status = TicketStatusEnum.Open,
            Priority = TicketPriorityEnum.P1Critical
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, ticketId)));

        _activityLogger.Verify(a => a.LogAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ActorRoleEnum>(), It.IsAny<string>(),
            It.IsAny<ActivityActionEnum>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoActiveTicket_NoOp()
    {
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket>().AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(Guid.NewGuid(), Guid.NewGuid())));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FallbackByAssetId_WhenNoRelatedTicketId()
    {
        var assetId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(), Code = "T-1", Title = "t", Description = "d",
            BatteryAssetId = assetId, Status = TicketStatusEnum.InProgress,
            Priority = TicketPriorityEnum.P2High
        };
        _ticketRepo.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.AsQueryable().BuildMock());

        await Build().Consume(Ctx(Event(assetId, relatedTicketId: null)));

        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
