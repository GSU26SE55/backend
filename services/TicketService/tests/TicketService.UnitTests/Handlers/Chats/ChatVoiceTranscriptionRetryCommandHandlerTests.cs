using Moq;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public sealed class ChatVoiceTranscriptionRetryCommandHandlerTests
{
    [Fact]
    public async Task Handle_FailedVoiceChat_ResetsPendingWritesOutboxAndCommits()
    {
        var ticket = Ticket();
        var chat = Chat(ticket, VoiceTranscriptionStatusEnum.Failed);
        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, chatSeed: new[] { chat });
        var outbox = Outbox();

        var result = await Handler(uow.Object, outbox.Object).Handle(Command(ticket.Id, chat.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);
        Assert.Equal(VoiceTranscriptionStatusEnum.Pending, chat.VoiceTranscriptionStatus);
        Assert.Equal("Audio đang được xử lý…", chat.Body);
        outbox.Verify(x => x.WriteAsync(It.Is<VoiceTranscriptionRequestedEvent>(e => e.ChatId == chat.Id && e.FileId == chat.AttachmentFileIds[0]), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_NotFailedVoiceChat_DoesNotCommit()
    {
        var ticket = Ticket();
        var chat = Chat(ticket, VoiceTranscriptionStatusEnum.Completed);
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, chatSeed: new[] { chat });

        var result = await Handler(uow.Object, Outbox().Object).Handle(Command(ticket.Id, chat.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Never);
    }

    [Fact]
    public async Task Handle_OutboxFailure_RollsBack()
    {
        var ticket = Ticket();
        var chat = Chat(ticket, VoiceTranscriptionStatusEnum.Failed);
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, chatSeed: new[] { chat });
        var outbox = Outbox();
        outbox.Setup(x => x.WriteAsync(It.IsAny<VoiceTranscriptionRequestedEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("outbox"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(uow.Object, outbox.Object).Handle(Command(ticket.Id, chat.Id), CancellationToken.None));
        uow.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_SoftDeletedChat_IsNotRetried()
    {
        var ticket = Ticket();
        var chat = Chat(ticket, VoiceTranscriptionStatusEnum.Failed);
        chat.IsDeleted = true;
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket }, chatSeed: new[] { chat });

        var result = await Handler(uow.Object, Outbox().Object).Handle(Command(ticket.Id, chat.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Never);
    }

    private static ChatVoiceTranscriptionRetryCommandHandler Handler(TicketService.Application.Interfaces.Repositories.ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outbox)
    {
        var authorization = new Mock<IChatAuthorizationService>();
        authorization.Setup(x => x.CanAccessTicketAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return new ChatVoiceTranscriptionRetryCommandHandler(uow, authorization.Object, outbox);
    }

    private static Mock<IIntegrationEventOutboxWriter> Outbox()
    {
        var mock = new Mock<IIntegrationEventOutboxWriter>();
        mock.Setup(x => x.WriteAsync(It.IsAny<VoiceTranscriptionRequestedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock;
    }

    private static ChatVoiceTranscriptionRetryCommand Command(Guid ticketId, Guid chatId) => new() { TicketId = ticketId, ChatId = chatId, UserId = Guid.NewGuid(), UserRole = ActorRoleEnum.Staff };
    private static Ticket Ticket() => new() { Id = Guid.NewGuid(), Code = "TKT-RETRY", Title = "Test", Description = "Test", Status = TicketStatusEnum.Open, Priority = TicketPriorityEnum.P3Normal, ImpactScope = ImpactScopeEnum.SingleAsset, UrgencyLevel = UrgencyLevelEnum.Low };
    private static TicketChat Chat(Ticket ticket, VoiceTranscriptionStatusEnum status) => new() { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, AuthorUserId = Guid.NewGuid(), AuthorRole = ActorRoleEnum.Staff, Body = "failed", AttachmentFileIds = new List<Guid> { Guid.NewGuid() }, VoiceTranscriptionStatus = status };
}
