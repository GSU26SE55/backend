using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ChatMarkAsReadCommandHandler> _logger;

    public ChatMarkAsReadCommandHandler(
        ITicketUnitOfWork uow,
        IChatReadReceiptQueue queue,
        ILogger<ChatMarkAsReadCommandHandler> logger)
    {
        _uow = uow;
        _queue = queue;
        _logger = logger;
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
            .Select(t => new
            {
                t.CustomerId,
                PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault(),
                AssignedStaffIds = t.Assignments.Where(a => !a.IsDeleted).Select(a => a.StaffId).ToList(),
                ParticipantUserIds = t.Participants.Where(p => !p.IsDeleted && p.RemovedAt == null).Select(p => p.UserId).ToList(),
                // Participant được cấp CanViewInternal vẫn THẤY tin internal ở list chat và vẫn bị
                // TicketUnreadCountQuery ĐẾM là chưa đọc. Không lấy cờ này về thì mark-read lọc bỏ
                // đúng những tin đó — user đọc mãi mà badge không bao giờ về 0.
                ActorCanViewInternal = t.Participants.Any(p => !p.IsDeleted && p.RemovedAt == null && p.UserId == request.UserId && p.CanViewInternal)
            })
            .FirstOrDefaultAsync(ct);
        if (ticket == null)
            return new ChatMarkAsReadResponse { IsSuccess = false, StatusCode = 404, Message = "Ticket not found." };

        var allowedUserIds = ticket.ParticipantUserIds.Concat(ticket.AssignedStaffIds).Distinct().ToList();
        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.UserId, request.ActorRoles, allowedUserIds))
            return new ChatMarkAsReadResponse { IsSuccess = false, StatusCode = 403, Message = "You do not have permission to access this ticket." };

        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, ticket.ActorCanViewInternal);

        // Tin do chính actor gửi KHÔNG bao giờ là chưa đọc, nên loại thẳng ở đây: mọi query đếm
        // unread đều bỏ qua chúng (AuthorUserId != actor), ghi receipt cho chúng chỉ tốn slot
        // trong hàng đợi có giới hạn — đầy hàng đợi là receipt của người khác bị rớt.
        var validChatsQuery = _uow.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => request.ChatIds.Contains(c.Id) && c.TicketId == request.TicketId && !c.IsDeleted
                        && c.AuthorUserId != request.UserId);

        if (!canViewInternalChats)
            validChatsQuery = validChatsQuery.Where(c => !c.IsInternal);

        var validChatIds = await validChatsQuery
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (validChatIds.Count == 0)
        {
            return new ChatMarkAsReadResponse { IsSuccess = true, StatusCode = 200, Message = "No valid chats to mark as read.", Data = 0 };
        }

        // Phải lọc !IsDeleted cho khớp với MỌI query đếm unread (chúng đều lọc). Thiếu điều kiện
        // này thì receipt đã soft-delete vẫn bị coi là "đã đọc" ở đây nên không enqueue lại, trong
        // khi query đếm bỏ qua nó nên tin vẫn là chưa đọc — unread kẹt vĩnh viễn.
        var alreadyReadChatIds = await _uow.TicketChatReads.GetAllAsync()
            .AsNoTracking()
            .Where(r => validChatIds.Contains(r.ChatId) && r.UserId == request.UserId && !r.IsDeleted)
            .Select(r => r.ChatId)
            .ToListAsync(ct);

        var newChatIds = validChatIds.Except(alreadyReadChatIds).ToList();
        var readAt = DateTime.UtcNow;
        var enqueuedCount = 0;
        var droppedCount = 0;

        foreach (var chatId in newChatIds)
        {
            if (_queue.TryEnqueue(new ChatReadReceiptItem(chatId, request.UserId, request.UserRole, readAt)))
                enqueuedCount++;
            else
                droppedCount++;
        }

        // Hàng đợi đầy — receipt bị rớt và KHÔNG có cơ chế retry nội bộ. Trả 200 ở đây là nói dối:
        // client ghi nhận "đã đọc", không gửi lại, và unread kẹt vĩnh viễn ở số cũ. Báo thất bại
        // để client biết đường thử lại; những receipt đã vào được hàng đợi vẫn được ghi bình thường.
        if (droppedCount > 0)
        {
            _logger.LogWarning(
                "Read-receipt queue full: dropped {DroppedCount}/{TotalCount} receipts for ticket {TicketId}, user {UserId}.",
                droppedCount, newChatIds.Count, request.TicketId, request.UserId);

            return new ChatMarkAsReadResponse
            {
                IsSuccess = false,
                StatusCode = 503,
                Message = "Read-receipt queue is full. Some chats were not marked as read — please retry.",
                Data = enqueuedCount
            };
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
