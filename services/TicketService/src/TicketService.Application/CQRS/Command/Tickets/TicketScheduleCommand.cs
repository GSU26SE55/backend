using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

public sealed class TicketScheduleCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    public DateTimeOffset ScheduledStartAt { get; set; }
    public string? Reason { get; set; }
    [JsonIgnore] public Guid ManagerId { get; set; }
    [JsonIgnore] public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();
        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });
        if (ScheduledStartAt == default)
            response.ListErrors.Add(new Errors { Field = "ScheduledStartAt", Detail = "A required offset-aware schedule must be provided." });
        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }
        return Task.FromResult(response);
    }
}
