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
    }
}
