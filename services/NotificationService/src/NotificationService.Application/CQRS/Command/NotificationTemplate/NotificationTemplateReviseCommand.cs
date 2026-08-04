using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationTemplate;

/// <summary>
/// Sửa nội dung template = <b>sinh phiên bản mới</b> rồi bật nó lên, KHÔNG ghi đè bản cũ.
///
/// <para><see cref="Id"/> là bản bất kỳ của cặp (Type × Channel) cần sửa — handler lấy Type/Channel
/// từ nó. Không nhận Type/Channel từ body: cho phép đổi cặp khi "sửa" nghĩa là biến bản ghi này
/// thành template của một cặp khác, phá vỡ chuỗi phiên bản của cả hai cặp.</para>
/// </summary>
public class NotificationTemplateReviseCommand
    : IRequest<NotificationTemplateActionResponse>, IValidatable<NotificationTemplateActionResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    public string TitleTemplate { get; set; } = string.Empty;

    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Set từ JWT claim, không nhận từ body — dùng cho audit.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationTemplateActionResponse> ValidateAsync()
    {
        var response = new NotificationTemplateActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id template không hợp lệ." });

        NotificationTemplateContentRules.Validate(
            response.ListErrors, type: null, channel: null, TitleTemplate, BodyTemplate);

        if (ActorUserId == Guid.Empty)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "ActorUserId",
                Detail = "Không xác định được người thực hiện từ token.",
            });
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
