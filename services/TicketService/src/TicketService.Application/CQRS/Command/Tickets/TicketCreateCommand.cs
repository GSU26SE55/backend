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
    /// Thời điểm Customer phát hiện pin bất thường (tùy chọn). Không được là thời điểm tương lai.
    /// Dùng để AI đối chiếu sensor tại thời điểm đó khi verify.
    /// </summary>
    public DateTime? DetectedAt { get; set; }

    [JsonIgnore]
    public Guid CustomerId { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (DetectedAt.HasValue && DetectedAt.Value > DateTime.UtcNow)
            response.ListErrors.Add(new Errors { Field = "DetectedAt", Detail = "Thời điểm phát hiện không được là tương lai." });

        if (string.IsNullOrWhiteSpace(Title))
            response.ListErrors.Add(new Errors { Field = "Title", Detail = "Tiêu đề không được để trống." });

        if (string.IsNullOrWhiteSpace(Description))
            response.ListErrors.Add(new Errors { Field = "Description", Detail = "Mô tả không được để trống." });

        if (CustomerId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "CustomerId", Detail = "CustomerId không hợp lệ." });

        if (BatteryAssetIds.Any(id => id == Guid.Empty))
            response.ListErrors.Add(new Errors { Field = "BatteryAssetIds", Detail = "Danh sách pin không được chứa ID rỗng." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
