using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Application.DTOs.Response.Preference;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Handler.Preference;

public class UpdateNotificationPreferenceCommandHandler
    : IRequestHandler<UpdateNotificationPreferenceCommand, NotificationPreferenceResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public UpdateNotificationPreferenceCommandHandler(INotificationUnitOfWork unitOfWork, ICacheService cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<NotificationPreferenceResponse> Handle(
        UpdateNotificationPreferenceCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.NotificationPreferences.GetAllAsync()
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && !p.IsDeleted, cancellationToken);

        TimeOnly? start = string.IsNullOrEmpty(request.QuietHoursStart)
            ? null
            : TimeOnly.ParseExact(request.QuietHoursStart, "HH:mm");

        TimeOnly? end = string.IsNullOrEmpty(request.QuietHoursEnd)
            ? null
            : TimeOnly.ParseExact(request.QuietHoursEnd, "HH:mm");

        if (existing is null)
        {
            existing = new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                PushEnabled = request.PushEnabled,
                EmailEnabled = request.EmailEnabled,
                SmsEnabled = request.SmsEnabled,
                InAppEnabled = request.InAppEnabled,
                QuietHoursStart = start,
                QuietHoursEnd = end,
                TimeZone = request.TimeZone,
                NotifyOnChat = request.NotifyOnChat ?? true,
                NotifyOnMention = request.NotifyOnMention ?? true,
                NotifyOnReaction = request.NotifyOnReaction ?? false,
                DigestWindowMinutes = request.DigestWindowMinutes,
            };
            await _unitOfWork.NotificationPreferences.AddAsync(existing);
        }
        else
        {
            existing.PushEnabled = request.PushEnabled;
            existing.EmailEnabled = request.EmailEnabled;
            existing.SmsEnabled = request.SmsEnabled;
            existing.InAppEnabled = request.InAppEnabled;
            existing.QuietHoursStart = start;
            existing.QuietHoursEnd = end;
            // TimeZone là hằng số của deployment, không client nào có ô sửa. Mobile hardcode
            // 'Asia/Ho_Chi_Minh' và gửi kèm mọi lần lưu, nên ghi đè vô điều kiện là giá trị
            // user đặt ở nơi khác bị thay mỗi lần họ bật/tắt một kênh trên điện thoại.
            if (!string.IsNullOrWhiteSpace(request.TimeZone))
                existing.TimeZone = request.TimeZone;
            // Chỉ ghi đè khi client thực sự gửi field — màn Profile chỉ PUT 4 channel
            // + quiet hours, trước đây các pref chat bị reset về default sau mỗi lần lưu.
            if (request.NotifyOnChat.HasValue)
                existing.NotifyOnChat = request.NotifyOnChat.Value;
            if (request.NotifyOnMention.HasValue)
                existing.NotifyOnMention = request.NotifyOnMention.Value;
            if (request.NotifyOnReaction.HasValue)
                existing.NotifyOnReaction = request.NotifyOnReaction.Value;
            if (request.DigestWindowMinutes.HasValue)
                existing.DigestWindowMinutes = request.DigestWindowMinutes;
            _unitOfWork.NotificationPreferences.UpdateAsync(existing);
        }

        await _unitOfWork.CommitTransactionAsync();

        // Invalidate dispatcher cache so next dispatch picks up new preference
        await _cache.RemoveAsync($"notif_pref:{request.UserId}", cancellationToken);

        var dto = new NotificationPreferenceDto
        {
            PushEnabled = existing.PushEnabled,
            EmailEnabled = existing.EmailEnabled,
            SmsEnabled = existing.SmsEnabled,
            InAppEnabled = existing.InAppEnabled,
            QuietHoursStart = existing.QuietHoursStart?.ToString("HH:mm"),
            QuietHoursEnd = existing.QuietHoursEnd?.ToString("HH:mm"),
            TimeZone = existing.TimeZone,
            NotifyOnChat = existing.NotifyOnChat,
            NotifyOnMention = existing.NotifyOnMention,
            NotifyOnReaction = existing.NotifyOnReaction,
            DigestWindowMinutes = existing.DigestWindowMinutes,
        };

        return new NotificationPreferenceResponse { IsSuccess = true, Data = dto };
    }
}
