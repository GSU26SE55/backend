using MediatR;
using Microsoft.Extensions.Logging;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatVoiceTranscribeCommandHandler : IRequestHandler<ChatVoiceTranscribeCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IVoiceTranscriptionService _voiceService;
    private readonly IFileUploadClient _fileUploadClient;
    private readonly IAudioTranscoder _audioTranscoder;
    private readonly ILogger<ChatVoiceTranscribeCommandHandler> _logger;

    public ChatVoiceTranscribeCommandHandler(
        ITicketUnitOfWork uow,
        IVoiceTranscriptionService voiceService,
        IFileUploadClient fileUploadClient,
        IAudioTranscoder audioTranscoder,
        ILogger<ChatVoiceTranscribeCommandHandler> logger)
    {
        _uow = uow;
        _voiceService = voiceService;
        _fileUploadClient = fileUploadClient;
        _audioTranscoder = audioTranscoder;
        _logger = logger;
    }

    public async Task<TicketActionResponse> Handle(ChatVoiceTranscribeCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket is null || ticket.IsDeleted)
            return Fail(404, "Không tìm thấy Ticket.");

        var audioFile = request.AudioFile!;

        // Read audio into byte array first so two separate streams can be created for parallel tasks
        using var rawMs = new MemoryStream();
        await audioFile.CopyToAsync(rawMs, ct);
        var audioBytes = rawMs.ToArray();

        // Transcribe dùng bytes GỐC — Gemini nhận webm/opus tốt, không cần transcode cho AI.
        var transcribeTask = _voiceService.TranscribeAsync(
            new MemoryStream(audioBytes, writable: false), audioFile.ContentType, ct);

        // Chuẩn hóa file LƯU về m4a (AAC) để iOS phát được — web ghi .webm mà iOS không decode.
        // Transcode lỗi (thiếu ffmpeg / file hỏng) → fallback giữ file gốc: không chặn gửi voice.
        var uploadBytes = audioBytes;
        var uploadContentType = audioFile.ContentType;
        var uploadFileName = audioFile.FileName;
        try
        {
            (uploadBytes, uploadContentType, uploadFileName) = await _audioTranscoder.ToM4aAsync(
                audioBytes, audioFile.ContentType, audioFile.FileName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ChatVoiceTranscribe] Transcode → m4a thất bại cho ticket {TicketId}, dùng file gốc ({ContentType}).",
                request.TicketId, audioFile.ContentType);
        }

        var uploadTask = _fileUploadClient.UploadAsync(
            new MemoryStream(uploadBytes, writable: false),
            uploadFileName, uploadContentType, uploadBytes.Length,
            request.AuthorizationHeader, ct);

        string transcribedText;
        Guid fileId;
        string downloadUrl;

        try
        {
            await Task.WhenAll(transcribeTask, uploadTask);
            transcribedText = await transcribeTask;
            (fileId, downloadUrl) = await uploadTask;
        }
        catch (InvalidOperationException ex) when (ex.Message == "RATE_LIMITED")
        {
            _logger.LogWarning("[ChatVoiceTranscribe] Gemini rate limit hit for ticket {TicketId}", request.TicketId);
            return Fail(429, "AI service đang bận, vui lòng thử lại sau ít giây.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatVoiceTranscribe] Failed for ticket {TicketId}", request.TicketId);
            throw;
        }

        if (string.IsNullOrWhiteSpace(transcribedText))
            return Fail(422, "Voice transcription returned empty result.");

        var source = request.UserRole == ActorRoleEnum.Customer
            ? AttachmentSourceEnum.CustomerSubmission
            : AttachmentSourceEnum.StaffWork;

        await _uow.BeginTransactionAsync();
        try
        {
            var chat = new TicketChat
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                AuthorUserId = request.UserId,
                AuthorRole = request.UserRole,
                AuthorDisplayName = request.UserDisplayName,
                Body = transcribedText,
                BodyFormat = ChatBodyFormatEnum.PlainText,
                IsInternal = false,
                AttachmentFileIds = new List<Guid> { fileId },
                Ticket = ticket
            };
            await _uow.TicketChats.AddAsync(chat);

            var attachment = new TicketAttachment
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ChatId = chat.Id,
                UploadedByUserId = request.UserId,
                FileId = fileId,
                FileName = uploadFileName,
                ContentType = uploadContentType,
                SizeBytes = uploadBytes.Length,
                Source = source,
                VirusScanStatus = VirusScanStatusEnum.Pending,
                Ticket = ticket,
                Chat = chat
            };
            await _uow.TicketAttachments.AddAsync(attachment);

            await _uow.CommitTransactionAsync();

            return new TicketActionResponse
            {
                IsSuccess = true,
                StatusCode = 201,
                Message = "Voice transcription thành công.",
                Data = new TicketActionDTO
                {
                    Id = chat.Id.ToString(),
                    TicketId = ticket.Id.ToString(),
                    Code = ticket.Code,
                    Status = ticket.Status
                }
            };
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    private static TicketActionResponse Fail(int statusCode, string message)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
