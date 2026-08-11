using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-03 — bỏ một người khỏi nhóm (xoá mềm dòng thành viên).
/// Nhóm <c>Role</c> trả <b>409</b>.
/// </summary>
public class NotificationGroupRemoveMemberCommand
    : IRequest<NotificationGroupActionResponse>, IValidatable<NotificationGroupActionResponse>
{
    [JsonIgnore]
    public Guid GroupId { get; set; }

    [JsonIgnore]
    public Guid UserId { get; set; }

    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationGroupActionResponse> ValidateAsync()
    {
        var response = new NotificationGroupActionResponse();
        NotificationGroupRules.ValidateActor(response.ListErrors, ActorUserId);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
