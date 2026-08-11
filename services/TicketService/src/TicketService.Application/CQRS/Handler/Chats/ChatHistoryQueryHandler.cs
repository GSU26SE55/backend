using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class ChatHistoryQueryHandler : IRequestHandler<ChatHistoryQuery, CommonResponse<List<ChatEditHistoryDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ChatHistoryQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<ChatEditHistoryDTO>>> Handle(ChatHistoryQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() ?? t.PrimaryHandlerStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return Fail(404, "Ticket not found");

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles))
            return Fail(403, "Forbidden");

        var chat = await _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.Id == request.ChatId && c.TicketId == request.TicketId)
            .FirstOrDefaultAsync(cancellationToken);

        if (chat is null)
            return Fail(404, "Comment not found.");

        var isAuthor = chat.AuthorUserId == request.ActorUserId;
        var isStaffOrAbove = TicketQueryHelper.CanViewInternalChats(request.ActorRoles); // Staff/Manager/Admin

        // Customer chỉ xem được history của chat do CHÍNH MÌNH viết — không thấy history chat của Staff.
        if (!isAuthor && !isStaffOrAbove)
            return Fail(403, "You do not have permission to view this comment's history.");

        var edits = await _unitOfWork.TicketChatEdits.GetAllAsync()
            .AsNoTracking()
            .Where(e => e.ChatId == chat.Id)
            .OrderBy(e => e.EditedAt)
            .Select(e => new ChatEditHistoryDTO
            {
                Id = e.Id.ToString(),
                ChatId = e.ChatId.ToString(),
                OldBody = e.OldBody,
                NewBody = e.NewBody,
                EditedAt = e.EditedAt,
                EditedByUserId = e.EditedByUserId.ToString(),
                EditedByRole = e.EditedByRole,
                EditReason = e.EditReason
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<List<ChatEditHistoryDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = edits
        };
    }

    private static CommonResponse<List<ChatEditHistoryDTO>> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
