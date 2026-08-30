using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

/// <summary>
/// Link cha–con là quan hệ "cùng nguyên nhân gốc", KHÔNG phải merge. Điểm quan trọng nhất mà
/// các test dưới đây khoá lại: ticket con phải giữ nguyên Status và không bị đóng — nếu ai đó
/// sau này gộp logic này vào merge thì test sẽ đỏ.
/// </summary>
public class TicketLinkParentCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidParent_SetsLinkAndLeavesStatusUntouched()
    {
        var customerId = Guid.NewGuid();
        var parent = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-ENV");
        var child = CreateTicket(TicketStatusEnum.InProgress, customerId, "TKT-BAT");
        var (uow, tickets, activities, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { parent, child });
        activities.Setup(x => x.AddAsync(It.IsAny<TicketActivity>())).Returns(Task.CompletedTask);

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = parent.Id, ActorId = Guid.NewGuid() },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        child.ParentTicketId.Should().Be(parent.Id);
        // Cốt lõi của tính năng: link KHÔNG đóng ticket con và KHÔNG đụng vòng đời của nó.
        child.Status.Should().Be(TicketStatusEnum.InProgress);
        child.MergedIntoTicketId.Should().BeNull();
        child.ClosedAt.Should().BeNull();
        tickets.Verify(x => x.UpdateAsync(child), Times.Once);
    }

    [Fact]
    public async Task Handle_NullParent_ClearsExistingLink()
    {
        var customerId = Guid.NewGuid();
        var child = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-BAT");
        child.ParentTicketId = Guid.NewGuid();
        var (uow, _, activities, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { child });
        activities.Setup(x => x.AddAsync(It.IsAny<TicketActivity>())).Returns(Task.CompletedTask);

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = null }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        child.ParentTicketId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DifferentCustomer_Returns409()
    {
        var parent = CreateTicket(TicketStatusEnum.Open, Guid.NewGuid(), "TKT-ENV");
        var child = CreateTicket(TicketStatusEnum.Open, Guid.NewGuid(), "TKT-BAT");
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { parent, child });

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = parent.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        child.ParentTicketId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SelfAsParent_Returns400()
    {
        var child = CreateTicket(TicketStatusEnum.Open, Guid.NewGuid(), "TKT-BAT");
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { child });

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = child.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_ParentAlreadyLinked_Returns409()
    {
        var customerId = Guid.NewGuid();
        var grandParent = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-ROOT");
        var parent = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-ENV");
        parent.ParentTicketId = grandParent.Id;
        var child = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-BAT");
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { grandParent, parent, child });

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = parent.Id }, CancellationToken.None);

        // Chỉ một cấp — nếu không thì panel phải duyệt cây và A→B→A thành vòng lặp.
        result.StatusCode.Should().Be(409);
        child.ParentTicketId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ChildHasOwnChildren_Returns409()
    {
        var customerId = Guid.NewGuid();
        var parent = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-ENV");
        var child = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-MID");
        var grandChild = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-LEAF");
        grandChild.ParentTicketId = child.Id;
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { parent, child, grandChild });

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = parent.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_MergedChild_Returns409()
    {
        var customerId = Guid.NewGuid();
        var parent = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-ENV");
        var child = CreateTicket(TicketStatusEnum.Open, customerId, "TKT-BAT");
        child.MergedIntoTicketId = Guid.NewGuid();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { parent, child });

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = child.Id, ParentTicketId = parent.Id }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: Array.Empty<Ticket>());

        var result = await new TicketLinkParentCommandHandler(uow.Object).Handle(
            new TicketLinkParentCommand { Id = Guid.NewGuid(), ParentTicketId = Guid.NewGuid() },
            CancellationToken.None);

        result.StatusCode.Should().Be(404);
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
}
