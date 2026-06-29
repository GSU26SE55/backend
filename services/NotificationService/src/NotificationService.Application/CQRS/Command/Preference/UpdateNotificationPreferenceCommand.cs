using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Preference;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Preference;

public class UpdateNotificationPreferenceCommand : IRequest<NotificationPreferenceResponse>, IValidatable<NotificationPreferenceResponse>
{
    [JsonIgnore]
    public Guid UserId { get; set; }

    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = false;
    public bool InAppEnabled { get; set; } = true;

    /// <summary>"HH:mm" hoặc null để xóa quiet hours.</summary>
    public string? QuietHoursStart { get; set; }
    public string? QuietHoursEnd { get; set; }

    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";

    // Chat-specific preferences (#570)
    public bool NotifyOnChat { get; set; } = true;
    public bool NotifyOnMention { get; set; } = true;
    public bool NotifyOnReaction { get; set; } = false;
    public int? DigestWindowMinutes { get; set; }

    public Task<NotificationPreferenceResponse> ValidateAsync()
    {
        var response = new NotificationPreferenceResponse();

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Không xác định được user." });

        if (!string.IsNullOrEmpty(QuietHoursStart) && !TimeOnly.TryParseExact(QuietHoursStart, "HH:mm", out _))
            response.ListErrors.Add(new Errors { Field = "QuietHoursStart", Detail = "Định dạng phải là HH:mm." });

        if (!string.IsNullOrEmpty(QuietHoursEnd) && !TimeOnly.TryParseExact(QuietHoursEnd, "HH:mm", out _))
            response.ListErrors.Add(new Errors { Field = "QuietHoursEnd", Detail = "Định dạng phải là HH:mm." });

        if (string.IsNullOrWhiteSpace(TimeZone))
            response.ListErrors.Add(new Errors { Field = "TimeZone", Detail = "TimeZone không được trống." });
        else if (TimeZone.Length > 100)
            response.ListErrors.Add(new Errors { Field = "TimeZone", Detail = "TimeZone tối đa 100 ký tự." });

        if (response.ListErrors.Count > 0)
            response.IsSuccess = false;
        return Task.FromResult(response);
    }
}
