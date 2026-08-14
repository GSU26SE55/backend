using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Notification;

/// <summary>
/// Đánh dấu 1 notification của user hiện tại là đã đọc (Status=Read, ReadAt=UtcNow).
/// Idempotent: đã Read rồi vẫn trả 200. Chỉ thao tác trên noti thuộc về chính user.
/// </summary>
public class MarkNotificationReadCommand : IRequest<NotificationActionResponse>, IValidatable<NotificationActionResponse>
{
    /// <summary>Set từ route, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid UserId { get; set; }

    public Task<NotificationActionResponse> ValidateAsync()
    {
        var response = new NotificationActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid notification Id." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Unable to determine the current user." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
