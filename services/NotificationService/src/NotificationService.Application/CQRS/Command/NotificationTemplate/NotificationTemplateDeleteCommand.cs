using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationTemplate;

/// <summary>
/// Xoá mềm một phiên bản template không còn dùng (dọn lịch sử).
///
/// <para><b>KHÔNG xoá được bản đang dùng.</b> Cặp (Type × Channel) mất bản active thì dispatcher lặng
/// lẽ rơi về chuỗi hardcode trong consumer — thông báo vẫn gửi nhưng mất nội dung tuỳ biến, và không
/// ai hay. Muốn bỏ bản đang dùng thì <c>activate</c> một bản khác trước, rồi mới xoá bản này.</para>
/// </summary>
public class NotificationTemplateDeleteCommand
    : IRequest<NotificationTemplateActionResponse>, IValidatable<NotificationTemplateActionResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>Set từ JWT claim, không nhận từ body — dùng cho audit.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationTemplateActionResponse> ValidateAsync()
    {
        var response = new NotificationTemplateActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid template Id." });

        if (ActorUserId == Guid.Empty)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "ActorUserId",
                Detail = "Unable to determine the actor from the token.",
            });
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
