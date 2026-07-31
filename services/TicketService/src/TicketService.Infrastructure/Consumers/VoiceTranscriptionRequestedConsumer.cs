using Grpc.Core;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;
using SharedContracts.Grpc.FileInternal;
using SharedInfrastructure.Idempotency;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Consumers;

public sealed class VoiceTranscriptionRequestedConsumer : IConsumer<VoiceTranscriptionRequestedEvent>
{
    private const string PendingBody = "Audio đang được xử lý…";
    private readonly ITicketUnitOfWork _uow;
    private readonly IInboxStore _inbox;
    private readonly FileInternal.FileInternalClient _files;
    private readonly IVoiceTranscriptionService _voice;
    private readonly ITicketChatRealtimeNotifier _notifier;
    private readonly ILogger<VoiceTranscriptionRequestedConsumer> _logger;

    public VoiceTranscriptionRequestedConsumer(
        ITicketUnitOfWork uow,
        IInboxStore inbox,
        FileInternal.FileInternalClient files,
        IVoiceTranscriptionService voice,
        ITicketChatRealtimeNotifier notifier,
        ILogger<VoiceTranscriptionRequestedConsumer> logger)
        => (_uow, _inbox, _files, _voice, _notifier, _logger) = (uow, inbox, files, voice, notifier, logger);

    public async Task Consume(ConsumeContext<VoiceTranscriptionRequestedEvent> context)
    {
        if (!await _inbox.TryMarkProcessedAsync(context.MessageId.GetValueOrDefault(), nameof(VoiceTranscriptionRequestedConsumer), context.CancellationToken))
            return;

        var message = context.Message;
        var claimed = await _uow.TicketChats.GetAllAsync()
            .Where(x => x.Id == message.ChatId && !x.IsDeleted && x.VoiceTranscriptionStatus == VoiceTranscriptionStatusEnum.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.VoiceTranscriptionStatus, VoiceTranscriptionStatusEnum.Processing)
                .SetProperty(x => x.TranscriptionStartedAt, DateTime.UtcNow), context.CancellationToken);
        if (claimed != 1)
            return;

        var chat = await _uow.TicketChats.GetAllAsync().FirstAsync(x => x.Id == message.ChatId, context.CancellationToken);
        try
        {
            using var call = _files.DownloadForTranscription(
                new DownloadForTranscriptionRequest { FileId = message.FileId.ToString() },
                deadline: DateTime.UtcNow.AddSeconds(45),
                cancellationToken: context.CancellationToken);
            await using var audio = new MemoryStream();
            var contentType = "application/octet-stream";
            long expectedSize = 0;
            while (await call.ResponseStream.MoveNext(context.CancellationToken))
            {
                var chunk = call.ResponseStream.Current;
                if (!string.IsNullOrEmpty(chunk.ContentType))
                    contentType = chunk.ContentType;
                if (chunk.TotalSize > 0)
                    expectedSize = chunk.TotalSize;
                if (audio.Length + chunk.Chunk.Length > ChatVoiceTranscribeCommand.MaxAudioFileSizeDefault)
                    throw new RpcException(new Status(StatusCode.ResourceExhausted, "Audio exceeds 20 MB."));
                chunk.Chunk.WriteTo(audio);
            }

            if (audio.Length == 0 || (expectedSize > 0 && audio.Length != expectedSize))
                throw new InvalidOperationException("Downloaded audio is incomplete.");
            audio.Position = 0;
            var transcript = await _voice.TranscribeAsync(audio, contentType, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(transcript))
                throw new InvalidOperationException("Gemini returned an empty transcript.");

            chat.Body = transcript.Trim();
            chat.VoiceTranscriptionStatus = VoiceTranscriptionStatusEnum.Completed;
            chat.VoiceTranscriptionError = null;
            chat.TranscribedAt = DateTime.UtcNow;
            await PersistAndNotifyAsync(chat, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice transcription failed for chat {ChatId}", chat.Id);
            chat.Body = PendingBody;
            chat.VoiceTranscriptionStatus = VoiceTranscriptionStatusEnum.Failed;
            chat.VoiceTranscriptionError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await PersistAndNotifyAsync(chat, context.CancellationToken);
        }
    }

    private async Task PersistAndNotifyAsync(TicketChat chat, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync();
        try
        {
            _uow.TicketChats.UpdateAsync(chat);
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        try
        {
            await _notifier.NotifyChatEditedAsync(new TicketChatDTO
            {
                Id = chat.Id.ToString(),
                TicketId = chat.TicketId.ToString(),
                AuthorUserId = chat.AuthorUserId.ToString(),
                AuthorRole = chat.AuthorRole,
                AuthorDisplayName = chat.AuthorDisplayName,
                Body = chat.Body,
                IsInternal = chat.IsInternal,
                AttachmentFileIds = chat.AttachmentFileIds.Select(x => x.ToString()).ToList(),
                CreatedAt = chat.CreatedAt,
                BodyFormat = chat.BodyFormat,
                VoiceTranscriptionStatus = chat.VoiceTranscriptionStatus,
                VoiceTranscriptionError = chat.VoiceTranscriptionError,
                TranscribedAt = chat.TranscribedAt
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice transcription SignalR update failed for chat {ChatId}", chat.Id);
        }
    }
}
