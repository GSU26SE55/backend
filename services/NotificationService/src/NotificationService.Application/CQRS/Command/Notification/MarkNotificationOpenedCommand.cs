using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Notification;

/// <summary>
/// Sprint 6.3 NOTI3-14 (#714) — client báo user đã **mở** notification (bấm vào push / deep link).
///
/// Khác <see cref="MarkNotificationReadCommand"/>: <c>Read</c> chỉ nghĩa là dòng đó đã hiện trên feed
/// và user bấm "đã đọc"; <c>Opened</c> là bằng chứng mạnh hơn — user chủ động mở nội dung. Hai chỉ số
/// này tách nhau để đo hiệu quả thật của kênh push (open rate), thứ mà chỉ Read không nói được.
///
/// Idempotent: đã Opened rồi vẫn trả 200. Chỉ thao tác trên noti thuộc về chính user.
/// </summary>
public class MarkNotificationOpenedCommand : IRequest<NotificationActionResponse>, IValidatable<NotificationActionResponse>
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
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id notification không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Không xác định được user." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
