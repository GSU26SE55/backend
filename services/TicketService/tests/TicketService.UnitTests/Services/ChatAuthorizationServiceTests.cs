using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Services;

public class ChatAuthorizationServiceTests
{
    private static Ticket MakeTicket(Guid? customerId = null, Guid? assignedStaffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        CustomerId = customerId ?? Guid.NewGuid(),
        AssignedStaffId = assignedStaffId,
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    private static TicketParticipant MakeParticipant(Ticket ticket, Guid userId, ParticipantTypeEnum type, bool canViewInternal, DateTime? removedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        Ticket = ticket,
        UserId = userId,
        UserRole = ActorRoleEnum.Staff,
        ParticipantType = type,
        CanPost = true,
        CanViewInternal = canViewInternal,
        AddedByUserId = Guid.NewGuid(),
        AddedAt = DateTime.UtcNow,
        RemovedAt = removedAt
    };

    #region CanAccessTicketAsync

    [Fact]
    public async Task CanAccessTicketAsync_ActiveParticipant_ReturnsTrue()
    {
        var ticket = MakeTicket();
        var collaboratorId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, collaboratorId, ParticipantTypeEnum.Collaborator, canViewInternal: false);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, collaboratorId, new[] { "Customer" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanAccessTicketAsync_RemovedParticipant_ReturnsFalse()
    {
        var ticket = MakeTicket();
        var removedUserId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, removedUserId, ParticipantTypeEnum.Watcher, canViewInternal: false, removedAt: DateTime.UtcNow);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, removedUserId, new[] { "Customer" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanAccessTicketAsync_NotParticipantNotCustomerNotStaff_ReturnsFalse()
    {
        var ticket = MakeTicket();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, Guid.NewGuid(), new[] { "Customer" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanAccessTicketAsync_TicketOwnerCustomer_ReturnsTrueWithoutParticipantRow()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, customerId, new[] { "Customer" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanAccessTicketAsync_TicketNotFound_ReturnsFalse()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(Guid.NewGuid(), Guid.NewGuid(), new[] { "Admin" });

        result.Should().BeFalse();
    }

    #endregion

    #region CanViewInternalChatsAsync

    [Fact]
    public async Task CanViewInternalChatsAsync_StaffRole_ReturnsTrue()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(Guid.NewGuid(), Guid.NewGuid(), new[] { "Staff" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanViewInternalChatsAsync_ParticipantWithCanViewInternalTrue_ReturnsTrue()
    {
        var ticket = MakeTicket();
        var previousAssigneeId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, previousAssigneeId, ParticipantTypeEnum.PreviousAssignee, canViewInternal: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(ticket.Id, previousAssigneeId, new[] { "Customer" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanViewInternalChatsAsync_ParticipantWithCanViewInternalFalse_ReturnsFalse()
    {
        var ticket = MakeTicket();
        var watcherId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, watcherId, ParticipantTypeEnum.Watcher, canViewInternal: false);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(ticket.Id, watcherId, new[] { "Customer" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanViewInternalChatsAsync_NotParticipantNotStaff_ReturnsFalse()
    {
        var ticket = MakeTicket();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(ticket.Id, Guid.NewGuid(), new[] { "Customer" });

        result.Should().BeFalse();
    }

    #endregion
}
