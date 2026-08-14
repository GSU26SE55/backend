using System.Globalization;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — SLA timer sắp hết (warning threshold) → notify Manager. GH-604: recipient resolve qua
/// <see cref="IRecipientResolver"/> (broadcast Manager).
///
/// Sprint 6.2 NOTI-05 (#676) — <c>SlaWarningEvent</c> nay mang <c>StaffId</c> nên Staff đang phụ
/// trách cũng được báo, đúng spec §3.4 (trước đó phải defer vì payload thiếu StaffId).
/// Ghi trực tiếp qua UnitOfWork (InApp + Push).
/// </summary>
public class SlaWarningConsumer : IConsumer<SlaWarningEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<SlaWarningConsumer> _logger;

    public SlaWarningConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<SlaWarningConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SlaWarningEvent> context)
    {
        // GH-765 — chỗ giữ có hạn ngắn, chỉ nâng lên cửa sổ 30 phút SAU KHI ghi xong.
        // Bản cũ chiếm key 30 phút ngay từ đầu, nên một lỗi DB/resolver ở lần đầu là mọi lần
        // gửi lại trong 30 phút đều bị coi là trùng ⇒ notification biến mất hẳn.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, "SlaWarning", _logger, async () =>
        {
            var evt = context.Message;

            var recipientIds = (await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager")).ToList();

            // Sprint 6.2 NOTI-05 (#676) — spec §3.4 yêu cầu báo CẢ Staff đang phụ trách, không chỉ Manager.
            if (evt.StaffId is { } staffId && staffId != Guid.Empty)
                recipientIds.Add(staffId);

            if (recipientIds.Count == 0)
            {
                _logger.LogWarning("No recipient resolved for SlaWarning ticket={TicketId} — skip.", evt.TicketId);
                return;
            }

            var pct = evt.Percentage.ToString("0.#", CultureInfo.InvariantCulture);
            // 03/08/2026 — nhắc luôn mã ticket. Trước đó payload chỉ có TicketId (một GUID) nên cả câu
            // lẫn template đều phải nói chung chung "Ticket đã dùng …%", đúng loại thông báo mà người
            // nhận cần biết NGAY là ticket nào để mở ra xử lý. Event cũ trong hàng đợi không có Code ⇒
            // chuỗi rỗng, câu tự lược phần mã đi thay vì hiện "Ticket  ".
            var codeSuffix = string.IsNullOrWhiteSpace(evt.Code) ? "" : $" {evt.Code}";

            var title = $"⏰ SLA warning — approaching deadline{codeSuffix}";
            var body = $"Ticket{codeSuffix} has used {pct}% of its SLA time (deadline {evt.WarningAt:dd/MM HH:mm}). Prompt action required.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                warningAt = evt.WarningAt,
                percentage = evt.Percentage,
                staffId = evt.StaffId,
                screen = "TicketDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipientIds.Distinct().ToList(), NotificationTypeEnum.SlaWarning, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}
