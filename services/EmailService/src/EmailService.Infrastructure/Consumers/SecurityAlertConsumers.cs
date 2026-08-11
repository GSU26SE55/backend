using System.Globalization;
using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

/// <summary>
/// Sprint 6.2 NOTI-04 (#675) — email cảnh báo đăng nhập bất thường.
///
/// <c>AuthService.AuthTokenIssuer</c> phát hiện login từ IP / User-Agent chưa từng thấy (đối chiếu
/// 50 session gần nhất) và publish <see cref="SuspiciousLoginDetectedEvent"/>, nhưng KHÔNG service
/// nào consume → công detect bỏ đi, user không được cảnh báo (reviewnotification.md §3.3).
///
/// Đi thẳng đường AuthService → EmailService như OTP (không qua NotificationService) vì đây là email
/// bảo mật, phải tới ngay và không phụ thuộc preference / quiet hours / digest của user.
/// </summary>
public class SuspiciousLoginDetectedConsumer : IConsumer<SuspiciousLoginDetectedEvent>
{
    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SuspiciousLoginDetectedConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SuspiciousLoginDetectedConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SuspiciousLoginDetectedConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SuspiciousLoginDetectedEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SuspiciousLoginDetectedConsumer), async () =>
        {
            if (string.IsNullOrWhiteSpace(msg.Email))
            {
                _logger.LogWarning("SuspiciousLogin account={AccountId}: thiếu email — bỏ qua.", msg.AccountId);
                return;
            }

            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";

                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = msg.Email,
                    ["IpAddress"] = string.IsNullOrWhiteSpace(msg.IpAddress) ? "Unknown" : msg.IpAddress,
                    ["UserAgent"] = string.IsNullOrWhiteSpace(msg.UserAgent) ? "Unknown" : msg.UserAgent,
                    ["Reason"] = SecurityAlertText.DescribeSuspiciousReason(msg.Reason),
                    ["DetectedAt"] = SecurityAlertText.FormatUtc(msg.DetectedAt),
                };

                var htmlBody = await _templateRenderer.RenderAsync(
                    EmailTemplates.SuspiciousLogin, values, context.CancellationToken);

                await _emailSender.SendAsync(
                    msg.Email, $"[Security Alert] New login on your account - {appName}",
                    htmlBody, context.CancellationToken);

                _logger.LogInformation("Suspicious login alert email sent to {Email}.", msg.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send suspicious login alert to {Email}.", msg.Email);
                throw;
            }
        });
    }
}

/// <summary>
/// Sprint 6.2 NOTI-04 (#675) — email cảnh báo refresh token bị dùng lại.
///
/// <c>RefreshTokenCommandHandler</c> phát hiện replay attack và đã revoke toàn bộ token family
/// (xử lý đúng), nhưng nạn nhân không được báo — chỉ thấy mình "bị logout" không rõ lý do và mất cơ
/// hội đổi mật khẩu kịp thời (reviewnotification.md §3.3).
///
/// <c>RefreshTokenReuseDetectedEvent</c> KHÔNG mang email; consumer lấy email từ
/// <c>ISecurityAlertRecipientLookup</c> (AuthService là nguồn sự thật của account).
/// </summary>
public class RefreshTokenReuseDetectedConsumer : IConsumer<RefreshTokenReuseDetectedEvent>
{
    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RefreshTokenReuseDetectedConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public RefreshTokenReuseDetectedConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<RefreshTokenReuseDetectedConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<RefreshTokenReuseDetectedEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(RefreshTokenReuseDetectedConsumer), async () =>
        {
            if (string.IsNullOrWhiteSpace(msg.Email))
            {
                _logger.LogWarning(
                    "RefreshTokenReuse account={AccountId}: event không kèm email — bỏ qua gửi cảnh báo.",
                    msg.AccountId);
                return;
            }

            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";

                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = msg.Email,
                    ["IpAddress"] = string.IsNullOrWhiteSpace(msg.IpAddress) ? "Unknown" : msg.IpAddress,
                    ["UserAgent"] = string.IsNullOrWhiteSpace(msg.UserAgent) ? "Unknown" : msg.UserAgent,
                    ["DetectedAt"] = SecurityAlertText.FormatUtc(msg.DetectedAt),
                    ["RevokedSessions"] = msg.RevokedFamilyCount.ToString(CultureInfo.InvariantCulture),
                };

                var htmlBody = await _templateRenderer.RenderAsync(
                    EmailTemplates.RefreshTokenReuse, values, context.CancellationToken);

                await _emailSender.SendAsync(
                    msg.Email, $"[Security Alert] Suspicious login session - {appName}",
                    htmlBody, context.CancellationToken);

                _logger.LogInformation("Refresh token reuse alert email sent to {Email}.", msg.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send refresh token reuse alert to {Email}.", msg.Email);
                throw;
            }
        });
    }
}

internal static class SecurityAlertText
{
    public static string DescribeSuspiciousReason(string? reason) => reason switch
    {
        "new_ip" => "an unfamiliar IP address",
        "new_user_agent" => "an unfamiliar device / browser",
        "new_ip_and_user_agent" => "both an unfamiliar IP address and device",
        _ => "unusual activity",
    };

    public static string FormatUtc(DateTime utc) =>
        utc.ToString("dd/MM/yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}
