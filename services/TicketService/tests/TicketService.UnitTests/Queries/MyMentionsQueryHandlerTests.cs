using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class MyMentionsQueryHandlerTests
{
    private static TicketChat MakeChat(Guid ticketId) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        Ticket = new Ticket { Id = ticketId, Code = "TKT-001", Title = "Test Ticket", Description = "Test Description" },
        AuthorUserId = Guid.NewGuid(),
        AuthorRole = ActorRoleEnum.Staff,
        Body = "Cần xác nhận"
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
    public async Task Handle_ReturnsOnlyMentionsOfActor()
    {
        var actorUserId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId);
        var myMention = MakeMention(chat, actorUserId);
        var otherMention = MakeMention(chat, Guid.NewGuid());

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        uow.SetupMentions(new List<TicketChatMention> { myMention, otherMention });

        var handler = new MyMentionsQueryHandler(uow.Object);

        var result = await handler.Handle(new MyMentionsQuery { ActorUserId = actorUserId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle();
        result.Data!.Items[0].MentionedUserId.Should().Be(actorUserId.ToString());
        result.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_UnreadOnly_FiltersAcknowledged()
    {
        var actorUserId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var chat = MakeChat(ticketId);
        var unread = MakeMention(chat, actorUserId, isAcknowledged: false);
        var read = MakeMention(chat, actorUserId, isAcknowledged: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        uow.SetupMentions(new List<TicketChatMention> { unread, read });

        var handler = new MyMentionsQueryHandler(uow.Object);

        var result = await handler.Handle(new MyMentionsQuery { ActorUserId = actorUserId, UnreadOnly = true }, CancellationToken.None);

        result.Data!.Items.Should().ContainSingle();
        result.Data!.Items[0].Id.Should().Be(unread.Id.ToString());
    }

    [Fact]
    public async Task Handle_NoMentions_ReturnsEmptyPage()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        uow.SetupMentions(new List<TicketChatMention>());

        var handler = new MyMentionsQueryHandler(uow.Object);

        var result = await handler.Handle(new MyMentionsQuery { ActorUserId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data!.TotalItems.Should().Be(0);
    }
}
