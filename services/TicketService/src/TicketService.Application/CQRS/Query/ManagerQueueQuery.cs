using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query;

public class ManagerQueueQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    public TicketPriorityEnum? Priority { get; set; }
    public TicketCategoryEnum? Category { get; set; }
}
