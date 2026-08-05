using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Infrastructure.Channels;

/// <summary>
/// Expo push channel — HTTP POST tới <c>https://exp.host/--/api/v2/push/send</c>.
/// Polly retry 3 lần (exponential backoff) được cấu hình ở DI level (named client "expo").
/// Nếu Expo trả DeviceNotRegistered → deactivate token trong DB.
///
/// Sprint 6.2 NOTI-16 (#687) — gửi BATCH: Expo nhận mảng tối đa 100 message / request.
/// Trước đây mỗi device token là 1 HTTP call; user có 3 thiết bị = 3 request cho cùng 1 notification.
///
/// Sprint 6.3 NOTI3-02 (#702) — lưu ticket id vào <c>push_receipts</c> để
/// <c>ExpoReceiptReconcileBackgroundService</c> đối soát sau, và chặn payload vượt trần 4KB của Expo.
/// </summary>
public class ExpoPushChannel : INotificationChannel
{
    private const string ExpoUrl = "https://exp.host/--/api/v2/push/send";

    /// <summary>Giới hạn Expo Push API: tối đa 100 message trong 1 request.</summary>
    private const int MaxBatchSize = 100;

    /// <summary>
    /// Sprint 6.3 NOTI3-02 (#702) — trần payload của một message Expo: 4096 byte.
    /// Vượt trần thì Expo trả lỗi <c>MessageTooBig</c> ở receipt, tức là *sau* khi đã báo 200 OK —
    /// người dùng không nhận được gì mà hệ thống vẫn tưởng đã gửi. Chặn ngay tại nguồn rẻ hơn nhiều.
    /// </summary>
    private const int MaxMessageBytes = 4096;

    /// <summary>Chừa chỗ cho các field cố định (to/title/sound/priority/channelId) khi cắt bớt.</summary>
    private const int MessageOverheadBytes = 512;

    /// <summary>
    /// Sprint 6.3 NOTI3-14 (#714) — khoá trong <c>data</c> mang id của chính notification.
    /// Client mobile cần nó để gọi <c>PATCH /api/notifications/{id}/opened</c> khi user bấm push;
    /// <see cref="SendRequest.PayloadJson"/> do consumer viết chỉ chứa context nghiệp vụ
    /// (<c>chatId</c>, <c>ticketId</c>, …) nên không bao giờ có sẵn id này.
    /// </summary>
    private const string NotificationIdKey = "notificationId";
    private const string EntityTypeKey = "entityType";
    private const string EntityIdKey = "entityId";

    private readonly IHttpClientFactory _httpFactory;
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ILogger<ExpoPushChannel> _logger;

