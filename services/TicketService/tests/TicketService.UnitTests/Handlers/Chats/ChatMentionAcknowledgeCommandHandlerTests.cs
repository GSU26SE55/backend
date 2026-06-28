using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatMentionAcknowledgeCommandHandlerTests
{
    private static TicketChat MakeChat(Guid ticketId, Guid id) => new()
    {
        Id = id,
        TicketId = ticketId,
        Ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" },
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Staff,
        Body = "Cần @StaffA xác nhận"
    };

    private static TicketChatMention MakeMention(TicketChat chat, Guid mentionedUserId, bool isAcknowledged = false) => new()
    {
        Id = Guid.NewGuid(),
        ChatId = chat.Id,
        Chat = chat,
        MentionedUserId = mentionedUserId,
        MentionedUserRole = ActorRoleEnum.Staff,
        MentionedDisplayName = "Staff A",
        IsAcknowledged = isAcknowledged
    };

    [Fact]
    public async Task Handle_ValidMention_MarksAcknowledged()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        var userId = Guid.NewGuid();
        var mention = MakeMention(chat, userId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention> { mention });

        var handler = new ChatMentionAcknowledgeCommandHandler(uow.Object);

        var command = new ChatMentionAcknowledgeCommand { MentionId = mention.Id, ActorUserId = userId };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.IsAcknowledged.Should().BeTrue();
        mention.IsAcknowledged.Should().BeTrue();
        mention.AcknowledgedAt.Should().NotBeNull();
        mentionsRepo.Verify(r => r.UpdateAsync(It.Is<TicketChatMention>(m => m.Id == mention.Id)), Times.Once);
    }

    [Fact]
    public async Task Handle_AlreadyAcknowledged_DoesNotUpdateAgain()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        var userId = Guid.NewGuid();
        var mention = MakeMention(chat, userId, isAcknowledged: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention> { mention });

        var handler = new ChatMentionAcknowledgeCommandHandler(uow.Object);

        var command = new ChatMentionAcknowledgeCommand { MentionId = mention.Id, ActorUserId = userId };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        mentionsRepo.Verify(r => r.UpdateAsync(It.IsAny<TicketChatMention>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbidden()
    {
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId, Guid.NewGuid());
        var mention = MakeMention(chat, Guid.NewGuid());

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var mentionsRepo = uow.SetupMentions(new List<TicketChatMention> { mention });

        var handler = new ChatMentionAcknowledgeCommandHandler(uow.Object);

        var command = new ChatMentionAcknowledgeCommand { MentionId = mention.Id, ActorUserId = Guid.NewGuid() };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        mentionsRepo.Verify(r => r.UpdateAsync(It.IsAny<TicketChatMention>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MentionNotFound_ReturnsNotFound()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        uow.SetupMentions(new List<TicketChatMention>());

        var handler = new ChatMentionAcknowledgeCommandHandler(uow.Object);

        var command = new ChatMentionAcknowledgeCommand { MentionId = Guid.NewGuid(), ActorUserId = Guid.NewGuid() };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Validate_EmptyMentionId_ReturnsError()
    {
        var command = new ChatMentionAcknowledgeCommand { MentionId = Guid.Empty, ActorUserId = Guid.NewGuid() };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "MentionId");
    }
}
