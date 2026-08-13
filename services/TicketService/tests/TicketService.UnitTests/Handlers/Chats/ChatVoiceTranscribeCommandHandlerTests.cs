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

/// <summary>Contract tests for the asynchronous, metadata-only voice request.</summary>
public class ChatVoiceTranscribeCommandHandlerTests
{
    [Fact]
    public async Task ValidateAsync_ValidUploadedAudioMetadata_Succeeds()
    {
        var result = await ValidCommand().ValidateAsync();
        Assert.True(result.IsSuccess);
        Assert.Empty(result.ListErrors);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMissingFileId()
    {
        var command = ValidCommand();
        command.FileId = Guid.Empty;
        var result = await command.ValidateAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains(result.ListErrors, error => error.Field == nameof(command.FileId));
    }

    [Fact]
    public async Task ValidateAsync_RejectsInvalidMimeTypeAndOversizedAudio()
    {
        var command = ValidCommand();
        command.ContentType = "application/pdf";
        command.SizeBytes = ChatVoiceTranscribeCommand.MaxAudioFileSizeDefault + 1;
        var result = await command.ValidateAsync();
        Assert.False(result.IsSuccess);
        Assert.Contains(result.ListErrors, error => error.Field == nameof(command.ContentType));
        Assert.Contains(result.ListErrors, error => error.Field == nameof(command.SizeBytes));
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesPlaceholderWritesOutboxAndCommits()
    {
        var ticket = Ticket();
        var (uow, _, _, _, _, _, _, chats, attachments, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        var authorization = Authorized();
        var outbox = Outbox();

        var result = await new ChatVoiceTranscribeCommandHandler(uow.Object, authorization.Object, outbox.Object)
            .Handle(Request(ticket.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(202, result.StatusCode);
        chats.Verify(x => x.AddAsync(It.Is<TicketChat>(c => c.Body == "Audio is being processed…" && c.VoiceTranscriptionStatus == VoiceTranscriptionStatusEnum.Pending)), Times.Once);
        attachments.Verify(x => x.AddAsync(It.Is<TicketAttachment>(a => a.FileId != Guid.Empty && a.Url == "https://storage.example/voice.webm")), Times.Once);
        outbox.Verify(x => x.WriteAsync(It.IsAny<VoiceTranscriptionRequestedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_InvalidRequest_IsRejectedBeforeHandler()
    {
        var request = Request(Ticket().Id);
        request.SizeBytes = 0;

        var result = await request.ValidateAsync();

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_DatabaseError_RollsBackTransaction()
    {
        var ticket = Ticket();
        var (uow, _, _, _, _, _, _, chats, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });
        chats.Setup(x => x.AddAsync(It.IsAny<TicketChat>())).ThrowsAsync(new InvalidOperationException("db"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChatVoiceTranscribeCommandHandler(uow.Object, Authorized().Object, Outbox().Object).Handle(Request(ticket.Id), CancellationToken.None));
        uow.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_SoftDeletedTicket_IsExcludedAndDoesNotCommit()
    {
        var ticket = Ticket();
        ticket.IsDeleted = true;
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var result = await new ChatVoiceTranscribeCommandHandler(uow.Object, Authorized().Object, Outbox().Object).Handle(Request(ticket.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        uow.Verify(x => x.CommitTransactionAsync(), Times.Never);
    }

    private static ChatVoiceTranscribeCommand ValidCommand() => new()
    {
        FileId = Guid.NewGuid(),
        FileName = "voice.webm",
        ContentType = "audio/webm",
        SizeBytes = 1024,
        Url = "https://storage.example/voice.webm"
    };

    private static ChatVoiceTranscribeCommand Request(Guid ticketId) => new()
    {
        TicketId = ticketId,
        UserId = Guid.NewGuid(),
        UserRole = ActorRoleEnum.Staff,
        UserDisplayName = "Staff",
        FileId = Guid.NewGuid(),
        FileName = "voice.webm",
        ContentType = "audio/webm",
        SizeBytes = 1024,
        Url = "https://storage.example/voice.webm"
    };

    private static Ticket Ticket() => new() { Id = Guid.NewGuid(), Code = "TKT-TEST", Title = "Test", Description = "Test", Priority = TicketPriorityEnum.P3Normal, ImpactScope = ImpactScopeEnum.SingleAsset, UrgencyLevel = UrgencyLevelEnum.Low, Status = TicketStatusEnum.Open };

    private static Mock<IChatAuthorizationService> Authorized()
    {
        var mock = new Mock<IChatAuthorizationService>();
        mock.Setup(x => x.CanAccessTicketAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return mock;
    }

    private static Mock<IIntegrationEventOutboxWriter> Outbox()
    {
        var mock = new Mock<IIntegrationEventOutboxWriter>();
        mock.Setup(x => x.WriteAsync(It.IsAny<VoiceTranscriptionRequestedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock;
    }
}
