using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ChatReadersQueryHandlerTests
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
    public async Task Handle_ReturnsReadersOrderedByReadAt()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        var firstReader = Guid.NewGuid();
        var secondReader = Guid.NewGuid();

        var reads = new List<TicketChatRead>
        {
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = secondReader, UserRole = ActorRoleEnum.Manager, ReadAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = firstReader, UserRole = ActorRoleEnum.Staff, ReadAt = DateTime.UtcNow.AddMinutes(-5) },
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { chat.Ticket! });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chat.Id)).ReturnsAsync(chat);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReads(reads);

        var handler = new ChatReadersQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReadersQuery { TicketId = ticketId, ChatId = chat.Id, ActorUserId = Guid.NewGuid(), ActorRoles = new[] { "Admin" } }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Should().HaveCount(2);
        result.Data![0].UserId.Should().Be(firstReader.ToString());
        result.Data![1].UserId.Should().Be(secondReader.ToString());
    }

    [Fact]
    public async Task Handle_ResolvesDisplayName_FromStaffAndCustomerAccounts()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        var staffReader = Guid.NewGuid();
        var customerReader = Guid.NewGuid();
        var unknownReader = Guid.NewGuid();

        var reads = new List<TicketChatRead>
        {
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = staffReader, UserRole = ActorRoleEnum.Staff, ReadAt = DateTime.UtcNow.AddMinutes(-10) },
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = customerReader, UserRole = ActorRoleEnum.Customer, ReadAt = DateTime.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), ChatId = chat.Id, Chat = chat, UserId = unknownReader, UserRole = ActorRoleEnum.Staff, ReadAt = DateTime.UtcNow },
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { chat.Ticket! },
            customerSeed: new[] { new CustomerAccount { AccountId = customerReader, FullName = "Customer A", AvatarUrl = "customer.png" } },
            staffSeed: new[] { new StaffAccount { AccountId = staffReader, FullName = "Technician B", AvatarUrl = "staff.png" } });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chat.Id)).ReturnsAsync(chat);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReads(reads);

        var handler = new ChatReadersQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReadersQuery { TicketId = ticketId, ChatId = chat.Id, ActorUserId = Guid.NewGuid(), ActorRoles = new[] { "Admin" } }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Should().HaveCount(3);
        result.Data![0].DisplayName.Should().Be("Technician B");
        result.Data![0].AvatarUrl.Should().Be("staff.png");
        result.Data![1].DisplayName.Should().Be("Customer A");
        result.Data![1].AvatarUrl.Should().Be("customer.png");
        // Không tìm thấy account (đã xoá / khác service) → fallback về UserId.
        result.Data![2].DisplayName.Should().Be(unknownReader.ToString());
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
        uow.SetupReads(new List<TicketChatRead>());

        var handler = new ChatReadersQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReadersQuery { TicketId = ticketId, ChatId = chatId, ActorUserId = Guid.NewGuid(), ActorRoles = new[] { "Admin" } }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActorWithoutTicketAccess_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        // Ticket thuộc Customer khác, actor không phải Staff được assign — không có quan hệ gì với ticket này.
        chat.Ticket!.CustomerId = Guid.NewGuid();
        chat.Ticket!.PrimaryHandlerStaffId = Guid.NewGuid();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { chat.Ticket! });
        var chatsRepo = new Mock<IGenericRepository<TicketChat>>();
        chatsRepo.Setup(r => r.GetByIdAsync(chat.Id)).ReturnsAsync(chat);
        uow.SetupGet(u => u.TicketChats).Returns(chatsRepo.Object);
        uow.SetupReads(new List<TicketChatRead>());

        var handler = new ChatReadersQueryHandler(uow.Object);

        var result = await handler.Handle(new ChatReadersQuery
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = new[] { "Staff" }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
