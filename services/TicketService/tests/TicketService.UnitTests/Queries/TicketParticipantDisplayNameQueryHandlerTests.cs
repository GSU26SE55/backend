using FluentAssertions;
using TicketService.Application.CQRS.Handler.Participants;
using TicketService.Application.CQRS.Query.TicketParticipants;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketParticipantDisplayNameQueryHandlerTests
{
    [Fact]
    public async Task Handle_ActiveCustomerParticipant_ReturnsDisplayNameFromCustomerAccount()
    {
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-001",
            CustomerId = Guid.NewGuid(),
            Title = "Test",
            Description = "Description",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.InProgress,
            Origin = TicketOriginEnum.ManualByCustomer
        };
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            FullName = "Customer mention candidate"
        };
        var participant = new TicketParticipant
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            UserId = customer.AccountId,
            UserRole = ActorRoleEnum.Customer,
            ParticipantType = ParticipantTypeEnum.Owner,
            CanPost = true,
            AddedByUserId = Guid.NewGuid(),
            AddedAt = DateTime.UtcNow
        };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            customerSeed: new[] { customer },
            participantSeed: new[] { participant });
        var handler = new TicketParticipantsQueryHandler(uow.Object);

        var result = await handler.Handle(new TicketParticipantsQuery
        {
            TicketId = ticket.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().ContainSingle();
        result.Data![0].DisplayName.Should().Be(customer.FullName);
        result.Data[0].UserRole.Should().Be(ActorRoleEnum.Customer);
        result.Data[0].ParticipantType.Should().Be(ParticipantTypeEnum.Owner);
    }
}
