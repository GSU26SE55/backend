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

public class ManagerQueueQueryHandler : IRequestHandler<ManagerQueueQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ITicketCurrentUserService _currentUserService;

    public ManagerQueueQueryHandler(ITicketUnitOfWork unitOfWork, ITicketCurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(ManagerQueueQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            .Where(t => !t.IsDeleted && t.Status == TicketStatusEnum.New && t.MergedIntoTicketId == null);

        if (request.Priority.HasValue)
            query = query.Where(t => t.Priority == request.Priority.Value);

        if (request.Category.HasValue)
            query = query.Where(t => t.Category == request.Category.Value);

        // P1 first, then P2, P3; within same priority older tickets first
        query = query.OrderBy(t => t.Priority).ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id); // tie-breaker cố định — pagination ổn định

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
            var actorRoles = new[] { _currentUserService.Role ?? "Manager" };
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
            Data = page.Map(t => TicketQueryHelper.MapToTicketDTO(t, unreadTicketIds.Contains(t.Id)))
        };
    }
}
