using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Sagas;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Sagas;

public class GetAlertTicketSagasQueryHandler : IRequestHandler<GetAlertTicketSagasQuery, AlertTicketSagaListResponse>
{
    private readonly IAlertTicketSagaQueryService _sagaQuery;

    public GetAlertTicketSagasQueryHandler(IAlertTicketSagaQueryService sagaQuery)
    {
        _sagaQuery = sagaQuery;
    }

    public async Task<AlertTicketSagaListResponse> Handle(GetAlertTicketSagasQuery request, CancellationToken cancellationToken)
    {
        var pageSize = request.PageSize is < 1 or > 200 ? 50 : request.PageSize;
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;

        var (items, total) = await _sagaQuery.QueryAsync(
            request.State,
            request.AlertId,
            request.BatteryAssetId,
            request.CustomerId,
            request.StartedFrom,
            request.StartedTo,
            request.IsFailed,
            pageNumber,
            pageSize,
            request.IsDescending,
            cancellationToken);

        return new AlertTicketSagaListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new PaginationResponse<AlertTicketSagaDto>
            {
                Items = items.ToList(),
                TotalItems = total,
                PageNumber = pageNumber,
                PageSize = pageSize
            }
        };
    }
}
