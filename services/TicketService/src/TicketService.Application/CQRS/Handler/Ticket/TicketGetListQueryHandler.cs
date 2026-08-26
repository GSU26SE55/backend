using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class TicketGetListQueryHandler : IRequestHandler<TicketGetListQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ITicketCurrentUserService _currentUserService;
    private readonly ISlaCalculator _slaCalculator;

    public TicketGetListQueryHandler(
        ITicketUnitOfWork unitOfWork,
        ITicketCurrentUserService currentUserService,
        ISlaCalculator slaCalculator)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _slaCalculator = slaCalculator;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(TicketGetListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            // Thiếu Include này thì t.Assignments luôn rỗng → mọi card danh sách
            // đều hiện "Chưa phân công" dù ticket đã gán Staff (detail thì đúng
            // vì TicketGetByIdQueryHandler có Include). Lọc !IsDeleted như bên detail.
            .Include(t => t.Assignments.Where(a => !a.IsDeleted))
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(kw) || t.Code.ToLower().Contains(kw));
        }

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);
        else
            // Open = awaiting Manager triage/assignment — that's the Queue's job (ManagerQueueQuery).
            // Hide it from the default "Tickets" list so the two views don't overlap; Manager can
            // still filter Status=Open explicitly to look it up.
            query = query.Where(t => t.Status != TicketStatusEnum.Open);

        if (request.Priority.HasValue)
            query = query.Where(t => t.Priority == request.Priority.Value);

        if (request.Category.HasValue)
            query = query.Where(t => t.Category == request.Category.Value);

        if (request.BatteryAssetId.HasValue)
            query = query.Where(t => t.BatteryAssetId == request.BatteryAssetId.Value);

        query = TicketQueryHelper.FilterBySla(query, request.Sla);

        // SortDir (mới) thắng nếu có; nếu không dùng IsDescending (legacy) để giữ tương thích ngược.
        var descending = string.IsNullOrWhiteSpace(request.SortDir)
            ? request.IsDescending
            : SortHelper.IsDescending(request.SortDir);

        // Whitelist switch-case: code | title | category | status | priority | createdAt (default).
        var ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "code" => descending ? query.OrderByDescending(t => t.Code) : query.OrderBy(t => t.Code),
            "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            "category" => descending ? query.OrderByDescending(t => t.Category) : query.OrderBy(t => t.Category),
            "status" => descending ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "priority" => descending ? query.OrderByDescending(t => t.Priority) : query.OrderBy(t => t.Priority),
            _ => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };
        query = ordered.ThenBy(t => t.Id); // tie-breaker cố định — pagination ổn định

        // Phân trang trên entity: sau đó còn phải truy vấn phụ (chat chưa đọc) rồi mới dựng DTO,
        // nên không chiếu sang DTO trong SQL được.
        var page = await query.ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);
        var rawItems = page.Items;

        var ticketIds = rawItems.Select(t => t.Id).ToList();
        HashSet<Guid> unreadTicketIds;
        if (ticketIds.Count == 0 || !Guid.TryParse(_currentUserService.UserId, out var actorId))
        {
            unreadTicketIds = new HashSet<Guid>();
        }
        else
        {
            var actorRoles = new[] { _currentUserService.Role ?? "Admin" };
            bool canViewInternal = TicketQueryHelper.CanViewInternalChats(actorRoles);
            var readChatIds = _unitOfWork.TicketChatReads.GetAllAsync().AsNoTracking()
                .Where(r => r.UserId == actorId && !r.IsDeleted).Select(r => r.ChatId);
            var chatsBase = _unitOfWork.TicketChats.GetAllAsync().AsNoTracking()
                .Where(c => ticketIds.Contains(c.TicketId) && !c.IsDeleted && c.AuthorUserId != actorId);
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
            Data = page.Map(t => TicketQueryHelper.MapToTicketDTO(
                t, _slaCalculator, DateTime.UtcNow, unreadTicketIds.Contains(t.Id), staffNames))
        };
    }
}
