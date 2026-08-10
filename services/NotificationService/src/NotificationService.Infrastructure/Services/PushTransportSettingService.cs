using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using SharedContracts.Interfaces;

namespace NotificationService.Infrastructure.Services;

/// <summary>
/// Đọc/ghi <see cref="PushTransportEnum"/> từ bảng <c>notification_settings</c>, có cache ngắn.
///
/// <para>Mọi lỗi khi ĐỌC đều rơi về giá trị mặc định thay vì ném lên: hàm này nằm trên đường đi của
/// từng lần gửi thông báo, nên một sự cố cache hay một dòng cấu hình bị gõ sai không được phép làm
/// đứng cả kênh Push. Lỗi khi GHI thì ném bình thường — người vận hành cần biết là chưa đổi được.</para>
/// </summary>
public class PushTransportSettingService : IPushTransportSettingService
{
    internal const string CacheKey = "notif:setting:push_transport";

    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly NotificationPushOptions _options;
    private readonly ILogger<PushTransportSettingService> _logger;

    public PushTransportSettingService(
        INotificationUnitOfWork unitOfWork,
        ICacheService cache,
        IOptions<NotificationPushOptions> options,
        ILogger<PushTransportSettingService> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PushTransportEnum> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Cache chuỗi chứ không phải enum: enum là kiểu giá trị nên cache-miss và giá trị 0
            // không phân biệt được, mà 0 lại không phải transport hợp lệ nào.
            var cached = await _cache.GetAsync<string>(CacheKey, cancellationToken);
            if (TryParseTransport(cached, out var cachedTransport))
                return cachedTransport;
        }
        catch (Exception ex)
        {
            // Cache hỏng thì đọc thẳng DB, không được vì thế mà ngừng gửi push.
            _logger.LogWarning(ex, "PushTransport: đọc cache lỗi — đọc thẳng từ database.");
        }

        var transport = await ReadFromDatabaseAsync(cancellationToken);

        try
        {
            await _cache.SetAsync(CacheKey, transport.ToString(),
                TimeSpan.FromSeconds(Math.Max(1, _options.CacheSeconds)), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PushTransport: ghi cache lỗi — lần đọc sau sẽ lại vào database.");
        }

        return transport;
    }

    public async Task SetAsync(PushTransportEnum transport, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(transport))
            throw new ArgumentOutOfRangeException(nameof(transport), transport, "Đường vận chuyển push không hợp lệ.");

        var row = await _unitOfWork.NotificationSettings.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Key == NotificationSettingKeys.PushTransport && !x.IsDeleted, cancellationToken);

        if (row is null)
        {
            await _unitOfWork.NotificationSettings.AddAsync(new NotificationSetting
            {
                Id = Guid.NewGuid(),
                Key = NotificationSettingKeys.PushTransport,
                Value = transport.ToString(),
                Description = "Đường vận chuyển kênh Push: SignalR (tự vận hành) / Expo / Both.",
            });
        }
        else
        {
            row.Value = transport.ToString();
            _unitOfWork.NotificationSettings.UpdateAsync(row);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Xoá cache SAU khi đã lưu chắc chắn. Xoá trước mà lưu hỏng thì lần đọc kế tiếp nạp lại
        // đúng giá trị cũ — không sai, nhưng xoá sau giữ cho thứ tự dễ suy luận hơn.
        try
        {
            await _cache.RemoveAsync(CacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PushTransport: đã lưu {Transport} nhưng xoá cache lỗi — giá trị mới có hiệu lực chậm nhất sau {Seconds}s.",
                transport, _options.CacheSeconds);
        }
    }

    private async Task<PushTransportEnum> ReadFromDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _unitOfWork.NotificationSettings.GetAllAsync()
                .Where(x => x.Key == NotificationSettingKeys.PushTransport && !x.IsDeleted)
                .Select(x => x.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(raw))
                return _options.DefaultTransport;

            if (TryParseTransport(raw, out var parsed))
                return parsed;

            _logger.LogWarning(
                "PushTransport: giá trị '{Raw}' trong notification_settings không hợp lệ — dùng mặc định {Default}.",
                raw, _options.DefaultTransport);
            return _options.DefaultTransport;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PushTransport: đọc database lỗi — dùng mặc định {Default} để không chặn đường gửi push.",
                _options.DefaultTransport);
            return _options.DefaultTransport;
        }
    }

    /// <summary>
    /// Chấp nhận cả tên ("Both") lẫn số ("3"): giá trị có thể do màn hình Admin ghi, do seed, hoặc
    /// do người vận hành sửa tay thẳng trong database.
    /// </summary>
    private static bool TryParseTransport(string? raw, out PushTransportEnum transport)
    {
        transport = default;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!Enum.TryParse(raw, ignoreCase: true, out transport) || !Enum.IsDefined(transport))
        {
            transport = default;
            return false;
        }

        return true;
    }
}
