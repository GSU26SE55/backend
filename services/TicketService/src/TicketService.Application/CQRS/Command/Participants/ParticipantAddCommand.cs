using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Participants;

public class ParticipantAddCommand : IRequest<ParticipantActionResponse>, IValidatable<ParticipantActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// ID của người dùng.
    /// </summary>
    public Guid UserId { get; set; }
    public ActorRoleEnum UserRole { get; set; }
    public ParticipantTypeEnum ParticipantType { get; set; }
    /// <summary>
    /// Can post.
    /// </summary>
    public bool CanPost { get; set; } = true;
    public bool CanViewInternal { get; set; }

    /// <summary>
    /// ID của người thực hiện yêu cầu.
    /// </summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }
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

    public Task<ParticipantActionResponse> ValidateAsync()
    {
        var response = new ParticipantActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (!ManuallyAssignableTypes.Contains(ParticipantType))
            response.ListErrors.Add(new Errors { Field = "ParticipantType", Detail = "ParticipantType chỉ được là Collaborator, Watcher hoặc Delegate khi thêm thủ công." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
