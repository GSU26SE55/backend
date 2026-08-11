using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>
/// Lọc bỏ chatId không hợp lệ/đã đọc, rồi enqueue vào <see cref="IChatReadReceiptQueue"/> —
/// KHÔNG ghi DB trực tiếp. <c>ChatReadReceiptBulkWriter</c> (BackgroundService) mới là nơi persist (#542).
/// Dùng chung cho cả endpoint POST mark-read (#541) và auto mark-read khi GetList (gọi từ Controller).
/// </summary>
public class ChatMarkAsReadCommandHandler : IRequestHandler<ChatMarkAsReadCommand, ChatMarkAsReadResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatReadReceiptQueue _queue;

    public ChatMarkAsReadCommandHandler(ITicketUnitOfWork uow, IChatReadReceiptQueue queue)
    {
        _uow = uow;
        _queue = queue;
    }

    public async Task<ChatMarkAsReadResponse> Handle(ChatMarkAsReadCommand request, CancellationToken ct)
    {
        if (request.ChatIds.Count == 0)
        {
            return new ChatMarkAsReadResponse { IsSuccess = true, StatusCode = 200, Message = "No chats to mark as read.", Data = 0 };
        }

        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
            return new ChatMarkAsReadResponse { IsSuccess = false, StatusCode = 404, Message = "Ticket not found." };

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.UserId, request.ActorRoles))
            return new ChatMarkAsReadResponse { IsSuccess = false, StatusCode = 403, Message = "You do not have permission to access this ticket." };

        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles);

        var validChatsQuery = _uow.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => request.ChatIds.Contains(c.Id) && c.TicketId == request.TicketId && !c.IsDeleted);

        if (!canViewInternalChats)
            validChatsQuery = validChatsQuery.Where(c => !c.IsInternal);

        var validChatIds = await validChatsQuery
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (validChatIds.Count == 0)
        {
            return new ChatMarkAsReadResponse { IsSuccess = true, StatusCode = 200, Message = "No valid chats to mark as read.", Data = 0 };
        }

        var alreadyReadChatIds = await _uow.TicketChatReads.GetAllAsync()
            .AsNoTracking()
            .Where(r => validChatIds.Contains(r.ChatId) && r.UserId == request.UserId)
            .Select(r => r.ChatId)
            .ToListAsync(ct);

        var newChatIds = validChatIds.Except(alreadyReadChatIds).ToList();
        var readAt = DateTime.UtcNow;
        var enqueuedCount = 0;

        foreach (var chatId in newChatIds)
        {
            if (_queue.TryEnqueue(new ChatReadReceiptItem(chatId, request.UserId, request.UserRole, readAt)))
                enqueuedCount++;
        }

        return new ChatMarkAsReadResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Marked as read successfully.",
            Data = enqueuedCount
        };
    }
}
