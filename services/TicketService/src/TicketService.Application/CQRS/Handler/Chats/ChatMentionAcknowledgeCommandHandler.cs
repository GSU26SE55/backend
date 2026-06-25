using MediatR;
using TicketService.Application.CQRS.Command.ChatMentionAcknowledge;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatMentionAcknowledgeCommandHandler : IRequestHandler<ChatMentionAcknowledgeCommand, ChatMentionActionResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatMentionAcknowledgeCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<ChatMentionActionResponse> Handle(ChatMentionAcknowledgeCommand request, CancellationToken ct)
    {
        var mention = await _uow.TicketChatMentions.GetByIdAsync(request.MentionId);
        if (mention == null || mention.IsDeleted)
            return Fail(404, "Không tìm thấy mention.");

        if (mention.MentionedUserId != request.ActorUserId)
            return Fail(403, "Không có quyền acknowledge mention này.");

        if (!mention.IsAcknowledged)
        {
            mention.IsAcknowledged = true;
            mention.AcknowledgedAt = DateTime.UtcNow;
            _uow.TicketChatMentions.UpdateAsync(mention);
            await _uow.SaveChangesAsync(ct);
        }

        return new ChatMentionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Acknowledge mention thành công.",
            Data = new TicketChatMentionDTO
            {
                Id = mention.Id.ToString(),
                ChatId = mention.ChatId.ToString(),
                MentionedUserId = mention.MentionedUserId.ToString(),
                MentionedUserRole = mention.MentionedUserRole,
                MentionedDisplayName = mention.MentionedDisplayName,
                IsAcknowledged = mention.IsAcknowledged,
                AcknowledgedAt = mention.AcknowledgedAt,
                CreatedAt = mention.CreatedAt
            }
        };
    }

    private static ChatMentionActionResponse Fail(int statusCode, string message)
    {
        return new ChatMentionActionResponse
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
