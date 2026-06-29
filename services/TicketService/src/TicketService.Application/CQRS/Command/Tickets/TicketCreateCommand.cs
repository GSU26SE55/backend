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
    /// ID của thiết bị pin.
    /// </summary>
    public Guid? BatteryAssetId { get; set; }

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

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
