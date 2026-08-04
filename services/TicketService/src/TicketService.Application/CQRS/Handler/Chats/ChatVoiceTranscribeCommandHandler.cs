using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public sealed class ChatVoiceTranscribeCommandHandler : IRequestHandler<ChatVoiceTranscribeCommand, TicketActionResponse>
{
    private const string PENDINGBODY = "Audio đang được xử lý…";
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatAuthorizationService _authorization;
    private readonly IIntegrationEventOutboxWriter _outbox;

    public ChatVoiceTranscribeCommandHandler(ITicketUnitOfWork uow, IChatAuthorizationService authorization, IIntegrationEventOutboxWriter outbox)
        => (_uow, _authorization, _outbox) = (uow, authorization, outbox);

    public async Task<TicketActionResponse> Handle(ChatVoiceTranscribeCommand request, CancellationToken ct)
    {
        var roles = new[] { request.UserRole.ToString() };
        if (!await _authorization.CanAccessTicketAsync(request.TicketId, request.UserId, roles, ct))
            return Fail(403, "Không có quyền truy cập ticket này.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket is null || ticket.IsDeleted)
            return Fail(404, "Không tìm thấy Ticket.");

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Ticket = ticket,
            AuthorUserId = request.UserId,
            AuthorRole = request.UserRole,
            AuthorDisplayName = request.UserDisplayName,
            Body = PENDINGBODY,
            BodyFormat = ChatBodyFormatEnum.PlainText,
            IsInternal = false,
            AttachmentFileIds = new List<Guid> { request.FileId },
            VoiceTranscriptionStatus = VoiceTranscriptionStatusEnum.Pending
        };
        var attachment = new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            ChatId = chat.Id,
            UploadedByUserId = request.UserId,
            FileId = request.FileId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = request.SizeBytes,
            Url = request.Url,
            Source = request.UserRole == ActorRoleEnum.Customer ? AttachmentSourceEnum.CustomerSubmission : AttachmentSourceEnum.StaffWork,
            VirusScanStatus = VirusScanStatusEnum.Pending,
            Ticket = ticket,
            Chat = chat
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.TicketChats.AddAsync(chat);
            await _uow.TicketAttachments.AddAsync(attachment);
            await _outbox.WriteAsync(new VoiceTranscriptionRequestedEvent(chat.Id, ticket.Id, request.FileId), ct);
            await _uow.CommitTransactionAsync();
        }
        catch { await _uow.RollbackTransactionAsync(); throw; }

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = "Audio đang được xử lý.",
            Data = new TicketActionDTO
            {
                Id = chat.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }
    private static TicketActionResponse Fail(int statusCode, string message) => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
