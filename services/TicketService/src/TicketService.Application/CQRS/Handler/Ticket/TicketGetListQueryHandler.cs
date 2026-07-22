using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class TicketGetListQueryHandler : IRequestHandler<TicketGetListQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ITicketCurrentUserService _currentUserService;

    public TicketGetListQueryHandler(ITicketUnitOfWork unitOfWork, ITicketCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(TicketGetListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            .Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(kw) || t.Code.ToLower().Contains(kw));
        }

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        if (request.Priority.HasValue)
            query = query.Where(t => t.Priority == request.Priority.Value);

        if (request.Category.HasValue)
            query = query.Where(t => t.Category == request.Category.Value);

        if (request.BatteryAssetId.HasValue)
            query = query.Where(t => t.BatteryAssetId == request.BatteryAssetId.Value);

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

        var total = await query.CountAsync(cancellationToken);
        var rawItems = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

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
