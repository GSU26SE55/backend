using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

public class CustomerSchedulePeriodicMaintenanceCommand
    : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid CustomerId { get; set; }

    public DateTimeOffset ScheduledStartAt { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();
        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });
        if (CustomerId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "CustomerId", Detail = "Invalid CustomerId." });
        if (ScheduledStartAt == default)
            response.ListErrors.Add(new Errors
            {
                Field = "ScheduledStartAt",
                Detail = "An offset-aware schedule is required."
            });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
        }

        return Task.FromResult(response);
    }
}
