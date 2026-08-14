using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketHoldCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    public PauseReasonEnum Reason { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RescheduledStartAt { get; set; }
    [JsonIgnore] public Guid StaffId { get; set; }
    [JsonIgnore] public string? StaffName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();
        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });
        if (Reason is not (PauseReasonEnum.CustomerUnavailable or PauseReasonEnum.WorkBlocked))
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Reason must be CustomerUnavailable or WorkBlocked." });
        if (string.IsNullOrWhiteSpace(Note))
            response.ListErrors.Add(new Errors { Field = "Note", Detail = "A hold note is required." });
        if (RescheduledStartAt == default)
            response.ListErrors.Add(new Errors { Field = "RescheduledStartAt", Detail = "A future offset-aware appointment is required." });
        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }
        return Task.FromResult(response);
    }
}
