using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatVoiceTranscribeCommandHandlerTests
{
    private readonly Mock<IVoiceTranscriptionService> _voiceService = new();
    private readonly Mock<IFileUploadClient> _fileUploadClient = new();
    private readonly Mock<ILogger<ChatVoiceTranscribeCommandHandler>> _logger = new();

    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private static Ticket BuildTicket() => new()
    {
        Id = TicketId,
        Code = "TKT-001",
        Status = TicketStatusEnum.Open,
        Title = "Test ticket",
        Description = "desc",
        Priority = TicketPriorityEnum.P3Normal,
        ImpactScope = ImpactScopeEnum.SingleAsset,
        UrgencyLevel = UrgencyLevelEnum.Low
    };

    private static Mock<IFormFile> BuildAudioFile(
        string contentType = "audio/mpeg",
        string fileName = "voice.mp3",
        long size = 1024)
    {
        var file = new Mock<IFormFile>();
        file.Setup(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.FileName).Returns(fileName);
        file.Setup(f => f.Length).Returns(size);
        file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((s, _) =>
            {
                var bytes = new byte[(int)size];
                s.Write(bytes, 0, bytes.Length);
            })
            .Returns(Task.CompletedTask);
        return file;
    }

    private ChatVoiceTranscribeCommandHandler BuildHandler(ITicketUnitOfWork uow)
        => new(uow, _voiceService.Object, _fileUploadClient.Object, _logger.Object);

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidAudio_Returns201WithChatId()
    {
        var ticket = BuildTicket();
        var (uow, _, _, _, _, _, _, chats, attachments, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        _voiceService
            .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), "audio/mpeg", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Xin chào, đây là tin nhắn giọng nói.");

        _fileUploadClient
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileId, "http://storage/voice.mp3"));

        var command = new ChatVoiceTranscribeCommand
        {
            TicketId = TicketId,
            UserId = UserId,
            UserRole = ActorRoleEnum.Staff,
            UserDisplayName = "Nguyễn Văn A",
            AudioFile = BuildAudioFile().Object
        };

        var handler = BuildHandler(uow.Object);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.NotNull(result.Data?.Id);
        Assert.Equal(ticket.Code, result.Data?.Code);

        chats.Verify(r => r.AddAsync(It.Is<TicketChat>(c =>
            c.Body == "Xin chào, đây là tin nhắn giọng nói." &&
            c.BodyFormat == ChatBodyFormatEnum.PlainText &&
            c.AuthorUserId == UserId)), Times.Once);

        attachments.Verify(r => r.AddAsync(It.Is<TicketAttachment>(a =>
            a.FileId == FileId &&
            a.Source == AttachmentSourceEnum.StaffWork)), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerRole_SetsAttachmentSourceToCustomerSubmission()
    {
        var ticket = BuildTicket();
        var (uow, _, _, _, _, _, _, _, attachments, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        _voiceService
            .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Customer message");

        _fileUploadClient
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileId, ""));

        var command = new ChatVoiceTranscribeCommand
        {
            TicketId = TicketId,
            UserId = UserId,
            UserRole = ActorRoleEnum.Customer,
            AudioFile = BuildAudioFile().Object
        };

        var handler = BuildHandler(uow.Object);
        await handler.Handle(command, default);

        attachments.Verify(r => r.AddAsync(It.Is<TicketAttachment>(a =>
            a.Source == AttachmentSourceEnum.CustomerSubmission)), Times.Once);
    }

    // ── Ticket not found ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(); // empty seed

        var command = new ChatVoiceTranscribeCommand
        {
            TicketId = Guid.NewGuid(),
            AudioFile = BuildAudioFile().Object
        };

        var handler = BuildHandler(uow.Object);
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        _voiceService.Verify(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DeletedTicket_Returns404()
    {
        var ticket = BuildTicket();
        ticket.IsDeleted = true;
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var command = new ChatVoiceTranscribeCommand
        {
            TicketId = TicketId,
            AudioFile = BuildAudioFile().Object
        };

        var handler = BuildHandler(uow.Object);
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    // ── Empty transcription ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_EmptyTranscription_Returns422()
    {
        var ticket = BuildTicket();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) =
            MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        _voiceService
            .Setup(s => s.TranscribeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        _fileUploadClient
            .Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileId, ""));

        var command = new ChatVoiceTranscribeCommand
        {
            TicketId = TicketId,
            AudioFile = BuildAudioFile().Object
        };

        var handler = BuildHandler(uow.Object);
        var result = await handler.Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    // ── Validation (ValidateAsync) ───────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_NullAudioFile_ReturnsError()
    {
        var command = new ChatVoiceTranscribeCommand { AudioFile = null };
        var result = await command.ValidateAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains(result.ListErrors, e => e.Field == "audioFile");
    }

    [Fact]
    public async Task ValidateAsync_InvalidMimeType_ReturnsError()
    {
        var command = new ChatVoiceTranscribeCommand
        {
            AudioFile = BuildAudioFile(contentType: "video/mp4").Object
        };
        var result = await command.ValidateAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains(result.ListErrors, e => e.Field == "audioFile" && e.Detail.Contains("Định dạng audio"));
    }

    [Fact]
    public async Task ValidateAsync_FileTooLarge_ReturnsError()
    {
        var command = new ChatVoiceTranscribeCommand
        {
            AudioFile = BuildAudioFile(size: ChatVoiceTranscribeCommand.MaxAudioFileSizeDefault + 1).Object
        };
        var result = await command.ValidateAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains(result.ListErrors, e => e.Field == "audioFile" && e.Detail.Contains("20 MB"));
    }

    [Fact]
    public async Task ValidateAsync_ValidAudio_ReturnsSuccess()
    {
        var command = new ChatVoiceTranscribeCommand
        {
            AudioFile = BuildAudioFile(contentType: "audio/wav", size: 512).Object
        };
        var result = await command.ValidateAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.ListErrors);
    }
}
