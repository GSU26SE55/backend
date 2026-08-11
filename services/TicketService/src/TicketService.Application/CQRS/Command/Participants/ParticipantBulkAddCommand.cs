using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Participants;

public class ParticipantBulkAddCommand : IRequest<ParticipantBulkActionResponse>, IValidatable<ParticipantBulkActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// Participants.
    /// </summary>
    public List<ParticipantBulkAddItem> Participants { get; set; } = new();

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Actor role.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum ActorRole { get; set; }

    [JsonIgnore]
    public string? ActorName { get; set; }

    private static readonly ParticipantTypeEnum[] ManuallyAssignableTypes =
    {
        ParticipantTypeEnum.Collaborator,
        ParticipantTypeEnum.Watcher,
        ParticipantTypeEnum.Delegate
    };

    public Task<ParticipantBulkActionResponse> ValidateAsync()
    {
        var response = new ParticipantBulkActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (Participants.Count == 0)
            response.ListErrors.Add(new Errors { Field = "Participants", Detail = "Participant list must not be empty." });

        var seenUserIds = new HashSet<Guid>();
        for (int i = 0; i < Participants.Count; i++)
        {
            var item = Participants[i];
            if (item.UserId == Guid.Empty)
                response.ListErrors.Add(new Errors { Field = $"Participants[{i}].UserId", Detail = "Invalid UserId." });
            else if (!seenUserIds.Add(item.UserId))
                response.ListErrors.Add(new Errors { Field = $"Participants[{i}].UserId", Detail = "Duplicate UserId in the list." });

            if (!ManuallyAssignableTypes.Contains(item.ParticipantType))
                response.ListErrors.Add(new Errors { Field = $"Participants[{i}].ParticipantType", Detail = "ParticipantType can only be Collaborator, Watcher, or Delegate when added manually." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}

public record ParticipantBulkAddItem(
    Guid UserId,
    ActorRoleEnum UserRole,
    ParticipantTypeEnum ParticipantType,
    bool CanPost,
    bool CanViewInternal
);
