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
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Unable to determine the current user." });
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
