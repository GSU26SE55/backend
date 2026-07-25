using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Ticket;

public sealed class ManagerQueueCountQueryHandler
    : IRequestHandler<ManagerQueueCountQuery, CommonResponse<int>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ManagerQueueCountQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<int>> Handle(ManagerQueueCountQuery request, CancellationToken cancellationToken)
    {
        var count = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .CountAsync(
                t => !t.IsDeleted
                    && t.Status == TicketStatusEnum.Open
                    && t.MergedIntoTicketId == null,
                cancellationToken);

        return new CommonResponse<int>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = count
        };
    }
}
