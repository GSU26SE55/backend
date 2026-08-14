using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>
/// Đếm tin nhắn chưa đọc gom theo Customer — dùng cho màn "Khách hàng" của Staff.
///
/// Quy tắc đếm giống hệt <see cref="MyUnreadCountQueryHandler"/> (đếm bản ghi
/// TicketChat), chỉ khác là group theo Ticket.CustomerId thay vì tổng 1 số.
/// Nhờ đếm theo chat row nên 1 tin nhắn có kèm @mention vẫn chỉ tính 1 lần —
/// mention nằm ở bảng con TicketChatMention nên KHÔNG được cộng thêm.
/// </summary>
public class MyUnreadCountByCustomerQueryHandler
    : IRequestHandler<MyUnreadCountByCustomerQuery, UnreadCountByCustomerResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public MyUnreadCountByCustomerQueryHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<UnreadCountByCustomerResponse> Handle(
        MyUnreadCountByCustomerQuery request,
        CancellationToken ct)
    {
        var actorUserId = request.ActorUserId;
        var actorRoles = request.ActorRoles;

        // Step 1: xác định các ticket actor được phép thấy (giống MyUnreadCountQueryHandler).
        bool isManagerOrAdmin = actorRoles.Contains("Admin") || actorRoles.Contains("Manager");
        IQueryable<Guid> ticketIdsQuery;

        if (isManagerOrAdmin)
        {
            ticketIdsQuery = _uow.Tickets.GetAllAsync().AsNoTracking()
                .Where(t => !t.IsDeleted)
                .Select(t => t.Id);
        }
        else
        {
            bool isCustomer = actorRoles.Contains("Customer");
            IQueryable<Guid> directIds;

            if (isCustomer)
            {
                directIds = _uow.Tickets.GetAllAsync().AsNoTracking()
                    .Where(t => !t.IsDeleted && t.CustomerId == actorUserId)
                    .Select(t => t.Id);
            }
            else
            {
                directIds = _uow.TicketAssignments.GetAllAsync().AsNoTracking()
                    .Where(a => a.StaffId == actorUserId && a.Role == AssignmentRoleEnum.PrimaryHandler && !a.IsDeleted)
                    .Select(a => a.TicketId);
            }

            var participantIds = _uow.TicketParticipants.GetAllAsync().AsNoTracking()
                .Where(p => p.UserId == actorUserId && p.RemovedAt == null && !p.IsDeleted)
                .Select(p => p.TicketId);

            ticketIdsQuery = directIds.Union(participantIds);
        }

        // Step 2: các chat actor đã đọc.
        var readChatIds = _uow.TicketChatReads.GetAllAsync().AsNoTracking()
            .Where(r => r.UserId == actorUserId && !r.IsDeleted)
            .Select(r => r.ChatId);

        // Step 3: chat chưa đọc (không phải do chính mình gửi, chưa đánh dấu đọc).
        bool canViewInternal = TicketQueryHelper.CanViewInternalChats(actorRoles);
        var chatsBase = _uow.TicketChats.GetAllAsync().AsNoTracking()
            .Where(c => ticketIdsQuery.Contains(c.TicketId) && !c.IsDeleted && c.AuthorUserId != actorUserId);

        if (!canViewInternal)
            chatsBase = chatsBase.Where(c => !c.IsInternal);

        var unreadChats = chatsBase.Where(c => !readChatIds.Contains(c.Id));

        // Step 4: join sang Ticket để lấy CustomerId rồi group — gộp ở DB, 1 round-trip.
        var tickets = _uow.Tickets.GetAllAsync().AsNoTracking().Where(t => !t.IsDeleted);

        var grouped = await unreadChats
            .Join(tickets, c => c.TicketId, t => t.Id, (c, t) => t.CustomerId)
            .GroupBy(customerId => customerId)
            .Select(g => new { CustomerId = g.Key, UnreadCount = g.Count() })
            .ToListAsync(ct);

        return new UnreadCountByCustomerResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = grouped
                .Select(x => new UnreadCountByCustomerDTO
                {
                    CustomerId = x.CustomerId.ToString(),
                    UnreadCount = x.UnreadCount
                })
                .ToList()
        };
    }
}
