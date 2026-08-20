using EmailService.Infrastructure.Services;
using EmailService.Infrastructure.Templates;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;

namespace EmailService.Infrastructure.Consumers;

/// <summary>
/// Gửi thư chào mừng cho khách hàng vừa được cấp tài khoản từ dữ liệu nhập của bên thứ ba.
/// </summary>
/// <remarks>
/// <para>
/// Thư <b>không</b> chứa mật khẩu. Tài khoản được tạo với một mật khẩu ngẫu nhiên mà không ai đọc;
/// khách tự đặt mật khẩu qua màn "Quên mật khẩu". Cách này giữ cho mật khẩu không nằm trong hộp
/// thư của khách, nơi nó sẽ ở lại vô thời hạn.
/// </para>
/// <para>
/// Không tái dùng được thư mời quản trị: thư đó dẫn tới màn chấp nhận lời mời, mà màn đó chỉ chạy
/// với tài khoản đang chờ xác minh — trong khi tài khoản nhập vào đã ở trạng thái hoạt động.
/// </para>
/// </remarks>
public class SendPartnerImportWelcomeConsumer : IConsumer<SendPartnerImportWelcomeEvent>
{
    private const string DefaultResetUrlBase = "http://localhost:5173/forgot-password";

    private readonly IEmailProvider _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendPartnerImportWelcomeConsumer> _logger;
    private readonly IInboxStore _inboxStore;

    public SendPartnerImportWelcomeConsumer(
        IEmailProvider emailSender,
        IEmailTemplateRenderer templateRenderer,
        IConfiguration configuration,
        ILogger<SendPartnerImportWelcomeConsumer> logger,
        IInboxStore inboxStore)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _configuration = configuration;
        _logger = logger;
        _inboxStore = inboxStore;
    }

    public async Task Consume(ConsumeContext<SendPartnerImportWelcomeEvent> context)
    {
        var msg = context.Message;

        await context.ProcessOnceAsync(_inboxStore, nameof(SendPartnerImportWelcomeConsumer), async () =>
        {
            try
            {
                var appName = _configuration["MailJet:DisplayName"] ?? "Solar Battery Maintenance";
                var values = new Dictionary<string, string?>
                {
                    ["AppName"] = appName,
                    ["UserName"] = string.IsNullOrWhiteSpace(msg.FullName) ? msg.Email : msg.FullName,
                    ["Email"] = msg.Email,
                    ["AcceptUrl"] = BuildResetUrl(msg.Email)
                };

                var htmlBody = await _templateRenderer.RenderAsync(
                    EmailTemplates.PartnerImportWelcome, values, context.CancellationToken);

                var subject = $"Your {appName} account is ready";
                await _emailSender.SendAsync(msg.Email, subject, htmlBody, context.CancellationToken);

                _logger.LogInformation(
                    "Partner import welcome email sent to {Email} for account {AccountId}.", msg.Email, msg.AccountId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send partner import welcome email to {Email}.", msg.Email);
                throw;
            }
        });
    }

    private string BuildResetUrl(string email)
    {
        var baseUrl = _configuration["PartnerImport:ResetPasswordUrlBase"]
                      ?? _configuration["Frontend:ForgotPasswordUrl"]
                      ?? DefaultResetUrlBase;

        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}email={Uri.EscapeDataString(email)}";
    }
}
