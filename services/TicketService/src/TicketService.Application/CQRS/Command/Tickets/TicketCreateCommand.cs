using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketCreateCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// Tiêu đề.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TicketCategoryEnum Category { get; set; }

    /// <summary>
    /// Danh sách ID thiết bị pin (có thể để trống, hoặc 1 hoặc nhiều cục pin).
    /// </summary>
    public List<Guid> BatteryAssetIds { get; set; } = new();

    /// <summary>
    /// Thời điểm bắt đầu phát hiện sự cố (bắt buộc).
    /// </summary>
    public DateTime? IncidentDetectedFrom { get; set; }

    /// <summary>
    /// Thời điểm kết thúc phát hiện sự cố (optional).
    /// </summary>
    public DateTime? IncidentDetectedTo { get; set; }

    [JsonIgnore]
    public Guid CustomerId { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được để trống." });

        if (string.IsNullOrWhiteSpace(Description))
            response.ListErrors.Add(new Errors { Field = "Description", Detail = "Mô tả không được để trống." });

        if (CustomerId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "CustomerId", Detail = "CustomerId không hợp lệ." });

        if (BatteryAssetIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Danh sách pin không được chứa ID rỗng." });

        if (!IncidentDetectedFrom.HasValue)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedFrom", Detail = "Thời điểm phát hiện sự cố không được để trống." });

        if (IncidentDetectedFrom.HasValue && IncidentDetectedFrom.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedFrom", Detail = "Thời điểm phát hiện sự cố không được trong tương lai." });

        if (IncidentDetectedTo.HasValue && IncidentDetectedTo.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedTo", Detail = "Thời điểm kết thúc phát hiện sự cố không được trong tương lai." });

        if (IncidentDetectedFrom.HasValue && IncidentDetectedTo.HasValue && IncidentDetectedFrom.Value > IncidentDetectedTo.Value)
            response.ListErrors.Add(new Errors { Field = "IncidentDetectedFrom", Detail = "'Từ' phải trước hoặc bằng 'Đến'." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
