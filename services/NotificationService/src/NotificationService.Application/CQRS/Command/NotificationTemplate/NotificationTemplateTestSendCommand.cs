using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationTemplate;

/// <summary>
/// Gửi thử một template tới <b>chính admin đang đăng nhập</b>.
///
/// <para><b>Không nhận địa chỉ tự do (R-46):</b> endpoint nhận địa chỉ tuỳ ý sẽ biến hệ thống thành
/// cổng gửi thư rác có xác thực — kẻ chiếm được một tài khoản admin có thể bắn nội dung tự soạn từ
/// domain có SPF/DKIM hợp lệ của chúng ta. Địa chỉ nhận LUÔN suy ra từ danh tính người gọi, không
/// bao giờ từ body.</para>
///
/// <para>Giới hạn 5 lần/giờ mỗi admin và ghi audit mỗi lần gửi. Chỉ hỗ trợ template kênh Email —
/// gửi thử SMS tốn tiền thật, push cần device token của admin.</para>
/// </summary>
public class NotificationTemplateTestSendCommand
    : IRequest<NotificationTemplateTestSendResponse>, IValidatable<NotificationTemplateTestSendResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>Dữ liệu mẫu để render, giống <c>preview</c>.</summary>
    public JsonElement? SampleData { get; set; }

    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// Email lấy từ claim JWT, dùng làm nguồn dự phòng khi read-model account chưa có bản ghi.
    /// Set từ token, không nhận từ body.
    /// </summary>
    [JsonIgnore]
    public string? ActorEmailFromClaim { get; set; }

    public Task<NotificationTemplateTestSendResponse> ValidateAsync()
    {
        var response = new NotificationTemplateTestSendResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid template Id." });

        if (ActorUserId == Guid.Empty)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "ActorUserId",
                Detail = "Unable to determine the UserId from the token.",
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