    public ExpoPushChannel(
        IHttpClientFactory httpFactory,
        INotificationUnitOfWork unitOfWork,
        ILogger<ExpoPushChannel> logger)
    {
        _httpFactory = httpFactory;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public NotificationChannelEnum ChannelType => NotificationChannelEnum.Push;

    public async Task<ChannelResult> SendAsync(SendRequest request, CancellationToken ct = default)
    {
        var tokens = ResolveTokens(request);

        if (tokens.Count == 0)
        {
            _logger.LogWarning("ExpoPushChannel: no Expo token for notification {NotificationId}", request.NotificationId);
            return new ChannelResult(false, "No Expo token");
        }

        var data = BuildData(request);

        // Sprint 6.3 NOTI3-02 (#702) — ép message vừa trần 4KB TRƯỚC khi gửi.
        var (title, body, dataToSend) = FitWithinSizeLimit(request, data);

        var errors = new List<string>();
        var anySuccess = false;
        var receipts = new List<PushReceipt>();

        for (var offset = 0; offset < tokens.Count; offset += MaxBatchSize)
        {
            var chunk = tokens.Skip(offset).Take(MaxBatchSize).ToList();
            var messages = chunk.Select(token => new
            {
                to = token,
                title,
                body,
                data = dataToSend,
                sound = "default",
                priority = request.IsCritical ? "high" : "normal",
                channelId = ResolveChannelId(request)
            }).ToArray();

            try
            {
                var client = _httpFactory.CreateClient("expo");
                var response = await client.PostAsJsonAsync(ExpoUrl, messages, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<ExpoBatchResponse>(cancellationToken: ct);
                var tickets = result?.Data ?? [];

                for (var i = 0; i < chunk.Count; i++)
                {
                    // Expo trả tickets theo đúng thứ tự message gửi lên.
                    var ticket = i < tickets.Count ? tickets[i] : null;

                    if (ticket is null)
                    {
                        // Không có ticket tương ứng → coi như gửi được (Expo đã nhận 2xx),
                        // nhưng ghi log để điều tra nếu lặp lại.
                        anySuccess = true;
                        continue;
                    }

                    if (ticket.Status == "error")
                    {
                        var errorCode = ticket.Details?.Error;
                        _logger.LogWarning(
                            "ExpoPushChannel: Expo error {Error} for token {Token} notification {NotificationId}",
                            errorCode, chunk[i], request.NotificationId);

                        if (errorCode == "DeviceNotRegistered")
                            await DeactivateTokenAsync(chunk[i], ct);

                        errors.Add(errorCode ?? "Expo error");
                    }
                    else
                    {
                        anySuccess = true;

                        // Sprint 6.3 NOTI3-02 (#702) — ticket "ok" mới chỉ nghĩa là Expo NHẬN được.
                        // Lưu lại id để worker hỏi kết quả giao hàng thật sau ~15 phút.
                        if (!string.IsNullOrWhiteSpace(ticket.Id))
                        {
                            receipts.Add(new PushReceipt
                            {
                                Id = Guid.NewGuid(),
                                NotificationId = request.NotificationId,
                                UserId = request.UserId,
                                TicketId = ticket.Id!,
                                DeviceToken = chunk[i],
                                Status = PushReceiptStatusEnum.Pending,
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ExpoPushChannel: failed to send push batch for notification {NotificationId}", request.NotificationId);
                errors.Add(ex.Message);
            }
        }

        await PersistReceiptsAsync(receipts, request.NotificationId, ct);

        // Gửi được ít nhất 1 thiết bị = coi như notification đã bàn giao cho Expo.
        // Giao hàng THẬT chỉ được xác nhận khi ExpoReceiptReconcileBackgroundService đối soát receipt.
        if (anySuccess)
            return new ChannelResult(true);

        return new ChannelResult(false, errors.Count > 0 ? string.Join("; ", errors.Distinct()) : "Expo push failed");
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-14 (#714) — dựng <c>data</c> gửi kèm push.
    ///
    /// Luôn có <see cref="NotificationIdKey"/>, cộng thêm context nghiệp vụ từ
    /// <see cref="SendRequest.PayloadJson"/>. Payload hỏng thì vẫn gửi được id — mất deep link còn
    /// hơn mất luôn khả năng báo "đã mở".
    /// </summary>
    private Dictionary<string, object?> BuildData(SendRequest request)
    {
        var data = new Dictionary<string, object?>
        {
            [NotificationIdKey] = request.NotificationId,
            ["entityType"] = request.EntityType,
            ["entityId"] = request.EntityId,
            ["createdAt"] = request.CreatedAt,
            ["notificationType"] = (int)request.Type,
        };

        // Gán cặp định tuyến NGAY, trước mọi nhánh thoát sớm. Đặt nó ở cuối hàm là bỏ sót trường
        // hợp PayloadJson rỗng — mà đó lại là trường hợp thường gặp, và chính là lúc client cần
        // entityType nhất vì không còn khoá payload nào để đoán.
        ApplyRoutingKeys(data, request);

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            return data;

        try
        {
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.PayloadJson);
            if (payload is not null)
            {
                foreach (var (key, value) in payload)
                {
                    // Payload do consumer viết KHÔNG được ghi đè id thật của notification.
                    if (string.Equals(key, NotificationIdKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    data[key] = value;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "ExpoPushChannel: payload của notification {NotificationId} không phải JSON object hợp lệ — "
                + "gửi kèm mỗi notificationId.",
                request.NotificationId);
        }

        // Gọi LẠI sau vòng lặp payload: consumer có thể đã ghi một khoá trùng tên vào payload, và
        // cặp định tuyến phải lấy từ bản ghi notification chứ không phải từ chữ consumer tự viết.
        ApplyRoutingKeys(data, request);

        return data;
    }

    private static string ResolveChannelId(SendRequest request)
    {
        if (request.IsCritical)
            return "alerts-critical";

        return request.Type is NotificationTypeEnum.ChatCreated
            or NotificationTypeEnum.ChatMentioned
            or NotificationTypeEnum.ChatReacted
            ? "chat-messages"
            : "alerts-default";
    }

    /// <summary>
    /// Ghi cặp <c>entityType</c>/<c>entityId</c> — thứ client dùng để chọn màn hình khi người dùng
    /// bấm vào push. Lấy từ bản ghi notification nên luôn khớp với danh sách trong ứng dụng.
    /// </summary>
    private static void ApplyRoutingKeys(Dictionary<string, object?> data, SendRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.EntityType))
            data[EntityTypeKey] = request.EntityType;

        if (request.EntityId is not null)
            data[EntityIdKey] = request.EntityId;
    }

    /// <summary>
    /// Sprint 6.3 NOTI3-02 (#702) — ép một message vừa trần 4096 byte của Expo.
    ///
    /// Thứ tự hy sinh: bỏ context nghiệp vụ trong <c>data</c> trước (mất deep link),
    /// rồi mới cắt <c>body</c> — vì tiêu đề + nội dung là thứ người dùng thực sự đọc.
    ///
    /// Sprint 6.3 NOTI3-14 (#714): trước đây bỏ SẠCH <c>data</c>. Nhưng mất
    /// <see cref="NotificationIdKey"/> nghĩa là client không báo được "đã mở" cho đúng những push
    /// dài nhất — thứ đáng đo nhất. Nay giữ lại bản tối thiểu (chỉ id, ~60 byte, luôn vừa trần).
    /// </summary>
    private (string Title, string Body, object? Data) FitWithinSizeLimit(
        SendRequest request, Dictionary<string, object?> data)
    {
        var title = request.Title ?? string.Empty;
        var body = request.Body ?? string.Empty;

        if (EstimateBytes(title, body, data) <= MaxMessageBytes)
            return (title, body, data);

        if (data.Count > 1)
        {
            _logger.LogWarning(
                "ExpoPushChannel: notification {NotificationId} vượt trần {Limit} byte — bỏ context nghiệp vụ, giữ notificationId.",
                request.NotificationId, MaxMessageBytes);
            data = new Dictionary<string, object?> { [NotificationIdKey] = request.NotificationId };

            if (EstimateBytes(title, body, data) <= MaxMessageBytes)
                return (title, body, data);
        }

        // Vẫn quá dài ⇒ cắt body. Trừ overhead cho các field cố định + tiêu đề.
        var budget = MaxMessageBytes - MessageOverheadBytes - Encoding.UTF8.GetByteCount(title);
        if (budget < 0)
            budget = 0;

        body = TruncateToBytes(body, budget);

        _logger.LogWarning(
            "ExpoPushChannel: notification {NotificationId} vẫn vượt trần — cắt body còn {Bytes} byte.",
            request.NotificationId, budget);

        return (title, body, data);
    }

    private static int EstimateBytes(string title, string body, object? data)
    {
        var size = MessageOverheadBytes
                   + Encoding.UTF8.GetByteCount(title)
                   + Encoding.UTF8.GetByteCount(body);

        if (data is not null)
            size += Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(data));

        return size;
    }

    /// <summary>Cắt chuỗi theo số BYTE (không phải ký tự) mà không làm vỡ ký tự UTF-8 nhiều byte.</summary>
    private static string TruncateToBytes(string value, int maxBytes)
    {
        if (maxBytes <= 0)
            return string.Empty;

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        // Cắt dần từ cuối cho tới khi vừa — tiếng Việt có dấu là 2-3 byte/ký tự nên không thể
        // cắt theo index byte trực tiếp (sẽ tạo ký tự hỏng).
        var chars = value.AsSpan();
        var take = Math.Min(chars.Length, maxBytes);

        while (take > 0 && Encoding.UTF8.GetByteCount(chars[..take]) > maxBytes)
            take--;

        return new string(chars[..take]).TrimEnd() + "…";
    }

    /// <summary>
    /// Lưu receipt. Lỗi ghi KHÔNG được làm hỏng kết quả gửi — push đã đi rồi, mất khả năng đối soát
    /// là thiệt hại nhỏ hơn nhiều so với báo ngược là gửi thất bại rồi gửi lại lần nữa.
    /// </summary>
    private async Task PersistReceiptsAsync(List<PushReceipt> receipts, Guid notificationId, CancellationToken ct)
    {
        if (receipts.Count == 0)
            return;

        try
        {
            foreach (var receipt in receipts)
                await _unitOfWork.PushReceipts.AddAsync(receipt);

            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ExpoPushChannel: không lưu được {Count} receipt của notification {NotificationId} — "
                + "mất khả năng đối soát giao hàng cho lần gửi này.",
                receipts.Count, notificationId);
        }
    }

    private static List<string> ResolveTokens(SendRequest request)
    {
        if (request.ExpoTokens is { Count: > 0 })
            return request.ExpoTokens.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();

        return string.IsNullOrWhiteSpace(request.ExpoToken) ? [] : [request.ExpoToken];
    }

    private async Task DeactivateTokenAsync(string token, CancellationToken ct)
    {
        var deviceToken = _unitOfWork.DeviceTokens
            .GetAllAsync()
            .Where(x => !x.IsDeleted && x.Token == token)
            .FirstOrDefault();

        if (deviceToken is null)
        {
            return;
        }

        deviceToken.IsActive = false;
        _unitOfWork.DeviceTokens.UpdateAsync(deviceToken);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    // Expo Push API response shapes — batch send trả "data" là MẢNG ticket.
    private sealed class ExpoBatchResponse
    {
        [JsonPropertyName("data")]
        public List<ExpoTicket>? Data { get; set; }
    }

    private sealed class ExpoTicket
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("details")]
        public ExpoTicketDetails? Details { get; set; }
    }

    private sealed class ExpoTicketDetails
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
