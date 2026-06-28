using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatAttachmentRemoveCommandHandler : IRequestHandler<ChatAttachmentRemoveCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public ChatAttachmentRemoveCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<TicketActionResponse> Handle(ChatAttachmentRemoveCommand request, CancellationToken ct)
    {
        var attachment = await _uow.TicketAttachments.GetByIdAsync(request.AttachmentId);
        if (attachment == null || attachment.IsDeleted || attachment.ChatId != request.ChatId || attachment.TicketId != request.TicketId)
            return Fail(404, "Không tìm thấy đính kèm.");

        var chat = await _uow.TicketChats.GetByIdAsync(request.ChatId);
        if (chat == null)
            return Fail(404, "Không tìm thấy bình luận.");

        var ticket = await _uow.Tickets.GetByIdAsync(request.TicketId);
        if (ticket == null)
            return Fail(404, "Không tìm thấy Ticket.");

        var isAuthor = chat.AuthorUserId == request.UserId;
        var isManagerOrAdmin = request.UserRole == ActorRoleEnum.Manager || request.UserRole == ActorRoleEnum.Admin;
        if (!isAuthor && !isManagerOrAdmin)
            return Fail(403, "Không có quyền xóa đính kèm này.");

        attachment.IsDeleted = true;
        attachment.DeletedAt = DateTime.UtcNow;
        _uow.TicketAttachments.UpdateAsync(attachment);

        await _uow.SaveChangesAsync(ct);

        return new TicketActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Xóa đính kèm thành công.",
            Data = new TicketActionDTO
            {
                Id = attachment.Id.ToString(),
                TicketId = ticket.Id.ToString(),
                Code = ticket.Code,
                Status = ticket.Status
            }
        };
    }

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
