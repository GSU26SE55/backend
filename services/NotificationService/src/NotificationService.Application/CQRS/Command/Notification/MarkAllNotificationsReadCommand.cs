using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Notification;

/// <summary>
/// Đánh dấu mọi notification chưa đọc (Status != Read) của user hiện tại thành đã đọc.
/// Trả về số notification đã được đánh dấu.
/// </summary>
public class MarkAllNotificationsReadCommand : IRequest<NotificationCountResponse>, IValidatable<NotificationCountResponse>
{
    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid UserId { get; set; }

    public Task<NotificationCountResponse> ValidateAsync()
    {
        var response = new NotificationCountResponse();

        if (UserId == Guid.Empty)
        {
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Không xác định được user." });
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
