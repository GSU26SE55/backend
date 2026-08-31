using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.SLAs;

namespace TicketService.Application.CQRS.Query.SLAs;

public sealed class GetSlaNonWorkingPeriodsQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<SlaNonWorkingPeriodDto>>>
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
}
