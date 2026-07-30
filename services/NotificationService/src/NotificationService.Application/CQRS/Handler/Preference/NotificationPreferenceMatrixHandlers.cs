using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Application.CQRS.Query.Preference;
using NotificationService.Application.DTOs.Response.Preference;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Handler.Preference;

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — dựng ma trận nhóm × kênh cho FE.
///
/// Luôn trả về **đủ 6 nhóm**, kể cả nhóm người dùng chưa tuỳ chỉnh: khi đó lấy giá trị suy từ tuỳ
/// chọn kênh toàn cục và đánh dấu <c>IsCustomized=false</c>, để FE vẽ được trạng thái "kế thừa"
/// mà không phải tự đoán nhóm nào thiếu.
/// </summary>
public class GetNotificationPreferenceMatrixQueryHandler
    : IRequestHandler<GetNotificationPreferenceMatrixQuery, NotificationPreferenceMatrixResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public GetNotificationPreferenceMatrixQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationPreferenceMatrixResponse> Handle(
        GetNotificationPreferenceMatrixQuery request, CancellationToken cancellationToken)
    {
        var channelPref = await _unitOfWork.NotificationPreferences.GetAllAsync(false)
            .FirstOrDefaultAsync(p => p.UserId == request.UserId && !p.IsDeleted, cancellationToken);

        var channelDto = MapChannelDto(channelPref);

        var categoryPrefs = await _unitOfWork.NotificationCategoryPreferences.GetAllAsync(false)
            .Where(p => p.UserId == request.UserId && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        var byCategory = categoryPrefs.ToDictionary(p => p.Category);

        var rows = Enum.GetValues<NotificationCategoryEnum>()
            .Select(category =>
            {
                if (byCategory.TryGetValue(category, out var saved))
                {
                    return new NotificationCategoryPreferenceDto
                    {
                        Category = category,
                        CategoryName = category.ToString(),
                        PushEnabled = saved.PushEnabled,
                        EmailEnabled = saved.EmailEnabled,
                        SmsEnabled = saved.SmsEnabled,
                        InAppEnabled = saved.InAppEnabled,
                        IsCustomized = true,
                    };
                }

                // Chưa tuỳ chỉnh ⇒ kế thừa công tắc kênh toàn cục.
                return new NotificationCategoryPreferenceDto
                {
                    Category = category,
                    CategoryName = category.ToString(),
                    PushEnabled = channelDto.PushEnabled,
                    EmailEnabled = channelDto.EmailEnabled,
                    SmsEnabled = channelDto.SmsEnabled,
                    InAppEnabled = channelDto.InAppEnabled,
                    IsCustomized = false,
                };
            })
            .ToList();

        return new NotificationPreferenceMatrixResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new NotificationPreferenceMatrixDto { Channels = channelDto, Categories = rows },
        };
    }

    internal static NotificationPreferenceDto MapChannelDto(NotificationPreference? pref) =>
        pref is null
            ? new NotificationPreferenceDto
            {
                PushEnabled = true,
                EmailEnabled = true,
                SmsEnabled = false,
                InAppEnabled = true,
                TimeZone = "Asia/Ho_Chi_Minh",
                NotifyOnChat = true,
                NotifyOnMention = true,
                NotifyOnReaction = false,
            }
            : new NotificationPreferenceDto
            {
                PushEnabled = pref.PushEnabled,
                EmailEnabled = pref.EmailEnabled,
                SmsEnabled = pref.SmsEnabled,
                InAppEnabled = pref.InAppEnabled,
                QuietHoursStart = pref.QuietHoursStart?.ToString("HH:mm"),
                QuietHoursEnd = pref.QuietHoursEnd?.ToString("HH:mm"),
                TimeZone = pref.TimeZone,
                NotifyOnChat = pref.NotifyOnChat,
                NotifyOnMention = pref.NotifyOnMention,
                NotifyOnReaction = pref.NotifyOnReaction,
                DigestWindowMinutes = pref.DigestWindowMinutes,
            };
}

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — vá từng dòng của ma trận.
///
/// Chỉ đụng tới nhóm có trong request; nhóm khác giữ nguyên. Sau khi ghi phải **xoá cache** của
/// đúng những nhóm đó, nếu không dispatcher còn dùng bản cũ tới 5 phút và người dùng tưởng
/// thiết lập không ăn.
/// </summary>
public class UpdateNotificationCategoryPreferenceCommandHandler
    : IRequestHandler<UpdateNotificationCategoryPreferenceCommand, NotificationPreferenceMatrixResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IMediator _mediator;

    public UpdateNotificationCategoryPreferenceCommandHandler(
        INotificationUnitOfWork unitOfWork, ICacheService cache, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _mediator = mediator;
    }

    public async Task<NotificationPreferenceMatrixResponse> Handle(
        UpdateNotificationCategoryPreferenceCommand request, CancellationToken cancellationToken)
    {
        var categories = request.Items.Select(i => i.Category).ToList();

        var existing = await _unitOfWork.NotificationCategoryPreferences.GetAllAsync()
            .Where(p => p.UserId == request.UserId && !p.IsDeleted && categories.Contains(p.Category))
            .ToListAsync(cancellationToken);

        var byCategory = existing.ToDictionary(p => p.Category);
        var now = DateTime.UtcNow;

        foreach (var item in request.Items)
        {
            if (byCategory.TryGetValue(item.Category, out var entity))
            {
                entity.PushEnabled = item.PushEnabled;
                entity.EmailEnabled = item.EmailEnabled;
                entity.SmsEnabled = item.SmsEnabled;
                entity.InAppEnabled = item.InAppEnabled;
                entity.UpdatedAt = now;
                _unitOfWork.NotificationCategoryPreferences.UpdateAsync(entity);
            }
            else
            {
                await _unitOfWork.NotificationCategoryPreferences.AddAsync(new NotificationCategoryPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    Category = item.Category,
                    PushEnabled = item.PushEnabled,
                    EmailEnabled = item.EmailEnabled,
                    SmsEnabled = item.SmsEnabled,
                    InAppEnabled = item.InAppEnabled,
                    CreatedAt = now,
                });
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Dispatcher cache tuỳ chọn nhóm 5 phút — không xoá thì thiết lập mới chưa có hiệu lực ngay.
        foreach (var category in categories)
            await _cache.RemoveAsync($"notif_cat_pref:{request.UserId}:{(int)category}", cancellationToken);

        var matrix = await _mediator.Send(
            new GetNotificationPreferenceMatrixQuery { UserId = request.UserId }, cancellationToken);

        matrix.Message = "Cập nhật tuỳ chọn theo nhóm thành công.";
        return matrix;
    }
}
