using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Api.Controllers;

/// <summary>
/// Sprint 6.3 NOTI3-12 (#712) — quản trị template notification.
///
/// Ba việc trước sprint này không làm được:
/// <list type="bullet">
/// <item><b>Xem trước</b> — sửa template xong chỉ biết đúng/sai khi có sự kiện thật xảy ra.</item>
/// <item><b>Gửi thử</b> — kiểm chứng bản dựng thật trong hộp thư, không phải đoán qua HTML.</item>
/// <item><b>Quay lui</b> — bản mới sai chính tả gửi cho hàng trăm khách thì phải sửa tay lại.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/admin/notification-templates")]
// Quy ước thật đang chạy trong repo là role-based, KHÔNG phải policy: JWT phát ra claim
// `role = "Admin"` (chuỗi), còn policy "AdminOnly" trong SharedInfrastructure đã bị comment
// toàn bộ từ lâu — và định nghĩa cũ của nó (`RequireClaim("Role","1")`) cũng không khớp token
// hiện tại. Dùng `[Authorize(Roles = "Admin")]` như 4 controller admin khác của dự án.
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class AdminNotificationTemplatesController : ControllerBase
{
    /// <summary>Trần gửi thử mỗi admin mỗi giờ — xem ghi chú ở <c>TestSend</c> (R-46).</summary>
    private const int TestSendPerHourLimit = 5;

    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _renderer;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ICacheService _cache;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<AdminNotificationTemplatesController> _logger;

    public AdminNotificationTemplatesController(
        INotificationUnitOfWork unitOfWork,
        ITemplateRenderer renderer,
        IPublishEndpoint publishEndpoint,
        ICacheService cache,
        INotificationAuditWriter auditWriter,
        ILogger<AdminNotificationTemplatesController> logger)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
        _publishEndpoint = publishEndpoint;
        _cache = cache;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    /// <summary>Danh sách template, lọc theo type/channel/locale.</summary>
    /// <remarks>**Quyền:** `AdminOnly`. Bao gồm cả bản không active để thấy lịch sử phiên bản.</remarks>
    /// <response code="200">Trả về danh sách template.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] NotificationTypeEnum? type,
        [FromQuery] NotificationChannelEnum? channel,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        var query = _unitOfWork.NotificationTemplates.GetAllAsync(false).Where(t => !t.IsDeleted);

        if (type.HasValue)
            query = query.Where(t => t.Type == type.Value);
        if (channel.HasValue)
            query = query.Where(t => t.Channel == channel.Value);
        if (!string.IsNullOrWhiteSpace(locale))
            query = query.Where(t => t.Locale == locale);

        var items = await query
            .OrderBy(t => t.Type).ThenBy(t => t.Channel).ThenBy(t => t.Locale).ThenByDescending(t => t.Version)
            .Select(t => new
            {
                id = t.Id,
                type = t.Type.ToString(),
                channel = t.Channel.ToString(),
                t.Locale,
                t.Version,
                t.IsActive,
                t.TitleTemplate,
                t.BodyTemplate,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return Ok(new CommonResponse<object> { IsSuccess = true, Data = items });
    }

    /// <summary>
    /// Dựng thử template với dữ liệu mẫu — **KHÔNG gửi đi đâu cả**.
    /// </summary>
    /// <remarks>
    /// **Quyền:** `AdminOnly`.
    ///
    /// Trả về `title`/`body` sau khi render. Placeholder không có trong `sampleData` sẽ rỗng —
    /// đó chính là cách phát hiện template gọi tên biến sai.
    ///
    /// Template hỏng cú pháp trả **400** kèm thông báo lỗi, thay vì ném 500.
    /// </remarks>
    /// <response code="200">Render thành công.</response>
    /// <response code="400">Template hỏng cú pháp Handlebars.</response>
    /// <response code="404">Không tìm thấy template.</response>
    [HttpPost("{id:guid}/preview")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview(
        Guid id, [FromBody] TemplatePreviewRequest? request, CancellationToken cancellationToken)
    {
        var template = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

        if (template is null)
            return NotFound(new CommonResponse<object> { IsSuccess = false, Message = "Không tìm thấy template." });

        var model = BuildModel(request?.SampleData);

        try
        {
            return Ok(new CommonResponse<object>
            {
                IsSuccess = true,
                Data = new
                {
                    type = template.Type.ToString(),
                    channel = template.Channel.ToString(),
                    template.Locale,
                    template.Version,
                    title = _renderer.RenderInline(template.TitleTemplate, model),
                    body = _renderer.RenderInline(template.BodyTemplate, model),
                },
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preview template {TemplateId} lỗi cú pháp.", id);
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                Message = $"Template hỏng cú pháp: {ex.Message}",
            });
        }
    }

    /// <summary>
    /// Gửi thử template tới **chính admin đang đăng nhập**.
    /// </summary>
    /// <remarks>
    /// **Quyền:** `AdminOnly`.
    ///
    /// **Không nhận địa chỉ tự do** (R-46): endpoint nhận địa chỉ tuỳ ý sẽ biến hệ thống thành cổng
    /// gửi thư rác có xác thực — kẻ chiếm được một tài khoản admin có thể bắn nội dung tự soạn từ
    /// domain có SPF/DKIM hợp lệ của chúng ta. Địa chỉ nhận LUÔN lấy từ read-model của chính người
    /// gọi, không bao giờ từ body.
    ///
    /// Giới hạn **5 lần/giờ mỗi admin** và ghi audit mỗi lần gửi.
    ///
    /// Chỉ hỗ trợ template kênh **Email**; kênh khác trả 400 (gửi thử SMS tốn tiền thật, push cần
    /// device token của admin).
    /// </remarks>
    /// <response code="200">Đã xếp hàng gửi tới email của admin.</response>
    /// <response code="400">Template không phải kênh Email, hoặc admin chưa có email.</response>
    /// <response code="404">Không tìm thấy template.</response>
    /// <response code="429">Vượt 5 lần/giờ.</response>
    [HttpPost("{id:guid}/test-send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TestSend(
        Guid id, [FromBody] TemplatePreviewRequest? request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var adminId))
            return BadRequest(new CommonResponse<object> { IsSuccess = false, Message = "Không xác định được UserId từ token." });

        var template = await _unitOfWork.NotificationTemplates.GetAllAsync(false)
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

        if (template is null)
            return NotFound(new CommonResponse<object> { IsSuccess = false, Message = "Không tìm thấy template." });

        if (template.Channel != NotificationChannelEnum.Email)
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                Message = "Chỉ gửi thử được template kênh Email.",
            });
        }

        // Địa chỉ nhận LUÔN thuộc về chính người gọi — không bao giờ lấy từ body (R-46).
        //
        // Hai nguồn, theo thứ tự:
        //   1) read-model account — chuẩn nhất, có đầy đủ thông tin;
        //   2) claim `email` trong JWT — dự phòng.
        //
        // Vì sao cần nguồn thứ 2: read-model chỉ được điền từ `AccountActivatedEvent`/
        // `AccountProfileUpdatedEvent`. Tài khoản admin seed thẳng vào `auth_db` (không đi qua
        // luồng kích hoạt) sẽ KHÔNG BAO GIỜ có mặt ở đây — phát hiện khi test E2E 30/07/2026:
        // mọi lần gọi test-send đều trả 400 dù người gọi là Admin hợp lệ.
        //
        // Lấy từ claim vẫn an toàn với R-46: đó là danh tính đã được JWT xác thực, không phải
        // địa chỉ tuỳ ý người gọi nhập vào.
        var admin = await _unitOfWork.Accounts.GetAllAsync(false)
            .FirstOrDefaultAsync(a => a.Id == adminId && !a.IsDeleted, cancellationToken);

        var recipient = admin?.Email;
        var recipientSource = "read-model";

        if (string.IsNullOrWhiteSpace(recipient))
        {
            recipient = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            recipientSource = "jwt-claim";
        }

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                Message = "Không xác định được email của admin đang đăng nhập (thiếu cả read-model lẫn claim email).",
            });
        }

        var quotaKey = $"tpl_test_send:{adminId:N}:{DateTime.UtcNow:yyyyMMddHH}";
        var used = await _cache.IncrementAsync(quotaKey, TimeSpan.FromHours(2), cancellationToken);

        if (used > TestSendPerHourLimit)
        {
            _logger.LogWarning("Test-send: admin {AdminId} vượt trần {Limit}/giờ.", adminId, TestSendPerHourLimit);
            return StatusCode(StatusCodes.Status429TooManyRequests, new CommonResponse<object>
            {
                IsSuccess = false,
                Message = $"Đã dùng hết {TestSendPerHourLimit} lượt gửi thử trong giờ này.",
            });
        }

        var model = BuildModel(request?.SampleData);
        string subject, body;

        try
        {
            subject = _renderer.RenderInline(template.TitleTemplate, model);
            body = _renderer.RenderInline(template.BodyTemplate, model);
        }
        catch (Exception ex)
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                Message = $"Template hỏng cú pháp: {ex.Message}",
            });
        }

        var notificationId = Guid.NewGuid();

        await _publishEndpoint.Publish(
            new SendNotificationEmailEvent(
                NotificationId: notificationId,
                ToEmail: recipient,
                Subject: $"[GỬI THỬ] {subject}",
                Body: body,
                SourceService: "notification-template-test",
                // Email gửi thử KHÔNG có link hủy: nó không phải thư gửi hàng loạt, và link hủy
                // trong bản thử sẽ tắt nhầm thông báo thật của chính admin.
                UnsubscribeUrl: null),
            cancellationToken);

        await _auditWriter.WriteAsync(
            NotificationAuditActionEnum.TemplateTestSent,
            notificationId,
            adminId,
            isSuccess: true,
            reason: "Gửi thử template",
            metadata: new Dictionary<string, object?>
            {
                ["templateId"] = template.Id,
                ["type"] = template.Type.ToString(),
                ["locale"] = template.Locale,
                ["version"] = template.Version,
                ["quotaUsed"] = used,
                ["recipientSource"] = recipientSource,
            },
            ct: cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Test-send template {TemplateId} tới {Email} (nguồn {Source}, admin {AdminId}, lượt {Used}/{Limit}).",
            template.Id, recipient, recipientSource, adminId, used, TestSendPerHourLimit);

        return Ok(new CommonResponse<object>
        {
            IsSuccess = true,
            Message = $"Đã gửi thử tới {recipient}.",
            Data = new { remainingThisHour = Math.Max(0, TestSendPerHourLimit - used) },
        });
    }

    /// <summary>
    /// Quay lui: kích hoạt lại một phiên bản template cũ.
    /// </summary>
    /// <remarks>
    /// **Quyền:** `AdminOnly`.
    ///
    /// Trong cùng bộ ba (Type × Channel × Locale) chỉ được có đúng một bản active, nên thao tác này
    /// tắt bản đang dùng rồi bật bản được chọn — trong **một** lần lưu, để không có khoảnh khắc nào
    /// bộ ba đó không có bản nào active (khoảnh khắc ấy dispatcher sẽ rơi về chuỗi hardcode).
    /// </remarks>
    /// <response code="200">Đã chuyển sang phiên bản được chọn.</response>
    /// <response code="404">Không tìm thấy template.</response>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var target = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);

        if (target is null)
            return NotFound(new CommonResponse<object> { IsSuccess = false, Message = "Không tìm thấy template." });

        var siblings = await _unitOfWork.NotificationTemplates.GetAllAsync()
            .Where(t => !t.IsDeleted
                        && t.Type == target.Type
                        && t.Channel == target.Channel
                        && t.Locale == target.Locale)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var sibling in siblings)
        {
            var shouldBeActive = sibling.Id == target.Id;
            if (sibling.IsActive == shouldBeActive)
                continue;

            sibling.IsActive = shouldBeActive;
            sibling.UpdatedAt = now;
            _unitOfWork.NotificationTemplates.UpdateAsync(sibling);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Template {Type}/{Channel}/{Locale} chuyển sang phiên bản {Version}.",
            target.Type, target.Channel, target.Locale, target.Version);

        return Ok(new CommonResponse<object>
        {
            IsSuccess = true,
            Message = $"Đã kích hoạt phiên bản {target.Version}.",
        });
    }

    /// <summary>
    /// Dữ liệu mẫu do người dùng gửi lên là JSON tuỳ ý; quy về dictionary phẳng để Handlebars đọc
    /// được. Không gửi gì thì render với model rỗng — placeholder sẽ trống, đúng ý đồ kiểm tra.
    /// </summary>
    private static Dictionary<string, object?> BuildModel(JsonElement? sampleData)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (sampleData is not { ValueKind: JsonValueKind.Object } element)
            return model;

        foreach (var property in element.EnumerateObject())
        {
            model[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.TryGetInt64(out var l) ? l : property.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }

        return model;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }
}

/// <summary>Sprint 6.3 NOTI3-12 (#712) — dữ liệu mẫu dùng để dựng thử template.</summary>
public class TemplatePreviewRequest
{
    /// <summary>
    /// Cặp khoá–giá trị ứng với placeholder trong template
    /// (vd <c>{ "ticketCode": "TK-001", "priority": "P1" }</c>).
    /// </summary>
    public JsonElement? SampleData { get; set; }
}
