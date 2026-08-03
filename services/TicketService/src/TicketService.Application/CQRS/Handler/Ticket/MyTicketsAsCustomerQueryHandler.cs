using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using SharedInfrastructure.Services;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class MyTicketsAsCustomerQueryHandler : IRequestHandler<MyTicketsAsCustomerQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUserService;

    public MyTicketsAsCustomerQueryHandler(ITicketUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(MyTicketsAsCustomerQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var customerId))
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
            .Where(t => !t.IsDeleted && t.CustomerId == customerId);

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        query = query.OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id); // tie-breaker cố định — pagination ổn định

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
            var readChatIds = _unitOfWork.TicketChatReads.GetAllAsync().AsNoTracking()
                .Where(r => r.UserId == customerId && !r.IsDeleted).Select(r => r.ChatId);
            var unreadList = await _unitOfWork.TicketChats.GetAllAsync().AsNoTracking()
                .Where(c => ticketIds.Contains(c.TicketId) && !c.IsDeleted
                    && c.AuthorUserId != customerId && !c.IsInternal
                    && !readChatIds.Contains(c.Id))
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
