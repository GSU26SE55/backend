using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationTemplate;

/// <summary>
/// Quay lui: bật lại một phiên bản cũ của cặp (Type × Channel).
///
/// <para>Trong cùng cặp chỉ được có đúng một bản active, nên thao tác này tắt bản đang dùng rồi bật
/// bản được chọn — trong MỘT giao dịch, để không có khoảnh khắc nào cặp đó không có bản active
/// (khoảnh khắc ấy dispatcher rơi về chuỗi hardcode trong consumer).</para>
/// </summary>
public class NotificationTemplateActivateCommand
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
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id template không hợp lệ." });

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
