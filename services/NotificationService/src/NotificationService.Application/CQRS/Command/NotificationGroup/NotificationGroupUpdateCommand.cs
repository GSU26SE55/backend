using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-02 — đổi tên / mô tả một nhóm.
///
/// <para>KHÔNG đổi được <c>Kind</c> và <c>RoleFilter</c>: đổi loại nhóm sẽ làm tập người nhận thay
/// đổi hoàn toàn mà không ai nhận ra — nhóm <c>Static</c> 3 người biến thành nhóm <c>Role</c> vài
/// chục người. Muốn khác thì tạo nhóm mới.</para>
///
/// <para>Nhóm hệ thống (<c>IsSystem</c>) bị từ chối bằng <b>409</b>.</para>
/// </summary>
public class NotificationGroupUpdateCommand
    : IRequest<NotificationGroupActionResponse>, IValidatable<NotificationGroupActionResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Set từ JWT claim, không nhận từ body — dùng cho audit.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationGroupActionResponse> ValidateAsync()
    {
        var response = new NotificationGroupActionResponse();
        NotificationGroupRules.ValidateNameAndDescription(response.ListErrors, Name, Description);
        NotificationGroupRules.ValidateActor(response.ListErrors, ActorUserId);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
