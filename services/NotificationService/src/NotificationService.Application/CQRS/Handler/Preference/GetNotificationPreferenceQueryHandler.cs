using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Preference;
using NotificationService.Application.DTOs.Response.Preference;
using NotificationService.Application.Interfaces.Repositories;

namespace NotificationService.Application.CQRS.Handler.Preference;

public class GetNotificationPreferenceQueryHandler
    : IRequestHandler<GetNotificationPreferenceQuery, NotificationPreferenceResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public GetNotificationPreferenceQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationPreferenceResponse> Handle(
        GetNotificationPreferenceQuery request,
        CancellationToken cancellationToken)
    {
        var pref = await _unitOfWork.NotificationPreferences.GetAllAsync()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && !p.IsDeleted, cancellationToken);

        // Return default values if user hasn't configured yet — no DB write
        var dto = pref is not null
            ? new NotificationPreferenceDto
            {
                PushEnabled = pref.PushEnabled,
                EmailEnabled = pref.EmailEnabled,
                SmsEnabled = pref.SmsEnabled,
                InAppEnabled = pref.InAppEnabled,
                QuietHoursStart = pref.QuietHoursStart?.ToString("HH:mm"),
                QuietHoursEnd = pref.QuietHoursEnd?.ToString("HH:mm"),
                TimeZone = pref.TimeZone,
            }
            : new NotificationPreferenceDto
            {
                PushEnabled = true,
                EmailEnabled = true,
                SmsEnabled = false,
                InAppEnabled = true,
                TimeZone = "Asia/Ho_Chi_Minh",
            };

        return new NotificationPreferenceResponse { IsSuccess = true, Data = dto };
    }
}
