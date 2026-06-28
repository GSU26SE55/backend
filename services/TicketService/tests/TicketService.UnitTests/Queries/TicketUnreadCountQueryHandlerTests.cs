using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Queries;

public class TicketUnreadCountQueryHandlerTests
{
    private static Ticket MakeTicket(Guid id, Guid customerId, Guid? assignedStaffId) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Test Ticket",
        Description = "Test Description",
        CustomerId = customerId,
        AssignedStaffId = assignedStaffId
    };

    private static TicketChat MakeChat(Guid ticketId, Guid authorId, bool isInternal = false) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        Ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" },
        AuthorUserId = authorId,
        AuthorRole = ActorRoleEnum.Staff,
        Body = "Nội dung chat",
        IsInternal = isInternal
    };

    [Fact]
    public async Task Handle_CountsUnreadChatsExcludingOwnAndRead()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId, Guid.NewGuid());

        var ownChat = MakeChat(ticketId, customerId); // chat của chính actor — KHÔNG tính
        var readChat = MakeChat(ticketId, Guid.NewGuid()); // đã đọc — KHÔNG tính
        var unreadChat = MakeChat(ticketId, Guid.NewGuid()); // chưa đọc — tính

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { ownChat, readChat, unreadChat });

        uow.SetupReads(new List<TicketChatRead>
        {
            new() { Id = Guid.NewGuid(), ChatId = readChat.Id, Chat = readChat, UserId = customerId, UserRole = ActorRoleEnum.Customer, ReadAt = DateTime.UtcNow }
        });

        var handler = new TicketUnreadCountQueryHandler(uow.Object);

        var query = new TicketUnreadCountQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = new[] { "Customer" }
        };

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CustomerCannotSeeInternalChats()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId, Guid.NewGuid());

        var internalChat = MakeChat(ticketId, Guid.NewGuid(), isInternal: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { internalChat });
        uow.SetupReads(new List<TicketChatRead>());

        var handler = new TicketUnreadCountQueryHandler(uow.Object);

        var query = new TicketUnreadCountQuery
        {
            TicketId = ticketId,
            ActorUserId = customerId,
            ActorRoles = new[] { "Customer" }
        };

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(0);
    }

    [Fact]
    public async Task Handle_StaffCanSeeInternalChats()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId, staffId);

        var internalChat = MakeChat(ticketId, Guid.NewGuid(), isInternal: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            chatSeed: new[] { internalChat });
        uow.SetupReads(new List<TicketChatRead>());

        var handler = new TicketUnreadCountQueryHandler(uow.Object);

        var query = new TicketUnreadCountQuery
        {
            TicketId = ticketId,
            ActorUserId = staffId,
            ActorRoles = new[] { "Staff" }
        };

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NoAccess_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, Guid.NewGuid(), Guid.NewGuid());

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket });
        uow.SetupReads(new List<TicketChatRead>());

        var handler = new TicketUnreadCountQueryHandler(uow.Object);

        var query = new TicketUnreadCountQuery
        {
            TicketId = ticketId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = new[] { "Customer" }
        };

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsNotFound()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        uow.SetupReads(new List<TicketChatRead>());

        var handler = new TicketUnreadCountQueryHandler(uow.Object);

        var query = new TicketUnreadCountQuery
        {
            TicketId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = new[] { "Customer" }
        };

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
