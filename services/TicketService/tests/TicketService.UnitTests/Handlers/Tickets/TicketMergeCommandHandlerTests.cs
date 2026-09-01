using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketMergeCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidNewSourceWithSharedBattery_ClosesSourceAndWritesOutbox()
    {
        var customerId = Guid.NewGuid();
        var source = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-SOURCE");
        var master = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-MASTER");
        var attachment = new TicketAttachment { Id = Guid.NewGuid(), TicketId = source.Id, Ticket = source, FileId = Guid.NewGuid(), UploadedByUserId = customerId, FileName = "photo.jpg", ContentType = "image/jpeg" };
        var (uow, _, activities, _, _, _, _, _, attachments, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { source, master }, attachmentSeed: new[] { attachment });
        var sharedBatteryId = Guid.NewGuid();
        var batteries = new[]
        {
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = source.Id, BatteryAssetId = sharedBatteryId },
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = master.Id, BatteryAssetId = sharedBatteryId }
        };
        uow.SetupGet(x => x.TicketBatteryAssets).Returns(BuildBatteryRepository(batteries).Object);
        activities.Setup(x => x.AddAsync(It.IsAny<TicketActivity>())).Returns(Task.CompletedTask);
        attachments.Setup(x => x.UpdateAsync(It.IsAny<TicketAttachment>()));
        var outbox = new Mock<IIntegrationEventOutboxWriter>();
        outbox.Setup(x => x.WriteAsync(It.IsAny<TicketMergedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await new TicketMergeCommandHandler(uow.Object, outbox.Object).Handle(
            new TicketMergeCommand { TicketId = source.Id, TargetTicketId = master.Id, ManagerId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        source.Status.Should().Be(TicketStatusEnum.Closed);
        source.CloseReason.Should().Be(TicketCloseReasonEnum.MergedDuplicate);
        source.MergedIntoTicketId.Should().Be(master.Id);
        attachment.TicketId.Should().Be(master.Id);
        attachment.SourceTicketId.Should().Be(source.Id);
        outbox.Verify(x => x.WriteAsync(It.IsAny<TicketMergedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DifferentCustomer_Returns409WithoutTransaction()
    {
        var source = CreateTicket(TicketStatusEnum.Open, Guid.NewGuid(), "TKT-SOURCE");
        var master = CreateTicket(TicketStatusEnum.Open, Guid.NewGuid(), "TKT-MASTER");
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { source, master });

        var result = await new TicketMergeCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>()).Handle(
            new TicketMergeCommand { TicketId = source.Id, TargetTicketId = master.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        uow.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoSharedBattery_Returns409()
    {
        var customerId = Guid.NewGuid();
        var source = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-SOURCE");
        var master = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-MASTER");
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { source, master });
        uow.SetupGet(x => x.TicketBatteryAssets).Returns(BuildBatteryRepository(new[]
        {
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = source.Id, BatteryAssetId = Guid.NewGuid() },
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = master.Id, BatteryAssetId = Guid.NewGuid() }
        }).Object);

        var result = await new TicketMergeCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>()).Handle(
            new TicketMergeCommand { TicketId = source.Id, TargetTicketId = master.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        uow.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConcurrencyFailure_RollsBackAndReturns409()
    {
        var customerId = Guid.NewGuid();
        var source = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-SOURCE");
        var master = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-MASTER");
        var (uow, _, activities, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { source, master });
        var batteryId = Guid.NewGuid();
        uow.SetupGet(x => x.TicketBatteryAssets).Returns(BuildBatteryRepository(new[]
        {
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = source.Id, BatteryAssetId = batteryId },
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = master.Id, BatteryAssetId = batteryId }
        }).Object);
        activities.Setup(x => x.AddAsync(It.IsAny<TicketActivity>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await new TicketMergeCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>()).Handle(
            new TicketMergeCommand { TicketId = source.Id, TargetTicketId = master.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        uow.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SourceHasParentLink_ClearsParentTicketId()
    {
        // Merging a ticket that was itself linked as a CHILD of some other parent must not
        // leave that link pointing at a Closed+Merged ticket — the parent's Related-tickets
        // panel would otherwise keep citing a dead end.
        var customerId = Guid.NewGuid();
        var grandParent = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-ENV");
        var source = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-SOURCE");
        source.ParentTicketId = grandParent.Id;
        var master = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-MASTER");
        var (uow, _, activities, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { grandParent, source, master });
        var sharedBatteryId = Guid.NewGuid();
        uow.SetupGet(x => x.TicketBatteryAssets).Returns(BuildBatteryRepository(new[]
        {
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = source.Id, BatteryAssetId = sharedBatteryId },
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = master.Id, BatteryAssetId = sharedBatteryId }
        }).Object);
        activities.Setup(x => x.AddAsync(It.IsAny<TicketActivity>())).Returns(Task.CompletedTask);

        var result = await new TicketMergeCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>()).Handle(
            new TicketMergeCommand { TicketId = source.Id, TargetTicketId = master.Id, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        source.ParentTicketId.Should().BeNull();
        // Two timeline entries: the merge itself, plus the unlink side effect.
        activities.Verify(
            x => x.AddAsync(It.Is<TicketActivity>(a => a.TicketId == source.Id)),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_SourceHasChildren_ClearsChildrenParentTicketId()
    {
        // The other direction: `source` is itself a PARENT with children pointing at it.
        // TicketLinkParentCommandHandler refuses to let a ticket-with-children become a child,
        // but Merge has no such gate — so nothing previously stopped a parent from being merged
        // away, leaving every child citing a dead ticket as its parent.
        var customerId = Guid.NewGuid();
        var source = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-SOURCE");
        var master = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-MASTER");
        var child1 = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-CHILD1");
        child1.ParentTicketId = source.Id;
        var child2 = CreateTicket(TicketStatusEnum.InProgress, customerId, "TKT-CHILD2");
        child2.ParentTicketId = source.Id;
        var (uow, tickets, activities, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { source, master, child1, child2 });
        var sharedBatteryId = Guid.NewGuid();
        uow.SetupGet(x => x.TicketBatteryAssets).Returns(BuildBatteryRepository(new[]
        {
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = source.Id, BatteryAssetId = sharedBatteryId },
            new TicketBatteryAsset { Id = Guid.NewGuid(), TicketId = master.Id, BatteryAssetId = sharedBatteryId }
        }).Object);
        activities.Setup(x => x.AddAsync(It.IsAny<TicketActivity>())).Returns(Task.CompletedTask);

        var result = await new TicketMergeCommandHandler(uow.Object, Mock.Of<IIntegrationEventOutboxWriter>()).Handle(
            new TicketMergeCommand { TicketId = source.Id, TargetTicketId = master.Id, ManagerId = Guid.NewGuid() },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        child1.ParentTicketId.Should().BeNull();
        child2.ParentTicketId.Should().BeNull();
        // The children's own status/SLA must NOT change — only the link is cleared.
        child1.Status.Should().Be(TicketStatusEnum.Open);
        child2.Status.Should().Be(TicketStatusEnum.InProgress);
        tickets.Verify(x => x.UpdateAsync(child1), Times.Once);
        tickets.Verify(x => x.UpdateAsync(child2), Times.Once);
    }

    private static Ticket CreateTicket(TicketStatusEnum status, Guid customerId, string code) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        Status = status,
        Code = code,
        Title = code,
        Description = "test",
        BatteryAssetId = Guid.NewGuid()
    };

    private static Mock<IGenericRepository<TicketBatteryAsset>> BuildBatteryRepository(IEnumerable<TicketBatteryAsset> seed)
    {
        var repository = new Mock<IGenericRepository<TicketBatteryAsset>>();
        repository.Setup(x => x.GetAllAsync()).Returns(seed.BuildMock());
        return repository;
    }
}
