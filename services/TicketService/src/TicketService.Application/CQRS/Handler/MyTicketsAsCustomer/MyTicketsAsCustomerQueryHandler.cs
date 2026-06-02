using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.MyTicketsAsCustomer;

public class MyTicketsAsCustomerQueryHandler : IRequestHandler<MyTicketsAsCustomerQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public MyTicketsAsCustomerQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(MyTicketsAsCustomerQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Where(t => !t.IsDeleted && t.CustomerId == request.ActorCustomerId);

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
