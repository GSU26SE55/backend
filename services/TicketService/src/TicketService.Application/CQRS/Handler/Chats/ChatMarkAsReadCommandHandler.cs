using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Command.ChatMarkAsRead;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

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
            return new ChatMarkAsReadResponse { IsSuccess = true, StatusCode = 200, Message = "Không có chat nào để mark-read.", Data = 0 };
        }

        var ticket = await _uow.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
            return new ChatMarkAsReadResponse { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy Ticket." };

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, request.UserId, request.ActorRoles))
            return new ChatMarkAsReadResponse { IsSuccess = false, StatusCode = 403, Message = "Không có quyền truy cập ticket." };

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
            return new ChatMarkAsReadResponse { IsSuccess = true, StatusCode = 200, Message = "Không có chat hợp lệ để mark-read.", Data = 0 };
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
            Message = "Mark-read thành công.",
            Data = enqueuedCount
        };
    }
}
