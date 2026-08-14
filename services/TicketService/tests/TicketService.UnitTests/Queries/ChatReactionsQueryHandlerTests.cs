using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ChatReactionsQueryHandlerTests
{
    private static TicketChat MakeChat(Guid ticketId, Guid id) => new()
    {
        Id = id,
        TicketId = ticketId,
        Ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" },
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Battery charges very slowly"
    };

    [Fact]
    public async Task Handle_AggregatesReactionsByType()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());

        var reactions = new List<TicketChatReaction>
        {
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Staff, ReactionType = ReactionTypeEnum.ThumbsUp },
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Staff, ReactionType = ReactionTypeEnum.ThumbsUp },
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Manager, ReactionType = ReactionTypeEnum.Resolved },
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { chat.Ticket! });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chat.Id)).ReturnsAsync(chat);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReactions(reactions);

        var handler = new ChatReactionsQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReactionsQuery { TicketId = ticketId, ChatId = chat.Id, ActorUserId = Guid.NewGuid(), ActorRoles = new[] { "Admin" } }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ThumbsUp.Count.Should().Be(2);
        result.Data!.Resolved.Count.Should().Be(1);
        result.Data!.Acknowledged.Count.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsNotFound()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((TicketChat?)null);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReactions(new List<TicketChatReaction>());

        var handler = new ChatReactionsQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReactionsQuery { TicketId = ticketId, ChatId = chatId, ActorUserId = Guid.NewGuid(), ActorRoles = new[] { "Admin" } }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActorWithoutTicketAccess_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        chat.Ticket!.CustomerId = Guid.NewGuid();
        chat.Ticket!.PrimaryHandlerStaffId = Guid.NewGuid();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { chat.Ticket! });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chat.Id)).ReturnsAsync(chat);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReactions(new List<TicketChatReaction>());

        var handler = new ChatReactionsQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReactionsQuery
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = new[] { "Customer" }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
