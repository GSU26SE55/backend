using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.TicketGetList;

public class TicketGetListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    public string? Keyword { get; set; }
    public TicketStatusEnum? Status { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    public TicketCategoryEnum? Category { get; set; }
    public Guid? BatteryAssetId { get; set; }
    public bool IsDescending { get; set; } = true;
}
