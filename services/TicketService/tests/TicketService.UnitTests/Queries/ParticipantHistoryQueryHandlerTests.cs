using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Application.CQRS.Query.ParticipantHistory;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ParticipantHistoryQueryHandlerTests
{
    private static Ticket MakeTicket() => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        CustomerId = Guid.NewGuid(),
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_StaffViewsFullHistoryIncludingRemoved_Returns200()
    {
        var ticket = MakeTicket();
        var activeParticipant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Owner,
            CanPost = true,
            CanViewInternal = false,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow
        };
        var removedParticipant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            ParticipantType = ParticipantTypeEnum.PreviousAssignee,
            CanPost = false,
            CanViewInternal = true,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow.AddDays(-1),
            RemovedAt = DateTime.UtcNow,
            RemovedByUserId = Guid.NewGuid(),
            RemoveReason = "reassigned"
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { activeParticipant, removedParticipant });

        var handler = new ParticipantHistoryQueryHandler(uow.Object);

        var result = await handler.Handle(new ParticipantHistoryQuery
        {
            TicketId = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().HaveCount(2);
        result.Data!.Should().Contain(d => d.RemovedAt != null && d.RemoveReason == "reassigned");
    }

    [Fact]
    public async Task Handle_CustomerCannotViewHistory_Returns403()
    {
        var ticket = MakeTicket();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var handler = new ParticipantHistoryQueryHandler(uow.Object);

        var result = await handler.Handle(new ParticipantHistoryQuery
        {
            TicketId = ticket.Id,
            ActorUserId = ticket.CustomerId,
            ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();

        var handler = new ParticipantHistoryQueryHandler(uow.Object);

        var result = await handler.Handle(new ParticipantHistoryQuery
        {
            TicketId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
