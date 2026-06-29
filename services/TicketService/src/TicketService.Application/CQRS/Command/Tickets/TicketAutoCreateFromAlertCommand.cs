using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketAutoCreateFromAlertCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// Origin alert id.
    /// </summary>
    public Guid OriginAlertId { get; set; }
    public string AnomalyCategory { get; set; } = string.Empty;
    public Guid BatteryAssetId { get; set; }
    /// <summary>
    /// Customer id.
    /// </summary>
    public Guid CustomerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (OriginAlertId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "OriginAlertId", Detail = "OriginAlertId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(AnomalyCategory))
            response.ListErrors.Add(new Errors { Field = "AnomalyCategory", Detail = "AnomalyCategory không được để trống." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
