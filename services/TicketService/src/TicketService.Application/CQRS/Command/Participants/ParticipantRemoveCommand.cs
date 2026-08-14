using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Participants;

public class ParticipantRemoveCommand : IRequest<ParticipantActionResponse>, IValidatable<ParticipantActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }

    /// <summary>
    /// Remove reason.
    /// </summary>
    public string? RemoveReason { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Actor role.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum ActorRole { get; set; }

    [JsonIgnore]
    public string? ActorName { get; set; }

    public Task<ParticipantActionResponse> ValidateAsync()
    {
        var response = new ParticipantActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Invalid UserId." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
