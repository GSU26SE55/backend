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
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (Participants.Count == 0)
            response.ListErrors.Add(new Errors { Field = "Participants", Detail = "Danh sách participant không được để trống." });

        var seenUserIds = new HashSet<Guid>();
        for (int i = 0; i < Participants.Count; i++)
        {
            var item = Participants[i];
            if (item.UserId == Guid.Empty)
                response.ListErrors.Add(new Errors { Field = $"Participants[{i}].UserId", Detail = "UserId không hợp lệ." });
            else if (!seenUserIds.Add(item.UserId))
                response.ListErrors.Add(new Errors { Field = $"Participants[{i}].UserId", Detail = "UserId bị trùng trong danh sách." });

            if (!ManuallyAssignableTypes.Contains(item.ParticipantType))
                response.ListErrors.Add(new Errors { Field = $"Participants[{i}].ParticipantType", Detail = "ParticipantType chỉ được là Collaborator, Watcher hoặc Delegate khi thêm thủ công." });
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
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
