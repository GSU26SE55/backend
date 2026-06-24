using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.Reports;
using TicketService.Application.DTOs.Response.Reports;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Reports;

public class SagaFailedRateReportQueryHandler
    : IRequestHandler<SagaFailedRateReportQuery, CommonResponse<List<SagaFailedRatePoint>>>
{
    private readonly ISagaReportService _sagaReport;
    public SagaFailedRateReportQueryHandler(ISagaReportService sagaReport) => _sagaReport = sagaReport;

    public async Task<CommonResponse<List<SagaFailedRatePoint>>> Handle(SagaFailedRateReportQuery request, CancellationToken ct)
    {
        var data = await _sagaReport.GetFailedRateAsync(request.From, request.To, request.Granularity, ct);
        return new CommonResponse<List<SagaFailedRatePoint>> { IsSuccess = true, StatusCode = 200, Data = data };
    }
}
