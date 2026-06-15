using MediatR;
using TicketService.Application.CQRS.Query.Sagas;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Sagas;

public class GetAlertTicketSagaByIdQueryHandler : IRequestHandler<GetAlertTicketSagaByIdQuery, AlertTicketSagaDetailResponse>
{
    private readonly IAlertTicketSagaQueryService _sagaQuery;

    public GetAlertTicketSagaByIdQueryHandler(IAlertTicketSagaQueryService sagaQuery)
    {
        _sagaQuery = sagaQuery;
    }

    public async Task<AlertTicketSagaDetailResponse> Handle(GetAlertTicketSagaByIdQuery request, CancellationToken cancellationToken)
    {
        var saga = await _sagaQuery.GetByAlertIdAsync(request.AlertId, cancellationToken);

        if (saga is null)
        {
            return new AlertTicketSagaDetailResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = $"Saga state cho AlertId {request.AlertId} không tồn tại."
            };
        }

        return new AlertTicketSagaDetailResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = saga
        };
    }
}
