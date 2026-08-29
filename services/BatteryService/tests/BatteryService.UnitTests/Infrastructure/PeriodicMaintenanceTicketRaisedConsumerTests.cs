using BatteryService.Domain.Entities;
using BatteryService.Infrastructure.Consumers;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace BatteryService.UnitTests.Infrastructure;

public class PeriodicMaintenanceTicketRaisedConsumerTests
{
    private readonly Mock<IInboxStore> _inbox = new();

    public PeriodicMaintenanceTicketRaisedConsumerTests()
    {
        _inbox
            .Setup(store => store.TryBeginAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "maintenance-link-test"));
        _inbox
            .Setup(store => store.CompleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task MatchingCycle_LinksTicketAndPersists()
    {
        var cycle = Cycle();
        var ticketId = Guid.NewGuid();
        var uow = new MockUnitOfWorkBuilder().WithMaintenanceCycles(cycle);

        await Consumer(uow).Consume(Context(Event(cycle, ticketId)));

        cycle.TicketId.Should().Be(ticketId);
        uow.MaintenanceCycles.Verify(
            repository => repository.UpdateAsync(cycle), Times.Once);
        uow.UnitOfWork.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CycleAlreadyLinkedToAnotherTicket_DoesNotOverwrite()
    {
        var existingTicketId = Guid.NewGuid();
        var cycle = Cycle();
        cycle.TicketId = existingTicketId;
        var uow = new MockUnitOfWorkBuilder().WithMaintenanceCycles(cycle);

        await Consumer(uow).Consume(Context(Event(cycle, Guid.NewGuid())));

        cycle.TicketId.Should().Be(existingTicketId);
        uow.MaintenanceCycles.Verify(
            repository => repository.UpdateAsync(It.IsAny<MaintenanceCycle>()), Times.Never);
        uow.UnitOfWork.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EventForAnotherBattery_DoesNotLinkCycle()
    {
        var cycle = Cycle();
        var uow = new MockUnitOfWorkBuilder().WithMaintenanceCycles(cycle);
        var message = new PeriodicMaintenanceTicketRaisedEvent(
            cycle.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TKT-WRONG-BATTERY",
            cycle.DueAtUtc);

        await Consumer(uow).Consume(Context(message));

        cycle.TicketId.Should().BeNull();
        uow.MaintenanceCycles.Verify(
            repository => repository.UpdateAsync(It.IsAny<MaintenanceCycle>()), Times.Never);
        uow.UnitOfWork.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownCycle_IsIgnoredWithoutCreatingData()
    {
        var uow = new MockUnitOfWorkBuilder();
        var message = new PeriodicMaintenanceTicketRaisedEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "TKT-MISSING-CYCLE",
            DateTime.UtcNow);

        await Consumer(uow).Consume(Context(message));

        uow.MaintenanceCycles.Verify(
            repository => repository.UpdateAsync(It.IsAny<MaintenanceCycle>()), Times.Never);
        uow.UnitOfWork.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private PeriodicMaintenanceTicketRaisedConsumer Consumer(MockUnitOfWorkBuilder uow) =>
        new(uow.Build(), _inbox.Object,
            NullLogger<PeriodicMaintenanceTicketRaisedConsumer>.Instance);

    private static MaintenanceCycle Cycle() => new()
    {
        Id = Guid.NewGuid(),
        BatteryAssetId = Guid.NewGuid(),
        CycleNo = 1,
        DueAtUtc = DateTime.UtcNow.AddDays(3),
        RecordedAtUtc = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    private static PeriodicMaintenanceTicketRaisedEvent Event(
        MaintenanceCycle cycle,
        Guid ticketId) =>
        new(cycle.Id, cycle.BatteryAssetId, ticketId, "TKT-MAINT-001", cycle.DueAtUtc);

    private static ConsumeContext<PeriodicMaintenanceTicketRaisedEvent> Context(
        PeriodicMaintenanceTicketRaisedEvent message)
    {
        var context = new Mock<ConsumeContext<PeriodicMaintenanceTicketRaisedEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.MessageId).Returns(message.Id);
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }
}
