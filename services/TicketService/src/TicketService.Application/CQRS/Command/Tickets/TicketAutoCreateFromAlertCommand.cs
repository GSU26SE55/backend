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

    /// <summary>
    /// Thời điểm anomaly được phát hiện (từ alert). Gán vào <c>Ticket.DetectedAt</c> để panel
    /// "Bằng chứng cảnh báo" lấy đúng cửa sổ log — trước đây bỏ trống nên ticket auto không
    /// hiện được bằng chứng dù saga có sẵn dữ liệu này.
    /// </summary>
    public DateTime? DetectedAt { get; set; }

    /// <summary>Serial pin — hiển thị trên FE (ticket Customer đã có, ticket auto trước đây bỏ trống).</summary>
    public string? BatterySerialNumber { get; set; }

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
