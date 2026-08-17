using Microsoft.Extensions.Logging;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatMarkAsReadCommandHandlerTests
{
    private static TicketChat MakeChat(Guid ticketId, Guid id) => new()
    {
        Id = id,
        TicketId = ticketId,
        Ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" },
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Customer,
        Body = "Pin sạc rất chậm"
    };

    private static Mock<IChatReadReceiptQueue> MakeQueue(List<ChatReadReceiptItem> captured)
    {
        var queue = new Mock<IChatReadReceiptQueue>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<ChatReadReceiptItem>()))
            .Returns((ChatReadReceiptItem item) => { captured.Add(item); return true; });
        return queue;
    }

    /// <summary>Hàng đợi đầy — TryEnqueue luôn trả false, không nhận thêm item nào.</summary>
    private static Mock<IChatReadReceiptQueue> MakeFullQueue()
    {
        var queue = new Mock<IChatReadReceiptQueue>();
        queue.Setup(q => q.TryEnqueue(It.IsAny<ChatReadReceiptItem>())).Returns(false);
        return queue;
    }

    private static Mock<ILogger<ChatMarkAsReadCommandHandler>> MakeLogger() => new();

    [Fact]
    public async Task Handle_EmptyChatIds_ReturnsZeroWithoutQuery()
    {
        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var captured = new List<ChatReadReceiptItem>();
        var queue = MakeQueue(captured);

        var handler = new ChatMarkAsReadCommandHandler(uow.Object, queue.Object, MakeLogger().Object);

        var command = new ChatMarkAsReadCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserRole = ActorRoleEnum.Staff,
            ChatIds = new List<Guid>()
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(0);
        captured.Should().BeEmpty();
        queue.Verify(q => q.TryEnqueue(It.IsAny<ChatReadReceiptItem>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AllChatsAlreadyRead_EnqueuesNothing()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chat1 = MakeChat(ticketId, Guid.NewGuid());
        var chat2 = MakeChat(ticketId, Guid.NewGuid());

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { chat1, chat2 });

        var reads = new List<TicketChatRead>
        {
            new() { Id = Guid.NewGuid(), ChatId = chat1.Id, Chat = chat1, UserId = userId, UserRole = ActorRoleEnum.Staff, ReadAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ChatId = chat2.Id, Chat = chat2, UserId = userId, UserRole = ActorRoleEnum.Staff, ReadAt = DateTime.UtcNow },
        };
        uow.SetupReads(reads);

        var captured = new List<ChatReadReceiptItem>();
        var queue = MakeQueue(captured);

        var handler = new ChatMarkAsReadCommandHandler(uow.Object, queue.Object, MakeLogger().Object);

        var command = new ChatMarkAsReadCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ChatIds = new List<Guid> { chat1.Id, chat2.Id }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(0);
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MixedNewAndOldChats_EnqueuesOnlyNewOnes()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var readChat = MakeChat(ticketId, Guid.NewGuid());
        var unreadChat = MakeChat(ticketId, Guid.NewGuid());

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { readChat, unreadChat });

        var reads = new List<TicketChatRead>
        {
            new() { Id = Guid.NewGuid(), ChatId = readChat.Id, Chat = readChat, UserId = userId, UserRole = ActorRoleEnum.Staff, ReadAt = DateTime.UtcNow },
        };
        uow.SetupReads(reads);

        var captured = new List<ChatReadReceiptItem>();
        var queue = MakeQueue(captured);

        var handler = new ChatMarkAsReadCommandHandler(uow.Object, queue.Object, MakeLogger().Object);

        var command = new ChatMarkAsReadCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ChatIds = new List<Guid> { readChat.Id, unreadChat.Id }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(1);
        captured.Should().ContainSingle(i => i.ChatId == unreadChat.Id);
    }

    [Fact]
    public async Task Handle_ChatIdsBelongingToOtherTicket_AreFilteredOut()
    {
        var ticketId = Guid.NewGuid();
        var otherTicketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chatOnOtherTicket = MakeChat(otherTicketId, Guid.NewGuid());

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { chatOnOtherTicket });
        uow.SetupReads(new List<TicketChatRead>());

        var captured = new List<ChatReadReceiptItem>();
        var queue = MakeQueue(captured);

        var handler = new ChatMarkAsReadCommandHandler(uow.Object, queue.Object, MakeLogger().Object);

        var command = new ChatMarkAsReadCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ChatIds = new List<Guid> { chatOnOtherTicket.Id }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(0);
        captured.Should().BeEmpty();
    }

    /// <summary>
    /// Tin do chính actor gửi không bao giờ là chưa đọc — các query đếm unread đều bỏ qua chúng,
    /// nên ghi receipt cho chúng chỉ tốn slot hàng đợi và đẩy receipt của người khác ra ngoài.
    /// </summary>
    [Fact]
    public async Task Handle_ChatsAuthoredByActor_AreFilteredOut()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var ownChat = MakeChat(ticketId, Guid.NewGuid());
        ownChat.AuthorUserId = userId;
        var otherChat = MakeChat(ticketId, Guid.NewGuid());

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { ownChat, otherChat });
        uow.SetupReads(new List<TicketChatRead>());

        var captured = new List<ChatReadReceiptItem>();
        var queue = MakeQueue(captured);

        var handler = new ChatMarkAsReadCommandHandler(uow.Object, queue.Object, MakeLogger().Object);

        var command = new ChatMarkAsReadCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ChatIds = new List<Guid> { ownChat.Id, otherChat.Id }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(1);
        captured.Should().ContainSingle(i => i.ChatId == otherChat.Id);
    }

    /// <summary>
    /// Hàng đợi đầy thì receipt bị rớt và không có retry nội bộ — trả 200 sẽ khiến client tưởng
    /// đã đọc xong và không gửi lại, làm unread kẹt vĩnh viễn. Phải báo thất bại để client retry.
    /// </summary>
    [Fact]
    public async Task Handle_QueueFull_ReportsFailureSoClientRetries()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());

        var ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" };
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, chatSeed: new[] { chat });
        uow.SetupReads(new List<TicketChatRead>());

        var queue = MakeFullQueue();

        var handler = new ChatMarkAsReadCommandHandler(uow.Object, queue.Object, MakeLogger().Object);

        var command = new ChatMarkAsReadCommand
        {
            TicketId = ticketId,
            UserId = userId,
            UserRole = ActorRoleEnum.Staff,
            ActorRoles = new[] { "Admin" },
            ChatIds = new List<Guid> { chat.Id }
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(503);
        result.Data.Should().Be(0);
    }
}
