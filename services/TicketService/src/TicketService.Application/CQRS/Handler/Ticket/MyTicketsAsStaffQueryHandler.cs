using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class MyTicketsAsStaffQueryHandler : IRequestHandler<MyTicketsAsStaffQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ITicketCurrentUserService _currentUserService;

    public MyTicketsAsStaffQueryHandler(ITicketUnitOfWork unitOfWork, ITicketCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(MyTicketsAsStaffQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var staffId))
        {
            return new CommonResponse<PaginationResponse<TicketDTO>>
            {
                IsSuccess = false,
                StatusCode = 401,
                Message = "Chưa đăng nhập."
            };
        }

        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            .Where(t => !t.IsDeleted && t.AssignedStaffId == staffId);

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        // Bảng SLA Monitor: chỉ lấy ticket đang trong vòng theo dõi SLA (server-side, không cap theo pageSize)
        if (request.SlaOpen == true)
            query = query.Where(t => TicketStatusGroups.SlaMonitored.Contains(t.Status) && t.SlaTimer != null);

        // sortBy=slaRemaining: DueAt tăng dần ≙ thời gian SLA còn lại tăng dần (gần breach lên đầu);
        // ticket không có timer xếp cuối (MaxValue thay cho NULL để tường minh trên mọi provider).
        query = string.Equals(request.SortBy, "slaRemaining", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(t => t.SlaTimer == null ? DateTime.MaxValue : t.SlaTimer.DueAt).ThenBy(t => t.Priority)
            : query.OrderBy(t => t.Priority).ThenByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(cancellationToken);
        var rawItems = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var ticketIds = rawItems.Select(t => t.Id).ToList();
        HashSet<Guid> unreadTicketIds;
        if (ticketIds.Count == 0)
        {
            unreadTicketIds = new HashSet<Guid>();
        }
        else
        {
            var actorRoles = new[] { _currentUserService.Role ?? "Staff" };
            bool canViewInternal = TicketQueryHelper.CanViewInternalChats(actorRoles);
            var readChatIds = _unitOfWork.TicketChatReads.GetAllAsync().AsNoTracking()
                .Where(r => r.UserId == staffId && !r.IsDeleted).Select(r => r.ChatId);
            var chatsBase = _unitOfWork.TicketChats.GetAllAsync().AsNoTracking()
                .Where(c => ticketIds.Contains(c.TicketId) && !c.IsDeleted && c.AuthorUserId != staffId);
            if (!canViewInternal)
                chatsBase = chatsBase.Where(c => !c.IsInternal);
            var unreadList = await chatsBase
                .Where(c => !readChatIds.Contains(c.Id))
                .Select(c => c.TicketId).Distinct()
                .ToListAsync(cancellationToken);
            unreadTicketIds = unreadList.ToHashSet();
        }

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketDTO>
            {
                Items = rawItems.Select(t => TicketQueryHelper.MapToTicketDTO(t, unreadTicketIds.Contains(t.Id))).ToList(),
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
