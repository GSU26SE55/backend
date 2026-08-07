using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmailService.Infrastructure.Services;

/// <summary>
/// Hiện thực <see cref="IEmailProvider"/> bằng Mailjet.
/// Sprint 6.3 NOTI3-05 (#705) — implement interface để về sau cắm provider thứ hai không phải
/// sửa business logic (xem R-44 và §17.6.3.5).
/// </summary>
public class EmailSenderService : IEmailProvider
{
    /// <inheritdoc />
    public string ProviderName => "mailjet";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<EmailSenderService>? _logger;

    public EmailSenderService(
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<EmailSenderService>? logger = null)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
        => SendAsync(to, subject, htmlBody, headers: null, cancellationToken);

    /// <inheritdoc />
    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Recipient email is required.", nameof(to));

        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(htmlBody))
            throw new ArgumentException("Body is required.", nameof(htmlBody));

        var apiKey = _configuration["MailJet:ApiKey"] ?? _configuration["Mailjet:ApiKey"];
        var apiSecret = _configuration["MailJet:ApiSecret"] ?? _configuration["Mailjet:ApiSecret"];
        var fromEmail = _configuration["MailJet:FromEmail"] ?? _configuration["Email:From"];
        var displayName = _configuration["MailJet:DisplayName"] ?? _configuration["Email:DisplayName"] ?? "Event Management";
        var endpoint = _configuration["MailJet:SendEndpoint"] ?? "https://api.mailjet.com/v3.1/send";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            throw new InvalidOperationException("MailJet API credentials are missing.");
        if (string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException("Sender email is missing. Configure Email:From or MailJet:FromEmail.");

        // Sprint 6.3 NOTI3-15 (#715) — Mailjet v3.1 nhận header tuỳ ý qua trường "Headers".
        // Bỏ trống hẳn trường này khi không có header, để payload của email giao dịch không đổi.
        var message = headers is { Count: > 0 }
            ? (object)new
            {
                From = new { Email = fromEmail, Name = displayName },
                To = new[] { new { Email = to } },
                Subject = subject,
                HTMLPart = htmlBody,
                Headers = headers.ToDictionary(kv => kv.Key, kv => kv.Value),
            }
            : new
            {
                From = new { Email = fromEmail, Name = displayName },
                To = new[] { new { Email = to } },
                Subject = subject,
                HTMLPart = htmlBody,
            };

        var payload = new { Messages = new[] { message } };

        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"MailJet send failed: {(int)response.StatusCode} - {responseBody}");

        // HTTP 200 KHÔNG có nghĩa là thư đã được nhận. Send API v3.1 xử lý từng message riêng và
        // trả kết quả của từng cái trong body: "the response can contain both success and error
        // notifications" (dev.mailjet.com/email/guides/send-api-v31). Địa chỉ sai, sender chưa
        // xác thực, quota hết… đều rơi vào nhánh này.
        //
        // Trước đây chỉ xét mã HTTP nên mọi lỗi loại đó bị nuốt: log ghi "đã gửi", người dùng chờ
        // mãi không thấy thư và không có gì để lần ra nguyên nhân.
        EnsureAllMessagesAccepted(responseBody, to);
    }

    /// <summary>
    /// Soi từng phần tử trong <c>Messages</c> của response; chỉ cần một message không có
    /// <c>Status = "success"</c> là ném lỗi kèm nguyên văn phần lỗi Mailjet trả về.
    ///
    /// Không parse được body (Mailjet đổi định dạng, trả HTML lỗi hạ tầng…) thì COI NHƯ ĐẠT: đã có
    /// HTTP 2xx, chặn ở đây sẽ biến một thay đổi phía provider thành sự cố gửi thư hàng loạt.
    /// Đánh đổi có chủ ý — ghi cảnh báo để còn lần ra, nhưng không chặn.
    /// </summary>
    private void EnsureAllMessagesAccepted(string responseBody, string to)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return;

        List<string> failures;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);

            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("Messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            failures = [];

            foreach (var message in messages.EnumerateArray())
            {
                var status = message.TryGetProperty("Status", out var s) ? s.GetString() : null;

                // So sánh không phân biệt hoa thường: giá trị tài liệu hoá là "success", nhưng
                // không đáng để một khác biệt hoa/thường làm chặn cả luồng gửi thư.
                if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Chi tiết lỗi nằm ở mảng "Errors"; giữ nguyên văn để log còn dùng được khi
                // đối chiếu với dashboard Mailjet.
                var detail = message.TryGetProperty("Errors", out var errors)
                    ? errors.GetRawText()
                    : message.GetRawText();

                failures.Add($"Status={status ?? "(thiếu)"} {detail}");
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "Không đọc được response của Mailjet để kiểm tra trạng thái từng message (to={To}). " +
                "Coi như đã gửi vì HTTP đã 2xx. Body: {Body}", to, Truncate(responseBody));
            return;
        }

        if (failures.Count == 0)
            return;

        throw new HttpRequestException(
            $"MailJet nhận request (HTTP 200) nhưng từ chối gửi tới {to}: {string.Join(" | ", failures)}");
    }

    private static string Truncate(string text, int max = 500)
        => text.Length > max ? text[..max] + "…" : text;
}
