using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// Kênh Push duy nhất mà dispatcher nhìn thấy. Bên trong nó rẽ sang
/// <see cref="SignalRPushChannel"/> và/hoặc <see cref="ExpoPushChannel"/> theo
/// <see cref="PushTransportEnum"/> đang cấu hình (ADR-0019).
///
/// <para><b>Vì sao phải gộp thay vì đăng ký cả hai làm <see cref="INotificationChannel"/>:</b>
/// dispatcher chọn kênh bằng <c>_channels.FirstOrDefault(c =&gt; c.ChannelType == …)</c>, nên đăng ký
/// hai lớp cùng <c>ChannelType = Push</c> thì cái đăng ký sau bị bỏ qua im lặng, và thứ tự đăng ký
/// trở thành cấu hình ngầm. Gộp ở đây khiến việc "gửi mấy đường" là một quyết định tường minh, có
/// một chỗ duy nhất để đọc.</para>
///
/// <para><b>Device token nạp ở đây, không phải ở dispatcher:</b> chỉ đường Expo mới cần token. Để
/// dispatcher nạp thì mỗi lần gửi đều tốn một truy vấn kể cả khi hệ thống đang chạy thuần SignalR —
/// và dispatcher phải biết chi tiết của một transport cụ thể, thứ nó không nên biết.</para>
///
/// <para><b>Quy ước kết quả:</b> thành công khi ÍT NHẤT MỘT đường thành công. Đây là ý nghĩa đúng
/// của <c>Both</c>: máy đang mở app nhận qua SignalR, máy đã tắt app nhận qua Expo; hỏng một đường
/// không có nghĩa là người dùng không nhận được gì. Chỉ khi mọi đường được chọn đều hỏng mới báo
/// thất bại, kèm lý do gộp của từng đường để log truy được nguyên nhân.</para>
/// </summary>
public sealed class CompositePushChannel : INotificationChannel
{
    private readonly ISignalRPushChannel _signalR;
    private readonly IExpoPushChannel _expo;
    private readonly IPushTransportSettingService _transportSetting;
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ILogger<CompositePushChannel> _logger;

    public CompositePushChannel(
        ISignalRPushChannel signalR,
        IExpoPushChannel expo,
        IPushTransportSettingService transportSetting,
        INotificationUnitOfWork unitOfWork,
        ILogger<CompositePushChannel> logger)
    {
        _signalR = signalR;
        _expo = expo;
        _transportSetting = transportSetting;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Push;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var transport = await _transportSetting.GetAsync(ct);

        var useSignalR = transport is PushTransportEnum.SignalR or PushTransportEnum.Both;
        var useExpo = transport is PushTransportEnum.Expo or PushTransportEnum.Both;

        var errors = new List<string>();
        var anySuccess = false;

        if (useSignalR)
        {
            var result = await SafeSendAsync(_signalR, request, "SignalR", ct);
            if (result.Success)
                anySuccess = true;
            else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                errors.Add($"SignalR: {result.ErrorMessage}");
        }

        if (useExpo)
        {
            var expoRequest = await BuildExpoRequestAsync(request, ct);

            if (expoRequest.ExpoTokens is not { Count: > 0 })
            {
                // Không có thiết bị nào đăng ký. Với Both thì đây là chuyện bình thường (người dùng
                // chỉ xài web) nên chỉ ghi Debug; với Expo-only thì đó là lý do thất bại thật sự.
                const string reason = "The recipient has no active device token registered.";
                if (transport == PushTransportEnum.Expo)
                    errors.Add($"Expo: {reason}");
                else
                    _logger.LogDebug(
                        "CompositePush: {UserId} không có device token — bỏ qua đường Expo, đã gửi qua SignalR.",
                        request.UserId);
            }
            else
            {
                var result = await SafeSendAsync(_expo, expoRequest, "Expo", ct);
                if (result.Success)
                    anySuccess = true;
                else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                    errors.Add($"Expo: {result.ErrorMessage}");
            }
        }

        if (anySuccess)
            return new ChannelResult(true);

        return new ChannelResult(
            false,
            errors.Count > 0
                ? string.Join("; ", errors)
                : $"No push transport is available (transport = {transport}).");
    }

    /// <summary>
    /// Một đường ném exception không được kéo đường còn lại xuống cùng. Dispatcher đã có lớp bắt
    /// exception riêng, nhưng ở đó nó bọc CẢ cụm — mất luôn cơ hội để đường thứ hai chạy.
    /// </summary>
    private async Task<ChannelResult> SafeSendAsync(
        INotificationChannel channel, SendRequest request, string label, CancellationToken ct)
    {
        try
        {
            return await channel.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "CompositePush: đường {Transport} ném exception cho notification {NotificationId}.",
                label, request.NotificationId);
            return new ChannelResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Bản sao của request có kèm device token. Tạo bản sao thay vì gán thẳng vào request gốc để
    /// đường SignalR (và người gọi ở dispatcher) không nhìn thấy field chỉ có nghĩa với Expo.
    /// </summary>
    private async Task<SendRequest> BuildExpoRequestAsync(SendRequest request, CancellationToken ct)
    {
        var tokens = await _unitOfWork.DeviceTokens.GetAllAsync()
            .Where(dt => dt.UserId == request.UserId && !dt.IsDeleted && dt.IsActive)
            .Select(dt => dt.Token)
            .ToListAsync(ct);

        return new SendRequest
        {
            NotificationId = request.NotificationId,
            UserId = request.UserId,
            Type = request.Type,
            Title = request.Title,
            Body = request.Body,
            PayloadJson = request.PayloadJson,
            IsCritical = request.IsCritical,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            CreatedAt = request.CreatedAt,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            ExpoToken = tokens.Count > 0 ? tokens[0] : null,
            ExpoTokens = tokens,
        };
    }
}
