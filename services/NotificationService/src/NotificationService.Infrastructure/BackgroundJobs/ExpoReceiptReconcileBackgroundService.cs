using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using SharedInfrastructure.Metrics;

namespace NotificationService.Infrastructure.BackgroundJobs;

/// <summary>
/// Sprint 6.3 NOTI3-02 (#702) — đối soát biên nhận Expo.
///
/// **Vấn đề gốc (§17.6.3, R-38):** Expo Push là relay bất đồng bộ. HTTP 200 + ticket
/// <c>status:"ok"</c> chỉ chứng minh *Expo nhận request*, KHÔNG chứng minh *thiết bị nhận được*.
/// Kết quả thật nằm ở <c>POST /push/getReceipts</c> — Expo giữ receipt khoảng 24 giờ.
/// Trước sprint này hệ thống vứt ticket id đi, nên:
/// <list type="bullet">
/// <item>token đã gỡ app vẫn nằm mãi trong DB, mọi lần gửi sau đều tiêu tốn quota vô ích;</item>
/// <item><c>Notification.Status = Sent</c> là con số dối — không ai biết bao nhiêu % thực sự tới nơi.</item>
/// </list>
///
/// **Cách chạy:** mỗi <c>PollIntervalSeconds</c>, lấy các receipt <c>Pending</c> đã đủ "chín"
/// (<c>MinAgeMinutes</c>, mặc định 15' — hỏi sớm hơn thì Expo hầu như trả rỗng), hỏi Expo theo lô
/// <c>BatchSize</c> (Expo cho tối đa 1000 id / request), rồi:
/// <list type="bullet">
/// <item><c>ok</c> → <see cref="NotificationStatusEnum.Delivered"/> + audit <c>PushDelivered</c> (NOTI3-14);</item>
/// <item><c>DeviceNotRegistered</c> → tắt device token (nguồn gốc của rác);</item>
/// <item><c>MessageTooBig</c> / <c>MessageRateExceeded</c> / <c>MismatchSenderId</c> /
/// <c>InvalidCredentials</c> → ghi lỗi để Grafana thấy, không retry mù.</item>
/// </list>
///
/// Receipt quá <c>MaxCheckAttempts</c> lần hỏi vẫn rỗng ⇒ đánh
/// <see cref="PushReceiptStatusEnum.Expired"/>: cửa sổ 24h của Expo đã đóng, hỏi thêm cũng vô nghĩa.
/// </summary>
public class ExpoReceiptReconcileBackgroundService : BackgroundService
{
    private const string ReceiptsUrl = "https://exp.host/--/api/v2/push/getReceipts";

    /// <summary>Giới hạn Expo: tối đa 1000 ticket id trong một lần hỏi receipt.</summary>
    private const int ExpoMaxIdsPerRequest = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ExpoReceiptOptions _options;
    private readonly ILogger<ExpoReceiptReconcileBackgroundService> _logger;

