using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketHoldCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    public PauseReasonEnum Reason { get; set; }
    /// <summary>
    /// Note.
    /// </summary>
    public string? Note { get; set; }

    [JsonIgnore]
    public Guid StaffId { get; set; }
    /// <summary>
    /// Staff name.
    /// </summary>
    [JsonIgnore]
    public string? StaffName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (!Enum.IsDefined(typeof(PauseReasonEnum), Reason))
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Lý do tạm dừng không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
