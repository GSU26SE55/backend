using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
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
            .Where(t => !t.IsDeleted && t.CustomerId == customerId);

        if (request.Status.HasValue)
            query = query.Where(t => t.Status == request.Status.Value);

        query = query.OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync(cancellationToken);
        var rawItems = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<TicketDTO>
            {
                Items = rawItems.Select(TicketQueryHelper.MapToTicketDTO).ToList(),
                TotalItems = total,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            }
        };
    }
}
