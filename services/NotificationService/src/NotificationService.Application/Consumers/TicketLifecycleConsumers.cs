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
/// Sprint 6.2 NOTI-07 (#678) — consumer cho các state cuối vòng đời ticket.
///
/// Trước sprint này review §4.1 chấm 🔴 cho toàn bộ nhóm "closed / pending-rate / rejected / reopen /
/// rating request": không event nào tồn tại ở SharedContracts nên NotificationService không thể
/// consume, và 2 enum <c>TicketStatusChanged(3)</c> / <c>TicketClosed(5)</c> nằm không producer.
/// Nhóm consumer dưới đây khớp 1-1 với các event mới trong <c>TicketLifecycleEvents.cs</c>.
/// </summary>
public class TicketStatusChangedConsumer : IConsumer<TicketStatusChangedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketStatusChangedConsumer> _logger;

    public TicketStatusChangedConsumer(
        INotificationUnitOfWork unitOfWork, ICacheService cache, ILogger<TicketStatusChangedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketStatusChangedEvent> context)
    {
        // GH-765 — xem NotificationDebounce.ProcessOnceAsync: chỗ giữ ngắn, chỉ nâng lên cửa sổ
        // 30 phút sau khi ghi xong, và nhả ngay khi lỗi để lần gửi lại chạy thật.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketStatusChangedEvent), _logger, async () =>
        {
            var evt = context.Message;
            if (evt.CustomerId == Guid.Empty)
                return;

            var title = $"Ticket {evt.Code} chuyển trạng thái";
            var body = $"Ticket {evt.Code}: {evt.OldStatusName} → {evt.NewStatusName}.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                oldStatus = evt.OldStatus,
                newStatus = evt.NewStatus,
                oldStatusName = evt.OldStatusName,
                newStatusName = evt.NewStatusName,
                screen = "TicketDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, [evt.CustomerId], NotificationTypeEnum.TicketStatusChanged, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}

/// <summary>Manager duyệt kết quả → Customer nhận thông báo kèm lời mời đánh giá.</summary>
public class TicketApprovedConsumer : IConsumer<TicketApprovedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketApprovedConsumer> _logger;

    public TicketApprovedConsumer(
        INotificationUnitOfWork unitOfWork, ICacheService cache, ILogger<TicketApprovedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketApprovedEvent> context)
    {
        // GH-765 — xem NotificationDebounce.ProcessOnceAsync: chỗ giữ ngắn, chỉ nâng lên cửa sổ
        // 30 phút sau khi ghi xong, và nhả ngay khi lỗi để lần gửi lại chạy thật.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketApprovedEvent), _logger, async () =>
        {
            var evt = context.Message;
            if (evt.CustomerId == Guid.Empty)
                return;

            var title = $"Ticket {evt.Code} đã được duyệt hoàn tất";
            var body = string.IsNullOrWhiteSpace(evt.ManagerComment)
                ? $"Ticket {evt.Code} đã được duyệt. Mời bạn đánh giá chất lượng xử lý."
                : $"Ticket {evt.Code} đã được duyệt: {evt.ManagerComment}. Mời bạn đánh giá chất lượng xử lý.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                approvedAt = evt.ApprovedAt,
                screen = "TicketRate"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, [evt.CustomerId], NotificationTypeEnum.TicketApproved, NotificationWriter.InAppPushEmail,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}

/// <summary>
/// Ticket bị từ chối. Người nhận phụ thuộc ngữ cảnh:
/// từ chối kết quả resolve → báo Staff; từ chối ở triage (ngoài scope) → báo Customer.
/// </summary>
public class TicketRejectedConsumer : IConsumer<TicketRejectedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketRejectedConsumer> _logger;

    public TicketRejectedConsumer(
        INotificationUnitOfWork unitOfWork, ICacheService cache, ILogger<TicketRejectedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketRejectedEvent> context)
    {
        // GH-765 — xem NotificationDebounce.ProcessOnceAsync: chỗ giữ ngắn, chỉ nâng lên cửa sổ
        // 30 phút sau khi ghi xong, và nhả ngay khi lỗi để lần gửi lại chạy thật.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketRejectedEvent), _logger, async () =>
        {
            var evt = context.Message;

            Guid recipient;
            string title;
            string body;

            if (evt.IsClosedRejected)
            {
                recipient = evt.CustomerId;
                title = $"Ticket {evt.Code} bị từ chối";
                body = $"Yêu cầu {evt.Code} nằm ngoài phạm vi dịch vụ nên đã được đóng. Lý do: {evt.Reason}";
            }
            else
            {
                recipient = evt.StaffId ?? Guid.Empty;
                title = $"Kết quả xử lý ticket {evt.Code} bị trả lại";
                body = $"Manager yêu cầu xử lý lại ticket {evt.Code}. Lý do: {evt.Reason}";
            }

            if (recipient == Guid.Empty)
            {
                _logger.LogWarning(
                    "TicketRejected ticket={TicketId}: không xác định được người nhận (isClosedRejected={Flag}) — skip.",
                    evt.TicketId, evt.IsClosedRejected);
                return;
            }

            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                reason = evt.Reason,
                isClosedRejected = evt.IsClosedRejected,
                rejectedAt = evt.RejectedAt,
                screen = "TicketDetail"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, [recipient], NotificationTypeEnum.TicketRejected, NotificationWriter.InAppPushEmail,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}

/// <summary>Ticket đóng hẳn — xác nhận với Customer, đồng thời báo Manager để chốt số liệu.</summary>
public class TicketClosedConsumer : IConsumer<TicketClosedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketClosedConsumer> _logger;

    public TicketClosedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<TicketClosedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketClosedEvent> context)
    {
        // GH-765 — xem NotificationDebounce.ProcessOnceAsync: chỗ giữ ngắn, chỉ nâng lên cửa sổ
        // 30 phút sau khi ghi xong, và nhả ngay khi lỗi để lần gửi lại chạy thật.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketClosedEvent), _logger, async () =>
        {
            var evt = context.Message;

            var title = $"Ticket {evt.Code} đã đóng";
            var body = evt.IsAutoClosed
                ? $"Ticket {evt.Code} tự động đóng do quá hạn đánh giá."
                : evt.Rating.HasValue
                    ? $"Cảm ơn bạn đã đánh giá {evt.Rating} sao. Ticket {evt.Code} đã đóng."
                    : $"Ticket {evt.Code} đã đóng.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                closedAt = evt.ClosedAt,
                isAutoClosed = evt.IsAutoClosed,
                rating = evt.Rating,
                screen = "TicketDetail"
            });

            var recipients = new List<Guid>();
            if (evt.CustomerId != Guid.Empty)
                recipients.Add(evt.CustomerId);

            var managers = await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager");
            recipients.AddRange(managers.Where(m => m != evt.CustomerId));

            if (recipients.Count == 0)
            {
                _logger.LogWarning("TicketClosed ticket={TicketId}: không có người nhận — skip.", evt.TicketId);
                return;
            }

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipients.Distinct().ToList(), NotificationTypeEnum.TicketClosed, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}

/// <summary>Customer mở lại ticket → báo Manager (+ Staff đang assign nếu có).</summary>
public class TicketReopenedConsumer : IConsumer<TicketReopenedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketReopenedConsumer> _logger;

    public TicketReopenedConsumer(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        ICacheService cache,
        ILogger<TicketReopenedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketReopenedEvent> context)
    {
        // GH-765 — xem NotificationDebounce.ProcessOnceAsync: chỗ giữ ngắn, chỉ nâng lên cửa sổ
        // 30 phút sau khi ghi xong, và nhả ngay khi lỗi để lần gửi lại chạy thật.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketReopenedEvent), _logger, async () =>
        {
            var evt = context.Message;

            var title = $"Ticket {evt.Code} được mở lại";
            var body = $"Khách hàng mở lại ticket {evt.Code} (lần {evt.ReopenCount}). Lý do: {evt.ReopenReason}";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                reopenReason = evt.ReopenReason,
                reopenCount = evt.ReopenCount,
                reopenedAt = evt.ReopenedAt,
                screen = "TicketDetail"
            });

            var recipients = (await _recipientResolver.GetActiveByRoleAsync(context.CancellationToken, "Manager")).ToList();
            if (evt.StaffId is { } staffId && staffId != Guid.Empty)
                recipients.Add(staffId);

            if (recipients.Count == 0)
            {
                _logger.LogWarning("TicketReopened ticket={TicketId}: không có người nhận — skip.", evt.TicketId);
                return;
            }

            await NotificationWriter.WriteAsync(
                _unitOfWork, recipients.Distinct().ToList(), NotificationTypeEnum.TicketReopened, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}

/// <summary>Nhắc Customer đánh giá ticket đang treo ở CLOSED_PENDING_RATE.</summary>
public class TicketRatingRequestedConsumer : IConsumer<TicketRatingRequestedEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<TicketRatingRequestedConsumer> _logger;

    public TicketRatingRequestedConsumer(
        INotificationUnitOfWork unitOfWork, ICacheService cache, ILogger<TicketRatingRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TicketRatingRequestedEvent> context)
    {
        // GH-765 — xem NotificationDebounce.ProcessOnceAsync: chỗ giữ ngắn, chỉ nâng lên cửa sổ
        // 30 phút sau khi ghi xong, và nhả ngay khi lỗi để lần gửi lại chạy thật.
        await NotificationDebounce.ProcessOnceAsync(_cache, context, nameof(TicketRatingRequestedEvent), _logger, async () =>
        {
            var evt = context.Message;
            if (evt.CustomerId == Guid.Empty)
                return;

            var title = $"Mời đánh giá ticket {evt.Code}";
            var body = evt.DaysUntilAutoClose > 0
                ? $"Ticket {evt.Code} đã xử lý xong {evt.DaysPending} ngày trước. Vui lòng đánh giá trong " +
                  $"{evt.DaysUntilAutoClose} ngày tới, sau đó ticket sẽ tự đóng."
                : $"Ticket {evt.Code} đã xử lý xong {evt.DaysPending} ngày trước. Vui lòng đánh giá để hoàn tất.";
            var payload = JsonSerializer.Serialize(new
            {
                ticketId = evt.TicketId,
                code = evt.Code,
                approvedAt = evt.ApprovedAt,
                daysPending = evt.DaysPending,
                daysUntilAutoClose = evt.DaysUntilAutoClose,
                screen = "TicketRate"
            });

            await NotificationWriter.WriteAsync(
                _unitOfWork, [evt.CustomerId], NotificationTypeEnum.TicketRatingRequested, NotificationWriter.InAppPush,
                title, body, payload, "Ticket", evt.TicketId, context.CancellationToken);
        });
    }
}