    public ExpoReceiptReconcileBackgroundService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IOptions<ExpoReceiptOptions> options,
        ILogger<ExpoReceiptReconcileBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "ExpoReceiptReconcile: TẮT theo cấu hình — không đối soát giao hàng push (Notification:ExpoReceipt:Enabled).");
            return;
        }

        _logger.LogInformation(
            "ExpoReceiptReconcile: bật — poll mỗi {Interval}s, chỉ hỏi receipt cũ hơn {MinAge}', lô {Batch}.",
            _options.PollIntervalSeconds, _options.MinAgeMinutes, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Không được để worker chết: một vòng lỗi không phải lý do bỏ đối soát vĩnh viễn.
                _logger.LogError(ex, "ExpoReceiptReconcile: vòng đối soát lỗi — thử lại ở chu kỳ sau.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _options.PollIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Một vòng đối soát. Tách riêng để test gọi trực tiếp, không phải chờ timer.</summary>
    public async Task<int> ReconcileOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<INotificationUnitOfWork>();
        var auditWriter = scope.ServiceProvider.GetService<INotificationAuditWriter>();

        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(0, _options.MinAgeMinutes));
        var batchSize = Math.Clamp(_options.BatchSize, 1, ExpoMaxIdsPerRequest);

        var pending = await unitOfWork.PushReceipts.GetAllAsync()
            .Where(x => !x.IsDeleted
                        && x.Status == PushReceiptStatusEnum.Pending
                        && x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return 0;

        var byTicketId = new Dictionary<string, PushReceipt>(StringComparer.Ordinal);
        foreach (var receipt in pending)
            byTicketId[receipt.TicketId] = receipt;

        var receipts = await FetchReceiptsAsync(byTicketId.Keys.ToList(), ct);

        // Expo không trả về ticket nào ⇒ chưa có kết quả. Chỉ tăng số lần hỏi để biết khi nào bỏ cuộc,
        // KHÔNG đánh lỗi (đánh lỗi ở đây sẽ biến "chưa biết" thành "thất bại" — sai lệch số liệu).
        if (receipts is null)
        {
            foreach (var receipt in pending)
                BumpAttempt(receipt, unitOfWork);

            await unitOfWork.SaveChangesAsync(ct);
            return 0;
        }

        var resolved = 0;

        foreach (var receipt in pending)
        {
            if (!receipts.TryGetValue(receipt.TicketId, out var result))
            {
                BumpAttempt(receipt, unitOfWork);
                continue;
            }

            receipt.CheckedAt = DateTime.UtcNow;
            receipt.CheckAttemptCount += 1;
            resolved++;

            if (string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                receipt.Status = PushReceiptStatusEnum.Ok;
                unitOfWork.PushReceipts.UpdateAsync(receipt);

                AppMetrics.ExpoReceiptTotal.WithLabels("ok", "none").Inc();

                await MarkDeliveredAsync(unitOfWork, auditWriter, receipt, ct);
                continue;
            }

            var errorCode = result.Details?.Error ?? "Unknown";

            receipt.Status = PushReceiptStatusEnum.Error;
            receipt.ErrorCode = errorCode;
            receipt.ErrorMessage = Truncate(result.Message, 1000);
            unitOfWork.PushReceipts.UpdateAsync(receipt);

            AppMetrics.ExpoReceiptTotal.WithLabels("error", errorCode).Inc();

            await HandleErrorAsync(unitOfWork, auditWriter, receipt, errorCode, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (resolved > 0)
        {
            _logger.LogInformation(
                "ExpoReceiptReconcile: đối soát xong {Resolved}/{Total} receipt.", resolved, pending.Count);
        }

        return resolved;
    }

    /// <summary>
    /// Chưa có kết quả — tăng số lần hỏi. Chạm trần thì đánh <c>Expired</c>: Expo chỉ giữ receipt 24h,
    /// hỏi mãi chỉ tốn request mà không bao giờ có câu trả lời.
    /// </summary>
    private void BumpAttempt(PushReceipt receipt, INotificationUnitOfWork unitOfWork)
    {
        receipt.CheckAttemptCount += 1;
        receipt.CheckedAt = DateTime.UtcNow;

        if (receipt.CheckAttemptCount >= Math.Max(1, _options.MaxCheckAttempts))
        {
            receipt.Status = PushReceiptStatusEnum.Expired;
            AppMetrics.ExpoReceiptTotal.WithLabels("expired", "none").Inc();

            _logger.LogWarning(
                "ExpoReceiptReconcile: bỏ cuộc với ticket {TicketId} sau {Attempts} lần hỏi — "
                + "cửa sổ 24h của Expo đã đóng.",
                receipt.TicketId, receipt.CheckAttemptCount);
        }

        unitOfWork.PushReceipts.UpdateAsync(receipt);
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-14 (#714) — nâng notification lên <c>Delivered</c>.
    /// KHÔNG hạ cấp: nếu user đã đọc (<c>Read</c>/<c>Opened</c>) trước khi receipt về thì giữ nguyên,
    /// vì đọc được nghĩa là chắc chắn đã nhận.
    /// </summary>
    private async Task MarkDeliveredAsync(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter? auditWriter,
        PushReceipt receipt,
        CancellationToken ct)
    {
        var notification = await unitOfWork.Notifications.GetAllAsync()
            .Where(x => !x.IsDeleted && x.Id == receipt.NotificationId)
            .FirstOrDefaultAsync(ct);

        if (notification is null)
            return;

        if (notification.Status is NotificationStatusEnum.Read
            or NotificationStatusEnum.Opened
            or NotificationStatusEnum.Delivered)
        {
            return;
        }

        notification.Status = NotificationStatusEnum.Delivered;
        unitOfWork.Notifications.UpdateAsync(notification);

        if (auditWriter is not null)
        {
            await auditWriter.WriteAsync(
                NotificationAuditActionEnum.PushDelivered,
                notification.Id,
                notification.UserId,
                isSuccess: true,
                reason: null,
                metadata: new Dictionary<string, object?>
                {
                    ["ticketId"] = receipt.TicketId,
                    ["channel"] = nameof(NotificationChannelEnum.Push),
                    ["type"] = notification.Type.ToString(),
                },
                ct: ct);
        }
    }

    /// <summary>Xử lý theo từng mã lỗi Expo — mỗi mã có nguyên nhân và cách chữa khác nhau.</summary>
    private async Task HandleErrorAsync(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter? auditWriter,
        PushReceipt receipt,
        string errorCode,
        CancellationToken ct)
    {
        switch (errorCode)
        {
            case "DeviceNotRegistered":
                // Người dùng gỡ app / đổi máy. Đây là nguồn rác lớn nhất: không tắt thì mọi lần gửi
                // sau đều thất bại và vẫn tính vào quota.
                await DeactivateTokenAsync(unitOfWork, receipt.DeviceToken, ct);
                break;

            case "MessageTooBig":
                // Lẽ ra ExpoPushChannel đã chặn ở nguồn (trần 4096 byte). Lọt tới đây nghĩa là guard
                // hụt — cần biết ngay chứ không im lặng.
                _logger.LogError(
                    "ExpoReceiptReconcile: notification {NotificationId} bị Expo từ chối vì MessageTooBig — "
                    + "guard 4KB ở ExpoPushChannel không chặn được, cần rà lại.",
                    receipt.NotificationId);
                break;

            case "MessageRateExceeded":
                // Vượt trần ~600 message/giây của project. Không phải lỗi nội dung — gửi lại được.
                _logger.LogWarning(
                    "ExpoReceiptReconcile: chạm trần tốc độ Expo (600 msg/s) — notification {NotificationId}. "
                    + "Cân nhắc giãn nhịp dispatcher nếu lặp lại.",
                    receipt.NotificationId);
                break;

            case "MismatchSenderId":
            case "InvalidCredentials":
                // Sai cấu hình FCM/APNs — mọi push đều hỏng cho tới khi người vận hành sửa.
                _logger.LogCritical(
                    "ExpoReceiptReconcile: cấu hình push hỏng ({ErrorCode}) — TOÀN BỘ push đang thất bại.",
                    errorCode);
                break;

            default:
                _logger.LogWarning(
                    "ExpoReceiptReconcile: mã lỗi Expo chưa xử lý riêng '{ErrorCode}' (notification {NotificationId}).",
                    errorCode, receipt.NotificationId);
                break;
        }

        await MarkFailedAsync(unitOfWork, auditWriter, receipt, errorCode, ct);
    }

    /// <summary>
    /// Notification chỉ chuyển sang <c>Failed</c> khi KHÔNG còn thiết bị nào nhận được.
    /// Người dùng có 3 máy, 1 máy gỡ app — đó không phải là thất bại giao hàng.
    /// </summary>
    private static async Task MarkFailedAsync(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter? auditWriter,
        PushReceipt receipt,
        string errorCode,
        CancellationToken ct)
    {
        var siblings = await unitOfWork.PushReceipts.GetAllAsync()
            .Where(x => !x.IsDeleted && x.NotificationId == receipt.NotificationId)
            .ToListAsync(ct);

        var anyDelivered = siblings.Any(x => x.Status == PushReceiptStatusEnum.Ok);
        var anyStillPending = siblings.Any(x => x.Id != receipt.Id && x.Status == PushReceiptStatusEnum.Pending);

        if (anyDelivered || anyStillPending)
            return;

        var notification = await unitOfWork.Notifications.GetAllAsync()
            .Where(x => !x.IsDeleted && x.Id == receipt.NotificationId)
            .FirstOrDefaultAsync(ct);

        if (notification is null)
            return;

        // Đã đọc rồi thì rõ ràng đã nhận — không được ghi đè thành Failed.
        if (notification.Status is NotificationStatusEnum.Read
            or NotificationStatusEnum.Opened
            or NotificationStatusEnum.Delivered)
        {
            return;
        }

        notification.Status = NotificationStatusEnum.Failed;
        notification.FailureReason = $"Expo receipt: {errorCode}";
        unitOfWork.Notifications.UpdateAsync(notification);

        if (auditWriter is not null)
        {
            await auditWriter.WriteAsync(
                NotificationAuditActionEnum.PushDelivered,
                notification.Id,
                notification.UserId,
                isSuccess: false,
                reason: errorCode,
                metadata: new Dictionary<string, object?> { ["ticketId"] = receipt.TicketId },
                ct: ct);
        }
    }

    private static async Task DeactivateTokenAsync(
        INotificationUnitOfWork unitOfWork, string token, CancellationToken ct)
    {
        var deviceToken = await unitOfWork.DeviceTokens.GetAllAsync()
            .Where(x => !x.IsDeleted && x.Token == token && x.IsActive)
            .FirstOrDefaultAsync(ct);

        if (deviceToken is null)
            return;

        deviceToken.IsActive = false;
        unitOfWork.DeviceTokens.UpdateAsync(deviceToken);

        AppMetrics.ExpoTokenDeactivatedTotal.Inc();
    }

    /// <summary>
    /// Hỏi Expo. Trả <c>null</c> nghĩa là *không hỏi được* (mạng/HTTP lỗi) — khác hẳn với
    /// "hỏi được nhưng chưa có kết quả" (dictionary rỗng). Phân biệt hai ca này rất quan trọng:
    /// gộp lại sẽ khiến sự cố mạng bị đếm thành thất bại giao hàng.
    /// </summary>
    private async Task<Dictionary<string, ExpoReceiptResult>?> FetchReceiptsAsync(
        List<string> ticketIds, CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient("expo");
            var response = await client.PostAsJsonAsync(ReceiptsUrl, new { ids = ticketIds }, ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<ExpoReceiptResponse>(cancellationToken: ct);
            return payload?.Data ?? new Dictionary<string, ExpoReceiptResult>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ExpoReceiptReconcile: không gọi được getReceipts cho {Count} ticket — giữ nguyên Pending.",
                ticketIds.Count);
            return null;
        }
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed class ExpoReceiptResponse
    {
        /// <summary>Expo trả về map <c>{ "<ticketId>": { status, message, details } }</c>.</summary>
        [JsonPropertyName("data")]
        public Dictionary<string, ExpoReceiptResult>? Data { get; set; }
    }

    private sealed class ExpoReceiptResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("details")]
        public ExpoReceiptDetails? Details { get; set; }
    }

    private sealed class ExpoReceiptDetails
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
