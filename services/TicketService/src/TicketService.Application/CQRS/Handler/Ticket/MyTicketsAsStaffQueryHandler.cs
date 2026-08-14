using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

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
                Message = "Not logged in."
            };
        }

        var assignedTicketIds = _unitOfWork.TicketAssignments != null
            ? _unitOfWork.TicketAssignments.GetAllAsync()
                .Where(a => a.StaffId == staffId && !a.IsDeleted)
                .Select(a => a.TicketId)
            : Enumerable.Empty<Guid>().AsQueryable();

        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            // Thiếu Include này thì t.Assignments luôn rỗng → mọi card danh sách
            // đều hiện "Chưa phân công" dù ticket đã gán Staff (detail thì đúng
            // vì TicketGetByIdQueryHandler có Include). Lọc !IsDeleted như bên detail.
            .Include(t => t.Assignments.Where(a => !a.IsDeleted))
            .Where(t => !t.IsDeleted && assignedTicketIds.Contains(t.Id));

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        // Bảng SLA Monitor: chỉ lấy ticket đang trong vòng theo dõi SLA (server-side, không cap theo pageSize)
        if (request.SlaOpen == true)
            query = query.Where(t => TicketStatusGroups.SlaMonitored.Contains(t.Status) && t.SlaTimer != null);

        // sortBy=slaRemaining: DueAt tăng dần ≙ thời gian SLA còn lại tăng dần (gần breach lên đầu);
        // ticket không có timer xếp cuối (MaxValue thay cho NULL để tường minh trên mọi provider).
        // .ThenBy(Id) ở cả hai nhánh: tie-breaker cố định — pagination ổn định.
        query = string.Equals(request.SortBy, "slaRemaining", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(t => t.SlaTimer == null ? DateTime.MaxValue : t.SlaTimer.DueAt).ThenBy(t => t.Priority).ThenBy(t => t.Id)
            : query.OrderBy(t => t.Priority).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id);

        // Phân trang trên entity: sau đó còn phải truy vấn phụ (chat chưa đọc) rồi mới dựng DTO,
        // nên không chiếu sang DTO trong SQL được.
        var page = await query.ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);
        var rawItems = page.Items;

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

        // Tên staff cho phần "phụ trách": gom StaffId của cả trang rồi tra 1 lần
        // (không phải mỗi ticket 1 query). Nhờ vậy MỌI role đọc được tên mà không
        // cần gọi /api/staff — endpoint đó chỉ mở cho Admin/Manager.
        var staffIds = page.Items
            .SelectMany(t => t.Assignments.Where(a => !a.IsDeleted).Select(a => a.StaffId))
            .Distinct()
            .ToList();
        var staffNames = staffIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _unitOfWork.StaffAccounts.GetAllAsync().AsNoTracking()
                .Where(s => staffIds.Contains(s.AccountId) && !s.IsDeleted)
                .ToDictionaryAsync(s => s.AccountId, s => s.FullName, cancellationToken);

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(t => TicketQueryHelper.MapToTicketDTO(t, unreadTicketIds.Contains(t.Id), staffNames))
        };
    }
}
