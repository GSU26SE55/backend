using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatVoiceTranscriptionRetryCommandHandler : IRequestHandler<ChatVoiceTranscriptionRetryCommand, TicketActionResponse>
{
    private const string PENDINGBODY = "Audio is being processed…";
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatAuthorizationService _authorization;
    private readonly IIntegrationEventOutboxWriter _outbox;
    public ChatVoiceTranscriptionRetryCommandHandler(ITicketUnitOfWork uow, IChatAuthorizationService authorization, IIntegrationEventOutboxWriter outbox)
        => (_uow, _authorization, _outbox) = (uow, authorization, outbox);

    public async Task<TicketActionResponse> Handle(ChatVoiceTranscriptionRetryCommand request, CancellationToken ct)
    {
        if (!await _authorization.CanAccessTicketAsync(request.TicketId, request.UserId, new[] { request.UserRole.ToString() }, ct))
            return Fail(403, "You do not have permission to access this ticket.");
        var chat = await _uow.TicketChats.GetAllAsync().FirstOrDefaultAsync(c => c.Id == request.ChatId && c.TicketId == request.TicketId && !c.IsDeleted, ct);
        if (chat is null)
            return Fail(404, "Audio chat not found.");
        if (chat.VoiceTranscriptionStatus != VoiceTranscriptionStatusEnum.Failed)
            return Fail(409, "Can only retry a failed transcription.");
        var fileId = chat.AttachmentFileIds.FirstOrDefault();
        if (fileId == Guid.Empty)
            return Fail(409, "Audio chat has no attached file.");

        chat.Body = PENDINGBODY;
        chat.VoiceTranscriptionStatus = VoiceTranscriptionStatusEnum.Pending;
        chat.VoiceTranscriptionError = null;
        chat.TranscriptionStartedAt = null;
        chat.TranscribedAt = null;
        await _uow.BeginTransactionAsync();
        try
        {
            _uow.TicketChats.UpdateAsync(chat);
            await _outbox.WriteAsync(new VoiceTranscriptionRequestedEvent(chat.Id, chat.TicketId, fileId), ct);
            await _uow.CommitTransactionAsync();
        }
        catch { await _uow.RollbackTransactionAsync(); throw; }
        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = "Audio is being reprocessed.",
            Data = new TicketActionDTO { Id = chat.Id.ToString(), TicketId = chat.TicketId.ToString() }
        };
    }
    private static TicketActionResponse Fail(int statusCode, string message) => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
