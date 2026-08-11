using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-03 — thêm nhiều người vào một nhóm trong một lệnh.
///
/// <para><b>Bỏ qua thay vì báo lỗi cả lô.</b> Id đã có trong nhóm, hoặc id không tìm thấy trong
/// read-model tài khoản, đều bị bỏ qua và đếm riêng trong response — không làm hỏng cả lần thêm.
/// Chọn 30 người rồi bị từ chối toàn bộ chỉ vì 1 người đã có sẵn là hành vi khó chịu và khiến admin
/// phải tự dò xem người nào trùng.</para>
///
/// <para>Nhóm <c>Role</c> trả <b>409</b>: thành viên do role quyết định, thêm tay không có ý nghĩa
/// và sẽ bị bỏ qua hoàn toàn lúc gửi.</para>
/// </summary>
public class NotificationGroupAddMembersCommand
    : IRequest<NotificationGroupAddMembersResponse>, IValidatable<NotificationGroupAddMembersResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid GroupId { get; set; }

    /// <summary>Danh sách AccountId cần thêm. Id trùng nhau trong chính mảng này được gộp lại.</summary>
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Trần số người thêm được trong một lệnh. Không phải giới hạn nghiệp vụ mà là chặn payload
    /// rác: mỗi id sinh một dòng INSERT trong cùng một transaction.
    /// </summary>
    public const int MaxUserIdsPerRequest = 500;

    public Task<NotificationGroupAddMembersResponse> ValidateAsync()
    {
        var response = new NotificationGroupAddMembersResponse();

        if (UserIds.Count == 0)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "UserIds",
                Detail = "At least one person must be selected.",
            });
        }
        else if (UserIds.Count > MaxUserIdsPerRequest)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "UserIds",
                Detail = $"A maximum of {MaxUserIdsPerRequest} people per add.",
            });
        }
        else if (UserIds.Any(id => id == Guid.Empty))
        {
            response.ListErrors.Add(new Errors
            {
                Field = "UserIds",
                Detail = "The list contains an empty id.",
            });
        }

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
