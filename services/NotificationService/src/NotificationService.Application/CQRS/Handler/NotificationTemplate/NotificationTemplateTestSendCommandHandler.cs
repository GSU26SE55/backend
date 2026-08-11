using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Handler.NotificationTemplate;

public class NotificationTemplateTestSendCommandHandler
    : IRequestHandler<NotificationTemplateTestSendCommand, NotificationTemplateTestSendResponse>
{
    /// <summary>Trần gửi thử mỗi admin mỗi giờ (R-46).</summary>
    private const int PerHourLimit = 5;

    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _renderer;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICacheService _cache;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationTemplateTestSendCommandHandler> _logger;

    public NotificationTemplateTestSendCommandHandler(
        INotificationUnitOfWork unitOfWork,
        ITemplateRenderer renderer,
        IPublishEndpoint publishEndpoint,
        ICacheService cache,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationTemplateTestSendCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
        _publishEndpoint = publishEndpoint;
        _cache = cache;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationTemplateTestSendResponse> Handle(
        NotificationTemplateTestSendCommand request, CancellationToken cancellationToken)
    {
        var template = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, cancellationToken);

        if (template is null)
        {
            return new NotificationTemplateTestSendResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Template not found.",
            };
        }

        if (template.Channel != NotificationChannelEnum.Email)
        {
            return new NotificationTemplateTestSendResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Test send is only supported for Email channel templates.",
            };
        }

        // Địa chỉ nhận LUÔN thuộc về chính người gọi — không bao giờ lấy từ body (R-46).
        //
        // Hai nguồn, theo thứ tự:
        //   1) read-model account — chuẩn nhất, có đầy đủ thông tin;
        //   2) claim `email` trong JWT — dự phòng.
        //
        // Vì sao cần nguồn thứ 2: read-model chỉ được điền từ AccountActivatedEvent/
        // AccountProfileUpdatedEvent. Tài khoản admin seed thẳng vào auth_db (không đi qua luồng kích
        // hoạt) sẽ KHÔNG BAO GIỜ có mặt ở đây — phát hiện khi test E2E 30/07/2026: mọi lần gọi
        // test-send đều trả 400 dù người gọi là Admin hợp lệ.
        //
        // Lấy từ claim vẫn an toàn với R-46: đó là danh tính đã được JWT xác thực, không phải địa chỉ
        // tuỳ ý người gọi nhập vào.
        var admin = await _unitOfWork.Accounts.GetAllAsync(false)
            .FirstOrDefaultAsync(a => a.Id == request.ActorUserId && !a.IsDeleted, cancellationToken);

        var recipient = admin?.Email;
        var recipientSource = "read-model";

        if (string.IsNullOrWhiteSpace(recipient))
        {
            recipient = request.ActorEmailFromClaim;
            recipientSource = "jwt-claim";
        }

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return new NotificationTemplateTestSendResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Unable to determine the logged-in admin's email (missing both read-model and claim email).",
            };
        }

        var quotaKey = $"tpl_test_send:{request.ActorUserId:N}:{DateTime.UtcNow:yyyyMMddHH}";
        var used = await _cache.IncrementAsync(quotaKey, TimeSpan.FromHours(2), cancellationToken);

        if (used > PerHourLimit)
        {
            _logger.LogWarning(
                "Test-send: admin {AdminId} vượt trần {Limit}/giờ.", request.ActorUserId, PerHourLimit);

            return new NotificationTemplateTestSendResponse
            {
                IsSuccess = false,
                StatusCode = 429,
                Message = $"Used up all {PerHourLimit} test-send attempts for this hour.",
            };
        }

        // Phải dùng BuildFor giống hệt màn hình xem trước — hai đường mà dựng model khác nhau thì
        // "xem trước thấy đúng, gửi thử lại khác" là lỗi rất khó truy.
        var model = TemplateSampleModel.BuildFor(template.Type, request.SampleData);
        string subject, body;

        try
        {
            subject = _renderer.RenderInline(template.TitleTemplate, model);
            body = _renderer.RenderInline(template.BodyTemplate, model);
        }
        catch (Exception ex)
        {
            return new NotificationTemplateTestSendResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = $"Template has invalid syntax: {ex.Message}",
            };
        }

        var notificationId = Guid.NewGuid();

        // GH-795 — GHI AUDIT VÀ COMMIT TRƯỚC, publish sau.
        //
        // Thứ tự cũ là publish rồi mới ghi audit + SaveChanges. Nếu lần ghi DB hỏng sau khi broker
        // đã nhận event, admin thấy HTTP 500 nhưng email VẪN được gửi, và không có bản ghi kiểm toán
        // nào để đối chiếu. Admin bấm lại vì tưởng chưa gửi ⇒ thư thứ hai.
        //
        // Đảo lại thì hỏng DB nghĩa là chưa có tác động ra ngoài nào cả, và bấm lại là hành vi đúng.
        await _auditWriter.WriteAsync(
            NotificationAuditActionEnum.TemplateTestSent,
            notificationId,
            request.ActorUserId,
            isSuccess: true,
            reason: "Send test template",
            metadata: new Dictionary<string, object?>
            {
                ["templateId"] = template.Id,
                ["type"] = template.Type.ToString(),
                ["version"] = template.Version,
                ["quotaUsed"] = used,
                ["recipientSource"] = recipientSource,
            },
            ct: cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _publishEndpoint.Publish(
                new SendNotificationEmailEvent(
                    NotificationId: notificationId,
                    ToEmail: recipient,
                    Subject: $"[TEST SEND] {subject}",
                    Body: body,
                    SourceService: "notification-template-test",
                    // Email gửi thử KHÔNG có link hủy: nó không phải thư gửi hàng loạt, và link hủy
                    // trong bản thử sẽ tắt nhầm thông báo thật của chính admin.
                    UnsubscribeUrl: null)
                {
                    // GH-792 — ID suy từ notificationId để EmailService nhận ra bản trùng nếu event
                    // này được phát lại (MassTransit retry, hoặc relay chạy lại).
                    Id = DeterministicEventId.From(notificationId, "email"),
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Chiều ngược lại của việc commit trước: broker hỏng thì audit đã ghi "đã gửi thử" mà
            // thư chưa đi. Ghi thêm một bản ghi thất bại để dấu vết kiểm toán nói đúng sự thật, thay
            // vì để lại một dòng "thành công" không có thư nào tương ứng.
            _logger.LogError(ex, "Test-send template {TemplateId}: publish thất bại sau khi đã ghi audit.",
                template.Id);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.TemplateTestSent,
                notificationId,
                request.ActorUserId,
                isSuccess: false,
                reason: $"Publish failed: {ex.Message}",
                metadata: new Dictionary<string, object?> { ["templateId"] = template.Id },
                ct: cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new NotificationTemplateTestSendResponse
            {
                IsSuccess = false,
                StatusCode = 502,
                Message = "Failed to send the test email: the queue system did not accept the request.",
            };
        }

        _logger.LogInformation(
            "Test-send template {TemplateId} tới {Email} (nguồn {Source}, admin {AdminId}, lượt {Used}/{Limit}).",
            template.Id, recipient, recipientSource, request.ActorUserId, used, PerHourLimit);

        return new NotificationTemplateTestSendResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Test email sent to {recipient}.",
            Data = new NotificationTemplateTestSendDto
            {
                // IncrementAsync trả long (Redis INCR); còn lại luôn nằm trong [0, PerHourLimit]
                // nên ép int an toàn.
                RemainingThisHour = (int)Math.Max(0, PerHourLimit - used),
            },
        };
    }
}
