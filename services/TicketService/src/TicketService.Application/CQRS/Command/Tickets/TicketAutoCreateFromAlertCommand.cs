using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using SharedContracts.Saga.AlertTicket;
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

    /// <summary>
    /// Site của pin, forward từ event V2 qua saga. Null với event V1 (không mang SiteId).
    /// Dùng để gom ticket này với ticket environmental cùng cabinet.
    /// </summary>
    public Guid? SiteId { get; set; }
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

    /// <summary>
    /// BE-AI structured — gợi ý của AI ở dạng có cấu trúc, ghi vào <c>ticket_ai_suggestions</c>.
    /// Null với ticket từ threshold engine (không gọi AI).
    /// </summary>
    /// <remarks>
    /// <c>Description</c> ở trên VẪN chứa đoạn "--- AI Prescription ---" như cũ — đây là dữ liệu
    /// BỔ SUNG để truy vấn/hiển thị được theo từng mục, không thay thế phần text.
    /// </remarks>
    public AiSuggestionPayload? AiSuggestion { get; set; }

    /// <summary>Mô tả tổng của prescription (bản gốc, chưa ghép chuỗi).</summary>
    public string? AiPrescriptionText { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (OriginAlertId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "OriginAlertId", Detail = "Invalid OriginAlertId." });

        if (string.IsNullOrWhiteSpace(AnomalyCategory))
            response.ListErrors.Add(new Errors { Field = "AnomalyCategory", Detail = "AnomalyCategory must not be empty." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
