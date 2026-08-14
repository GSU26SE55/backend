using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketAssignCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    [JsonIgnore]
    public Guid TicketId { get; set; }
    public Guid PrimaryHandlerStaffId { get; set; }
    public TicketPriorityEnum Priority { get; set; }
    public DateTimeOffset ScheduledStartAt { get; set; }
    public List<Guid> SupporterStaffIds { get; set; } = new();
    public string? Notes { get; set; }

    [JsonIgnore]
    public Guid ManagerId { get; set; }
    [JsonIgnore]
    public string? ManagerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });
        if (PrimaryHandlerStaffId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "PrimaryHandlerStaffId", Detail = "Invalid PrimaryHandlerStaffId." });
        if (!Enum.IsDefined(Priority))
            response.ListErrors.Add(new Errors { Field = "Priority", Detail = "Invalid ticket priority." });
        if (ScheduledStartAt == default)
            response.ListErrors.Add(new Errors { Field = "ScheduledStartAt", Detail = "A required offset-aware schedule must be provided." });
        if (SupporterStaffIds.Contains(PrimaryHandlerStaffId))
            response.ListErrors.Add(new Errors { Field = "SupporterStaffIds", Detail = "PrimaryHandler cannot also be a Supporter." });
        if (SupporterStaffIds.Count != SupporterStaffIds.Distinct().Count())
            response.ListErrors.Add(new Errors { Field = "SupporterStaffIds", Detail = "Supporter list must not contain duplicate IDs." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
